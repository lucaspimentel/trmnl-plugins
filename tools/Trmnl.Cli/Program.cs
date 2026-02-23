using System.Text.Json;

namespace Trmnl.Cli;

internal static class Program
{
    private static async Task Main()
    {
        var deviceId = Environment.GetEnvironmentVariable("TRMNL_DEVICE_ID");
        var deviceApiKey = Environment.GetEnvironmentVariable("TRMNL_DEVICE_API_KEY");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "https://usetrmnl.com/api/current_screen");
        requestMessage.Headers.Add("ID", deviceId);
        requestMessage.Headers.Add("Access-Token", deviceApiKey);

        using var httpClient = new HttpClient();
        using var responseMessage = await httpClient.SendAsync(requestMessage);
        responseMessage.EnsureSuccessStatusCode();

        var json = await responseMessage.Content.ReadAsStringAsync();
        // Console.WriteLine($"Result: {json}");
        var currentScreen = JsonSerializer.Deserialize<CurrentScreen>(json);
        var imageUrl = currentScreen?.ImageUrl;

        if (string.IsNullOrEmpty(imageUrl))
        {
            Console.WriteLine("No image URL found.");
            return;
        }

        await using var httpStream = await httpClient.GetStreamAsync(imageUrl);
        await using var fileStream = File.Open($"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        await httpStream.CopyToAsync(fileStream);

        fileStream.Position = 0;
        var sixel = SixPix.Sixel.Encode(fileStream);

        Console.WriteLine();
        Console.WriteLine(sixel.ToString());
        Console.WriteLine();
    }
}
