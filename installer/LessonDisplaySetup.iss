; ClassSync — Windows installer (Inno Setup)
;
; Expects the two apps already published (self-contained, win-x64) into:
;   publish\Server\   <- output of: dotnet publish src/LessonDisplay.Server -r win-x64 --self-contained true -p:PublishSingleFile=true
;   publish\Kiosk\    <- output of: dotnet publish src/LessonDisplay.Kiosk  -r win-x64 --self-contained true -p:PublishSingleFile=true
; See build.ps1 (or the GitHub Actions workflow) which produces these before
; calling ISCC.exe on this script.
;
; Installs:
;   - ClassSync.Server.exe, registered as a Scheduled Task that starts
;     at system boot (as SYSTEM, before anyone logs in) and restarts itself
;     if it ever exits.
;   - ClassSync.Kiosk.exe, registered as a Scheduled Task that starts
;     whenever any user logs on (needs a desktop session to show windows).
;   - A firewall rule opening the chosen port so other computers on the
;     school network can reach the Admin page.
;   - A Start Menu / Desktop shortcut straight to the Admin page.

#define MyAppName "ClassSync"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ClassSync"
#define MyAppExeName "ClassSync.Server.exe"
#define MyKioskExeName "ClassSync.Kiosk.exe"
#define MyAppPort "8420"

