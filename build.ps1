#Requires -Version 5.1
<#
  Local build script — an alternative to the GitHub Actions workflow for
  anyone with a Windows PC that already has the .NET SDK and Inno Setup
  installed. Produces installer\Output\ClassSyncSetup.exe.

  1. Install the .NET SDK: https://dotnet.microsoft.com/download (8.0 or later)
  2. Install Inno Setup:   https://jrsoftware.org/isdl.php
  3. Open PowerShell in this folder and run:  .\build.ps1
#>

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "Publishing ClassSync.Server..." -ForegroundColor Cyan
dotnet publish "$root\src\LessonDisplay.Server\LessonDisplay.Server.csproj" `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o "$root\publish\Server"

Write-Host "Publishing ClassSync.Kiosk..." -ForegroundColor Cyan
dotnet publish "$root\src\LessonDisplay.Kiosk\LessonDisplay.Kiosk.csproj" `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o "$root\publish\Kiosk"

Write-Host "Publishing LicenseTool (for you, the seller — not part of the installer)..." -ForegroundColor Cyan
dotnet publish "$root\tools\LicenseTool\LicenseTool.csproj" `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o "$root\publish\LicenseTool"

$iscc = @(
  "$Env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
  "$Env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  throw "Could not find ISCC.exe (Inno Setup's compiler). Install Inno Setup from https://jrsoftware.org/isdl.php and re-run."
}

Write-Host "Building installer..." -ForegroundColor Cyan
& $iscc "$root\installer\LessonDisplaySetup.iss"

Write-Host "`nDone. Installer at: $root\installer\Output\ClassSyncSetup.exe" -ForegroundColor Green
Write-Host "LicenseTool (keep private, don't hand out) at: $root\publish\LicenseTool\LicenseTool.exe" -ForegroundColor Green
