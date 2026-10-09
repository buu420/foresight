using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class RenderedCharacterCardSpeechTests
{
    [Fact]
    public void ANameThatLooksLikeAStatIsPreservedAndMpZeroIsNotAKnockout()
        => Assert.Equal("HP 0. HP 1/80. MP 0/10",
            RenderedCharacterCardSpeech.Format(["HP 0", ":", "HP", "1/", "80", ":", "MP", "0/", "10"]));

    [Fact]
    public void AReserveCardWithoutANameCanStillDescribeVisibleKnockout()
        => Assert.Equal("Knocked out. MP 5/10",
            RenderedCharacterCardSpeech.Format(["HP", "0/", "80", ":", "MP", "5/", "10"], renderedName: false));

    [Theory]
    [InlineData("---")][InlineData("0x")][InlineData("0/0")][InlineData("0/---")]
    public void UnreadableOrPlaceholderHpDoesNotInventAKnockout(string value)
        => Assert.False(RenderedCharacterCardSpeech.IsKnockedOut("HP", [value]));
}
