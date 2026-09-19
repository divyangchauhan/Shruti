namespace Shruti.Platform.Windows;

public readonly record struct FloatingPillBounds(int X, int Y, int Width, int Height);

public static class FloatingPillPlacement
{
    public static FloatingPillBounds BottomCenter(int left, int top, int right, int bottom,
        double widthDip, double heightDip, uint dpi)
    {
        double scale = dpi == 0 ? 1 : dpi / 96d;
        int width = Math.Min(Math.Max(1, right - left), (int)Math.Round(widthDip * scale));
        int height = Math.Min(Math.Max(1, bottom - top), (int)Math.Round(heightDip * scale));
        return new FloatingPillBounds(left + (right - left - width) / 2,
            Math.Max(top, bottom - height - (int)Math.Round(16 * scale)), width, height);
    }
}
