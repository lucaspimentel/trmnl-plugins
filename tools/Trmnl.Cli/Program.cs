using System.CommandLine;
using System.Text.Json;

namespace Trmnl.Cli;

internal static class Program
{
    private enum WaitResult { Refresh, Rerender, Quit }

    private static bool nightMode;
    private static async Task<int> Main(string[] args)
    {
        var deviceIdOption = new Option<string?>("--device-id", "-d")
        {
            Description = "Device ID (falls back to TRMNL_DEVICE_ID env var)"
        };

        var apiKeyOption = new Option<string?>("--api-key", "-k")
        {
            Description = "Device API key (falls back to TRMNL_DEVICE_API_KEY env var)"
        };

        var outputOption = new Option<FileInfo?>("--output", "-o")
        {
            Description = "Save image to file and exit (no display)"
        };

        var refreshOption = new Option<int>("--refresh", "-r")
        {
            Description = "Refresh interval in minutes (display mode only)",
            DefaultValueFactory = _ => 15
        };

        var inputOption = new Option<FileInfo?>("--input", "-i")
        {
            Description = "Display a local image file (no API calls, device ID/key not required)"
        };

        var rootCommand = new RootCommand("TRMNL e-ink display CLI")
        {
            deviceIdOption,
            apiKeyOption,
            outputOption,
            refreshOption,
            inputOption
        };

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var deviceId = parseResult.GetValue(deviceIdOption);
            var apiKey = parseResult.GetValue(apiKeyOption);
            var output = parseResult.GetValue(outputOption);
            var refreshMinutes = parseResult.GetValue(refreshOption);
            var input = parseResult.GetValue(inputOption);

            await Run(deviceId, apiKey, output, input, refreshMinutes, cancellationToken);
        });

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static async Task Run(string? deviceId, string? apiKey, FileInfo? output, FileInfo? input, int refreshMinutes, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<byte[]?>> fetchImage;
        string sourceLabel;

        if (input is not null)
        {
            if (!input.Exists)
            {
                Console.Error.WriteLine($"Error: File not found: {input.FullName}");
                return;
            }

            fetchImage = async ct => await File.ReadAllBytesAsync(input.FullName, ct);
            sourceLabel = input.Name;
        }
        else
        {
            deviceId ??= Environment.GetEnvironmentVariable("TRMNL_DEVICE_ID");
            apiKey ??= Environment.GetEnvironmentVariable("TRMNL_DEVICE_API_KEY");

            var hasErrors = false;

            if (string.IsNullOrEmpty(deviceId))
            {
                Console.Error.WriteLine("Error: Device ID required. Use --device-id or set TRMNL_DEVICE_ID.");
                hasErrors = true;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Console.Error.WriteLine("Error: API key required. Use --api-key or set TRMNL_DEVICE_API_KEY.");
                hasErrors = true;
            }

            if (hasErrors)
                return;

            var httpClient = new HttpClient();
            fetchImage = ct => FetchScreenImage(httpClient, deviceId!, apiKey!, ct);
            sourceLabel = $"Refreshing every {refreshMinutes}m";
        }

        if (output is not null)
        {
            var outputBytes = await fetchImage(cancellationToken);

            if (outputBytes is null)
                return;

            await File.WriteAllBytesAsync(output.FullName, outputBytes, cancellationToken);
            Console.WriteLine($"Saved to {output.FullName}");
            return;
        }

        var refreshInterval = input is not null ? TimeSpan.MaxValue : TimeSpan.FromMinutes(refreshMinutes);

        byte[]? imageBytes = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            imageBytes ??= await fetchImage(cancellationToken);

            if (imageBytes is null)
                return;

            var sixel = SixelEncoder.Encode(imageBytes, invert: nightMode);

            Console.Write("\x1b[2J\x1b[H");
            Console.WriteLine(sixel);
            Console.WriteLine($"[{(nightMode ? "Night" : "Day")} mode | {sourceLabel} — N night mode, R refresh, Q/Esc/Ctrl+C exit]");

            WaitResult result;

            try
            {
                result = await WaitForIntervalOrKeypress(refreshInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result == WaitResult.Quit)
                break;

            if (result == WaitResult.Refresh)
                imageBytes = null; // force re-fetch next iteration

            // Rerender: keep imageBytes, loop re-encodes with new nightMode
        }
    }

    private static async Task<byte[]?> FetchScreenImage(HttpClient httpClient, string deviceId, string apiKey, CancellationToken cancellationToken)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "https://usetrmnl.com/api/current_screen");
        requestMessage.Headers.Add("ID", deviceId);
        requestMessage.Headers.Add("Access-Token", apiKey);

        using var responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken);
        responseMessage.EnsureSuccessStatusCode();

        var json = await responseMessage.Content.ReadAsStringAsync(cancellationToken);
        var currentScreen = JsonSerializer.Deserialize<CurrentScreen>(json);
        var imageUrl = currentScreen?.ImageUrl;

        if (string.IsNullOrEmpty(imageUrl))
        {
            Console.Error.WriteLine("No image URL found.");
            return null;
        }

        return await httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
    }

    private static async Task<WaitResult> WaitForIntervalOrKeypress(TimeSpan interval, CancellationToken cancellationToken)
    {
        var end = interval == TimeSpan.MaxValue ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow + interval;

        while (DateTimeOffset.UtcNow < end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                    return WaitResult.Quit;

                if (key.Key is ConsoleKey.N)
                {
                    nightMode = !nightMode;
                    return WaitResult.Rerender;
                }

                if (key.Key is ConsoleKey.R)
                    return WaitResult.Refresh;
            }

            await Task.Delay(250, cancellationToken);
        }

        return WaitResult.Refresh;
    }
}
