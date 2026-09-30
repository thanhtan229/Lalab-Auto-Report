using FluentAssertions;
using LalabAutoReport.Core.Services;
using Xunit;

namespace LalabAutoReport.Tests;

public class SizeNormalizerTests
{
    [Theory]
    [InlineData("20x30", "20x30")]
    [InlineData("30x20", "20x30")]
    [InlineData("20 x 30", "20x30")]
    [InlineData("30 x 20", "20x30")]
    [InlineData("20x 30", "20x30")]
    [InlineData("20 x30", "20x30")]
    [InlineData("20X30", "20x30")]
    [InlineData("20 X 30", "20x30")]
    [InlineData("20×30", "20x30")]
    [InlineData("30×20", "20x30")]
    [InlineData("20-30", "20x30")]
    [InlineData("30-20", "20x30")]
    [InlineData("20_30", "20x30")]
    [InlineData("30_20", "20x30")]
    [InlineData("20*30", "20x30")]
    [InlineData("30*20", "20x30")]
    [InlineData("20x30cm", "20x30")]
    [InlineData("30 x 20 cm", "20x30")]
    [InlineData("40x60", "40x60")]
    [InlineData("60x40", "40x60")]
    [InlineData("25x25", "25x25")]
    [InlineData("20x25", "20x25")]
    [InlineData("25x20", "20x25")]
    public void NormalizeSize_ShouldCanonicalizeOrientationAndFormat(string input, string expectedCanonical)
    {
        string actual = SizeNormalizer.NormalizeSize(input);
        actual.Should().Be(expectedCanonical);
    }

    [Theory]
    [InlineData("30x20 ab", "20x30", "ab")]
    [InlineData("ab 30x20", "20x30", "ab")]
    [InlineData("30 X 20 Album", "20x30", "Album")]
    [InlineData("alb_30-20", "20x30", "alb")]
    [InlineData("20×30 alb", "20x30", "alb")]
    [InlineData("30*20 ALB", "20x30", "ALB")]
    [InlineData("20x30alb", "20x30", "alb")]
    [InlineData("alb20x30", "20x30", "alb")]
    [InlineData("ab 20x20", "20x20", "ab")]
    [InlineData("20x20 ab", "20x20", "ab")]
    [InlineData("album 20x20", "20x20", "album")]
    [InlineData("20x20 album", "20x20", "album")]
    [InlineData("alb20x20", "20x20", "alb")]
    [InlineData("20x20alb", "20x20", "alb")]
    [InlineData("13x18 in", "13x18", "in")]
    [InlineData("40x60 TG", "40x60", "TG")]
    public void TryExtractDimensions_ShouldExtractCanonicalSizeAndRemainder(string input, string expectedCanonical, string expectedRemainder)
    {
        bool success = SizeNormalizer.TryExtractDimensions(input, out string canonical, out string remainder, out var match);
        success.Should().BeTrue();
        canonical.Should().Be(expectedCanonical);
        remainder.Should().Be(expectedRemainder);
        match.Should().NotBeNull();
    }

    [Fact]
    public void AreSizesEqual_ShouldRecognizeOrientationInvariance()
    {
        SizeNormalizer.AreSizesEqual("20x30", "30x20").Should().BeTrue();
        SizeNormalizer.AreSizesEqual("60x40", "40x60").Should().BeTrue();
        SizeNormalizer.AreSizesEqual("20 x 30 cm", "30-20").Should().BeTrue();
        SizeNormalizer.AreSizesEqual("20x30", "20x25").Should().BeFalse();
    }
}
