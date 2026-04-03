# 配置说明

CLI 与 UI 共享一份 config.json 结构。可以直接使用 src/SeedRollerCli/config.example.jsonc 作为模板（删除注释后存为 .json）。

`jsonc
{
  "gameDataPath": "I:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64",
  "startSeed": "1234567890",
  "count": 50,
  "mode": "random",
  "character": "ironclad",
  "ascension": 0,
  "resultJson": "seed_results.json",
  "filter": {
    "kind": "positive",
    "relicTerms": ["诅咒"],
    "relicIds": ["ARCANE_SCROLL"],
    "cardIds": ["COMPACT"],
    "potionIds": []
  }
}
`

## 字段详情

| 字段 | 说明 |
| --- | --- |
| gameDataPath | 必填。游戏 data_* 目录。也可通过环境变量 STS2_DATA_PATH 传入。 |
| startSeed | 种子初值。仅当 mode 为 incremental 时会逐个递增。 |
| count | 需要尝试的种子数量。 |
| mode | andom（与游戏一致）或 incremental。 |
| character | ironclad / silent / egent / 
ecrobinder / defect。 |
| scension | 心境（Ascension）等级，范围 0~20。 |
| esultJson | 保存筛选结果的文件。默认 seed_results.json。 |
| ilter.kind | positive（正向）或 curse（代价项），留空则不过滤。 |
| ilter.relicTerms | 按遗物名称 / 描述 / 备注进行模糊匹配的关键字数组。 |
| ilter.relicIds | 遗物 ID 数组，表示必须命中指定遗物。UI 中可直接从下拉列表添加。 |
| ilter.cardIds | 需要出现在奖励详情中的卡牌 ID，全部命中才算匹配。 |
| ilter.potionIds | 需要命中的药水 ID。 |

> seed_info.json 已作为内置资源随发布包更新，普通用户无需再选择。若可执行文件目录存在新的 seed_info.json，程序会自动覆盖内置版本；开发者也可以在 CLI 中使用 --seed-info 来测试其他路径。

> 提示：若希望“只指定遗物 ID”，可以把对应条目在 UI 的“遗物/卡牌/药水筛选”面板中逐一加入，elicTerms 留空即可。

## 结果结构

seed_results.json 会包含以下字段：

- generatedAt：生成时间。
- 	otalSeeds：总共尝试的种子数。
- matchedSeeds：符合筛选条件的种子数。
- matchedOptions：命中的选项总数。
- seeds：数组，元素为 { "seed": "1234567890", "options": [...] }。
- options：每个元素包含 kind（Positive/Curse）、elicId、	itle、description、details（奖励内容列表）。

奖励详情（details）会列出生成的卡牌/药水/金币等，名称由内置或外部 seed_info.json 决定。
