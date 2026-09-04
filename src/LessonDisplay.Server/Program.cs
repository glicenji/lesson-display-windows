using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LessonDisplay.Server;
using LessonDisplay.Server.Mdns;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Configuration — sane defaults so the app "just works" once installed,
// everything overridable via environment variables for advanced setups.
//   LESSONDISPLAY_PORT      default 8420
//   LESSONDISPLAY_DATA_DIR  default %ProgramData%\LessonDisplay\data
//   LESSONDISPLAY_HOSTNAME  default "lessons" -> advertises lessons.local
// ---------------------------------------------------------------------
var port = int.TryParse(Environment.GetEnvironmentVariable("LESSONDISPLAY_PORT"), out var p) ? p : 8420;

var defaultDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "LessonDisplay", "data");
var dataDir = Environment.GetEnvironmentVariable("LESSONDISPLAY_DATA_DIR") is { Length: > 0 } dd ? dd : defaultDataDir;
var dataPath = Path.Combine(dataDir, "lessons.json");

var hostnameAlias = Environment.GetEnvironmentVariable("LESSONDISPLAY_HOSTNAME") is { Length: > 0 } h ? h : "lessons";

builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

var app = builder.Build();

var seedPath = Path.Combine(app.Environment.ContentRootPath, "seed", "lessons.json");
var store = new DataStore(dataPath, File.Exists(seedPath) ? seedPath : null);

var webRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot),
});

// Best-effort friendly-hostname advertising on the LAN (see Mdns/MdnsAnnouncer.cs).
// Always non-fatal: if this never resolves for someone, the tray app's plain
// IP address link still works.
var mdns = new MdnsAnnouncer(new[] { $"{hostnameAlias}.local" }, app.Logger);
mdns.Start();
app.Lifetime.ApplicationStopping.Register(() => mdns.Dispose());

app.Logger.LogInformation("Lesson Display Server starting on port {Port}, data at {DataPath}", port, dataPath);

// -----------------------------------------------------------------
// helpers
// -----------------------------------------------------------------
static async Task<JsonObject> ReadBodyAsync(HttpRequest req)
{
    try
    {
        var node = await req.ReadFromJsonAsync<JsonNode>();
        return node as JsonObject ?? new JsonObject();
    }
    catch
    {
        return new JsonObject();
    }
}

static string? AsString(JsonNode? node) =>
    node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

static int? AsInt(JsonNode? node)
{
    if (node is JsonValue v)
    {
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d) && d == Math.Floor(d)) return (int)d;
    }
    return null;
}

static bool AsBool(JsonNode? node) =>
    node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

static string Slugify(string name)
{
    var slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    return string.IsNullOrEmpty(slug) ? "course" : slug;
}

static IResult BadRequest(string msg) => Results.BadRequest(new { error = msg });
static IResult NotFoundErr(string msg) => Results.NotFound(new { error = msg });

var timeRe = new Regex(@"^([01]\d|2[0-3]):([0-5]\d)$", RegexOptions.Compiled);
var hexColorRe = new Regex(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
var fontNameRe = new Regex(@"^[A-Za-z0-9 ]{1,60}$", RegexOptions.Compiled);

// -----------------------------------------------------------------
// Pages
// -----------------------------------------------------------------
app.MapGet("/", () => Results.File(Path.Combine(webRoot, "admin.html"), "text/html"));
app.MapGet("/admin", () => Results.File(Path.Combine(webRoot, "admin.html"), "text/html"));
app.MapGet("/display/learning-intention", () => Results.File(Path.Combine(webRoot, "display.html"), "text/html"));
app.MapGet("/display/success-criteria", () => Results.File(Path.Combine(webRoot, "display.html"), "text/html"));

// -----------------------------------------------------------------
// Read API
// -----------------------------------------------------------------
app.MapGet("/api/data", async () => Results.Json(await store.GetSnapshotAsync()));

app.MapGet("/api/server-info", () =>
{
    var ip = LocalNetwork.GetPrimaryIPv4()?.ToString() ?? "unknown";
    return Results.Json(new
    {
        ip,
        port,
        hostname_alias = $"{hostnameAlias}.local",
        admin_url = $"http://{ip}:{port}/admin",
        li_url = $"http://{ip}:{port}/display/learning-intention",
        sc_url = $"http://{ip}:{port}/display/success-criteria",
    });
});

// -----------------------------------------------------------------
// Write API
// -----------------------------------------------------------------
app.MapPost("/api/lesson/current", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var courseKey = AsString(body["course_key"]);
    var index = AsInt(body["index"]);
    if (courseKey is null || index is null)
        return BadRequest("course_key (str) and index (int) required");

    return await store.UpdateAsync(data =>
    {
        if (((JsonObject)data["courses"]!)[courseKey] is not JsonObject course)
            return NotFoundErr("no such course");
        var lessons = (JsonArray)course["lessons"]!;
        if (index < 0 || index >= lessons.Count)
            return BadRequest("index out of range");
        course["current_index"] = index;
        return Results.Json(new { ok = true });
    });
});

