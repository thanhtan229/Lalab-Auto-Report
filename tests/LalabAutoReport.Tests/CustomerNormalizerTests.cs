using FluentAssertions;
using LalabAutoReport.Core.Services;
using Xunit;

namespace LalabAutoReport.Tests;

public class CustomerNormalizerTests
{
    [Theory]
    [InlineData("Văn An", "văn an")]
    [InlineData(" Văn An  ", "văn an")]
    [InlineData("A. An", "a.an")]
    [InlineData("A.An", "a.an")]
    [InlineData("Studio   Minh  ", "studio minh")]
    [InlineData("Chị - Hương", "chị-hương")]
    public void Normalize_ShouldTrim_CollapseSpaces_AndPreserveDiacritics(string input, string expected)
    {
        string actual = CustomerNormalizer.Normalize(input);
        actual.Should().Be(expected);
    }

    [Fact]
    public void RemoveDiacritics_ShouldStripVietnameseAccentsForFuzzySearch()
    {
        string text = "Văn An";
        string stripped = CustomerNormalizer.RemoveDiacritics(text);
        stripped.Should().Be("Van An");

        string dText = "Đặng Đức";
        string strippedD = CustomerNormalizer.RemoveDiacritics(dText);
        strippedD.Should().Be("Dang Duc");
    }

    [Fact]
    public void Similarity_ExactNormalizedMatch_ShouldReturn1()
    {
        double sim = CustomerNormalizer.Similarity("Văn An", "van an");
        sim.Should().Be(1.0);
    }

    [Fact]
    public void Similarity_CloseMatch_ShouldHaveHighScore()
    {
        double sim = CustomerNormalizer.Similarity("Văn An", "Anh An");
        sim.Should().BeGreaterThan(0.5);
    }
}
