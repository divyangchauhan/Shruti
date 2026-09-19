using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsModifierChordTests
{
    [Theory]
    [InlineData(0xA2, 0x5B)]
    [InlineData(0x5B, 0xA2)]
    [InlineData(0x5C, 0xA3)]
    public void StartsOnSecondModifierAndFinishesAfterBothRelease(uint first, uint second)
    {
        var chord = new WindowsModifierChord(10);
        Assert.False(chord.Update(first, true));
        Assert.False(chord.IsPressed);
        Assert.True(chord.Update(second, true));
        Assert.True(chord.IsPressed);
        Assert.True(chord.Update(second, true)); // Repeat remains suppressed.
        Assert.False(chord.Update(first, false)); // Balance the delivered first keydown.
        Assert.True(chord.IsPressed);
        Assert.True(chord.Update(second, false));
        Assert.False(chord.IsPressed);
        Assert.False(chord.Update(first, true));
        Assert.True(chord.Update(second, true));
        Assert.True(chord.IsPressed);
    }

    [Fact]
    public void ReleasingSecondModifierFirstDoesNotFinishEarly()
    {
        var chord = new WindowsModifierChord(10);
        chord.Update(0xA2, true);
        chord.Update(0x5B, true);
        Assert.True(chord.Update(0x5B, false));
        Assert.True(chord.IsPressed);
        Assert.False(chord.Update(0xA2, false));
        Assert.False(chord.IsPressed);
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0xA0)]
    public void ExtraHeldKeyDoesNotStartDictation(uint extra)
    {
        var chord = new WindowsModifierChord(10);
        chord.Update(extra, true);
        chord.Update(0xA2, true);
        Assert.False(chord.Update(0x5B, true));
        Assert.False(chord.IsPressed);
    }
}
