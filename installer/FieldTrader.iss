[Setup]
AppId={{B3D2F7F0-45A8-49D6-9A73-44F0E80DE094}
AppName=战地行商 · 听声识物
AppPublisher=取名字真麻烦
AppVersion=0.8.5
AppVerName=战地行商 · 听声识物 0.8.5
DefaultDirName={localappdata}\Programs\FieldTrader
DefaultGroupName=战地行商
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=FieldTrader-0.8.5-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\FieldTrader.exe
CloseApplications=yes
RestartApplications=no
DisableWelcomePage=no
SetupLogging=yes
InfoBeforeFile=../src/FieldTrader/ThirdParty/第三方组件说明.txt

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Messages]
WelcomeLabel2=作者：取名字真麻烦%n抖音号：21360166462　QQ：3445838743%n免费使用，禁止倒卖。%n%n即将安装战地行商 · 听声识物 0.8.5。%n%n已包含完整运行组件、109 个物品（107 件有可用图片，2 件待补图）、24 个声音组和 389 段声音样本。安装过程无需下载其他组件。%n%n支持真人麦克风与音效混音，已随附标准VB-CABLE驱动原始组件，可选安装，需管理员确认并按提示重启。新增哈基米主题游戏语音板：8条短句、8段已放大录音、中文备注及快捷键。按键说话可自动按住T播放，自由说话无需按键。%n%n首次启动将自动准备物品数据库。新版采用波形匹配与户外低频降噪复核，70%匹配门槛；检测到声音会显示分析或不确定状态。
FinishedLabel=安装完成。%n%n选择游戏使用的声音输出设备，点击“开始监听”即可打开悬浮窗。%n%n您的个人数据库独立保存，卸载程序时会保留。%n%n虚拟麦克风组件：勾选下面的VB-CABLE安装项，在官方窗口点Install Driver，完成后重启电脑。已有组件时将跳过。VB-CABLE由VB-Audio开发，属于Donationware，欢迎自愿付费支持。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\战地行商 · 听声识物"; Filename: "{app}\FieldTrader.exe"; WorkingDir: "{app}"
Name: "{group}\卸载战地行商"; Filename: "{uninstallexe}"
Name: "{autodesktop}\战地行商 · 听声识物"; Filename: "{app}\FieldTrader.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ThirdParty\VB-CABLE\original-package\VBCABLE_Setup_x64.exe"; WorkingDir: "{app}\ThirdParty\VB-CABLE\original-package"; Verb: "runas"; Description: "安装虚拟麦克风组件 VB-CABLE（需管理员确认及重启）"; Flags: shellexec waituntilterminated postinstall skipifsilent; Check: NeedCableDriver
Filename: "{app}\FieldTrader.exe"; Description: "启动战地行商 · 听声识物"; Flags: nowait postinstall skipifsilent




[Code]
function NeedCableDriver: Boolean;
var Code: Integer;
begin
  Result := True;
  if FileExists(ExpandConstant('{app}\FieldTrader.exe')) then
    if Exec(ExpandConstant('{app}\FieldTrader.exe'), '--has-vbcable', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := Code <> 0;
end;
