using ObtainiumPC.Models;
using ObtainiumPC.Services.Store;
using Xunit;

namespace ObtainiumPC.Tests;

/// Overlapping add / upsert / remove / snapshot / save must lose nothing and never throw.
public class TrackedAppListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obpc-list-" + Guid.NewGuid().ToString("N"));
    public TrackedAppListTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static TrackedApp A(int i, string v = "1") => new() { Id = "id" + i, Url = "https://github.com/a/r" + i, InstalledVersion = v };

    [Fact]
    public async Task Concurrent_add_upsert_remove_snapshot_and_save_lose_nothing()
    {
        var list = new TrackedAppList();
        var store = new AppStore(_dir);
        for (var i = 0; i < 50; i++) Assert.Null(list.TryAdd(A(i)));

        var errors = new List<Exception>();
        void Guard(Action a) { try { a(); } catch (Exception e) { lock (errors) errors.Add(e); } }

        var tasks = new List<Task>();
        // writers: add new apps 100..199
        tasks.Add(Task.Run(() => Guard(() => { for (var i = 100; i < 200; i++) list.TryAdd(A(i)); })));
        // upsert (replace) existing apps 0..39 with a new installed version, installed bookkeeping must survive
        tasks.Add(Task.Run(() => Guard(() => { for (var i = 0; i < 40; i++) Assert.True(list.Upsert(A(i, "2"))); })));
        // remove 40..49 only
        tasks.Add(Task.Run(() => Guard(() => { for (var i = 40; i < 50; i++) list.Remove(list.Snapshot().First(x => x.Id == "id" + i)); })));
        // readers + saves in parallel
        for (var k = 0; k < 4; k++)
            tasks.Add(Task.Run(async () =>
            {
                try { for (var n = 0; n < 15; n++) { _ = list.Snapshot().Count; await store.SaveAsync(list.Snapshot()); } }
                catch (Exception e) { lock (errors) errors.Add(e); }
            }));
        await Task.WhenAll(tasks);

        Assert.Empty(errors);
        var final = list.Snapshot();
        // 0..39 kept (replaced), 40..49 removed, 100..199 added
        Assert.Equal(40 + 100, final.Count);
        Assert.All(final.Where(a => int.Parse(a.Id[2..]) < 40), a => Assert.Equal("2", a.InstalledVersion));
        Assert.DoesNotContain(final, a => int.Parse(a.Id[2..]) is >= 40 and < 50);

        // persisted completeness: one last save reloads to exactly the same ids
        await store.SaveAsync(list.Snapshot());
        var loaded = await new AppStore(_dir).LoadAsync();
        Assert.Equal(final.Select(a => a.Id).OrderBy(x => x), loaded.Select(a => a.Id).OrderBy(x => x));
        Assert.Equal("2", loaded.First(a => a.Id == "id0").InstalledVersion);
    }

    [Fact]
    public void TryAdd_rejects_duplicate_id_or_url_and_returns_existing()
    {
        var list = new TrackedAppList();
        Assert.Null(list.TryAdd(A(1)));
        Assert.Equal("id1", list.TryAdd(new TrackedApp { Id = "other", Url = "https://github.com/a/r1" })!.Id);
        Assert.Equal("id1", list.TryAdd(new TrackedApp { Id = "id1", Url = "https://x" })!.Id);
        Assert.Single(list.Snapshot());
    }
}
