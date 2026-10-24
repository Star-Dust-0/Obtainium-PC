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

    public async Task<List<TrackedApp>> LoadAsync()
    {
        LastLoadFailed = false;
        LastLoadBackupPath = null;
        try
        {
            if (!File.Exists(FilePath)) return new List<TrackedApp>();
            var json = await File.ReadAllTextAsync(FilePath);
            return JsonSerializer.Deserialize<List<TrackedApp>>(json) ?? new List<TrackedApp>();
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
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(apps, JsonOpts));
    }

    /// <summary>
    /// Builds an Obtainium export document. exportLevel: 0 = apps only, 1 = with
    /// settings (credentials stripped), 2 = with settings incl. credentials.
    /// </summary>
    public static string Export(IEnumerable<TrackedApp> apps, Dictionary<string, object?>? settings, int exportLevel, string appVersion)
    {
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
                    if (exportLevel < 2 && kv.Key.EndsWith("-creds")) continue;
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
                .Select(e => e.Deserialize<TrackedApp>() ?? new TrackedApp())
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
