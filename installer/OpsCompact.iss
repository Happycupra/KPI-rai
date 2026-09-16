#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{8E15AF2F-4A07-44A4-86D0-51D7EA6D2B78}
AppName=OpsCompact
AppVersion={#MyAppVersion}
AppPublisher=OpsCompact
AppPublisherURL=https://github.com/Happycupra/KPI-rai
DefaultDirName={localappdata}\Programs\OpsCompact
DefaultGroupName=OpsCompact
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=OpsCompact-Setup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Produktionsplanung.App\Assets\OpsCompact.ico
UninstallDisplayIcon={app}\OpsCompact.exe
VersionInfoCompany=OpsCompact
VersionInfoDescription=OpsCompact – Planen · Produzieren · Verbessern
VersionInfoProductName=OpsCompact
VersionInfoProductVersion={#MyAppVersion}

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Aufgaben:"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\OpsCompact"; Filename: "{app}\OpsCompact.exe"
Name: "{autodesktop}\OpsCompact"; Filename: "{app}\OpsCompact.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OpsCompact.exe"; Description: "OpsCompact starten"; Flags: nowait postinstall skipifsilent
