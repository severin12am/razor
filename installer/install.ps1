param(
    [switch]$StartWithWindows,
    [switch]$DesktopShortcut
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$built = Join-Path $root 'dist\ResourcePanel.exe'
if (-not (Test-Path $built)) {
    & (Join-Path $root 'build.ps1')
}
if (-not (Test-Path $built)) {
    throw "Build did not produce dist\ResourcePanel.exe."
}

$destDir = Join-Path $env:LOCALAPPDATA 'ResourcePanel'
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$dest = Join-Path $destDir 'ResourcePanel.exe'
Copy-Item $built $dest -Force

$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\ResourcePanel.lnk'
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = $dest
$shortcut.WorkingDirectory = $destDir
$shortcut.Description = 'See what is using this PC and pause what you do not need.'
$shortcut.Save()

if ($DesktopShortcut) {
    $desktop = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'ResourcePanel.lnk'))
    $desktop.TargetPath = $dest
    $desktop.WorkingDirectory = $destDir
    $desktop.Save()
}

if ($StartWithWindows) {
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $run -Name 'ResourcePanel' -Value "`"$dest`""
}

Write-Host "Installed to $dest"
Write-Host "Start menu shortcut created."
if (-not $StartWithWindows) {
    Write-Host "It does not start with Windows unless you pass -StartWithWindows or turn that on in the panel menu."
}
