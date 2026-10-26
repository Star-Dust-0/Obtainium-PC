using ObtainiumPC.Models;
using ObtainiumPC.Services.Store;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Data-safety contract: a corrupt apps.json must never be silently
/// overwritten. Load preserves the original (with a timestamped copy), blocks
/// saves for the session, and reports through the log store.
/// </summary>
public class AppStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "obpc-store-" + Guid.NewGuid().ToString("N"));

    public AppStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string StorePath => Path.Combine(_dir, "apps.json");

    [Fact]
    public async Task Corrupt_store_is_preserved_and_saves_are_blocked()
    {
        var original = "{ this is not valid json !!!";
        await File.WriteAllTextAsync(StorePath, original);

        var store = new AppStore(_dir);
        var apps = await store.LoadAsync();

        Assert.Empty(apps);
        Assert.True(store.LastLoadFailed);
        Assert.NotNull(store.LastLoadBackupPath);
        Assert.Equal(original, await File.ReadAllTextAsync(store.LastLoadBackupPath!));

        // A save attempt after a failed load must leave the user's file untouched.
        await store.SaveAsync(new[] { new TrackedApp { Id = "x", Url = "https://github.com/a/b" } });
        Assert.Equal(original, await File.ReadAllTextAsync(StorePath));
    }

    [Fact]
    public async Task Save_is_atomic_and_leaves_no_temp_file()
    {
        var store = new AppStore(_dir);
        var apps = new List<ObtainiumPC.Models.TrackedApp> { new() { Id = "a", Url = "https://x/y", Name = "A", Author = "x" } };
        await store.SaveAsync(apps);
        apps.Add(new() { Id = "b", Url = "https://x/z", Name = "B", Author = "x" });
        await store.SaveAsync(apps);
        Assert.False(File.Exists(Path.Combine(_dir, "apps.json.tmp")));
        var back = await new AppStore(_dir).LoadAsync();
        Assert.Equal(2, back.Count);
        // a stale temp from an interrupted earlier write must not break the next save
        File.WriteAllText(Path.Combine(_dir, "apps.json.tmp"), "{truncated");
        await store.SaveAsync(apps);
        Assert.Equal(2, (await new AppStore(_dir).LoadAsync()).Count);
    }

    [Fact]
    public async Task Valid_store_loads_saves_and_reloads()
    {
        var store = new AppStore(_dir);
        Assert.Empty(await store.LoadAsync()); // missing file: empty, not a failure
        Assert.False(store.LastLoadFailed);

        await store.SaveAsync(new[]
        {
            new TrackedApp
            {
                Id = "seed-obtainium",
                Url = "https://github.com/ImranR98/Obtainium",
                Author = "ImranR98",
                Name = "Obtainium",
            },
        });

        var reloaded = await new AppStore(_dir).LoadAsync();
        var app = Assert.Single(reloaded);
        Assert.Equal("seed-obtainium", app.Id);
        Assert.Equal("https://github.com/ImranR98/Obtainium", app.Url);
    }

    [Fact]
    public async Task Store_recovers_after_the_corrupt_file_is_replaced()
    {
        await File.WriteAllTextAsync(StorePath, "garbage{");
        var store = new AppStore(_dir);
        await store.LoadAsync();
        Assert.True(store.LastLoadFailed);

        // User (or a repair step) replaces the file with valid JSON; the next
        // load clears the failure and saving works again.
        await File.WriteAllTextAsync(StorePath, "[]");
        await store.LoadAsync();
        Assert.False(store.LastLoadFailed);
        await store.SaveAsync(new[] { new TrackedApp { Id = "y" } });
        Assert.Contains("\"y\"", await File.ReadAllTextAsync(StorePath));
    }
}
