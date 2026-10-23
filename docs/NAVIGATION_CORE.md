# 通用导航核心：Mac 开发阶段

基线：`09700c7`。本阶段只增加导航基础与离线验证，不实现购买、兑换、出售或上架提交，不更改听声识物、混音、音频线程或交易账本语义。代码在 `FieldTrader.Navigation` 命名空间，避免与旧 `FieldTrader.Market.NavigationPlanner` 重名。

## 实际架构审计

| 已有文件（相对 src/FieldTrader/Market） | 实际类/接口 | 本次复用与边界 |
| --- | --- | --- |
| `MarketCore.cs` | `Region`、`VisualToken`、`Frame`、`MarketPage`、`EvidenceFusion.Fuse`、`MarketExecutor.SimulateAsync` | 复用几何与证据类型。EvidenceFusion 是早期双引擎相互同意页型/物品 ID 的融合实现 |
| `VisionModels.cs` | `CapturedScreen`、`VisionResult`、`IScreenSource.CaptureAsync`、`IVisionPipeline.AnalyzeAsync`、`Geometry`、`ImageFileCapture` | 直接复用；包含 ID、哈希、时间、原始像素尺寸、窗口句柄、屏幕原点与 Live 标志 |
| `PythonVisionPipeline.cs` | `PythonVisionPipeline : IVisionPipeline` | 一个持久 Python 进程；JSONL 通讯；单请求门锁；60 秒超时终止 worker；取消处理 |
| `vision_worker.py` | `detect` / `read`、Ultralytics YOLO、RapidOCR | 同一冻结截图并行推理，校验 SHA256/类别，返回原图归一化坐标；有字段 ROI OCR。具体模型文件由本机配置提供，不在本次测试中加载 |
| `MarketPerception.cs` | `MarketPerception.Fuse`、`CatalogItem`、`PerceptionResult` | 实际运行的页面、卡片、报价融合；新 `MarketPageClassifier` 委托它识别已支持的页型 |
| `MarketDatabase.cs`、`MarketSeed.cs` | `MarketDatabase`（partial）、目录/价格/统计/失败退避、种子导入 | 保留 A/B 市场采集业务，不重复实现 |
| `MarketScheduler.cs` | `MarketScheduler.Next`、`ScheduledTarget` | 6 小时覆盖优先、5 分钟最小间隔、失败退避；后续提供导航目标 |
| `MarketWorkflow.cs` | 旧 `NavigationPlanner.Plan`、`IMouseExecutor`、`MarketWorkflow.NavigateAsync/RunPatrolAsync/CollectAsync` | 已有最多五步市场导航和预演；保留原入口，尚未将 WPF 巡查切换到新核心 |
| `TradingDomain.cs`、`TradingAnalysis.cs` | `ExchangeProfitEngine`、`ListingQuotaManager`、`TradingAnalysis.Observe`、`SimulationRunner` | C/D 兑换、特殊商品分析和模拟流程；不重写业务 |
| `TradeScreenReader.cs` | `TradeScreenReader.Read`、`TradeScreenFacts`、`PurchaseReceipt` | 独立读取交易结果/额度/上架证据；导航结果绝不替代交易凭证 |
| `TradingSimulation.cs`、`TradingStore.cs` | `TradingSimulation`、`TradeCommand`、`TradeProof`、`SimulationPhase`、模拟持久化 | 已有 pending/Unknown、幂等、预算、库存与逐笔验证；仅将 TradeSession 锁指向统一导航锁 |
| `MarketJournal.cs` | `MarketJournal.Append` | 原有 JSONL 会计日志；新导航日志独立，不把导航成功写成成交 |
| `Windows/GameScreenCapture.cs` | `GameScreenCapture : IScreenSource` | DPI 上下文、前台 UAGame 客户区截图、屏幕原点和尺寸；本次未在游戏运行 |
| `Windows/WindowsNavigationMouse.cs` | `WindowsNavigationMouse : IMouseExecutor` | 原有真实点击适配器。旧 armed=true 入口现已停用，避免绕过新调度/本地测试限制；类型与构造函数保留 |
| `Windows/MarketWindow.cs`、`Windows/TradingPanel.cs` | 市场 WPF 入口 | 已有采集/预演 UI；本次不增加真实交易开关 |

仓库中不存在名为 **DetectionFusion** 的类。现有融合分布在 `EvidenceFusion`、`MarketPerception` 与 `TradeScreenReader`，不能假称有这个接口。YOLO11m / PP-OCRv4 的来源、14 类及本地配置约定见 `MARKET_AB.md`；本轮测试使用同协议模拟输出，不表示真实模型精度验收。

