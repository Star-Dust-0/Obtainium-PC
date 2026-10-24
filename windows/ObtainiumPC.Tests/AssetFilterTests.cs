using ObtainiumPC.Sources;
using Xunit;

namespace ObtainiumPC.Tests;

public class AssetFilterTests
{
    [Theory]
    [InlineData("app-x86_64.msix", true)]
    [InlineData("app-setup.exe", true)]
    [InlineData("app.zip", false)]
    [InlineData("app.zip", true, true)]
    [InlineData("app.tar.gz", true, false, true)]
    [InlineData("checksums.txt", false)]
    public void Container_classification(string name, bool expected, bool includeArchives = false, bool includeTarballs = false)
    {
        Assert.Equal(expected, AssetFilterService.IsAppOrContainerFile(name, includeArchives, includeTarballs));
    }
}
