using System.Reflection;
using System.Runtime.InteropServices;
using UsageViewer;
using Drawing = System.Drawing;

internal static class WindowsChecks
{
    [STAThread]
    private static void Main(string[] args)
    {
        var now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
        var example = new Limits(new Cycle(.65, 300, now.AddHours(3)),
            new Cycle(.25, 10080, now.AddDays(2.8)));
        var margin = new Limits(new Cycle(.25, 300, now.AddHours(2)), example.Weekly);
        if (args.Length > 0)
        {
            SavePreview(args[0], example, now);
            SavePreview(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0])!,
                "tray-ring-margin-preview.png"), margin, now);
        }
        int[] sizes = [16, 20, 24, 32, 48];
        foreach (int size in sizes)
        {
          foreach (var limits in new[] { example, margin })
          {
            using var icon = Draw(limits, now, size);
            using var bitmap = icon.ToBitmap();
            Require(bitmap.Width == size && bitmap.Height == size, "native icon matches the requested DPI size");
            Require(bitmap.GetPixel(0, 0).A == 0, "transparent corners");
            Require(bitmap.GetPixel(size / 2, size / 2).ToArgb() == Drawing.Color.FromArgb(37, 37, 37).ToArgb(),
                "opaque center separates the icon from either Windows theme");

            int redPixels = 0;
            int greenPixels = 0;
            int whitePixels = 0;
            int grayPixels = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    double radius = Math.Sqrt(Math.Pow((x + .5) * 16 / size - 8, 2) +
                        Math.Pow((y + .5) * 16 / size - 8, 2));
                    if (pixel.R > 180 && pixel.R > pixel.G * 1.5)
                    {
                        redPixels++;
                        Require(radius > 3 && radius < 7.8, "overlap stays on the single thick ring");
                    }
                    if (pixel.G > 130 && pixel.G > pixel.R * 1.5)
                    {
                        greenPixels++;
                        Require(radius > 3 && radius < 7.8, "margin stays on the single thick ring");
                    }
                    if (pixel.R > 230 && pixel.G > 230 && pixel.B > 230) whitePixels++;
                    if (pixel.ToArgb() == Drawing.Color.FromArgb(133, 133, 133).ToArgb()) grayPixels++;
                }
            }
            Require(whitePixels > 0 && grayPixels > 0,
                "consumed and remaining quota stay visible even at the smallest size");
            Require(limits == example ? redPixels > 0 && greenPixels == 0 : greenPixels > 0 && redPixels == 0,
                "the icon shows only 5-hour overlap or margin, independent of the weekly quota");
          }
        }

        using var missing = Draw(Limits.Empty, now, 32);
        using var expired = Draw(new Limits(new Cycle(.5, 300, now), new Cycle(.5, 10080, now)), now, 32);
        using var weeklyOnly = Draw(new Limits(null, example.Weekly), now, 32);
        using var missingBitmap = missing.ToBitmap();
        using var expiredBitmap = expired.ToBitmap();
        using var weeklyOnlyBitmap = weeklyOnly.ToBitmap();
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                var pixel = missingBitmap.GetPixel(x, y);
                Require(pixel.R == pixel.G && pixel.G == pixel.B, "missing quota stays neutral");
                Require(pixel == expiredBitmap.GetPixel(x, y), "expired quota is not shown as current");
                Require(pixel == weeklyOnlyBitmap.GetPixel(x, y), "weekly data does not populate the 5-hour ring");
            }
        }

        var background = Drawing.Color.FromArgb(37, 37, 37);
        Require(Contrast(Drawing.Color.FromArgb(133, 133, 133), background) >= 3, "remaining track contrasts with its background");
        Require(Contrast(Drawing.Color.FromArgb(160, 160, 160), Drawing.Color.FromArgb(32, 32, 32)) >= 3,
            "outline contrasts with a dark taskbar");
        Require(Contrast(background, Drawing.Color.FromArgb(243, 243, 243)) >= 3,
            "background contrasts with a light taskbar");

        var data = typeof(TrayIndicator).GetNestedType("IconData", BindingFlags.NonPublic)!;
        var identifier = typeof(TrayIndicator).GetNestedType("IconIdentifier", BindingFlags.NonPublic)!;
        Require(Marshal.SizeOf(data) == (IntPtr.Size == 8 ? 976 : 956), "notification structure matches Windows");
        Require(Marshal.SizeOf(identifier) == (IntPtr.Size == 8 ? 40 : 28), "icon identifier matches Windows");
        Console.WriteLine("OK: single thick 5-hour tray ring at 16/20/24/32/48 px, quota colors, theme contrast, missing/expired quota and notification structures.");
    }

    private static Drawing.Icon Draw(Limits limits, DateTimeOffset now, int size)
    {
        var draw = typeof(TrayIndicator).GetMethod("DrawIcon", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Drawing.Icon)draw.Invoke(null, [limits, now, size])!;
    }

    private static double Contrast(Drawing.Color first, Drawing.Color second)
    {
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminance(Drawing.Color color) =>
        .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }

    private static void SavePreview(string path, Limits limits, DateTimeOffset now)
    {
        using var sheet = new Drawing.Bitmap(800, 360);
        using var graphics = Drawing.Graphics.FromImage(sheet);
        using var font = new Drawing.Font("Segoe UI", 11);
        graphics.Clear(Drawing.Color.White);
        graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.Half;
        int[] sizes = [16, 20, 24, 32];
        for (int theme = 0; theme < 2; theme++)
        {
            using var background = new Drawing.SolidBrush(theme == 0 ?
                Drawing.Color.FromArgb(243, 243, 243) : Drawing.Color.FromArgb(32, 32, 32));
            using var text = new Drawing.SolidBrush(theme == 0 ? Drawing.Color.Black : Drawing.Color.White);
            graphics.FillRectangle(background, 0, theme * 180, 800, 180);
            for (int column = 0; column < sizes.Length; column++)
            {
                int size = sizes[column];
                using var icon = Draw(limits, now, size);
                using var bitmap = icon.ToBitmap();
                int left = column * 200 + 14;
                int top = theme * 180;
                graphics.DrawString($"{size} px", font, text, left, top + 12);
                graphics.DrawImage(bitmap, left, top + 80, size, size);
                graphics.DrawImage(bitmap, left + 52, top + 44, size * 4, size * 4);
            }
        }
        sheet.Save(path, Drawing.Imaging.ImageFormat.Png);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
