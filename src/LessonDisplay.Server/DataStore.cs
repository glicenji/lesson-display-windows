using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LessonDisplay.Server;

/// <summary>
/// Everything the lesson data lives in: one JSON file, migrated in place on
/// every load so older data files never lose content, and written with a
/// write-to-temp-then-rename so a crash mid-save can never leave a half
/// written file behind. This is a direct port of the original Flask app's
/// storage layer (app.py: load_data / save_data / migrate_data).
/// </summary>
public sealed class DataStore
{
    private readonly string _dataPath;
    private readonly string? _seedPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DataStore(string dataPath, string? seedPath)
    {
        _dataPath = dataPath;
        _seedPath = seedPath;
        EnsureDataFile();
    }

    private void EnsureDataFile()
    {
        if (File.Exists(_dataPath)) return;
        var dir = Path.GetDirectoryName(_dataPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (_seedPath is not null && File.Exists(_seedPath))
        {
            File.Copy(_seedPath, _dataPath);
        }
        else
        {
            var seed = new JsonObject
            {
                ["courses"] = new JsonObject(),
                ["schedules"] = new JsonObject { ["regular"] = new JsonArray() },
                ["active_schedule"] = "regular",
            };
            File.WriteAllText(_dataPath, seed.ToJsonString(WriteOptions));
        }
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private JsonObject ReadAndMigrate()
    {
        var text = File.ReadAllText(_dataPath);
        var data = (JsonObject?)JsonNode.Parse(text) ?? new JsonObject();
        if (Migrate(data))
        {
            WriteFile(data);
        }
        return data;
    }

    private void WriteFile(JsonObject data)
    {
        var tmpPath = _dataPath + ".tmp";
        File.WriteAllText(tmpPath, data.ToJsonString(WriteOptions));
        File.Move(tmpPath, _dataPath, overwrite: true);
    }

    /// <summary>Read-only snapshot of the current data (used by /api/data).</summary>
    public async Task<JsonObject> GetSnapshotAsync()
    {
        await _lock.WaitAsync();
        try { return ReadAndMigrate(); }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Load, let the caller mutate the tree and compute a result, then
    /// always persist afterwards — mirrors every Flask write endpoint's
    /// "with _lock: data = load_data(); ...; save_data(data)" pattern.
    /// </summary>
    public async Task<T> UpdateAsync<T>(Func<JsonObject, T> mutate)
    {
        await _lock.WaitAsync();
        try
        {
            var data = ReadAndMigrate();
            var result = mutate(data);
            WriteFile(data);
            return result;
        }
        finally { _lock.Release(); }
    }

    // -----------------------------------------------------------------
    // Migration — additive only, never removes or renames a key that
    // already exists. Safe to run on every load.
    // -----------------------------------------------------------------
    private static bool Migrate(JsonObject data)
    {
        var changed = false;

        if (data["schedules"] is not JsonObject)
        {
            var periods = data["periods"] as JsonArray ?? new JsonArray();
            data.Remove("periods");
            data["schedules"] = new JsonObject { ["regular"] = periods.DeepClone() };
            data["active_schedule"] = "regular";
            data["schedule_labels"] = new JsonObject { ["regular"] = "Regular Schedule" };
            changed = true;
        }

        var schedules = (JsonObject)data["schedules"]!;
        var activeSchedule = AsString(data["active_schedule"]);
        if (activeSchedule is null || !schedules.ContainsKey(activeSchedule))
        {
            var first = schedules.Select(kv => kv.Key).FirstOrDefault() ?? "regular";
            data["active_schedule"] = first;
            if (!schedules.ContainsKey(first)) schedules[first] = new JsonArray();
            changed = true;
        }

        if (data["schedule_labels"] is not JsonObject)
        {
            data["schedule_labels"] = new JsonObject();
            changed = true;
        }
        var labels = (JsonObject)data["schedule_labels"]!;
        foreach (var key in schedules.Select(kv => kv.Key).ToList())
        {
            if (!labels.ContainsKey(key))
            {
                labels[key] = TitleCase(key.Replace('-', ' ').Replace('_', ' '));
                changed = true;
            }
        }

        if (data["display_styles"] is not JsonObject)
        {
            data["display_styles"] = new JsonObject();
            changed = true;
        }
        var styles = (JsonObject)data["display_styles"]!;
        foreach (var screen in new[] { "li", "sc" })
        {
            var defaults = DefaultStyle(screen);
            if (styles[screen] is not JsonObject s)
            {
                styles[screen] = defaults;
                changed = true;
            }
            else
            {
                foreach (var kv in defaults)
                {
                    if (!s.ContainsKey(kv.Key))
                    {
                        s[kv.Key] = kv.Value?.DeepClone();
                        changed = true;
                    }
                }
            }
        }

        if (data["restart_signal"] is null)
        {
            data["restart_signal"] = 0;
            changed = true;
        }

        return changed;
    }

    public static JsonObject DefaultStyle(string screen) => screen == "li"
        ? new JsonObject { ["font_family"] = "", ["font_size"] = 96, ["bg_start"] = "#4338CA", ["bg_end"] = "#7C3AED", ["accent"] = "#FDE047" }
        : new JsonObject { ["font_family"] = "", ["font_size"] = 46, ["bg_start"] = "#047857", ["bg_end"] = "#10B981", ["accent"] = "#FCD34D" };

    private static string TitleCase(string s) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    public static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
