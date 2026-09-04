; Lesson Display — Windows installer (Inno Setup)
;
; Expects the two apps already published (self-contained, win-x64) into:
;   publish\Server\   <- output of: dotnet publish src/LessonDisplay.Server -r win-x64 --self-contained true -p:PublishSingleFile=true
;   publish\Kiosk\    <- output of: dotnet publish src/LessonDisplay.Kiosk  -r win-x64 --self-contained true -p:PublishSingleFile=true
; See build.ps1 (or the GitHub Actions workflow) which produces these before
; calling ISCC.exe on this script.
;
; Installs:
;   - LessonDisplay.Server.exe, registered as a Scheduled Task that starts
;     at system boot (as SYSTEM, before anyone logs in) and restarts itself
;     if it ever exits.
;   - LessonDisplay.Kiosk.exe, registered as a Scheduled Task that starts
;     whenever any user logs on (needs a desktop session to show windows).
;   - A firewall rule opening the chosen port so other computers on the
;     school network can reach the Admin page.
;   - A Start Menu / Desktop shortcut straight to the Admin page.

#define MyAppName "Lesson Display"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Lesson Display"
#define MyAppExeName "LessonDisplay.Server.exe"
#define MyKioskExeName "LessonDisplay.Kiosk.exe"
#define MyAppPort "8420"

[Setup]
AppId={{9C7E2B1E-6B6E-4B6B-9C1E-7B1C6E6D9A11}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Lesson Display
DefaultGroupName=Lesson Display
DisableProgramGroupPage=yes
OutputBaseFilename=LessonDisplaySetup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\Server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\Kiosk\*"; DestDir: "{app}\Kiosk"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Lesson Display Admin"; Filename: "http://localhost:{#MyAppPort}/admin"
Name: "{group}\Uninstall Lesson Display"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Lesson Display Admin"; Filename: "http://localhost:{#MyAppPort}/admin"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut to the Admin page"; GroupDescription: "Additional shortcuts:"

[Run]
; --- Register the server to start at boot, before any user logs on ---
Filename: "{sys}\schtasks.exe"; \
  Parameters: "/Create /F /RU SYSTEM /RL HIGHEST /SC ONSTART /TN ""Lesson Display Server"" /TR ""'{app}\Server\{#MyAppExeName}'"""; \
  Flags: runhidden

; --- Register the kiosk launcher to start whenever anyone logs on (it
;     needs a desktop session to open windows and show the tray icon) ---
Filename: "{sys}\schtasks.exe"; \
  Parameters: "/Create /F /SC ONLOGON /RL HIGHEST /TN ""Lesson Display Kiosk"" /TR ""'{app}\Kiosk\{#MyKioskExeName}'"""; \
  Flags: runhidden

; --- Open the firewall for other computers on the network to reach Admin ---
Filename: "{sys}\netsh.exe"; \
  Parameters: "advfirewall firewall add rule name=""Lesson Display"" dir=in action=allow protocol=TCP localport={#MyAppPort}"; \
  Flags: runhidden

; --- Start it now, so Finish can offer to open the Admin page immediately
;     without waiting for a reboot ---
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""Lesson Display Server"""; Flags: runhidden
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""Lesson Display Kiosk"""; Flags: runhidden nowait
Filename: "http://localhost:{#MyAppPort}/admin"; Description: "Open the Admin page now"; Flags: postinstall shellexec skipifsilent nowait

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN ""Lesson Display Kiosk"""; Flags: runhidden; RunOnceId: "StopKioskTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN ""Lesson Display Server"""; Flags: runhidden; RunOnceId: "StopServerTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""Lesson Display Kiosk"""; Flags: runhidden; RunOnceId: "DeleteKioskTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""Lesson Display Server"""; Flags: runhidden; RunOnceId: "DeleteServerTask"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Lesson Display"""; Flags: runhidden; RunOnceId: "DeleteFirewallRule"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyKioskExeName} /F"; Flags: runhidden; RunOnceId: "KillKiosk"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillServer"

[UninstallDelete]
; Program files only — lesson content in %ProgramData%\LessonDisplay\data is
; deliberately left in place so an uninstall/reinstall (or version upgrade)
; never loses a teacher's lessons.
Type: filesandordirs; Name: "{app}"
