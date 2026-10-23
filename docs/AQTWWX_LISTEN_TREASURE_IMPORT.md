# 暗区情报站行商图鉴导入

## 来源

- 页面：[行商听声鉴宝练习](https://aqtwwx.net/listen-treasure)
- 采集日期：2026-10-23
- 页面图鉴：17 种拾取音效、138 件藏品
- 本地快照：`tools/AqtwwxListenTreasure/snapshot-20261023.json`

页面为动态图鉴，物品数据由站内接口提供。本次采集以浏览器中实际渲染的“声音图鉴”为准，逐个读取每个声音组下的物品卡片。

## 字段映射

- `name` → `Item.Name`
- 页面价格 → `Item.Price`
- 页面品质颜色 → `Item.QualityColor`，并映射为 `Item.Quality`
- 图片 `/images/items/<id>.png` → `SeedData/images/aqtwwx-listen-<hash>.png`
- 页面 `rows`、`cols` → `GridHeight`、`GridWidth`
- `cols > rows` → 横放；`cols < rows` → 竖放；相等 → 方形
- `soundId` → `SoundGroupId`
- `/static/listen-treasure/sounds/<soundId>.wav` → `SoundSamples`

页面当前有两件藏品没有可用图片：`玉制手串`、`USP电源`。这两件保存为待补图，不复制占位图冒充真实图片。

## 结果

- 物品：138
- 实际图片：136
- 待补图：2
- 声音组：17
- 声音样本：17

品质颜色分布：红色 33、金色 55、紫色 50。候选图片画布使用对应品质色调，透明像素直接显示该颜色，文字区保持中性背景。

## 重新导入

```powershell
pwsh -NoProfile -File tools/AqtwwxListenTreasure/import.ps1
```

脚本会清空 `SeedData/images` 和 `SeedData/samples` 后重新下载图片与声音，校验 PNG/WAV 文件头，并生成 `SeedData/catalog.json`。缺少图片的单个条目会保留为待补图，其他下载或格式错误会中止导入。
