using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class FloatingPillPlacementTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1040, 96, 878, 984, 164, 40)]
    [InlineData(-1920, 0, 0, 1040, 96, -1042, 984, 164, 40)]
    [InlineData(0, 0, 3840, 2080, 192, 1756, 1968, 328, 80)]
    [InlineData(60, 0, 1920, 1080, 96, 908, 1024, 164, 40)]
    public void UsesWorkAreaCenterAndBottomClearance(int left, int top, int right, int bottom,
        uint dpi, int x, int y, int width, int height)
    {
        Assert.Equal(new FloatingPillBounds(x, y, width, height),
            FloatingPillPlacement.BottomCenter(left, top, right, bottom, 164, 40, dpi));
    }

    [Fact]
    public void ExpandingPillKeepsItsBottomAndCenterAnchored()
    {
        var idle = FloatingPillPlacement.BottomCenter(0, 0, 1920, 1040, 48, 12, 144);
        var recording = FloatingPillPlacement.BottomCenter(0, 0, 1920, 1040, 164, 40, 144);
        Assert.Equal(idle.Y + idle.Height, recording.Y + recording.Height);
        Assert.Equal(idle.X + idle.Width / 2, recording.X + recording.Width / 2);
    }
}
