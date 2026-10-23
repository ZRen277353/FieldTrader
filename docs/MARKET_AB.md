# 阶段 A/B：模型接入、行情采集与巡查基础

## 实施范围

继续使用第一阶段 MarketCore / MarketJournal / MarketValidation 和既有 WPF 应用。主程序新增“市场巡查”入口，原音频、混音、HUD、设备生命周期未改。当前界面开放截图回放、前台游戏只读采集、连续只读采集、目录确认和导航预演。真实鼠标适配器默认未启用；搜索键盘输入、真实导航校准及游戏验证属于阶段 E，当前不声称已通过。

## 阶段 A

- 实际微调模型是 YOLO11m，14 类：product_card、detail_panel、trade_panel、confirm_dialog、result_region、primary_button、secondary_button、quantity_field、price_field、notification、material_row、material_card、listed_item、market_entry。这是界面区域分类，不是游戏商品目录，不能据此识别无文字的仓库物品身份。
- PythonVisionPipeline 启动单个独立低优先级进程，加载原 .pt 文件。vision_worker.py 校验 SHA256 和类别列表，复用本机 Ultralytics、PyTorch、RapidOCR 与三份 OCR ONNX；没有导出、下载或训练模型。YOLO 与 OCR 使用同一张冻结截图并行推理；字段小数字另做局部识别。各模型 CPU 线程限制为2，最多可配置4。
- 所有输出带截图ID、SHA256、时间、原始像素尺寸、归一化坐标、各自置信度与实际耗时。启动、推理、读写超时或协议错误终止进程并暂停采集；取消令牌和紧急停止可中断等待。
- ImageFileCapture 保存历史文件修改时间作为回放时间，**不是经过验证的真实拍摄时刻**。GameScreenCapture 只截前台 UAGame 可见客户区，记录窗口、屏幕原点和像素尺寸，采用当前线程DPI上下文，资源均释放。实机截图路径已编译，尚未做真实游戏验收。
- MarketPerception 用页面锚点、框内包含关系和唯一候选关联标题、价格和数量。卡片底部价格不能跨卡片配对；交易弹窗屏蔽背景卡片。金额只接受整数/规范千分位，不猜测K、字母或小数点。数量漏读保留null，不补1。OCR置信度和YOLO置信度分别保留。
- Sell页面采集的是“当前显示的出售单价”，还不能证明它未经用户修改或就是推荐售价；不用于自动出售。

## 阶段 B

- MarketDatabase：独立 SQLite WAL，参数化SQL、事务写入、外键、schema版本迁移；与 catalog.json 音频目录分离。
- ItemCatalog：规范名称唯一，OCR发现的商品默认为未确认候选；人工确认/目录导入后进入巡查队列。原声音物品目录不会自动视为市场可识别目录。
- PriceObservations：价格、可空数量、UTC观察时间、OCR/检测置信度、截图ID/哈希、位置、上下文、来源、回放标记。Reference、OcrObservation、ActualFill分别查询；导入历史成交需独立凭证ID，但本阶段不生成成交记录。
- 双重去重：同图同区域不重复；同商品、价格、数量、上下文5分钟内去重，避免动态背景截图提高同报价权重。历史回放不进入实时统计或覆盖率。
- PriceStatistics、HourlyPriceStatistics 提供SQL基础聚合；C#统计接口补充线性插值中位数/P25/P75。按国服UTC+8分小时与星期；参考、实际观察、实际成交和不同报价上下文不混合。历史最高价未用于任何交易决策。
- MarketScheduler：已确认商品目录、最长6小时未巡查优先、5分钟最小间隔、失败指数退避（最长15分钟）。其余商品按波动、达到样本门槛的当前星期/时段价格和外部配方优先级排序；配方引擎本轮未实施。覆盖率为24小时有效实时采集商品数/已确认目录数，候选和错误单独展示。
- NavigationPlanner：OCR精确匹配商品文字，要求同时位于YOLO商品卡片；识别搜索占位文字、分类与“下一页”文字。当前样例没有文字分页锚点的页面会报告不支持，不伪造翻页成功；滚动条拖动未实现。搜索支持计划和预演，真实键盘输入未接入。未知布局需要新截图及配置。
- MarketWorkflow：统一 Capture→Vision→Fusion→Plan→Mouse→Verify，同一信号量独占采集/导航。每步新截图验证页型和内容变化；最多5步，未知状态暂停，失败退避，验证码/反作弊提示暂停。RunPatrolAsync可由模拟适配器或未来通过实机验收的导航适配器驱动；A/B面板只调用模拟适配器。
- WindowsNavigationMouse：普通Windows点击，检查前台、窗口尺寸/位置、证据时效和禁止交易按钮。默认未武装、面板没有开启真实输入的开关。没有游戏进程读写、注入或规避功能。

## 运行

配置参考 `tools/MarketReplay/vision-config.example.json`。所有路径需指向已有本机文件；模型和Python依赖不纳入Git。复制个人配置为程序旁 `market-vision.local.json`，或在市场面板选择配置。程序保存本机配置路径到现有用户数据目录的 `market/config-path.txt`。

原程序入口增加以下验证参数：

```
FieldTrader --market-ab-test <报告目录>
FieldTrader --market-vision <配置JSON> <截图目录> <报告目录>
FieldTrader --market-integration-test <配置JSON> <截图目录> <报告目录>
FieldTrader --market-ui-preview <识别证据JSON> <预览目录>
```

已有 tools/MarketReplay 同样支持 `--stage-ab-test` 和 `--vision`，用于无WPF测试；没有新增独立产品项目。

商品目录JSON是 CatalogItem 数组。字段为 Id、Name、Category、Material、Product、Special、Confirmed。未验证商品设Confirmed=false；不要根据价格或模型类别自动设为可交易。导入时同一规范商品名保留原内部ID，避免破坏已采集历史引用。

## 验收与剩余工作

本机实际编译、核心测试、模型回放、并行软件流水线测试结果见交付目录的阶段AB验收报告。历史训练图/开发验证图只用于接入和回归，不能报告为独立准确率。部分联络人/提示页面返回Unknown是受支持范围边界，完整兑换逻辑留给阶段C/D。

实时截图、连续只读采集、窗口缩放与前台保护需要真实游戏手工验收；原音频设备与虚拟麦硬件未在本轮打开。下一轮应提供独立新截图：市场完整列表/搜索输入/搜索结果、分类切换前后、翻页或滚动前后、购买弹窗（数量清晰）、未知/无结果提示；同时覆盖常用分辨率和缩放。

完成A/B软件接入与离线验证不代表全市场覆盖或实机巡查完成。主窗口继续使用原用户数据路径，新市场库和截图保存在其 market 子目录；连续采集会持续保存证据，用户应按磁盘空间停止和归档。
