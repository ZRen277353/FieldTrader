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

## 暗区情报站行商图鉴数据

- 来源：[暗区情报站 · 行商听声鉴宝](https://aqtwwx.net/listen-treasure)。
- 采集日期：2026-10-23；公开页面图鉴包含 17 种拾取音效和 138 件藏品。
- 本软件导入物品名称、页面标注价格、占格、横竖方向、图片和拾取音效，不调用联网接口，也不声称价格是实时成交价。
- 图鉴页说明音效与数据仅供练习参考，与游戏官方没有直接关系。图片、声音和游戏数据的权利归各自权利人。
- 采集快照与字段映射见 `tools/AqtwwxListenTreasure` 和 [AQTWWX_LISTEN_TREASURE_IMPORT.md](AQTWWX_LISTEN_TREASURE_IMPORT.md)。

## 游戏图像、声音与语音素材

游戏图像、游戏音效与其他音频由用户提供的截图/录音/视频整理而来；相关权利归各自权利人。声音样本来源批次及时间范围可在 `SeedData/catalog.json` 中查看，语音音效备注位于 `VoiceSeed/library.json`。

“战地行商 · 听声识物”是独立工具，非游戏官方产品。作者署名指本工具的开发与资料整理，不代表拥有游戏素材或其他第三方音频的著作权。
