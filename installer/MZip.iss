; MZip Windows 安装包脚本（Inno Setup 6）
; 使用 scripts\build-installer.ps1 编译，也可直接执行:
;   ISCC.exe /DMyAppVersion=1.0.0 installer\MZip.iss

#define MyAppName "MZip"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "MZip"
#define MyAppExeName "MZip.exe"
#define MyAppURL "https://gitee.com/hongchang_meng/quick-zip"

[Setup]
AppId={{C7F2E5BA-8A1D-4E63-B9F4-3D6A2C8E5B70}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=mailto:593192219@qq.com
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\MZip
DefaultGroupName=MZip
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=MZip-{#MyAppVersion}-Setup
SetupIconFile=..\src\MZip\Assets\MZip.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
AllowNoIcons=yes
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=MZip 安装程序
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式（可选）"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
Source: "..\build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\MZip"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 MZip"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MZip"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "运行 MZip"; Flags: nowait postinstall skipifsilent
