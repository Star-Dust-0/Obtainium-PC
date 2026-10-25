using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Store;

/// <summary>
/// lib/components/app_list_tile.dart AppsFilter: the apps-list filter dialog
/// model. Pure and unit-tested; AppsPage renders the dialog and applies it.
/// </summary>
public sealed class AppsFilter
{
    public string NameFilter = "";
    public string AuthorFilter = "";
    public string IdFilter = "";
    public bool IncludeUptodate = true;
    public bool IncludeNonInstalled = true;
    public HashSet<string> CategoryFilter = new();
    public string SourceFilter = "";

    public bool IsDefault =>
        NameFilter.Length == 0 && AuthorFilter.Length == 0 && IdFilter.Length == 0
        && IncludeUptodate && IncludeNonInstalled
        && CategoryFilter.Count == 0 && SourceFilter.Length == 0;

    /// <param name="sourceName">Resolved source name for the app ("" when unknown).</param>
    public bool Matches(TrackedApp app, bool hasUpdate, string sourceName)
    {
        // AppsFilter: every set field narrows the list.
        if (NameFilter.Trim().Length > 0
            && !app.FinalName.Contains(NameFilter.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (AuthorFilter.Trim().Length > 0
            && !app.FinalAuthor.Contains(AuthorFilter.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (IdFilter.Trim().Length > 0
            && !app.Id.Contains(IdFilter.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!IncludeUptodate && !hasUpdate) return false;
        if (!IncludeNonInstalled && app.InstalledVersion == null) return false;
        if (CategoryFilter.Count > 0 && !app.Categories.Any(CategoryFilter.Contains)) return false;
        if (SourceFilter.Length > 0
            && !string.Equals(sourceName, SourceFilter, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
