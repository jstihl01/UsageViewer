using System;

namespace UsageViewer.Assets
{
    public static class AppLogoRenderer
    {
        // Sample exact annular sectors so the shared edges remain radial at every size.
        public static byte[] Render(int size)
        {
            if (size < 1 || size > 256) throw new ArgumentOutOfRangeException("size");
            const int samples = 16;
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int covered = 0, red = 0, green = 0, blue = 0;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    double dx = (x + (sx + .5) / samples) * 256 / size - 128;
                    double dy = (y + (sy + .5) / samples) * 256 / size - 128;
                    double radiusSquared = dx * dx + dy * dy;
                    if (radiusSquared < 64 * 64 || radiusSquared >= 112 * 112) continue;
                    double angle = Math.Atan2(dx, -dy);
                    if (angle < 0) angle += Math.PI * 2;
                    int part = Math.Min(2, (int)(angle / (Math.PI * 2 / 3)));
                    covered++;
                    if (part == 0) { red += 255; green += 255; blue += 255; }
                    else if (part == 1) { red += 55; green += 212; blue += 147; }
                    else { red += 133; green += 133; blue += 133; }
                }
                if (covered == 0) continue;
                int offset = (y * size + x) * 4;
                pixels[offset] = (byte)Math.Round((double)blue / covered);
                pixels[offset + 1] = (byte)Math.Round((double)green / covered);
                pixels[offset + 2] = (byte)Math.Round((double)red / covered);
                pixels[offset + 3] = (byte)Math.Round(255.0 * covered / (samples * samples));
            }
            return pixels;
        }
    }
}