A/B 已有模型与采集、目录/统计/调度；C/D 已有兑换/交易分析与模拟账本；E 有 Windows 截图和输入代码，但真实页面覆盖、导航校准与游戏测试仍未完成。

## 新增模块与接口

代码位于 `src/FieldTrader/Market/Navigation/`，WPF 自动编译这些源码；独立 `src/FieldTrader.Navigation.Core` 项目链接同一套源码和三个已有市场类型文件。这样不复制逻辑、不让 WPF 引用带重复 Market 类型的第二份程序集。`MarketReplay.csproj` 也链接导航源码，保证旧 TradeSession 解析到同一进程内的静态锁。

| 文件 | 主要接口/职责 |
| --- | --- |
| `Contracts.cs` | `NavigationTask`、`NavigationStep`、`TargetQuery`、`VisualTarget`、`ActionBatch`、状态/模式/错误类型；`IInputController`、`IPageClassifier`、`INavigationLog`、`NavigationControl`、`NavigationInputGate` |
| `VisualTargetResolver.cs` | `Resolve(PageObservation, TargetQuery)` 一次推理可解析多个目标；`CoordinateTransform.ToScreen/FromLetterbox`；`MarketPageClassifier.Classify` |
| `NavigationPlanner.cs` | `NavigationPlanner.Locate` 根据任务步骤和当前页定位；`BatchActionPlanner.Plan` 按页型、顺序、动态边界、关键动作、数量/时间限制生成当前可执行段；`NavigationSafety.Check` |
| `NavigationVerifier.cs` | `Before`：新证据验证旧坐标仍有效；`After`：独立新截图核对预期页、结果文字、错误、布局与窗口 |
| `NavigationExecutor.cs` | `RunAsync(task, control, token)`；整任务互斥、状态日志、执行/取消/超时/暂停/重试；`SimulatedInputController` 与持久化 `JsonNavigationLog` |
| `NavigationRecipes.cs` | `OpenMarketProduct` 从已确认 CatalogItem 与校准查询生成浏览任务；`BrowseContact` 接受校准路线并限制终点为 Contact |
| `../Windows/WindowsInputController.cs` | 复用已有窗口/DPI API，普通 SendInput 点击/移动/滚轮，只允许当前进程显式指定的本地测试窗口 |

步骤包含当前预期页、目标名/视觉类、区域与上下文约束、操作、后置页、超时、失败策略、批量许可、布局影响、关键动作标志、预期结果文字、可选 ExpectedResultTarget（区域/视觉类别/上下文约束）与滚动量。任务携带三种执行模式、批次模式、最大动作数、批次时限、动作间隔、重试次数与证据时效。

当前页型继续使用已有 MarketPage 字符串。市场首页、分类/列表的细分应由经过标注验证的 `IPageClassifier` 返回更细粒度名称；本轮不虚构其视觉锚点。

## 目标融合与坐标约定

匹配同时要求：

1. 推理输出与截图的 ID、SHA256、时间、尺寸一致，所有框与置信度合法。
2. OCR 名称精确规范化匹配，处于指定区域内的指定 YOLO 类别框中。
3. 指定上下文文字出现在指定上下文区域。
4. 一个框内同名文字唯一，且该文字不落在多个候选框；执行一步还要求目标结果唯一。
5. 出现交易弹窗时，不允许解析背景商品目标；只有明确的关闭/返回/取消按钮例外。

结果保留名称、类别、框、点击坐标、置信度、截图 ID/时间/哈希。`Resolve` 返回数组，业务可对同一 VisionResult 的多个目标分别筛选，不需要重新运行模型。若同名物品不唯一，应缩小区域或增加上下文，不随便选第一个。

现有 worker 已输出**原始客户区像素归一化**坐标；不能再做一次 letterbox 反变换。`FromLetterbox` 只用于未来尚未还原的模型输出。`ToScreen` 使用客户区物理尺寸与屏幕原点，允许负数多显示器坐标；WPF DIP 不能直接传入，也不能重复乘 DPI。

## 批量行为与验证

