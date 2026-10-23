[Setup]
AppId={{B3D2F7F0-45A8-49D6-9A73-44F0E80DE094}
AppName=战地行商 · 听声识物
AppPublisher=取名字真麻烦
AppVersion=0.9.3
AppVerName=战地行商 · 听声识物 0.9.3
DefaultDirName={localappdata}\Programs\FieldTrader
DefaultGroupName=战地行商
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=FieldTrader-0.9.3-Setup
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
WelcomeLabel2=作者：取名字真麻烦%n抖音号：21360166462　QQ：3445838743%n免费使用，禁止倒卖。%n%n即将安装战地行商 · 听声识物 0.9.3。%n%n初始库来自暗区情报站“行商听声鉴宝”图鉴，包含 138 个物品（136 件有可用图片，2 件待补图）、17 个声音组和 17 段拾取音效。安装过程无需下载其他组件。%n%n悬浮窗支持拖动任意边缘或四角调整大小，并自动记忆宽高。物品名称、价格、品质颜色、实际占格、横竖方向和声音均按图鉴标注保存，并支持 1/2/3/4/6/8 格与横竖筛选。悬浮窗用品质颜色填充物品图片透明区域，并可按价格、品质或名字运行升降序排序；透明度和排序会自动记忆。软件设置可修改红、金、紫三种品质底色。%n%n保留 Alt+W 全局监听快捷键及悬浮窗参考价。支持真人麦克风与音效混音，已随附标准VB-CABLE驱动原始组件，可选安装，需管理员确认并按提示重启。
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
