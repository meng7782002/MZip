; JsjmZip Windows 安装包脚本（Inno Setup 6）
; 使用 scripts\build-installer.ps1 编译，也可直接执行:
;   ISCC.exe /DMyAppVersion=1.0.0 installer\JsjmZip.iss

#define MyAppName "JsjmZip"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "西安极速聚米软件科技有限公司"
#define MyAppExeName "JsjmZip.exe"
#define MyAppURL "https://gitee.com/hongchang_meng/quick-zip"

[Setup]
AppId={{B6E1D4A9-3C7F-4B52-8E0A-2F5C9D7A1B64}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\JsjmZip
DefaultGroupName=JsjmZip
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=JsjmZip-{#MyAppVersion}-Setup
SetupIconFile=..\src\JsjmZip\Assets\JsjmZip.ico
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
VersionInfoDescription=JsjmZip 安装程序
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
Name: "{group}\JsjmZip"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 JsjmZip"; Filename: "{uninstallexe}"
Name: "{autodesktop}\JsjmZip"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "运行 JsjmZip"; Flags: nowait postinstall skipifsilent
