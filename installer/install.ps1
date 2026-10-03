param(
    [switch]$StartWithWindows,
    [switch]$DesktopShortcut
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$built = Join-Path $root 'dist\Razor.exe'
if (-not (Test-Path $built)) {
    & (Join-Path $root 'build.ps1')
}
if (-not (Test-Path $built)) {
    throw "Build did not produce dist\Razor.exe."
}

$destDir = Join-Path $env:LOCALAPPDATA 'Razor'
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$dest = Join-Path $destDir 'Razor.exe'
Copy-Item $built $dest -Force

$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Razor.lnk'
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = $dest
$shortcut.WorkingDirectory = $destDir
$shortcut.Description = 'See what is using this PC and pause what you do not need.'
$shortcut.Save()

if ($DesktopShortcut) {
    $desktop = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Razor.lnk'))
    $desktop.TargetPath = $dest
    $desktop.WorkingDirectory = $destDir
    $desktop.Save()
}

if ($StartWithWindows) {
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $run -Name 'Razor' -Value "`"$dest`""
}

Write-Host "Installed to $dest"
Write-Host "Start menu shortcut created."
if (-not $StartWithWindows) {
    Write-Host "It does not start with Windows unless you pass -StartWithWindows or turn that on in the panel menu."
}
