# ClassSync for Windows

A from-scratch C#/.NET rewrite of the original "Lesson Display" project
(Python/Flask running in Docker on a NAS), now named **ClassSync** and
packaged as a normal Windows installer so any teacher can put it on a mini
PC with two monitors, with zero command line work.

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

- **ClassSync.Server.exe** (source: `src/LessonDisplay.Server/`) — the web
  server (courses, lessons, bell schedule, display styling, JSON API).
  Installed to run as a background process that starts at boot, before
  anyone logs in.
- **ClassSync.Kiosk.exe** (source: `src/LessonDisplay.Kiosk/`) — on logon,
  waits for the server to come up, then opens Microsoft Edge in fullscreen
  kiosk mode on each monitor (Learning Intention on the first, Success
  Criteria on the second), and leaves a small system tray icon behind with
  "Open Admin Page", "Copy Admin Link", "Restart Kiosk Displays", and "Exit
  Kiosk".

(The source folders and internal C# namespaces still say `LessonDisplay` —
that's the original project name, kept for the source tree only since
renaming it touches every file for zero user-visible benefit. Anything a
teacher or buyer actually sees — the installer, the shipped .exe names,
window/page titles, Start Menu entries — says ClassSync.)

Both are plain .NET apps with **no third-party NuGet packages** — the
server uses only ASP.NET Core and the JSON types built into .NET; the kiosk
app uses only WinForms. That was a deliberate choice so the build stays
reliable on a machine that has nothing but the .NET SDK installed.

## Getting the installer

You don't need Visual Studio or even a Windows PC to produce
`ClassSyncSetup.exe` — GitHub will build it for you for free:

1. Create a new GitHub repository and push this folder to it.
2. Open the repo's **Actions** tab. The "Build Windows Installer" workflow
   runs automatically on push (or click "Run workflow" to trigger it by
   hand).
3. When it finishes (a few minutes), open the run and download the
   **ClassSyncSetup** artifact — that's `ClassSyncSetup.exe`, ready to hand
   to any teacher.

If you'd rather build locally on a Windows PC that already has the [.NET
SDK](https://dotnet.microsoft.com/download) and [Inno
Setup](https://jrsoftware.org/isdl.php) installed, open PowerShell in this
folder and run `.\build.ps1` — it does the same two steps (publish, then
compile the installer) and leaves the result at
`installer\Output\ClassSyncSetup.exe`.

## Installing it (what a teacher does)

1. Run `ClassSyncSetup.exe` on the classroom mini PC (needs administrator
   rights, once, for the install).
2. It registers the server to start at boot and the kiosk launcher to start
   at logon, opens the firewall for port 8420, and offers to open the Admin
   page immediately.
3. Plug in both monitors before starting the PC (or use the tray icon's
   "Restart Kiosk Displays" after connecting the second one).
4. From the tray icon, "Copy Admin Link" gives the address other teachers'
   computers can use — or open the Links tab inside Admin itself.

Uninstalling removes the app and the scheduled tasks/firewall rule, but
**deliberately leaves lesson content in place**
(`%ProgramData%\ClassSync\data\lessons.json`) so a reinstall or upgrade
never loses anyone's lessons.

## Configuration

Everything has a sensible default; these environment variables (set them
system-wide via System Properties → Environment Variables, then restart the
scheduled tasks) override them:

| Variable              | Default                          | Meaning                          |
|------------------------|-----------------------------------|-----------------------------------|
| `CLASSSYNC_PORT`       | `8420`                            | Port the web server listens on   |
| `CLASSSYNC_DATA_DIR`   | `%ProgramData%\ClassSync\data`    | Where `lessons.json` lives       |
| `CLASSSYNC_HOSTNAME`   | `lessons`                         | Advertises `<value>.local`       |

## Licensing (7-day trial + paid keys)

Every fresh install runs as a **free 7-day trial** automatically — nothing
to enter, the clock starts the first time the server runs. Once those 7
days are up, the Admin page and both display monitors switch to a
"Trial Expired" screen that asks for a license key; entering a valid one
unlocks everything again instantly, no restart, no reinstall. A teacher
who already bought a key can skip the trial entirely by activating it from
day one (Admin → Display Links tab → License card).

This works **fully offline** — there's no license server to run, pay for,
or keep online, and no third-party service (Gumroad, etc.) taking a cut or
able to shut off access. It's a self-signed scheme: a private key (which
only you hold) signs each license key, and the app only trusts keys signed
by the matching public key baked into it. Nobody can forge a working key
without your private key, but you also never have to be online to check
one.

### Selling a key — the short version

1. **Generate your real signing keypair once**, on your own machine, using
   the `LicenseTool` you can download from the same GitHub Actions run
   that builds the installer (Actions → the run → **LicenseTool** artifact
   → `LicenseTool.exe`). Or build it yourself with the .NET SDK from
   `tools/LicenseTool`.

   ```
   LicenseTool.exe genkey --out-dir C:\Keys\ClassSync
   ```

   This writes `private.key` and `public.key` there and prints the public
   key's contents to the screen.

