# MerchantHelper 物品与声音导入

## 来源

- 仓库：[A1Chao/MerchantHelper](https://github.com/A1Chao/MerchantHelper)
- 提交：`5a0f5249cd5c7b4522866ba962952326b5fed23e`
- 数据：`assets/database.json`
- 素材：`assets/images/*.png`、`assets/sounds/*.wav`

上游说明为仅供学习交流、禁止商业用途。图片、声音和游戏数据的权利归各自权利人；本项目只做本地格式转换，不改变其归属。

## 合并规则

- 当前库已有同名物品，或属于明确名称变体的，不重复添加。
- MerchantHelper 共 167 件物品，其中 98 件已存在或已由名称变体覆盖，实际新增 69 件。
- 普通物品按 `sound_type` 共享声学组；`special_items` 使用各自的独立声音组。
- `slot + orientation` 转换为当前项目字段：
  - `1 + 正` → `1 × 1`
  - `2 + 横` → `2 × 1`
  - `2 + 竖` → `1 × 2`
  - `4 + 正` → `2 × 2`
  - `6 + 横` → `3 × 2`
  - `6 + 竖` → `2 × 3`
- 图片与 WAV 使用稳定哈希文件名复制到 `SeedData/images` 和 `SeedData/samples`。
- 物品和样本通过 `SourceName`、`SourceRecording`、`SourceBatch` 保留来源。

## 导入结果

- 新增物品：69
- 新增物品图片：69
- 新增声音组：23
- 新增声音样本：23
- 合并后：178 件物品、47 个声音组、412 段声音样本

## 重新生成

```powershell
pwsh -NoProfile -File tools/ImportMerchantHelper.ps1 -Source <MerchantHelper仓库目录>
```

脚本按名称去重并使用确定性 ID，不会覆盖已有物品。实际导入后仍需运行安装级验证，确认图片、声音、模板和重复初始化均通过。
