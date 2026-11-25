; AirPlay Desktop - Inno Setup Script
; High-performance, self-contained Windows AirPlay installer

#ifndef AppArch
#define AppArch "x64"
#endif

#if AppArch == "arm64"
  #define SetupArchAllowed "arm64"
  #define SetupArch64BitMode "arm64"
  #define OutputName "AirPlayDesktop-Windows-ARM64-Setup"
#else
  #define SetupArchAllowed "x64compatible"
  #define SetupArch64BitMode "x64compatible"
  #define OutputName "AirPlayDesktop-Setup-x64"
#endif

#define MyAppName "AirPlay Desktop"
#define MyAppVersion "1.2.5"
#define MyAppPublisher "emipasca"
#define MyAppURL "https://github.com/emipasca/Airplay-windows"
#define MyAppExeName "AirPlayDesktop.exe"

[Setup]
AppId={{5C4C2977-C41E-4CCD-9348-B37010303D27}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\AirPlayDesktop
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\out\artifacts
OutputBaseFilename={#OutputName}
SetupIconFile=..\resources\newicon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode={#SetupArch64BitMode}
ArchitecturesAllowed={#SetupArchAllowed}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=force
RestartApplications=no
CloseApplicationsFilter=*.exe,*.dll
DisableFinishedPage=yes
DisableStartupPrompt=yes
DirExistsWarning=no
ShowLanguageDialog=auto
UsePreviousLanguage=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[CustomMessages]
english.AutoStartDesc=Launch AirPlay Desktop automatically on Windows startup
spanish.AutoStartDesc=Iniciar AirPlay Desktop automáticamente al arrancar Windows
french.AutoStartDesc=Lancer AirPlay Desktop automatiquement au démarrage de Windows
german.AutoStartDesc=AirPlay Desktop automatisch beim Windows-Start ausführen
italian.AutoStartDesc=Avvia automaticamente AirPlay Desktop all'avvio di Windows
portuguese.AutoStartDesc=Iniciar o AirPlay Desktop automaticamente ao iniciar o Windows
brazilianportuguese.AutoStartDesc=Iniciar o AirPlay Desktop automaticamente ao iniciar o Windows
russian.AutoStartDesc=Автоматически запускать AirPlay Desktop при загрузке Windows
polish.AutoStartDesc=Uruchamiaj AirPlay Desktop automatycznie podczas uruchamiania systemu Windows
dutch.AutoStartDesc=AirPlay Desktop automatisch starten bij het opstarten van Windows
japanese.AutoStartDesc=Windows起動時にAirPlay Desktopを自動起動する
ukrainian.AutoStartDesc=Автоматично запускати AirPlay Desktop під час завантаження Windows

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "{cm:AutoStartDesc}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\out\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AirPlayDesktop"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Automatically configure Bonjour Service and open Windows Firewall ports for AirPlay
Filename: "{app}\fix_bonjour.bat"; Parameters: "/silent"; Flags: runhidden waituntilterminated
; Always launch AirPlay Desktop immediately upon completion
Filename: "{app}\{#MyAppExeName}"; Flags: nowait

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop ""Bonjour Service"""; Flags: runhidden

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Silently terminate running instances (WITHOUT /T to prevent killing the installer if spawned as a child from the running app)
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AirPlayDesktop.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM uxplay.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Allow Windows and .NET runtime to cleanly release all DLL file locks
  Sleep(800);
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Silently terminate running instances before uninstallation (WITHOUT /T)
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AirPlayDesktop.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM uxplay.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

