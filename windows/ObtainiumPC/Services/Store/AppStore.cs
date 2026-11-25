using System.Text.Json;
using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Store;

/// <summary>
/// Internal persistence plus Obtainium-compatible export/import.
/// Export schema v2: {schemaVersion, exportedAt, appVersion, apps[], settings?},
/// with per-app credential keys ('*-creds') stripped unless secrets are included.
/// Import accepts the schema'd format, the legacy {apps:[...]} wrapper, and a
/// bare list, exactly as upstream does.
/// </summary>
public sealed class AppStore
{
    public const int CurrentExportSchemaVersion = 2;

    private readonly string? _dataDir;

    public AppStore(string? dataDir = null) => _dataDir = dataDir;

    private string FilePath =>
        Path.Combine(_dataDir ?? Settings.AppSettings.DataDir, "apps.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// True when the last load found the store unreadable/corrupt. Saves are
    /// blocked in that state: a user's real apps.json must never be silently
    /// overwritten just because it failed to parse once.
    /// </summary>
    public bool LastLoadFailed { get; private set; }

    /// <summary>Backup copy of the unreadable store, when one could be made.</summary>
    public string? LastLoadBackupPath { get; private set; }

    public int LastSkippedRecords { get; private set; }
    public string? LastSkippedRecordsPath { get; private set; }

    /// Reads each array element on its own. A file that is not an array at all still throws (whole-file corruption path).
    internal static List<TrackedApp> ParseRecords(string json, out List<string> badRecords)
    {
        badRecords = new List<string>();
        var arr = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonArray
                  ?? throw new JsonException("apps.json is not an array");
        var apps = new List<TrackedApp>();
        foreach (var node in arr)
        {
            try { apps.Add(node.Deserialize<TrackedApp>() ?? throw new JsonException("null record")); }
            catch (Exception) { badRecords.Add(node?.ToJsonString() ?? "null"); }
        }
        return apps;
    }

    public async Task<List<TrackedApp>> LoadAsync()
    {
        LastLoadFailed = false;
        LastLoadBackupPath = null;
        LastSkippedRecords = 0; LastSkippedRecordsPath = null;
        try
        {
            if (!File.Exists(FilePath)) return new List<TrackedApp>();
            var json = await File.ReadAllTextAsync(FilePath);
            var loaded = ParseRecords(json, out var badRecords);
            if (badRecords.Count > 0)
            {
                // One malformed record must not take the other apps down. The unreadable records
                // are kept verbatim next to the store before anything is saved.
                var side = FilePath + ".bad-records-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                try { File.WriteAllText(side, "[" + string.Join(",\n", badRecords) + "]"); } catch (Exception ce) { Logging.AppLogger.Error("could not keep the unreadable app records", ce); }
                Logging.AppLogger.Warn($"apps.json: {badRecords.Count} record(s) could not be read and were kept in {side}; the other apps loaded");
                LastSkippedRecords = badRecords.Count; LastSkippedRecordsPath = side;
            }
            foreach (var a in loaded) OpenCredentials(a);
            return loaded;
        }
        catch (Exception e)
        {
            LastLoadFailed = true;
            try
            {
                LastLoadBackupPath = FilePath + ".corrupt-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(FilePath, LastLoadBackupPath, overwrite: false);
            }
            catch (Exception copyError)
            {
                Logging.AppLogger.Error("apps.json is unreadable and its preservation copy failed", copyError);
            }
            Logging.AppLogger.Error("apps.json is unreadable; the original is preserved and store changes will not be saved this session", e);
            return new List<TrackedApp>();
        }
    }

    public async Task SaveAsync(IEnumerable<TrackedApp> apps)
    {
        if (LastLoadFailed)
        {
            Logging.AppLogger.Warn("Skipping save: apps.json failed to load this session, so overwriting it is blocked. " +
                (LastLoadBackupPath != null ? $"Preserved copy: {LastLoadBackupPath}" : "No preservation copy could be made."));
            return;
        }
        // Snapshot first: another thread (update check, deep-link refresh) may add or
        // replace entries while this save serializes.
        var snapshot = apps.ToList();
        // One save at a time: concurrent saves shared the same .tmp file.
        var gateClock = System.Diagnostics.Stopwatch.StartNew();
        await _saveGate.WaitAsync();
        if (gateClock.ElapsedMilliseconds > 500) Logging.AppLogger.Warn($"apps.json save waited {gateClock.ElapsedMilliseconds} ms for another save to finish");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Atomic: write a sibling temp file, then replace. A crash or full disk
            // mid-write leaves the previous apps.json intact instead of a truncated one.
            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, SerializeForDisk(snapshot));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
        finally { _saveGate.Release(); }
    }

    private static bool IsCredentialKey(string k) => k.EndsWith("-creds", StringComparison.Ordinal);