app.MapPost("/api/lesson/save", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var courseKey = AsString(body["course_key"]);
    var index = AsInt(body["index"]); // null -> new lesson
    var topic = (AsString(body["topic"]) ?? "").Trim();
    var learningIntention = (AsString(body["learning_intention"]) ?? "").Trim();
    var successCriteriaRaw = AsString(body["success_criteria"]) ?? "";
    var makeCurrent = AsBool(body["make_current"]);

    if (courseKey is null) return BadRequest("course_key required");

    var successCriteria = successCriteriaRaw
        .Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0)
        .ToArray();

    return await store.UpdateAsync(data =>
    {
        if (((JsonObject)data["courses"]!)[courseKey] is not JsonObject course)
            return NotFoundErr("no such course");
        var lessons = (JsonArray)course["lessons"]!;

        int newIndex;
        if (index is null)
        {
            var nextNumber = lessons.Count == 0
                ? 1
                : lessons.Max(l => AsInt(((JsonObject)l!)["lesson_number"]) ?? 0) + 1;
            var lesson = new JsonObject
            {
                ["lesson_number"] = nextNumber,
                ["topic"] = topic.Length > 0 ? topic : $"Lesson {nextNumber}",
                ["learning_intention"] = learningIntention,
                ["success_criteria"] = new JsonArray(successCriteria.Select(s => (JsonNode)s).ToArray()),
            };
            lessons.Add(lesson);
            newIndex = lessons.Count - 1;
        }
        else
        {
            if (index < 0 || index >= lessons.Count) return BadRequest("index out of range");
            var lesson = (JsonObject)lessons[index.Value]!;
            lesson["topic"] = topic.Length > 0 ? topic : (AsString(lesson["topic"]) ?? "");
            lesson["learning_intention"] = learningIntention;
            lesson["success_criteria"] = new JsonArray(successCriteria.Select(s => (JsonNode)s).ToArray());
            newIndex = index.Value;
        }

        if (makeCurrent) course["current_index"] = newIndex;
        return Results.Json(new { ok = true, index = newIndex });
    });
});

app.MapPost("/api/lesson/delete", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var courseKey = AsString(body["course_key"]);
    var index = AsInt(body["index"]);
    if (courseKey is null || index is null)
        return BadRequest("course_key (str) and index (int) required");

    return await store.UpdateAsync(data =>
    {
        if (((JsonObject)data["courses"]!)[courseKey] is not JsonObject course)
            return NotFoundErr("no such course");
        var lessons = (JsonArray)course["lessons"]!;
        if (index < 0 || index >= lessons.Count) return BadRequest("index out of range");
        if (lessons.Count == 1) return BadRequest("a course must keep at least one lesson");

        lessons.RemoveAt(index.Value);
        var currentIndex = AsInt(course["current_index"]) ?? 0;
        if (currentIndex >= lessons.Count) course["current_index"] = lessons.Count - 1;
        else if (currentIndex > index) course["current_index"] = currentIndex - 1;
        return Results.Json(new { ok = true });
    });
});

app.MapPost("/api/course/save", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var name = (AsString(body["name"]) ?? "").Trim();
    if (name.Length == 0) return BadRequest("name required");

    return await store.UpdateAsync(data =>
    {
        var courses = (JsonObject)data["courses"]!;
        var key = Slugify(name);
        var baseKey = key;
        var n = 2;
        while (courses.ContainsKey(key)) key = $"{baseKey}-{n++}";

        courses[key] = new JsonObject
        {
            ["name"] = name,
            ["current_index"] = 0,
            ["lessons"] = new JsonArray(new JsonObject
            {
                ["lesson_number"] = 1,
                ["topic"] = "New Lesson",
                ["learning_intention"] = "",
                ["success_criteria"] = new JsonArray(),
            }),
        };
        return Results.Json(new { ok = true, key });
    });
});

app.MapPost("/api/schedule/save", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var name = AsString(body["name"]);
    var periods = body["periods"] as JsonArray;
    if (string.IsNullOrWhiteSpace(name)) return BadRequest("name (str) required");
    if (periods is null) return BadRequest("periods (list) required");

    return await store.UpdateAsync(data =>
    {
        var schedules = (JsonObject)data["schedules"]!;
        if (!schedules.ContainsKey(name)) return NotFoundErr("no such schedule");

        var validCourses = ((JsonObject)data["courses"]!).Select(kv => kv.Key).ToHashSet();
        var cleaned = new JsonArray();
        var i = 0;
        foreach (var pNode in periods)
        {
            var p = pNode as JsonObject ?? new JsonObject();
            var label = (AsString(p["label"]) ?? "").Trim();
            var course = AsString(p["course"]);
            var start = (AsString(p["start"]) ?? "").Trim();
            var end = (AsString(p["end"]) ?? "").Trim();
            i++;
            if (label.Length == 0 || course is null || !validCourses.Contains(course))
                return BadRequest($"invalid period row: {p}");
            if (!timeRe.IsMatch(start) || !timeRe.IsMatch(end))
                return BadRequest($"times must be HH:MM 24-hour, got start='{start}' end='{end}'");
            cleaned.Add(new JsonObject
            {
                ["period"] = AsInt(p["period"]) ?? i,
                ["label"] = label,
                ["course"] = course,
                ["start"] = start,
                ["end"] = end,
            });
        }
        schedules[name] = cleaned;
        return Results.Json(new { ok = true });
    });
});

