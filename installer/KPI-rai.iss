#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{8E15AF2F-4A07-44A4-86D0-51D7EA6D2B78}
AppName=KPI-rai
AppVersion={#MyAppVersion}
AppPublisher=KPI-rai
DefaultDirName={localappdata}\Programs\KPI-rai
DefaultGroupName=KPI-rai
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=KPI-rai-Setup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Produktionsplanung.exe

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Aufgaben:"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\KPI-rai"; Filename: "{app}\Produktionsplanung.exe"
Name: "{autodesktop}\KPI-rai"; Filename: "{app}\Produktionsplanung.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Produktionsplanung.exe"; Description: "KPI-rai starten"; Flags: nowait postinstall skipifsilent
