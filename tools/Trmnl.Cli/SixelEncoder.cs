using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Trmnl.Cli;

internal static class SixelEncoder
{
    public static string Encode(byte[] pngBytes, bool invert = false)
    {
        using var image = Image.Load<Rgba32>(pngBytes);

        int bandCount = (image.Height + 5) / 6;
        var sb = new StringBuilder((image.Width + 2) * bandCount + 64);
        sb.Append("\x1bPq");           // DCS
        sb.Append($"\"1;1;{image.Width};{image.Height}"); // 1:1 pixel aspect ratio
        sb.Append("#0;2;0;0;0");       // color 0 = black
        sb.Append("#1;2;100;100;100"); // color 1 = white

        image.ProcessPixelRows(accessor =>
        {
            for (int bandY = 0; bandY < accessor.Height; bandY += 6)
            {
                sb.Append("#1");

                for (int x = 0; x < accessor.Width; x++)
                {
                    int sixelByte = 0;

                    for (int b = 0; b < 6; b++)
                    {
                        int y = bandY + b;

                        if (y >= accessor.Height)
                            break;

                        var row = accessor.GetRowSpan(y);
                        ref var pixel = ref row[x];
                        double luminance = pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114;
                        bool isLight = luminance > 127;

                        if (isLight != invert)
                            sixelByte |= (1 << b);
                    }

                    sb.Append((char)(63 + sixelByte));
                }

                sb.Append('-');
            }
        });

        sb.Append("\x1b\\"); // ST
        return sb.ToString();
    }
}
