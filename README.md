# SeedRoller

SeedRoller 是一个针对《Slay the Spire 2》首层奖励的种子滚动工具，包含：

- **SeedRollerCli**：命令行批量滚动种子，输出结构化 JSON，方便脚本处理。
- **SeedRollerUI**：WPF 图形界面，可视化填写条件、实时查看日志与结果。

整个项目以 .NET 9.0 编写，默认使用 `seed_info.json` 提供的中文文本在控制台/UI 中展示完整的遴选信息。

## 仓库结构

```
├─README.md
├─SeedRoller.sln            # 解决方案
├─docs/                     # 说明文档
├─game_libs/                # 随仓库提供的游戏 DLL（sts2.dll、0Harmony.dll）
├─src/
│  ├─SeedRollerCli/
│  └─SeedRollerUI/
└─.github/workflows/
```

> **注意**：`game_libs/` 下的 DLL 直接来自游戏本体，若要公开仓库，请先确认版权与授权问题；必要时可改为手动复制，自行删除这些文件。

## 快速上手

### A. 本地编译

1. 安装 [.NET SDK 9.0](https://dotnet.microsoft.com/)。
2. 保证 `game_libs/` 已包含 `sts2.dll` 与 `0Harmony.dll`（仓库默认已放置；如需更新可自行替换）。
3. 准备游戏 `data_sts2_windows_x86_64` 目录，并记录绝对路径（若缺省则使用 `game_libs/`）。
4. `seed_info.json` 已作为项目内置资源，发布物会附带同名文件；若可执行文件同目录存在新版 `seed_info.json`，会自动覆盖内置版本。
5. 运行 CLI：
   ```powershell
   dotnet run --project src/SeedRollerCli/SeedRollerCli.csproj -- --count 50 --character ironclad
   ```
6. 运行 UI：
   ```powershell
   dotnet run --project src/SeedRollerUI/SeedRollerUI.csproj
   ```
   在界面中填写 `Data 路径` 后即可开始筛选，遗物/卡牌/药水列表会自动从内置 `seed_info.json` 加载。

### B. 使用 Release 包

GitHub Actions 会在 push / PR 时自动构建 `seedroller-ui-win-x64` 工件；如创建 Release，将同一 zip 附在版本页面。下载并解压后：

1. 根目录会看到 `SeedRollerUI/` 子文件夹、`SeedRollerUI.lnk` 快捷方式以及一份便于编辑的 `seed_info.json`。程序仍以 `SeedRollerUI/` 内部文件为准（`SeedRollerUI.exe`、`game_libs/`、`cli/` 等），根目录的 `seed_info.json` 只是方便查看/替换用的副本。
2. UI：双击根目录的 `SeedRollerUI.lnk`（或手动进入 `SeedRollerUI/` 运行 `SeedRollerUI.exe`），按提示填写 `Data 路径` 与筛选条件即可。
3. CLI：进入 `SeedRollerUI/cli/` 目录运行 `SeedRollerCli.exe --config config.json` 等命令行，功能与 UI 一致。

### SeedRoller UI 使用细节

> UI 分为“游戏与资料 / 基础参数 / 筛选条件 / 运行状态”四个区域，下文逐一说明。

1. **游戏与资料**
   - **Data 路径**：指向你本地的 `Slay the Spire 2\data_sts2_windows_x86_64` 目录。若留空会退回到 exe 同级的 `game_libs/`，该目录只含少量 DLL，无法用于实际运行，请务必填写真实路径。
   - **seed_info 数据**：UI 启动时会加载内置的 `seed_info.json`，并在同级目录存在新版文件时自动覆盖，无需手动选择。
   - **载入配置**：如果你有一份 `config.json`，点击“载入”即可一次性填充上述路径和全部参数，UI 与 CLI 共用同一个结构，详见 [docs/CONFIGURATION.md](docs/CONFIGURATION.md)。

2. **基础参数**
   - **角色**：Ironclad/Silent/Regent/Necrobinder/Defect，等同于 CLI `--character`。
   - **模式**：`Random`（每次重置 RNG）或 `Incremental`（从 StartSeed 递增），对应 CLI `--mode`。
   - **Start Seed / Count / Ascension**：分别对应 `--start-seed`、`--count`、`--ascension`。`Start Seed` 在随机模式下仅用于展示，在递增模式下会逐个加 1。
   - **ResultJson**：结果保存路径，默认写到工作目录的 `seed_results.json`，可以改成任意绝对/相对路径。

3. **筛选条件**
   - **类型**：`正向` / `代价` / `全部`，对应 CLI `--filter-kind`，可快速锁定想要的首层奖励类别。
   - **遗物关键字**：保留原有的模糊查询逻辑，会在遗物 ID、名称、描述和备注里做不区分大小写的包含匹配，适合描述性的需求。
   - **遗物/卡牌/药水筛选**：每个选项区域都提供一个下拉列表（数据源为 `seed_info.json`），以及“添加/删除”按钮。
     - 选择任意条目后点击“添加”即可把精确 ID 加入当前过滤条件，重复添加会被自动忽略。
     - 已选条目会列在下方，点击右侧的 `×` 可删除。最终的 ID 集合会同步写入 `config.json` 并传递给 CLI。
     - 卡牌与药水选择逻辑与遗物一致，可用于进一步锁定奖励详情。

4. **运行状态**
   - 点击“开始 Roll”后，右侧日志会输出初始化进度、警告与命中的种子数；进度条与三个计数器分别表示“已扫描种子 / 命中种子 / 命中选项”。
   - “运行状态”下方会实时显示结果保存路径，点击即可在文件管理器中打开；若需要中断，点击“停止”或关闭窗口，后台任务会被取消。

5. **ID 从哪里来？**
   - 所有 ID 都来自游戏本体的 Model 数据。UI 下拉列表即是读取 `seed_info.json` 后生成的完整目录，通常无需再手动翻查。
   - 运行 CLI/UI 之后生成的 `seed_results.json` 同样保留原始 ID，可作为验证：如果某个奖励结果满足要求，把它的 `relicId` / `details[].id` 记录下来，下次即可直接在下拉列表中定位同名条目。
   - 若你希望一次性导出最新的 `seed_info.json`，可以参考 `src/SeedRollerCli/seed_info.json` 的示例结构，自行从游戏或 MOD 工具中导出中文表。ID 命名与 MegaCrit 官方 `ModelId.Entry` 保持一致（全大写 + 下划线）。

## 文档索引

- [快速开始](docs/GETTING_STARTED.md)
- [配置项详解](docs/CONFIGURATION.md)
- [seed_info.json 结构](docs/SEED_INFO.md)
- [发布与打包指南](docs/PUBLISHING.md)

## 贡献

- 所有源代码均位于 `src/` 目录，可通过 `SeedRoller.sln` 打开。
- UI 项目引用 CLI，因此修改 CLI 的导出模型后需同步更新 UI 绑定。
- 提交 PR 之前请至少运行：
  ```powershell
  dotnet build src/SeedRollerCli/SeedRollerCli.csproj
  dotnet build src/SeedRollerUI/SeedRollerUI.csproj
  ```

## 持续集成 & 发布

- `.github/workflows/ui-build.yml`：针对 `main/master` 的 push/PR，负责常规构建并上传包含 UI + CLI + seed_info 的 zip artifact。
- `.github/workflows/release-build.yml`：当推送 `v*` 标签时运行，重新打包并把同一个 zip 自动上传到 GitHub Release。
- 若不希望直接托管 DLL，可删除 `game_libs/`，并在 CI 中改为私有下载方案。

## 许可

该仓库包含对游戏数据结构的引用，请在公开发布到 GitHub 前确认已满足游戏的使用条款，并根据需要补充适当的 LICENSE 文件。