[Setup]
AppId={{9C7E2B1E-6B6E-4B6B-9C1E-7B1C6E6D9A11}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ClassSync
DefaultGroupName=ClassSync
DisableProgramGroupPage=yes
OutputBaseFilename=ClassSyncSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\assets\icon.ico
; Illustration shown on the Welcome/Finished pages (164x314 px, .bmp) and
; the small logo shown in the corner of every page (55x58 px, .bmp), both
; built from the real app icon artwork. installer/images/README.md explains
; how to regenerate these (and the slideshow below) from new photos.
WizardImageFile=images\wizard-image.bmp
WizardSmallImageFile=images\wizard-small.bmp
InfoBeforeFile=WelcomeInfo.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\Server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\Kiosk\*"; DestDir: "{app}\Kiosk"; Flags: ignoreversion recursesubdirs createallsubdirs
; Slideshow crossfade frames shown on the custom "About ClassSync" page
; (see [Code] below). dontcopy = bundled into Setup for extraction with
; ExtractTemporaryFile at runtime, never installed onto the machine.
Source: "images\slideshow\*.bmp"; Flags: dontcopy noencryption

[Icons]
Name: "{group}\ClassSync Admin"; Filename: "http://localhost:{#MyAppPort}/admin"; IconFilename: "{app}\Server\{#MyAppExeName}"
Name: "{group}\Uninstall ClassSync"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ClassSync Admin"; Filename: "http://localhost:{#MyAppPort}/admin"; Tasks: desktopicon; IconFilename: "{app}\Server\{#MyAppExeName}"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut to the Admin page"; GroupDescription: "Additional shortcuts:"

[Run]
; --- Register the server to start at boot, before any user logs on ---
Filename: "{sys}\schtasks.exe"; \
  Parameters: "/Create /F /RU SYSTEM /RL HIGHEST /SC ONSTART /TN ""ClassSync Server"" /TR ""'{app}\Server\{#MyAppExeName}'"""; \
  Flags: runhidden

; --- Register the kiosk launcher to start whenever anyone logs on (it
;     needs a desktop session to open windows and show the tray icon) ---
Filename: "{sys}\schtasks.exe"; \
  Parameters: "/Create /F /SC ONLOGON /RL HIGHEST /TN ""ClassSync Kiosk"" /TR ""'{app}\Kiosk\{#MyKioskExeName}'"""; \
  Flags: runhidden

; --- Open the firewall for other computers on the network to reach Admin ---
Filename: "{sys}\netsh.exe"; \
  Parameters: "advfirewall firewall add rule name=""ClassSync"" dir=in action=allow protocol=TCP localport={#MyAppPort}"; \
  Flags: runhidden

; --- Start it now, so Finish can offer to open the Admin page immediately
;     without waiting for a reboot ---
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""ClassSync Server"""; Flags: runhidden
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""ClassSync Kiosk"""; Flags: runhidden nowait
Filename: "http://localhost:{#MyAppPort}/admin"; Description: "Open the Admin page now"; Flags: postinstall shellexec skipifsilent nowait

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN ""ClassSync Kiosk"""; Flags: runhidden; RunOnceId: "StopKioskTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN ""ClassSync Server"""; Flags: runhidden; RunOnceId: "StopServerTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""ClassSync Kiosk"""; Flags: runhidden; RunOnceId: "DeleteKioskTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""ClassSync Server"""; Flags: runhidden; RunOnceId: "DeleteServerTask"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""ClassSync"""; Flags: runhidden; RunOnceId: "DeleteFirewallRule"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyKioskExeName} /F"; Flags: runhidden; RunOnceId: "KillKiosk"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillServer"

[UninstallDelete]
; Program files only — lesson content in %ProgramData%\ClassSync\data is
; deliberately left in place so an uninstall/reinstall (or version upgrade)
; never loses a teacher's lessons.
Type: filesandordirs; Name: "{app}"

[Code]
// ---------------------------------------------------------------------------
// "About ClassSync" page: a custom wizard page (inserted right after
// Welcome) that shows the three classroom photos in installer/images/
// slideshow, each held for 7 seconds and crossfading into the next. All the
// crossfade frames themselves are pre-rendered (see
// installer/images/generate_assets.py) — this code just displays a plain
// .bmp file on a timer and swaps which one every tick. It never blends or
// decodes anything itself, which keeps it simple enough to trust without
// being able to compile/run Inno Setup locally (see the project README's
// "What's been tested vs. not" section).
//
// The one non-obvious part is the timer: Inno Setup's Pascal Script has no
// built-in TTimer, so this uses the pattern documented on Inno Setup's own
// help page for CreateCustomPage/CreateCallback — a real Win32 SetTimer
// with a callback created via CreateCallback.
const
  FramesPerTransition = 12;   // must match generate_assets.py
  SlideHoldMs = 7000;         // how long each photo is shown before fading
  SlideTickMs = 50;           // time between crossfade frames (~600ms fade)

function SetTimer(hWnd: HWND; nIDEvent: UINT_PTR; uElapse: UINT; lpTimerFunc: NativeInt): UINT_PTR;
  external 'SetTimer@user32.dll stdcall';
function KillTimer(hWnd: HWND; uIDEvent: UINT_PTR): BOOL;
  external 'KillTimer@user32.dll stdcall';

var
  SlideshowPage: TWizardPage;
  SlideshowImage: TBitmapImage;
  SlideshowTimerId: UINT_PTR;
  SlideshowPairIndex: Integer;
  SlideshowFrameIndex: Integer;
  SlideshowInTransition: Boolean;
  SlideshowPairPrefix: array[0..2] of String;

function Pad2(N: Integer): String;
begin
  if N < 10 then
    Result := '0' + IntToStr(N)
  else
    Result := IntToStr(N);
end;

function SlideshowFramePath(Prefix: String; Frame: Integer): String;
begin
  Result := ExpandConstant('{tmp}\') + Prefix + '_' + Pad2(Frame) + '.bmp';
end;

procedure SlideshowStartTimer(IntervalMs: UINT); forward;

procedure SlideshowTimerProc(H: HWND; Msg: UINT; TimerId: UINT_PTR; Time: DWORD);
begin
  if SlideshowInTransition then
  begin
    SlideshowImage.Bitmap.LoadFromFile(
      SlideshowFramePath(SlideshowPairPrefix[SlideshowPairIndex], SlideshowFrameIndex));
    SlideshowFrameIndex := SlideshowFrameIndex + 1;
    if SlideshowFrameIndex >= FramesPerTransition then
    begin
      SlideshowInTransition := False;
      SlideshowPairIndex := (SlideshowPairIndex + 1) mod 3;
      SlideshowStartTimer(SlideHoldMs);
    end;
  end
  else
  begin
    SlideshowInTransition := True;
    SlideshowFrameIndex := 0;
    SlideshowStartTimer(SlideTickMs);
  end;
end;

procedure SlideshowStartTimer(IntervalMs: UINT);
begin
  if SlideshowTimerId <> 0 then
  begin
    KillTimer(0, SlideshowTimerId);
    SlideshowTimerId := 0;
  end;
  SlideshowTimerId := SetTimer(0, 0, IntervalMs, CreateCallback(@SlideshowTimerProc));
end;

procedure SlideshowStopTimer;
begin
  if SlideshowTimerId <> 0 then
  begin
    KillTimer(0, SlideshowTimerId);
    SlideshowTimerId := 0;
  end;
end;

procedure ExtractSlideshowFrames;
var
  P, F: Integer;
begin
  for P := 0 to 2 do
    for F := 0 to FramesPerTransition - 1 do
      ExtractTemporaryFile(SlideshowPairPrefix[P] + '_' + Pad2(F) + '.bmp');
end;

procedure InitializeWizard;
var
  Caption: TNewStaticText;
begin
  SlideshowPairPrefix[0] := 't12';
  SlideshowPairPrefix[1] := 't23';
  SlideshowPairPrefix[2] := 't31';
  SlideshowTimerId := 0;
  SlideshowPairIndex := 0;
  SlideshowFrameIndex := 0;
  SlideshowInTransition := False;

  ExtractSlideshowFrames;

  SlideshowPage := CreateCustomPage(wpWelcome,
    'About ClassSync',
    'A look at what appears on the classroom monitors.');

  SlideshowImage := TBitmapImage.Create(SlideshowPage);
  SlideshowImage.Parent := SlideshowPage.Surface;
  SlideshowImage.Left := 8;
  SlideshowImage.Top := 8;
  SlideshowImage.Width := 380;
  SlideshowImage.Height := 214;
  SlideshowImage.Stretch := True;
  SlideshowImage.Bitmap.LoadFromFile(SlideshowFramePath(SlideshowPairPrefix[0], 0));

  Caption := TNewStaticText.Create(SlideshowPage);
  Caption.Parent := SlideshowPage.Surface;
  Caption.Left := 8;
  Caption.Top := SlideshowImage.Top + SlideshowImage.Height + 12;
  Caption.Width := 380;
  Caption.AutoSize := False;
  Caption.WordWrap := True;
  Caption.Caption :=
    'Two monitors, always showing what students need: the Learning ' +
    'Intention, the Success Criteria, and today''s lesson — automatically, ' +
    'on your bell schedule.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (SlideshowPage <> nil) and (CurPageID = SlideshowPage.ID) then
  begin
    SlideshowPairIndex := 0;
    SlideshowFrameIndex := 0;
    SlideshowInTransition := False;
    SlideshowImage.Bitmap.LoadFromFile(SlideshowFramePath(SlideshowPairPrefix[0], 0));
    SlideshowStartTimer(SlideHoldMs);
  end
  else
    SlideshowStopTimer;
end;

procedure DeinitializeSetup;
begin
  SlideshowStopTimer;
end;
