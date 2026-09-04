[Setup]
AppName=Simpink
AppVersion=1.0
AppPublisher=SimpinkNative
DefaultDirName={autopf}\Simpink
DefaultGroupName=Simpink
OutputBaseFilename=Simpink_Setup_Win64
OutputDir=.\Installer
Compression=lzma2
SolidCompression=yes
SetupIconFile=.\icon.ico
UninstallDisplayIcon={app}\Simpink.exe
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\Simpink.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net8.0-windows\win-x64\publish\Simpink.pdb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Simpink"; Filename: "{app}\Simpink.exe"
Name: "{autodesktop}\Simpink"; Filename: "{app}\Simpink.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\Simpink.exe"; Description: "Launch Simpink"; Flags: nowait postinstall skipifsilent
