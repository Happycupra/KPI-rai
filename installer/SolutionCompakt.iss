#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{8E15AF2F-4A07-44A4-86D0-51D7EA6D2B78}
AppName=SolutionCompakt
AppVersion={#MyAppVersion}
AppPublisher=SolutionCompakt
AppPublisherURL=https://github.com/Happycupra/KPI-rai
DefaultDirName={localappdata}\Programs\SolutionCompakt
DefaultGroupName=SolutionCompakt
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=SolutionCompakt-Setup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Produktionsplanung.App\Assets\SolutionCompakt.ico
UninstallDisplayIcon={app}\SolutionCompakt.exe
VersionInfoCompany=SolutionCompakt
VersionInfoDescription=SolutionCompakt – Planen · Organisieren · Voranbringen
VersionInfoProductName=SolutionCompakt
VersionInfoProductVersion={#MyAppVersion}

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Aufgaben:"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\SolutionCompakt"; Filename: "{app}\SolutionCompakt.exe"
Name: "{autodesktop}\SolutionCompakt"; Filename: "{app}\SolutionCompakt.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SolutionCompakt.exe"; Description: "SolutionCompakt starten"; Flags: nowait postinstall skipifsilent
