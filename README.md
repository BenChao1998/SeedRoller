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
├─src/
│  ├─SeedRollerCli/         # CLI 工程
│  │  ├─config.example.json
│  │  ├─config.example.jsonc
│  │  ├─seed_info.json      # 示例本地化数据
│  │  └─启动说明.txt
│  └─SeedRollerUI/          # WPF 工程
│     ├─config.json         # UI 的默认配置示例
│     └─seed_info.json
└─.gitignore
```

## 快速上手

### A. 本地编译

1. 安装 [.NET SDK 9.0](https://dotnet.microsoft.com/)。
2. 准备游戏 `data_sts2_windows_x86_64` 目录，并记录绝对路径。
3. 将 `seed_info.json` 放置到可执行文件同目录，或通过配置指定它的位置。
4. 运行 CLI：
   ```powershell
   dotnet run --project src/SeedRollerCli/SeedRollerCli.csproj -- --count 50 --character ironclad --seed-info seed_info.json
   ```
5. 运行 UI：
   ```powershell
   dotnet run --project src/SeedRollerUI/SeedRollerUI.csproj
   ```
   在界面中填写 `Data 路径` 与 `seed_info.json` 后即可开始筛选。

### B. 使用 Release 包

GitHub Actions 会在 push / PR 时自动构建 `seedroller-ui-win-x64` 工件；如创建 Release，将同一 zip 附在版本页面。下载后：

1. 解压 zip，将 `SeedRollerUI.exe` 与 `seed_info.json` 放在同一目录。
2. 双击运行，按 UI 提示填写 `Data 路径` 与筛选条件即可。

CLI 版本可参照 [docs/PUBLISHING.md](docs/PUBLISHING.md) 使用 `dotnet publish` 生成单文件可执行包。

更多细节请参阅 [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md)。

## 文档索引

- [快速开始](docs/GETTING_STARTED.md)
- [配置项详解](docs/CONFIGURATION.md)
- [seed_info.json 结构](docs/SEED_INFO.md)
- [发布与打包指南](docs/PUBLISHING.md)

## 贡献

- 所有源代码均位于 `src/` 目录，可通过 `SeedRoller.sln` 直接在 Visual Studio / Rider / VS Code 中打开。
- UI 项目引用 CLI，因此修改 CLI 的导出模型后需同步更新 UI 绑定。
- 提交 PR 之前请至少运行：
  ```powershell
  dotnet build src/SeedRollerCli/SeedRollerCli.csproj
  dotnet build src/SeedRollerUI/SeedRollerUI.csproj
  ```

## 持续集成 & 发布

- `.github/workflows/ui-build.yml` 会在 push / PR 时自动 `dotnet restore/build/publish`，并上传 `seedroller-ui-win-x64` 单文件包作为构建工件。
- 如需扩展 CLI 或自动发布，可在该工作流基础上新增 job（例如上传 Release、代码签名等）。

## 许可

该仓库包含对游戏数据结构的引用，请在公开发布到 GitHub 前确认已满足游戏的使用条款，并根据需要补充适当的 LICENSE 文件。