app.MapPost("/api/schedule/create", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var name = (AsString(body["name"]) ?? "").Trim();
    if (name.Length == 0) return BadRequest("name required");
    var copyFrom = AsString(body["copy_from"]);

    return await store.UpdateAsync(data =>
    {
        var schedules = (JsonObject)data["schedules"]!;
        var key = Slugify(name);
        var baseKey = key;
        var n = 2;
        while (schedules.ContainsKey(key)) key = $"{baseKey}-{n++}";

        var source = (copyFrom is not null && schedules[copyFrom] is JsonArray src)
            ? (JsonArray)src.DeepClone()
            : new JsonArray();
        schedules[key] = source;

        if (data["schedule_labels"] is not JsonObject) data["schedule_labels"] = new JsonObject();
        ((JsonObject)data["schedule_labels"]!)[key] = name;
        return Results.Json(new { ok = true, key });
    });
});

app.MapPost("/api/schedule/rename", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var key = AsString(body["key"]);
    var name = (AsString(body["name"]) ?? "").Trim();
    if (key is null || name.Length == 0) return BadRequest("key and name required");

    return await store.UpdateAsync(data =>
    {
        var schedules = (JsonObject)data["schedules"]!;
        if (!schedules.ContainsKey(key)) return NotFoundErr("no such schedule");
        if (data["schedule_labels"] is not JsonObject) data["schedule_labels"] = new JsonObject();
        ((JsonObject)data["schedule_labels"]!)[key] = name;
        return Results.Json(new { ok = true });
    });
});

app.MapPost("/api/schedule/delete", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var key = AsString(body["key"]);
    if (key is null) return BadRequest("key required");

    return await store.UpdateAsync(data =>
    {
        var schedules = (JsonObject)data["schedules"]!;
        if (!schedules.ContainsKey(key)) return NotFoundErr("no such schedule");
        if (schedules.Count == 1) return BadRequest("must keep at least one schedule");

        schedules.Remove(key);
        (data["schedule_labels"] as JsonObject)?.Remove(key);
        if (AsString(data["active_schedule"]) == key)
            data["active_schedule"] = schedules.Select(kv => kv.Key).First();
        return Results.Json(new { ok = true });
    });
});

app.MapPost("/api/schedule/active", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var key = AsString(body["key"]);
    if (key is null) return BadRequest("key required");

    return await store.UpdateAsync(data =>
    {
        var schedules = (JsonObject)data["schedules"]!;
        if (!schedules.ContainsKey(key)) return NotFoundErr("no such schedule");
        data["active_schedule"] = key;
        return Results.Json(new { ok = true });
    });
});

app.MapPost("/api/style/save", async (HttpRequest req) =>
{
    var body = await ReadBodyAsync(req);
    var screen = AsString(body["screen"]);
    if (screen != "li" && screen != "sc") return BadRequest("screen must be 'li' or 'sc'");

    var fontFamily = (AsString(body["font_family"]) ?? "").Trim();
    if (fontFamily.Length > 0 && !fontNameRe.IsMatch(fontFamily))
        return BadRequest("font_family must be letters/numbers/spaces only, e.g. 'Poppins' or 'Roboto Slab'");

    var fontSize = AsInt(body["font_size"]);
    if (fontSize is null) return BadRequest("font_size must be a whole number");
    if (fontSize < 20 || fontSize > 160) return BadRequest("font_size must be between 20 and 160");

    var colors = new Dictionary<string, string>();
    foreach (var key in new[] { "bg_start", "bg_end", "accent" })
    {
        var val = (AsString(body[key]) ?? "").Trim();
        if (!hexColorRe.IsMatch(val)) return BadRequest($"{key} must be a hex color like #4338CA");
        colors[key] = val;
    }

    return await store.UpdateAsync(data =>
    {
        if (data["display_styles"] is not JsonObject) data["display_styles"] = new JsonObject();
        ((JsonObject)data["display_styles"]!)[screen] = new JsonObject
        {
            ["font_family"] = fontFamily,
            ["font_size"] = fontSize,
            ["bg_start"] = colors["bg_start"],
            ["bg_end"] = colors["bg_end"],
            ["accent"] = colors["accent"],
        };
        return Results.Json(new { ok = true });
    });
});

app.MapPost("/api/restart/trigger", async () =>
{
    // Server and displays run on the same PC in this architecture (unlike
    // the original NAS+separate-mini-PC setup), so there's no need for a
    // poll-and-reboot handshake — just ask Windows to restart directly.
    await store.UpdateAsync(data =>
    {
        data["restart_signal"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return true;
    });

    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/r /t 5 /c \"Restarting to apply Lesson Display changes\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to invoke shutdown /r");
        return Results.Json(new { ok = false, error = "Could not start the restart — try restarting the PC manually." }, statusCode: 500);
    }

    return Results.Json(new { ok = true });
});

app.Run();
