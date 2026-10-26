using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Store;

/// <summary>
/// The in-memory tracked-app list. Structural changes and snapshots take one lock
/// that guards only in-memory operations (it is never held across an await), so
/// background workers (update checks, deep-link refresh, saves) get a consistent
/// snapshot while the UI thread adds, removes or replaces entries.
/// </summary>
public sealed class TrackedAppList
{
    private readonly object _gate = new();
    private List<TrackedApp> _items = new();

    /// The live list. UI-thread readers may enumerate it; workers must use Snapshot().
    public List<TrackedApp> Items { get { lock (_gate) return _items; } set { lock (_gate) _items = value; } }

    public List<TrackedApp> Snapshot() { lock (_gate) return _items.ToList(); }

    /// Adds unless the id or url exists; returns the existing entry otherwise.
    public TrackedApp? TryAdd(TrackedApp app)
    {
        lock (_gate)
        {
            var dupe = _items.FirstOrDefault(t => t.Id == app.Id || t.Url == app.Url);
            if (dupe != null) return dupe;
            _items.Add(app);
            return null;
        }
    }

    /// Replaces the entry with the same id or adds it; true when it replaced one.
    public bool Upsert(TrackedApp app)
    {
        lock (_gate)
        {
            var i = _items.FindIndex(x => x.Id == app.Id);
            if (i >= 0) { _items[i] = app; return true; }
            _items.Add(app);
            return false;
        }
    }

    public bool Remove(TrackedApp app) { lock (_gate) return _items.Remove(app); }
}
