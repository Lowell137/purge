; Purge kurulumu (Inno Setup 6). Derle: ISCC.exe installer\Purge.iss  (önce: dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish)
#define AppName "Purge"
#define AppVersion "1.0.0"
#define AppPublisher "Lowell137"
#define AppExe "Purge.exe"

[Setup]
AppId={{6E0C2B7A-6F3B-4C8E-9A51-3B2F8D1C7E40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableDirPage=auto
DisableReadyPage=yes
; Varsayılan: yalnız bu kullanıcı, yönetici izni gerekmez; kurulumda "tüm kullanıcılar" seçilebilir
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=PurgeSetup-{#AppVersion}-x64
SetupIconFile=..\Assets\Purge.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
