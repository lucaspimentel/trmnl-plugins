using System.Globalization;

namespace TrmnlApi.GeoDataBuilder;

public sealed record BuilderOptions(string Input, string Output, double SimplifyTolerance)
{
    /// <summary>
    /// Douglas-Peucker tolerance in degrees. 0.01 is about 1.1 km, which is finer than any
    /// decision made from these polygons: the point being tested has already been snapped to a
    /// 0.01-degree grid, and the answer is a state name.
    /// </summary>
    private const double DefaultSimplifyTolerance = 0.01;

    public static BuilderOptions? Parse(string[] args)
    {
        string? input = null;
        var output = "geo.sqlite";
        var tolerance = DefaultSimplifyTolerance;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" when i + 1 < args.Length:
                    input = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--simplify" when i + 1 < args.Length:
                    tolerance = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                default:
                    Console.Error.WriteLine($"Unrecognized argument '{args[i]}'.");
                    return null;
            }
        }

        if (input is null)
        {
            Console.Error.WriteLine("Usage: GeoDataBuilder --input <dir> [--output geo.sqlite] [--simplify 0.01]");
            return null;
        }

        if (!Directory.Exists(input))
        {
            Console.Error.WriteLine($"Input directory '{input}' does not exist.");
            return null;
        }

        if (File.Exists(output))
        {
            File.Delete(output);
        }

        return new BuilderOptions(input, output, tolerance);
    }
}