    /// Per-app credentials ("...-creds" values) are encrypted with the Windows user's key on disk only.
    /// In memory and in exports they stay clear. A value this user cannot decrypt is kept as its
    /// original blob and written back unchanged.
    internal static string SerializeForDisk(List<TrackedApp> apps)
    {
        var arr = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(apps, JsonOpts))!.AsArray();
        for (int i = 0; i < apps.Count; i++)
        {
            var add = apps[i].AdditionalSettings;
            if (!add.Keys.Any(IsCredentialKey) && apps[i].LockedSecrets.Count == 0) continue;
            var o = new Dictionary<string, JsonElement>(add);
            foreach (var k in add.Keys.Where(IsCredentialKey).ToList())
                if (add[k].ValueKind == JsonValueKind.String)
                    o[k] = JsonSerializer.SerializeToElement(Settings.SecretProtector.Protect(add[k].GetString()));
            foreach (var kv in apps[i].LockedSecrets)
                if (!o.TryGetValue(kv.Key, out var cur) || cur.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(cur.GetString()))
                    o[kv.Key] = JsonSerializer.SerializeToElement(kv.Value);
            arr[i]!["additionalSettings"] = JsonSerializer.Serialize(o);
        }
        return arr.ToJsonString(JsonOpts);
    }

    internal static void OpenCredentials(TrackedApp app)
    {
        var add = app.AdditionalSettings;
        var changed = false;
        foreach (var k in add.Keys.Where(IsCredentialKey).ToList())
        {
            if (add[k].ValueKind != JsonValueKind.String) continue;
            var stored = add[k].GetString();
            if (Settings.SecretProtector.TryUnprotect(stored, out var plain))
            { if (!ReferenceEquals(plain, stored)) { add[k] = JsonSerializer.SerializeToElement(plain); changed = true; } }
            else { app.LockedSecrets[k] = stored!; add.Remove(k); changed = true; }
        }
        if (changed) app.AdditionalSettings = add;
    }

    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private static readonly object FileWriteLock = new();

    /// True when both paths name the same file (full-path, case-insensitive like NTFS).
    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>
    /// Writes text to a user-chosen path without ever leaving it truncated: the text goes to a
    /// temp file in the same directory (same volume, so the replace is atomic), then replaces the
    /// target. A failed write keeps the previous file and removes the temp file. Overlapping
    /// callers (a debounced auto-export racing another) are serialized.
    /// </summary>
    public static void WriteFileAtomic(string target, string text)
    {
        lock (FileWriteLock)
        {
            var tmp = target + ".tmp";
            try
            {
                File.WriteAllText(tmp, text);
                if (File.Exists(target)) File.Replace(tmp, target, null);
                else File.Move(tmp, target);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }
    }

    /// <summary>
    /// Builds an Obtainium export document. exportLevel: 0 = apps only, 1 = with
    /// settings (credentials stripped), 2 = with settings incl. credentials.
    /// </summary>
    public static string Export(IEnumerable<TrackedApp> apps, Dictionary<string, object?>? settings, int exportLevel, string appVersion, bool complete = false)
    {
        // Credentials (per-app "-creds" values and the *PersonalAccessToken settings) are stripped below level 2.
        // complete: true is the explicit opt-in that forces level 2; callers pass it only when the user turned
        // on "Include credentials in exports".
        if (complete) exportLevel = 2;
        var appList = new List<Dictionary<string, JsonElement>>();
        foreach (var app in apps)
        {
            var json = JsonSerializer.SerializeToElement(app).Deserialize<Dictionary<string, JsonElement>>()!;
            if (exportLevel < 2)
            {
                var additional = app.AdditionalSettings;
                foreach (var key in additional.Keys.Where(k => k.EndsWith("-creds")).ToList())
                    additional.Remove(key);
                json["additionalSettings"] = JsonSerializer.SerializeToElement(JsonSerializer.Serialize(additional));
            }
            appList.Add(json);
        }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentExportSchemaVersion);
            writer.WriteString("exportedAt", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"));
            writer.WriteString("appVersion", appVersion);
            writer.WritePropertyName("apps");
            writer.WriteStartArray();
            foreach (var a in appList) JsonSerializer.Serialize(writer, a);
            writer.WriteEndArray();
            if (exportLevel > 0 && settings != null)
            {
                writer.WritePropertyName("settings");
                writer.WriteStartObject();
                foreach (var kv in settings)
                {
                    // Upstream strips settings keys ending in "-creds"
                    // (apps_provider_import_export.dart); the port's typed
                    // settings carry secrets as *PersonalAccessToken
                    // properties, so strip that suffix too - otherwise the
                    // GitLab/Forgejo PATs leak into "no credentials" exports.
                    if (exportLevel < 2 && (kv.Key.EndsWith("-creds") || kv.Key.EndsWith("PersonalAccessToken"))) continue;
                    writer.WritePropertyName(kv.Key);
                    JsonSerializer.Serialize(writer, kv.Value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Parses an import payload. Returns the apps and whether settings were
    /// present. Throws ObtainiumImportException on undecodable payloads.
    /// </summary>
    public static (List<TrackedApp> Apps, bool HasSettings, Dictionary<string, JsonElement>? Settings) Import(string appsJson)
    {
        JsonElement decoded;
        try { decoded = JsonSerializer.Deserialize<JsonElement>(appsJson); }
        catch (Exception e) { throw new ObtainiumImportException("Failed to import: " + e.Message); }

        var hasSchemaVersion = decoded.ValueKind == JsonValueKind.Object && decoded.TryGetProperty("schemaVersion", out _);
        List<TrackedApp> imported;
        Dictionary<string, JsonElement>? settings = null;
        try
        {
            JsonElement appsElement;
            if (decoded.ValueKind == JsonValueKind.Array)
            {
                appsElement = decoded;
            }
            else
            {
                appsElement = decoded.GetProperty("apps");
                if (decoded.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object)
                    settings = s.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            }
            imported = appsElement.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Object ? e.Deserialize<TrackedApp>() : null)
                .Where(a => a != null).Select(a => a!)
                .ToList();
        }
        catch (Exception e) { throw new ObtainiumImportException("Failed to import: " + e.Message); }
        return (imported, settings != null, settings);
    }
}

public sealed class ObtainiumImportException : Exception
{
    public ObtainiumImportException(string message) : base(message) { }
}
