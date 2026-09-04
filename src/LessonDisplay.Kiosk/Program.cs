using System.Windows.Forms;

namespace LessonDisplay.Kiosk;

internal static class Program
{
    /// <summary>
    /// This app has no visible main window — it launches the two fullscreen
    /// kiosk browser windows (one per monitor) and lives on in the system
    /// tray so the teacher has somewhere to click "Open Admin Page",
    /// "Copy Admin Link" and "Restart Kiosk Displays" without needing a
    /// keyboard/mouse plugged into the classroom PC.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new KioskContext());
    }
}
