using System.Buffers.Binary;

namespace TrmnlApi.Geo;

/// <summary>
/// The packed geometry format shared by the dataset builder and the runtime lookup.
/// </summary>
/// <remarks>
/// Little-endian: <c>int32 ringCount</c>, then per ring <c>int32 pointCount</c> followed by
/// <c>pointCount</c> pairs of <c>float32 lon, float32 lat</c>.
/// <para>
/// float32 rather than float64, and a flat span rather than a geometry object graph. The design
/// note's "60-100 MB resident" estimate came from a library that allocates an object per
/// coordinate; the 1.3 million points in the admin-1 layer are ~10 MB packed, and only the two or
/// three blobs an R-tree query returns are ever read.
/// </para>
/// </remarks>
public static class PolygonBlob
{
    public static byte[] Encode(IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> rings)
    {
        var size = 4 + rings.Sum(r => 4 + (r.Count * 8));
        var bytes = new byte[size];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, rings.Count);
        var offset = 4;

        foreach (var ring in rings)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], ring.Count);
            offset += 4;
            foreach (var (lon, lat) in ring)
            {
                BinaryPrimitives.WriteSingleLittleEndian(span[offset..], (float)lon);
                BinaryPrimitives.WriteSingleLittleEndian(span[(offset + 4)..], (float)lat);
                offset += 8;
            }
        }

        return bytes;
    }

    /// <summary>
    /// Even-odd ray casting over every ring at once. Holes fall out of the parity rule for free,
    /// which is why the rings are not tagged as outer or inner when they are written.
    /// </summary>
    public static bool Contains(ReadOnlySpan<byte> blob, double longitude, double latitude)
    {
        if (blob.Length < 4)
        {
            return false;
        }

        var inside = false;
        var ringCount = BinaryPrimitives.ReadInt32LittleEndian(blob);
        var offset = 4;

        for (var r = 0; r < ringCount; r++)
        {
            if (offset + 4 > blob.Length)
            {
                return inside;
            }

            var pointCount = BinaryPrimitives.ReadInt32LittleEndian(blob[offset..]);
            offset += 4;
            var ringBytes = pointCount * 8;
            if (pointCount < 3 || ringBytes < 0 || offset + ringBytes > blob.Length)
            {
                return inside;
            }

            var ring = blob.Slice(offset, ringBytes);
            offset += ringBytes;

            var previous = (pointCount - 1) * 8;
            var jLon = (double)BinaryPrimitives.ReadSingleLittleEndian(ring[previous..]);
            var jLat = (double)BinaryPrimitives.ReadSingleLittleEndian(ring[(previous + 4)..]);

            for (var i = 0; i < pointCount; i++)
            {
                var iLon = (double)BinaryPrimitives.ReadSingleLittleEndian(ring[(i * 8)..]);
                var iLat = (double)BinaryPrimitives.ReadSingleLittleEndian(ring[((i * 8) + 4)..]);

                if ((iLat > latitude) != (jLat > latitude)
                    && longitude < (((jLon - iLon) * (latitude - iLat) / (jLat - iLat)) + iLon))
                {
                    inside = !inside;
                }

                jLon = iLon;
                jLat = iLat;
            }
        }

        return inside;
    }

    /// <summary>
    /// Great-circle-ish distance in kilometres from a point to the nearest polygon edge. Used only
    /// when <see cref="Contains"/> has already said no, so a point inside a ring is not a case
    /// this has to answer for.
    /// </summary>
    /// <remarks>
    /// Distance to the real edges, not to a bounding box. A box is worthless for a feature whose
    /// parts are spread across an ocean: Natural Earth ships Kiribati as one feature straddling
    /// the antimeridian, so its box spans 349 degrees of longitude and covers points more than a
    /// thousand kilometres from any of its land.
    /// <para>
    /// Longitude differences are taken relative to the query point and wrapped to
    /// [-180, 180], which is what keeps that same antimeridian feature from measuring its own
    /// width the long way round. Everything is then flat: at the radius this is compared against,
    /// an equirectangular plane centred on the query point is far closer than the 0.01-degree
    /// simplification the rings already carry.
    /// </para>
    /// </remarks>
    public static double DistanceKm(ReadOnlySpan<byte> blob, double longitude, double latitude)
    {
        if (blob.Length < 4)
        {
            return double.MaxValue;
        }

        // One degree of longitude shrinks towards the poles; one of latitude does not.
        var lonScale = Math.Cos(latitude * Math.PI / 180.0) * GeoDistance.KmPerDegree;
        var best = double.MaxValue;
        var ringCount = BinaryPrimitives.ReadInt32LittleEndian(blob);
        var offset = 4;

        for (var r = 0; r < ringCount; r++)
        {
            if (offset + 4 > blob.Length)
            {
                return best;
            }

            var pointCount = BinaryPrimitives.ReadInt32LittleEndian(blob[offset..]);
            offset += 4;
            var ringBytes = pointCount * 8;
            if (pointCount < 3 || ringBytes < 0 || offset + ringBytes > blob.Length)
            {
                return best;
            }

            var ring = blob.Slice(offset, ringBytes);
            offset += ringBytes;

            var previous = (pointCount - 1) * 8;
            Project(ring, previous, longitude, latitude, lonScale, out var jx, out var jy);

            for (var i = 0; i < pointCount; i++)
            {
                Project(ring, i * 8, longitude, latitude, lonScale, out var ix, out var iy);
                var distance = SegmentDistance(ix, iy, jx, jy);
                if (distance < best)
                {
                    best = distance;
                }

                jx = ix;
                jy = iy;
            }
        }

        return best;

        static void Project(
            ReadOnlySpan<byte> ring, int at, double longitude, double latitude, double lonScale,
            out double x, out double y)
        {
            var lon = (double)BinaryPrimitives.ReadSingleLittleEndian(ring[at..]);
            var lat = (double)BinaryPrimitives.ReadSingleLittleEndian(ring[(at + 4)..]);
            x = Math.IEEERemainder(lon - longitude, 360.0) * lonScale;
            y = (lat - latitude) * GeoDistance.KmPerDegree;
        }

        // Distance from the origin to the segment, both endpoints already relative to it.
        static double SegmentDistance(double ax, double ay, double bx, double by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var lengthSquared = (dx * dx) + (dy * dy);
            var t = lengthSquared > 0
                ? Math.Clamp(-((ax * dx) + (ay * dy)) / lengthSquared, 0.0, 1.0)
                : 0.0;
            var cx = ax + (t * dx);
            var cy = ay + (t * dy);
            return Math.Sqrt((cx * cx) + (cy * cy));
        }
    }
}