- **A 稳定页**：多个显式 Stable 步骤可共享原截图定位结果和顺序。每个动作前仍取新截图，验证原框、目标、上下文和布局完全一致，之后才复用原点击坐标。每步后验证结果，同时在批末完成最终验证。
- **B 动态分段**：可合并稳定前缀，但弹窗、切页、滚动或任意 MayChange 动作必须是最后一个动作。验证后下一段重新截图、识别、定位。A 模式遇到动态动作会单独形成 B 段。
- 默认最大 8 动作（硬上限 32）、最长 10 秒、100ms 间隔；每步独立超时。每个动作之间均可中断。一次原生 down/up 点击是不可拆分的小边界，避免取消后鼠标一直按下。
- 关键动作强制独立批次并前后验证。当前实现比“只在批末验证”更保守：每步后都取图，所以不以效率换取不受控旧坐标。
- 任何已知布局/窗口变化、目标消失、错误提示、模糊识别、取消、暂停或超时都会停止后续操作。指纹不做坐标舍入；模型抖动也可能让批次保守停止。
- 同页 Click 必须有**点击前不存在、点击后存在的预期文字标记**，否则暂停；“还在原页”或“鼠标调用返回”不算动作成功。未来需支持无文字选择状态时，应新增经验证的状态证据，而非放宽此约束。
- 如果截图与输入之间出现极短暂 UI 变化，仅凭软件截图无法完全消除竞态。当前通过即时重验与 Windows 前台/窗口重验缩小间隔；它不是任意游戏页面的稳定性保证。

普通导航只允许已列出的浏览类别，拒绝 primary_button、交易类别和购买/出售/兑换/上架/确认/提交等文字。secondary_button 仅允许关闭、返回、取消等明确语义。导航没有资源提交 API。资源操作继续交给独立 TradingSimulation/TradeProof 逐笔验证，本轮不会启用真实交易。

## 状态、互斥与恢复

`IDLE → CAPTURE → RECOGNIZE → LOCATE → PLAN → EXECUTE → VERIFY → SUCCESS`。

EXECUTE 日志区分 SINGLE_ACTION/BATCH_ACTION，并记录任务、步骤、截图和原因。失败按策略进入 RETRY/FAILED/PAUSED。

- `NavigationInputGate.Shared` 独占整个导航任务；多个 Executor 实例也无法交错控制鼠标。旧 MarketOperationGate.TradeSession 现在指向此锁。不要在持有该锁的旧 NavigateAsync 内再次调用新 RunAsync，否则会重复取锁。
- 锁是进程内锁。未来多进程输入必须另加系统级独占；本版没有多进程导航功能。
- `RetryFresh` 仅用于尚未发出输入的证据失败，每次重新捕获并拒绝复用 ID 或非递增时间。输入之后结果不明一律暂停，不盲目重复点击。
- `Pause()` 使当前任务在最近检查点返回 PAUSED；`Resume()` 后由调用方提交新 RunAsync，重新捕获和评估当前页面，不能续用旧 ActionBatch。取消后不能假定最后一步未发生。
- `EmergencyStop()` 为控制对象锁存状态。新任务要创建新控制对象；多个 UI 任务应共享同一个会话控制对象，才能一键停止整个会话。
- 截图/推理等待可取消；输入适配器必须在发出输入前观察取消，并在输入结束后才返回。对于不守约的挂起适配器，执行器不会提前释放鼠标锁让另一任务接管，避免晚到输入并发。
- JSONL 日志独立持久化并 Flush；日志写入失败会阻止继续执行，不写入交易账本。

## 三种模式

- **RecognizeOnly**：只捕获、识别并生成当前段计划，不调用输入；返回 Completed=0，成功仅表示计划生成。可以使用历史 ImageFileCapture。
- **Simulated**：要求模拟控制器；真实鼠标从不调用。多步成功验证需提供会随模拟操作更新的测试画面序列，不能把同一张静态图片伪装成成功导航。
- **Real**：要求非模拟控制器、Live 截图。当前 WindowsInputController 只接受当前进程的显式本地测试窗口。游戏操作未开放，也没有暗中激活旧 armed=true 入口。

## 运行与接入

在仓库根目录，用 .NET 8 SDK：

```sh
dotnet run --project tests/FieldTrader.Navigation.Tests -- artifacts/navigation-results.json
dotnet run --project tools/MarketReplay -- --test
dotnet run --project tools/MarketReplay -- --stage-ab-test artifacts/market-ab
dotnet run --project tools/MarketReplay -- --stage-cd-test artifacts/market-cd
# Mac 仅交叉编译，不是运行 WPF：
dotnet build src/FieldTrader/FieldTrader.csproj -p:EnableWindowsTargeting=true
```

测试项目为无需第三方测试框架的可执行回归套件，失败返回非零退出码并生成 JSON 报告。CI 新增 macOS/Windows 矩阵，但本地运行成功不代表远端 CI 已运行。

