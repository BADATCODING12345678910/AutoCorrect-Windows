; Inno Setup script â€” compile after running ..\publish-win.cmd
; Requires Inno Setup 6: https://jrsoftware.org/isinfo.php
;
; Output: installer\Output\AutoCorrectSetup.exe

#define MyAppName "AutoCorrect"
#define MyAppVersion "0.2.30"
#define MyAppPublisher "AutoCorrect"
#define MyAppExeName "AutoCorrect.App.exe"
#define PublishDir "..\artifacts\win-x64"
#define MyAppIcon "..\src\AutoCorrect.App\Assets\autocorrect.ico"

[Setup]
AppId={{8F3C2A91-6B4E-4D7A-9C1F-AutoCorrectNative}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=AutoCorrectSetup
SetupIconFile={#MyAppIcon}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "startup"; Description: "Start AutoCorrect when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch AutoCorrect"; Flags: nowait postinstall skipifsilent

; NOTE: Do NOT DirExists({#PublishDir}) at install time â€” that path is only required
; when compiling this script (after publish-win.cmd). The Setup.exe already embeds
; those files via [Files]. A runtime check blocked every install outside the build tree.
