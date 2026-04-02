# seed_info.json 结构

`seed_info.json` 为静态资料表，用于在 CLI/UI 中显示中文的遗物、卡牌与药水名称/描述。文件格式示例：

```json
{
  "generatedAt": "2026-04-01T12:00:00Z",
  "language": "zhs",
  "options": [
    {
      "relicId": "ARCANE_SCROLL",
      "kind": "positive",
      "title": "奥术卷轴",
      "description": "拾起时，将一张随机稀有牌加入你的牌组。"
    }
  ],
  "cards": [
    { "id": "COMPACT", "name": "压缩" },
    { "id": "ITERATION", "name": "迭代" }
  ],
  "potions": [
    { "id": "POTION_OF_GLASS", "name": "玻璃药剂" }
  ]
}
```

## 字段说明

- `options`：按遗物 ID 存储第一层选择的标题与描述。`kind` 仅用于提示（可选）。
- `cards` / `potions`：映射 ID 到中文名称。缺失时会退回英文原文。
- `generatedAt` / `language`：元数据，可为空。

## 更新方式

1. 启动游戏或 MOD，获取最新文本数据。
2. 将遗物/卡牌/药水的 ID 与本地化名称导出到上述结构中。
3. 将文件命名为 `seed_info.json` 并与 CLI 或 UI 可执行文件放在同一目录。

> 若希望在不同语言之间切换，可以准备多份 `seed_info.json`，再通过 `--seed-info` 参数（或 UI 设置）指定使用哪一份。
