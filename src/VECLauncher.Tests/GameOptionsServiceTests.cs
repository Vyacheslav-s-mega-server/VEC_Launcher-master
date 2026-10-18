using VECLauncher.Services;

namespace VECLauncher.Tests;

public class GameOptionsServiceTests
{
    [Theory]
    [InlineData("1.20.4", "ru", "ru_ru")]
    [InlineData("1.18.2", "ru", "ru_ru")]
    [InlineData("1.11", "ru", "ru_ru")]
    [InlineData("1.10", "ru", "ru_RU")]
    [InlineData("1.7.10", "ru", "ru_RU")]
    [InlineData("1.20.4", "en", "en_us")]
    [InlineData("1.10", "en", "en_US")]
    [InlineData("1.20.4", "de", "ru_ru")]
    public void LanguageCodeFor_ReturnsCorrectCode(string mcVersion, string lang, string expected)
    {
        var result = GameOptionsService.LanguageCodeFor(mcVersion, lang);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void LanguageCodeFor_DefaultsToRussian()
    {
        var result = GameOptionsService.LanguageCodeFor("1.20.4");
        Assert.Equal("ru_ru", result);
    }

    [Theory]
    [InlineData("1.20.4", true)]
    [InlineData("1.10", false)]
    [InlineData("1.9.4", false)]
    public void LanguageCodeFor_OldVersionsUseUppercase(string mcVersion, bool modern)
    {
        var result = GameOptionsService.LanguageCodeFor(mcVersion, "ru");
        if (modern)
            Assert.Equal("ru_ru", result);
        else
            Assert.Equal("ru_RU", result);
    }
}
