using System.Diagnostics;
using TrmnlApi.GeoDataBuilder;

// Builds geo.sqlite from the three upstream datasets. Not referenced by TrmnlApi and not in the
// runtime image: the artifact it produces is published as a release asset, and api/Dockerfile
// fetches that by pinned URL and sha256. See docs/geographic-telemetry.md.
//
//   dotnet run --project api/tools/GeoDataBuilder -- \
//       --input <dir> --output geo.sqlite [--simplify 0.01]
//
// The input directory must contain:
//   ne_10m_admin_1_states_provinces.shp (with its .dbf and .shx)
//   cities1000.txt
//   admin1CodesASCII.txt
//   allCountries.txt          (the postal one, not the GeoNames dump of the same name)

var options = BuilderOptions.Parse(args);
if (options is null)
{
    return 1;
}

var timer = Stopwatch.StartNew();
using var writer = new GeoDatabaseWriter(options.Output);

writer.CreateSchema();
var subdivisions = writer.WriteAdmin1(
    Path.Combine(options.Input, "ne_10m_admin_1_states_provinces.shp"),
    options.SimplifyTolerance);
Console.WriteLine($"admin1: {subdivisions.Features} features, {subdivisions.Points} points, {subdivisions.Countries} countries");

var admin1Names = writer.WriteAdmin1Names(Path.Combine(options.Input, "admin1CodesASCII.txt"));
Console.WriteLine($"admin1_name: {admin1Names} rows");

var cities = writer.WriteCities(Path.Combine(options.Input, "cities1000.txt"));
Console.WriteLine($"city: {cities.Cities} rows, {cities.Aliases} aliases");

var postal = writer.WritePostal(Path.Combine(options.Input, "allCountries.txt"));
Console.WriteLine($"postal: {postal} rows");

writer.Finish();

var megabytes = new FileInfo(options.Output).Length / 1024.0 / 1024.0;
Console.WriteLine($"wrote {options.Output} ({megabytes:F1} MB) in {timer.Elapsed.TotalSeconds:F0}s");
return 0;
