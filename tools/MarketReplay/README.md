# 市场回放（无游戏输入）

阶段A/B已接入主WPF程序，新增真实模型推理和SQLite采集流程，详见 `docs/MARKET_AB.md`。下文是第一阶段模拟交易入口，其限制仍成立；不要把它与真实成交混淆。

`dotnet run --project tools/MarketReplay -- --test`

`dotnet run --project tools/MarketReplay -- replay.json output-directory`

回放入口接受 Replay 结构（见 Program.cs）：Now、Budget、OcrRemainingListings、Observations、Steps。每个步骤含 Step、Before、After；每个页面含 Frame、Yolo、Ocr。坐标为0–1归一化。必须来自同一截图的两个识别器且页面、规范物品ID一致；截图时间不能伪装成当前时间用于真实决策。回放使用输入的Now以保证可复现。

输出 observations.jsonl 与 simulation.json。重复导入同一观察ID不会重复计价；日志损坏直接报错。该工具只验证输入证据并生成模拟动作事件，没有任何鼠标执行能力。截图路径仅作为证据引用，当前不会加载图片或运行模型。日志目前只支持单进程写入。

上面的旧 JSON 回放是阶段1入口，预算和额度只在该执行器生命周期内保留。新的 C/D 入口使用 SQLite 持久化状态、兑换利润分析、特殊商品规则与模拟账本，详情见 `docs/MARKET_CD.md`。两种模拟入口都不能用于真实账务。
# 初始参考目录

`--seed <snapshot.json> <database.sqlite>` 将带来源的初始目录一次性导入指定 SQLite 数据库；重复运行不会追加。GUI 首次打开市场窗口时自动应用打包的 `MarketSeed/aqtwwx.json`。

`--stage-ab-test <output>` 同时验证初始导入、事务回滚、旧版本升级、人工设置保护、来源隔离和完整目录调度。原始网页分页快照可用 `prepare_market_seed.py <pages.json> <output-directory>` 转换，详见打包数据目录 README。

`--stage-cd-test <output>` 运行隔离的兑换/特殊路径闭环、费用、额度、UNKNOWN、重启恢复、过期挂单、库存批次测试；所有测试配方都是虚构夹具。

`--cd-vision <config.json> <image-directory> <output>` 使用真实本地模型解析图片，同时输出额度、费用、价格变化拒绝和购买结果字段。历史图片保持 Replay 来源，不计真实成交或实时巡查覆盖。
