# Lesson Display for Windows

A from-scratch C#/.NET rewrite of the Lesson Display project (originally
Python/Flask running in Docker on a NAS), packaged as a normal Windows
installer so any teacher can put it on a mini PC with two monitors, with
zero command line work.

## How it's different from the original NAS version

The original setup had two machines: a NAS running the server, and a
separate mini PC driving two monitors that talked to the NAS over the
network. This rewrite is designed for **one machine per classroom**: the
same mini PC runs the server *and* drives both monitors. That's simpler to
install and simpler to explain to a non-technical teacher, and it means:

- The kiosk displays point at `http://localhost:8420/...` — they never
  depend on the network being up.
- "Restart" in the Admin page restarts that PC directly (no NAS-side
  polling handshake needed, since there's no second machine to reach).
- Other teachers' own computers reach the Admin page over the LAN at
  `http://<that PC's IP>:8420/admin`. The server also broadcasts a friendly
  `http://lessons.local:8420/admin` name best-effort (see **Limitations**
  below) — the Links tab in Admin always shows the plain IP address too, so
  there's always a way in even if the friendly name doesn't resolve on a
  given network.

## Two apps, one installer

- **LessonDisplay.Server** — the web server (courses, lessons, bell
  schedule, display styling, JSON API). Installed to run as a background
  process that starts at boot, before anyone logs in.
- **LessonDisplay.Kiosk** — on logon, waits for the server to come up, then
  opens Microsoft Edge in fullscreen kiosk mode on each monitor (Learning
  Intention on the first, Success Criteria on the second), and leaves a
  small system tray icon behind with "Open Admin Page", "Copy Admin Link",
  "Restart Kiosk Displays", and "Exit Kiosk".

Both are plain .NET apps with **no third-party NuGet packages** — the
server uses only ASP.NET Core and the JSON types built into .NET; the kiosk
app uses only WinForms. That was a deliberate choice so the build stays
reliable on a machine that has nothing but the .NET SDK installed.

## Getting the installer

You don't need Visual Studio or even a Windows PC to produce
`LessonDisplaySetup.exe` — GitHub will build it for you for free:

1. Create a new GitHub repository and push this folder to it.
2. Open the repo's **Actions** tab. The "Build Windows Installer" workflow
   runs automatically on push (or click "Run workflow" to trigger it by
   hand).
3. When it finishes (a few minutes), open the run and download the
   **LessonDisplaySetup** artifact — that's `LessonDisplaySetup.exe`, ready
   to hand to any teacher.

If you'd rather build locally on a Windows PC that already has the [.NET
SDK](https://dotnet.microsoft.com/download) and [Inno
Setup](https://jrsoftware.org/isdl.php) installed, open PowerShell in this
folder and run `.\build.ps1` — it does the same two steps (publish, then
compile the installer) and leaves the result at
`installer\Output\LessonDisplaySetup.exe`.

## Installing it (what a teacher does)

1. Run `LessonDisplaySetup.exe` on the classroom mini PC (needs
   administrator rights, once, for the install).
2. It registers the server to start at boot and the kiosk launcher to start
   at logon, opens the firewall for port 8420, and offers to open the Admin
   page immediately.
3. Plug in both monitors before starting the PC (or use the tray icon's
   "Restart Kiosk Displays" after connecting the second one).
4. From the tray icon, "Copy Admin Link" gives the address other teachers'
   computers can use — or open the Links tab inside Admin itself.

Uninstalling removes the app and the scheduled tasks/firewall rule, but
**deliberately leaves lesson content in place**
(`%ProgramData%\LessonDisplay\data\lessons.json`) so a reinstall or upgrade
never loses anyone's lessons.

## Configuration

Everything has a sensible default; these environment variables (set them
system-wide via System Properties → Environment Variables, then restart the
scheduled tasks) override them:

| Variable                   | Default                                    | Meaning                          |
|-----------------------------|---------------------------------------------|-----------------------------------|
| `LESSONDISPLAY_PORT`       | `8420`                                      | Port the web server listens on   |
| `LESSONDISPLAY_DATA_DIR`   | `%ProgramData%\LessonDisplay\data`          | Where `lessons.json` lives       |
| `LESSONDISPLAY_HOSTNAME`   | `lessons`                                   | Advertises `<value>.local`       |

## What's been tested vs. not

This code was written and packaged in a Linux sandbox with no access to a
real Windows machine, so here's the honest state of it:

- **LessonDisplay.Server** — fully built and exercised here: every API
  endpoint (lessons, courses, schedules, display styles, validation
  failures) was run against a live instance and checked against the
  original Python app's behavior line-by-line. This part is solid.
- **LessonDisplay.Kiosk** (the WinForms tray/kiosk launcher) — written
  carefully but **could not be compiled or run in this environment**
  (WinForms needs the Windows Desktop runtime, which only exists on
  Windows). Its first real build and test will be the GitHub Actions run or
  your own machine. If Edge kiosk windows don't land on the right monitors
  on the first try, the likely fix is in `KioskContext.cs`'s
  `LaunchKioskWindow` (the `--window-position`/`--window-size` arguments).
- **The installer script** (Inno Setup) — written following Inno Setup's
  documented syntax but not run through the actual compiler, since that
  also only runs on Windows.

Worth a real-world test pass on actual hardware before handing this to
other teachers, particularly: two-monitor kiosk positioning, the Scheduled
Task launching before/at logon as expected, and the firewall rule actually
letting another computer in.

## Limitations

- **mDNS (`lessons.local`) is best-effort.** It's a small hand-written
  broadcaster (see `Mdns/MdnsAnnouncer.cs`) with no external dependency, but
  many school Wi-Fi networks block multicast between devices, and older
  Windows PCs without Bonjour/iTunes installed have no `.local` resolver at
  all — the exact issue you ran into with the NAS version. The plain IP
  address shown in Admin → Links always works regardless.
- **Requires Microsoft Edge** (ships with Windows 10/11) for the kiosk
  windows — there's no bundled browser engine.
- Seed data (`src/LessonDisplay.Server/seed/lessons.json`) is a generic
  "Sample Course" placeholder for distributing to other teachers, not your
  own Cybersecurity/CSA content — every fresh install starts blank.
