# 开发与构建

## 技术栈与结构

- C# / .NET 8 / WPF，Windows x64。
- NAudio 2.2.1；WASAPI Loopback 用于游戏输出监听，WASAPI Capture 用于实体麦克风。
- 本地 JSON + 图片 + WAV，无需数据库服务器。

```text
src/FieldTrader/            WPF 项目及完整初始资源
  AudioCaptureService.cs   单一游戏输出捕获与循环缓冲
  RecognitionPipeline.cs   检测、事件排队和匹配调度
  WaveformRecognitionService.cs  声音模板匹配
  SessionState.cs          主界面与 HUD 共享状态
  SeedData/                物品、声音组、样本及图片
  VoiceBoardWindow.*       语音板界面、配置和热键
  VoiceMicrophoneMixer.cs  真人声/音效混音及发送增强
  VoicePlaybackController.cs  自动说话键生命周期
  VoiceSeed/               内置语音录音和中文备注
  ThirdParty/              完整 VB-CABLE 原始组件与说明
installer/                 Inno Setup 脚本与中文语言文件
build.ps1                  发布与可选安装包构建入口
docs/                      使用、开发、截图和第三方说明
.github/workflows/         Windows 构建与离线回归验证
```

主窗口和 HUD 共用一个游戏音频捕获服务。真人麦克风采集属于语音混音功能，和游戏输出监听互相独立，不应复用或重复启动两套游戏 Loopback 捕获。

主要扩展点是 `IAudioRecognitionService`、`RecognitionResult`、`Item`、`SoundGroup`、`SoundSample`。真实模板匹配与 Mock 展示分离；同一声音组可以关联多个物品。

## 本地运行

在 Windows 安装 .NET 8 SDK，然后执行：

```powershell
dotnet restore src/FieldTrader/FieldTrader.csproj
dotnet run --project src/FieldTrader/FieldTrader.csproj
```

## 发布

```powershell
./build.ps1
# artifacts/publish/FieldTrader.exe，需连同该目录其他文件一起分发
```

该脚本生成自包含 win-x64 版本。也可以直接执行：

```powershell
dotnet publish src/FieldTrader/FieldTrader.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

安装包需要 Inno Setup 6：

```powershell
./build.ps1 -CreateInstaller
# 或显式指定编译器
./build.ps1 -CreateInstaller -InnoCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

安装脚本保留既有 AppId，支持旧版覆盖升级。VB-CABLE 的 31 个原始文件不得擅自修改；相关许可和署名见 [第三方说明](THIRD_PARTY.md)。安装器仅在用户选择后打开官方驱动安装界面并请求管理员确认，不应在构建或测试中安装驱动。

## 离线回归验证

以下命令只写入指定测试目录，不向游戏发送按键，不执行驱动安装：

```powershell
artifacts/publish/FieldTrader.exe --microphone-mix-test artifacts/tests/mix
artifacts/publish/FieldTrader.exe --ptt-test artifacts/tests/ptt
artifacts/publish/FieldTrader.exe --cable-bundle-test artifacts/tests/cable
```

图形程序建议使用 `Start-Process -Wait -PassThru` 并核对退出码和对应 JSON 报告中的 `Passed`。混音测试检查发送采样增益、实时音效音量、独立静音、峰值保护和重采样；自动按键测试使用模拟输入。

本机原生验证另外有 `--voiceboard-test <目录>`（SAPI、界面、快捷键注册/释放）和 `--installation-test <独立数据目录> <报告文件>`（数据库与窗口）。这些依赖 Windows 音频/桌面环境。`--microphone-probe <目录>` 会短暂采集实体麦克风并只统计帧数，请在需要时主动执行。

`--self-test --audio-test` 会实际播放测试音并运行 Loopback；旧批次审计命令依赖外部录音，不属于可独立运行的常规 CI 检查。

CI 不代表游戏内准确率、所有蓝牙驱动兼容性或实际队友收音效果；这些需要在具体设备与游戏中验证。

## 数据维护

初始库位于 `SeedData/catalog.json`，包含 Item / SoundGroup / SoundSample 和相对资源路径。用户运行库位于 `%LOCALAPPDATA%\FieldTrader`，首次启动与升级时通过 `SeedCatalog` 合并。

新增物品请为稳定 ID、声音组、占格和横竖方向提供明确关系；共享声音的物品使用同一组，不用强行训练成唯一物品。源码仓库只提交用于软件分发的初始样本，不提交私人诊断录音或原始视频。
