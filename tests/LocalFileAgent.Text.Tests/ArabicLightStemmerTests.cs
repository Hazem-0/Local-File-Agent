using FluentAssertions;
using Xunit;

namespace LocalFileAgent.Text.Tests;

public class ArabicLightStemmerTests
{
    private readonly ArabicLightStemmer _stemmer = new();

    [Theory]
    // Prefix stripping: ال, وال, بال, كال, فال, لل
    [InlineData("الكتاب", "كتاب")]
    [InlineData("والكتاب", "كتاب")]
    [InlineData("بالقاهرة", "قاهرة")]
    [InlineData("كالماء", "ماء")]
    [InlineData("فالنتيجة", "نتيجة")]
    [InlineData("للشركة", "شركة")]
    // Leading waw under length guard (>= 4)
    [InlineData("وسيارة", "سيارة")]
    // Suffix stripping: ها, ان, ات, ون, ين, يه, ية, ه, ي
    [InlineData("كتابها", "كتاب")]
    [InlineData("مهندسان", "مهندس")]
    [InlineData("شركات", "شرك")]
    [InlineData("معلمون", "معلم")]
    [InlineData("معلمين", "معلم")]
    [InlineData("كتابه", "كتاب")]
    [InlineData("كتابي", "كتاب")]
    // Combined prefix and suffix
    [InlineData("والمهندسين", "مهندس")]
    [InlineData("للشركات", "شرك")]
    // Short word protection (words <= 3 letters should not be aggressively stripped)
    [InlineData("ولد", "ولد")] // 'و' not stripped because length is 3
    [InlineData("عن", "عن")]
    [InlineData("في", "في")]
    [InlineData("يد", "يد")]
    public void Stem_ProducesExpectedStem(string normalizedToken, string expectedStem)
    {
        var stem = _stemmer.Stem(normalizedToken);
        stem.Should().Be(expectedStem);
    }
}
