using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace LessonDisplay.Kiosk;

/// <summary>
/// Runs with no visible main form. On startup it waits for the local
/// LessonDisplay.Server to come up, then opens one fullscreen Edge "kiosk
/// mode" window per monitor pointed at the Learning Intention / Success
/// Criteria pages, and leaves a small tray icon behind for control.
/// </summary>
public sealed class KioskContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly List<Process> _launched = new();
    private readonly int _port;

    public KioskContext()
    {
        _port = int.TryParse(Environment.GetEnvironmentVariable("LESSONDISPLAY_PORT"), out var envPort) ? envPort : 8420;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Admin Page", null, (_, _) => OpenAdmin());
        menu.Items.Add("Copy Admin Link", null, (_, _) => _ = CopyAdminLinkAsync());
        menu.Items.Add("Restart Kiosk Displays", null, (_, _) => RestartDisplays());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Kiosk", null, (_, _) => ExitKiosk());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Lesson Display",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenAdmin();

        _ = LaunchDisplaysAsync();
    }

    private async Task LaunchDisplaysAsync()
    {
        await WaitForServerAsync(_port);

        var screens = Screen.AllScreens;
        var liScreen = screens.Length > 0 ? screens[0] : Screen.PrimaryScreen ?? screens[0];
        var scScreen = screens.Length > 1 ? screens[1] : liScreen;

        LaunchKioskWindow("li", $"http://localhost:{_port}/display/learning-intention", liScreen);
        LaunchKioskWindow("sc", $"http://localhost:{_port}/display/success-criteria", scScreen);

        if (screens.Length < 2)
        {
            ShowBalloon("Only one monitor was detected, so both displays opened on it. " +
                        "Connect a second monitor, then use \"Restart Kiosk Displays\" from this tray icon.");
        }
    }

    private static async Task WaitForServerAsync(int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var resp = await http.GetAsync($"http://localhost:{port}/api/data");
                if (resp.IsSuccessStatusCode) return;
            }
            catch
            {
                // server isn't accepting connections yet — normal right after boot
            }
            await Task.Delay(2000);
        }
    }

    private void LaunchKioskWindow(string tag, string url, Screen screen)
    {
        var edge = EdgeLocator.Find();
        if (edge is null)
        {
            MessageBox.Show(
                "Microsoft Edge could not be found on this PC. Lesson Display uses Edge " +
                "(included with Windows 10/11) to show the kiosk screens.",
                "Lesson Display", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LessonDisplay", "browser-profiles", tag);
        Directory.CreateDirectory(profileDir);

        var b = screen.Bounds;
        var args = string.Join(' ', new[]
        {
            $"--kiosk \"{url}\"",
            "--edge-kiosk-type=fullscreen",
            $"--user-data-dir=\"{profileDir}\"",
            $"--window-position={b.X},{b.Y}",
            $"--window-size={b.Width},{b.Height}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-translate",
            "--overscroll-history-navigation=0",
        });

        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = edge,
                Arguments = args,
                UseShellExecute = false,
            });
            if (proc is not null) _launched.Add(proc);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start the {tag} display: {ex.Message}", "Lesson Display",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestartDisplays()
    {
        foreach (var p in _launched)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
        }
        _launched.Clear();
        _ = LaunchDisplaysAsync();
    }

    private void OpenAdmin() =>
        Process.Start(new ProcessStartInfo($"http://localhost:{_port}/admin") { UseShellExecute = true });

    private async Task CopyAdminLinkAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var json = await http.GetStringAsync($"http://localhost:{_port}/api/server-info");
            using var doc = JsonDocument.Parse(json);
            var url = doc.RootElement.GetProperty("admin_url").GetString();
            if (!string.IsNullOrEmpty(url))
            {
                Clipboard.SetText(url);
                ShowBalloon($"Copied: {url}");
            }
            else
            {
                ShowBalloon("Could not read this PC's address.");
            }
        }
        catch
        {
            ShowBalloon("Could not reach the server to look up its address.");
        }
    }

    private void ShowBalloon(string text)
    {
        _tray.BalloonTipTitle = "Lesson Display";
        _tray.BalloonTipText = text;
        _tray.ShowBalloonTip(6000);
    }

    private void ExitKiosk()
    {
        foreach (var p in _launched)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
        }
        _tray.Visible = false;
        Application.Exit();
    }
}
