using ObtainiumPC.Services.Localization;
using Xunit;

namespace ObtainiumPC.Tests;

public class ArabicOverlayTests
{
    [Theory]
    [InlineData("Export complete")]
    [InlineData("Import failed")]
    [InlineData("Enter a source URL first.")]
    [InlineData("Update available: {0} {1}")]
    public void Previously_untranslated_messages_have_an_Arabic_text(string en)
    {
        var ar = Loc.TranslateAs(en, "ar");
        Assert.NotEqual(en, ar);
        Assert.Equal(Loc.TranslateAs(en, "en"), en);
    }
}
