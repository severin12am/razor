; Optional installer. Requires Inno Setup 6.
; The app also runs as the single exe from dist\, with no installer.

#define MyAppName "ResourcePanel"
#define MyAppVersion "1.0.0"
#define MyAppExe "ResourcePanel.exe"

[Setup]
AppId={{A7B1E5C4-2F44-4A0E-9C31-6D0E5F2A11B8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\ResourcePanel
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=ResourcePanelSetup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\{#MyAppExe}
DisableProgramGroupPage=yes

[Tasks]
Name: startup; Description: "Start with Windows"
Name: desktopicon; Description: "Add a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\dist\ResourcePanel.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ResourcePanel"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\ResourcePanel"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ResourcePanel"; ValueData: """{app}\{#MyAppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Open ResourcePanel"; Flags: nowait postinstall skipifsilent
