using System.Text.Json;
using ObtainiumPC.Services.Settings;
using Xunit;

namespace ObtainiumPC.Tests;

public class TrayDefaultTests
{
    [Fact]
    public void New_settings_have_tray_on() => Assert.True(new AppSettings().TrayIcon);

    [Fact]
    public void Missing_key_in_settings_json_means_on() =>
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{\"FirstRunHandled\":true}")!.TrayIcon);

    [Fact]
    public void Explicit_false_is_kept() =>
        Assert.False(JsonSerializer.Deserialize<AppSettings>("{\"TrayIcon\":false}")!.TrayIcon);

    [Fact]
    public void Explicit_true_is_kept() =>
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{\"TrayIcon\":true}")!.TrayIcon);
}