### 市场巡查

1. 继续由 `MarketScheduler.Next` 选已确认 CatalogItem。
2. 利用已校准的 product_card 区域和市场上下文构造 TargetQuery，调用 `NavigationRecipes.OpenMarketProduct`。如果商品不在当前页，调用方提供经校准的分类/翻页路线；本模型没有 category_tab/next_page 类时必须报告不支持，不退化到 OCR 坐标点击。
3. 将已有 `IScreenSource`、`PythonVisionPipeline`、`MarketPageClassifier` 和选定输入控制器注入新执行器。
4. OpenMarketProduct 还要求商品名处于 detail_panel 且具有“购买数量”上下文，避免被背景列表同名文字误导。SUCCESS 只表示已进入详情并验证商品证据。随后由原 MarketWorkflow.CollectAsync 重新采集报价、写入数据库、更新覆盖。不要用点击次数推进巡查成功。
5. UI 切换工作在 Windows 阶段完成；不要同时运行旧导航和新导航，也不要在音频回调里调用导航。

### 联络人 / 仓库

继续复用 `TradeScreenReader`、配方目录、任务调度和模拟账本。由业务提供 BrowseContact 的显式路线和页面锚点，抵达 Contact 后仅采集，不点兑换提交。现有 MarketPerception 对完整联络人/仓库覆盖不足，需补新截图、标签和 IPageClassifier 的独立验收。仓库批量选择仅在位置稳定且有可验证结果标记时走 A；物品消失、重排、弹窗一律走 B。

## Windows 待验收

- WPF 导航页面、暂停/恢复/紧急停止按钮和日志展示，接入现有会话生命周期。
- 程序自身本地测试窗口 + Live IScreenSource：窗口定位、实际截图、前台丢失、最小化、移窗、缩放、多屏负坐标、100/125/150/200% DPI。
- SendInput 结构大小与实际点击/滚轮、部分发送后的释放、连续输入取消；本次仅交叉编译。
- 真实 YOLO11m/PP-OCRv4 权重、独立新样本、细分页型、上下文区域、识别延迟及 5 秒原生输入证据上限。
- 原音频监听与混音同时工作的性能/线程验收；本轮未开启音频设备。
- 确认游戏规则允许后才能设计游戏真实输入入口及校准验收；当前适配器不能对 UAGame 发送输入。
- 自动交易双开关、真实购买/兑换/上架均不在本次交付中。

## 本轮实际测试结果（2026-10-23，macOS arm64）

使用微软 .NET SDK 8.0.426。本机实际执行：

| 检查 | 结果 |
| --- | --- |
| 独立导航回归（合成截图/推理协议、模拟输入） | 48/48 通过 |
| 原 MarketReplay `--test` | 通过 |
| 原 A/B 回归（含种子导入） | 44 项通过 |
| 原 C/D 回归（模拟交易） | 34 项通过 |
| 平台无关核心编译 | 成功，0 警告、0 错误 |
| 完整 WPF 项目 Mac 交叉编译 | 成功，0 警告、0 错误 |
| Windows WPF 运行 / 真实截图 / SendInput / 游戏导航 | 未执行，待 Windows 验证 |
| 真实 YOLO11m / PP-OCRv4 权重推理 | 未执行；本轮用同协议模拟输出 |
| 实际购买、兑换、上架 | 未执行且未实现真实提交 |

48 项导航检查覆盖：单/多目标、区域/上下文/类别约束、OCR 单独不能点击、同名歧义、重叠框、非法证据、负屏幕原点/letterbox、单步/多步状态机、稳定复用/动态重定位、布局变化、目标消失、预期页/文字/错误、关键动作隔离、数量上限、取消、暂停恢复、紧急停止锁存、步/批/采集超时、新截图重试、重试耗尽、输入异常不重试、三模式约束、交易目标阻断、过期/未来/重复截图、窗口变化、模拟输入、两个任务整段串行、队列取消、原市场分类器、持久化日志、同页无效果拒绝、弹窗背景阻断、滚轮/移动、输入所有权、业务路线及详情面板结果关联。

最后改动清单：新增 6 个 Navigation 源文件、WindowsInputController、独立核心 csproj、测试 csproj/Program.cs、导航 CI 与本文；修改 TradingSimulation 的共享锁、旧 WindowsNavigationMouse 禁用入口、MarketReplay 链接项及 DEVELOPMENT 文档。没有修改音频、混音或模型 worker。