2. **Put your real public key in the app**, replacing the demo one, so
   copies you build after this point actually check against your key:
   open `src/LessonDisplay.Server/Licensing/EmbeddedPublicKey.cs` and
   paste your `public.key` contents in place of the placeholder PEM
   string, following the instructions in that file's comment. Commit that
   one-line change and push — the next installer build picks it up.

3. **Never commit or share `private.key`.** Back it up somewhere safe
   (password manager, encrypted drive) — it's the only thing that can mint
   valid keys for your app, and if you lose it you'd have to switch
   everyone to a new public key (old keys they already have keep working,
   but you couldn't issue new ones under the old identity). `.gitignore`
   already blocks a `private.key`/`public.key` pair from being committed
   by accident if you generate them inside this folder.

4. **Issue one key per sale:**

   ```
   LicenseTool.exe issue --private C:\Keys\ClassSync\private.key --name "Jane Smith" --email jane@school.edu
   ```

   That prints a `CSN1....` string — send that to the buyer, they paste it
   into Admin → Display Links → License → Activate. Add `--type
   subscription --years 1` instead of the default (perpetual, never
   expires) if you'd rather sell renewing access.

   You can sanity-check any key before sending it:
   ```
   LicenseTool.exe verify --public C:\Keys\ClassSync\public.key --key CSN1.xxxx.yyyy
   ```

### Important: replace the demo key before selling anything

`demo-keys/private.key` and `demo-keys/public.key` in this repo are
throwaway keys generated so the app has *something* to test licensing
against out of the box. Because `demo-keys/private.key` sits in a public
GitHub repo, **anyone can mint a "valid" license for any copy still using
the demo public key** — that's fine for testing, but means you must
complete steps 1–2 above (generate your own real keypair and swap
`EmbeddedPublicKey.cs`) before you distribute a copy you intend to charge
for. `tools/LicenseTool` itself is deliberately not installed by
`ClassSyncSetup.exe` — it's a seller-only tool, kept out of what teachers
receive.

## What's been tested vs. not

This code was written and packaged in a Linux sandbox with no access to a
real Windows machine, so here's the honest state of it:

- **ClassSync.Server** — fully built and exercised here: every API
  endpoint (lessons, courses, schedules, display styles, validation
  failures) was run against a live instance and checked against the
  original Python app's behavior line-by-line. This part is solid.
- **ClassSync.Kiosk** (the WinForms tray/kiosk launcher) — written
  carefully but **could not be compiled or run in this environment**
  (WinForms needs the Windows Desktop runtime, which only exists on
  Windows). Its first real build and test will be the GitHub Actions run or
  your own machine. If Edge kiosk windows don't land on the right monitors
  on the first try, the likely fix is in `KioskContext.cs`'s
  `LaunchKioskWindow` (the `--window-position`/`--window-size` arguments).
- **The installer script** (Inno Setup) — written following Inno Setup's
  documented syntax but not run through the actual compiler, since that
  also only runs on Windows. This includes the wizard illustration/info
  page settings (`WizardImageFile`, `WizardSmallImageFile`,
  `InfoBeforeFile`) — Inno Setup requires plain `.bmp` files at exact pixel
  sizes (see `installer/images/README.md`); the next GitHub Actions run is
  the first real check that they display correctly.
- **The "About ClassSync" slideshow page** (the `[Code]` section at
  the bottom of `LessonDisplaySetup.iss`) — this is the single riskiest
  piece in the whole project, and worth calling out specifically: it's a
  custom wizard page that crossfades between the three classroom photos
  using a real Win32 timer (`SetTimer`/`CreateCallback`), since Inno
  Setup's built-in `WizardImageFile` only supports one static image, not
  an animated sequence. Every piece of it (the timer callback pattern, the
  `TBitmapImage`/`TNewStaticText` classes, the `dontcopy` + wildcard +
  `ExtractTemporaryFile` technique for bundling the 36 crossfade frames)
  is copied from or closely follows Inno Setup's own official
  documentation and example scripts — but Pascal Script genuinely cannot
  be compiled or run outside Windows, so this has not been visually
  confirmed. If it doesn't behave right, look here first. The photos
  themselves and the app icon can be swapped any time by re-running
  `installer/images/generate_assets.py` — see that folder's README.
- **Licensing** (`LicenseTool`, the trial clock, the Admin activation
  screen) — fully built and exercised here end to end: generating a
  keypair, issuing a key, activating it in a running instance, rejecting a
  tampered key, and a back-dated trial correctly locking out both page
  views and write API calls until a valid key is entered. This part is
  solid; only `LicenseTool`'s packaging as a Windows `.exe` (via the CI
  step above) hasn't been confirmed on a real Windows machine yet.

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
