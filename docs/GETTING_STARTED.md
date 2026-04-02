# 快速开始

本文档帮助你在本地运行 SeedRoller CLI 与图形界面。仓库根目录记为 `<repo-root>`。

## 1. 安装依赖

1. 安装 [.NET SDK 9.0](https://dotnet.microsoft.com/)。
2. 准备《Slay the Spire 2》的 `data_sts2_windows_x86_64` 目录，并确保可从命令行访问。
3. 准备 `seed_info.json`：可以复用游戏内的中文文本，也可以使用仓库随附的示例文件。CLI 与 UI 的默认行为是从可执行文件所在目录读取 `seed_info.json`。

## 2. 运行 CLI

在仓库根目录执行：

```powershell
# Debug 运行
dotnet run --project src/SeedRollerCli/SeedRollerCli.csproj -- --count 20 --character ironclad --seed-info seed_info.json

# 使用配置文件（建议先复制 config.example.jsonc 并去掉注释）
dotnet run --project src/SeedRollerCli/SeedRollerCli.csproj -- --config config.json
```

常见参数：

- `--game-data-path`：指向游戏 `data_*` 目录。
- `--seed-info`：显式指定 `seed_info.json` 路径。
- `--result-json`：结果输出，默认写入工作目录 `seed_results.json`。

CLI 控制台只输出统计信息，匹配的种子与选项会序列化到 JSON，便于后续分析或通过 UI 展示。

## 3. 运行图形界面

```powershell
dotnet run --project src/SeedRollerUI/SeedRollerUI.csproj
```

第一次启动请打开“游戏与资料”分组，填写：

- `Data 路径`：游戏 `data_*` 目录。
- `seed_info.json`：本地化文本文件，可使用仓库同名示例。

之后设置角色、模式与筛选条件，点击“开始 Roll”即可。结果将写入 `ResultJson` 文本框指定的路径（默认 `seed_results.json`）。

若需要生成可分发版本，可参考 [docs/PUBLISHING.md](PUBLISHING.md)。
