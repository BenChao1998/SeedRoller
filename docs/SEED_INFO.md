# seed_info.json 结构

seed_info.json 为静态资料表，用于在 CLI/UI 中显示中文的遗物、卡牌与药水名称/描述。文件格式示例：

`json
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
`

## 字段说明

- options：按遗物 ID 存储第一层奖励的标题与描述。kind 仅用于提示（可选）。
- cards / potions：映射 ID 到中文名称。缺失时会退回英文原文。
- generatedAt / language：元数据，可为空。

## 更新方式

1. 启动游戏或 MOD，获取最新文本数据。
2. 将遗物/卡牌/药水的 ID 与本地化名称导出到上述结构中。
3. 用新的数据覆盖 src/SeedRollerCli/seed_info.json。构建时该文件会被嵌入到 CLI DLL，并被 UI 复用；GitHub Actions 也会把同名文件拷贝到发布包根目录，供需要手动编辑的用户参考。

> 若想测试其他语言，可以放置多份 seed_info.json 并通过 CLI --seed-info 参数指定路径；若把新文件放在 exe 同目录，也会自动覆盖内置版本。
