# 作者与第三方组件

## 本工具

作者：取名字真麻烦；抖音号 21360166462；QQ 3445838743。

作者声明：**本软件免费使用，禁止倒卖。转载分发请保留作者信息与说明。** 原文见 [作者说明](../src/FieldTrader/作者说明.txt)。本仓库没有将第三方组件或素材改为本工具作者所有。

## VB-CABLE

- 开发者：Vincent Burel / VB-Audio Software。
- 官网：https://vb-audio.com/Cable/ 、https://www.vb-cable.com/
- 许可及分发规则：https://vb-audio.com/Services/licensing.htm （VB-CABLE Distribution with other product）
- 标准版属于 **Donationware**，欢迎用户觉得有用时自愿付费支持 VB-Audio 作者。
- 本项目保留标准包的完整 31 个原始文件、签名、说明和原始许可，不含 A+B、C+D 扩展包。
- 来源包：`VBCABLE_Driver_Pack45.zip`，SHA256：`B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB`。
- 原始说明与许可：[readme.txt](../src/FieldTrader/ThirdParty/VB-CABLE/original-package/readme.txt)。
- 文件清单与校验：[vbcable-manifest.json](../src/FieldTrader/ThirdParty/vbcable-manifest.json)。
- 完整中文说明：[第三方组件说明.txt](../src/FieldTrader/ThirdParty/第三方组件说明.txt)。

标准包中的原始 readme 保持原样；当前公开分发规则允许在标明来源且保留 Donationware 模式的条件下随应用分发。系统驱动安装仍由官方安装器完成，需要管理员确认和按提示重启。卸载本工具不会自动移除共享驱动。

## NAudio

项目通过 NuGet 引用 [NAudio 2.2.1](https://www.nuget.org/packages/NAudio/2.2.1)，上游源码与许可见 [NAudio](https://github.com/naudio/NAudio)。NAudio 使用 MIT 许可；随可运行版本提供其原始许可副本。

## MerchantHelper 数据与素材

- 来源：[A1Chao/MerchantHelper](https://github.com/A1Chao/MerchantHelper) 的 `assets/database.json`、物品图片和声音文件。
- 快照提交：`5a0f5249cd5c7b4522866ba962952326b5fed23e`；文件 SHA-256：`6D82472F750D64478B887F73804C6BC8A79E65CAB15A372742E89E29064C8F56`。
- 本软件读取物品名称与 `reference_value` 生成名称下方的 `W` 参考价，不调用联网接口，也不声称它是实时成交价。
- 导入时跳过当前库已有名称及明确名称变体，新增 69 件物品；其图片和声音按当前项目的图片路径、声学组、样本、占格和方向字段保存。
- 上游说明为仅供学习交流、禁止商业用途；本软件不改变 MerchantHelper、游戏数据或相关素材的权利归属。价格快照说明见 `src/FieldTrader/PriceSeed/MerchantHelper/README.md`，物品与声音导入规则见 [MERCHANTHELPER_IMPORT.md](MERCHANTHELPER_IMPORT.md)。

## 游戏图像、声音与语音素材

游戏图像、游戏音效与其他音频由用户提供的截图/录音/视频整理而来；相关权利归各自权利人。声音样本来源批次及时间范围可在 `SeedData/catalog.json` 中查看，语音音效备注位于 `VoiceSeed/library.json`。

“战地行商 · 听声识物”是独立工具，非游戏官方产品。作者署名指本工具的开发与资料整理，不代表拥有游戏素材或其他第三方音频的著作权。
