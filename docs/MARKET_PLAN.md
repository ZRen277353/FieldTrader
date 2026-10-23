# 市场模块：架构核查与实施计划

基线：main / 8e16c2de71f445cbe2c929ae457b44b797e9aa3b。

## 实际架构

- `FieldTrader.csproj`：net8.0-windows / WPF，仅 NAudio 2.2.1。没有 YOLO、RapidOCR、ONNX Runtime 或 SQLite 依赖，也没有已接入的视觉模型。
- `SessionState`：共享 WPF 状态，80ms DispatcherTimer；音频回调提交 RecognitionPipeline。`RecognitionPipeline` 使用两个有界 Channel，检测和匹配各自 Task.Run。不得将视觉推理或数据库写入放入这些回调。
- `WaveformRecognitionService`：现有听声识物实现。`VoiceBoardWindow` / `VoiceMicrophoneMixer`：独立麦克风混音、设备生命周期及故障事件。市场模块不得停止、重建或占有这些对象。
- `CatalogStore`：catalog.json，临时文件替换；不是关系数据库。市场历史使用独立存储，不能混入声音样本目录表。
- `MainWindow`：事件驱动 WPF，HUD 共用 SessionState。后续市场面板只通过 Dispatcher 消费不可变快照。

## 接口与文件改动（先规划，后实施）

1. 新增 `Market/MarketCore.cs`：不可变页面证据、截图/视觉接口、同帧融合、路线、保守利润、巡查公平排序；统一单交易模拟执行器。默认没有真实鼠标实现。
2. 新增 `Market/MarketJournal.cs`：独立 JSONL 持久事件、重启去重、历史统计；挂单与成交事件分开。正式并发/大规模部署前迁移 SQLite WAL + 唯一约束 + 事务。
3. 新增 `Market/MarketValidation.cs` 与独立验证项目：不加载 WPF/音频的可重复核心验证。
4. 后续阶段才新增 Windows 截图、YOLO11m ONNX、PP-OCRv4 适配器。需要用户实际权重、类别映射、预处理、OCR字典、窗口缩放及脱敏页面标注；不能把通用模型输出视为已校准证据。
5. 后续新增 WPF 市场面板、独立限速 worker（容量1，丢弃旧截图）、停止/暂停/预算与日志。性能验证需同时运行音频与混音，验证回调延迟和丢帧。
6. 最后在符合运营规则的授权环境接鼠标执行器：每次操作前后重新截图、检查前台窗口、坐标变换、证据时效、页面与物品一致性；未知结果冻结并人工对账。没有授权及真实页面验收时保持模拟。

## 交易约定与阶段门槛

材料购买→联络人兑换→成品出售必须保存配方、逐步材料成本和确认凭证；子弹/钥匙使用独立路线配置，不能套用兑换路径。仓库只提供存在性，数量来自交易弹窗；记录确认购买数量。售价取游戏推荐价，但不能声称它是平均成交价。普通600/会员1200、每单同类1–5件为用户给定规则，必须由页面OCR核验剩余额度与周周期；额度未知禁止上架，到期不返还次数。

决策：保守售价折扣后的收入−全部采购成本−预计手续费−上架费−风险准备金 > 最低利润。挂单仅占用额度和记承诺收入，实际成交才确认收入；价格变化拒绝是正常终止，不自动追价。

阶段1交付独立核心及可回放证据、日志、验证源；阶段2接模型并进行标注回放验收；阶段3接UI和采集worker、音频并行压力验证；阶段4仅在规则允许且页面验收后逐步开放真实操作。前三阶段不产生真实交易。

## 规则核查（2026-10-21）

仓库描述对应《暗区突围：无限》。官方安全中心已有AI视觉识别脚本获利处罚案例：
https://www.arenabreakoutinfinite.com/act/a20250421safecentercag/detail.html?Language=EN&content_id=3b387f53a7877a4178aac13ad515d38bcc38&current_tab=0

官方行为准则包含作弊及扰乱经济环境处罚：
https://www.arenabreakoutinfinite.com/act/a20250421safecentercag/punish_rules.html?Language=zh-TW

这些来源不能证明自动交易被允许；屏幕识别和模拟输入也不能视为安全豁免。不实现隐藏输入、驱动注入、进程读写或绕过反作弊。
