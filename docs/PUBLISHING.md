# 发布与打包

## CLI 单文件发布

```powershell
# 生成自包含的 x64 单文件版本
dotnet publish src/SeedRollerCli/SeedRollerCli.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true
```

产物位于 `src/SeedRollerCli/bin/Release/net9.0/win-x64/`。将以下文件放在同一目录：

- `SeedRollerCli.exe`
- `seed_info.json`（必需）
- `config.json`（可选）

然后即可通过命令行运行。

## WPF UI 发布

```powershell
dotnet publish src/SeedRollerUI/SeedRollerUI.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true
```

发布目录会在 `src/SeedRollerUI/bin/Release/net9.0-windows/win-x64/publish/`。

部署时请携带：

1. `SeedRollerUI.exe`（发布产物）。
2. `seed_info.json`（与 exe 同目录）。
3. 可选：`config.json`，允许用户预填游戏路径与筛选条件。

## 常见问题

- **运行后显示英文名称**：确认 `seed_info.json` 与 exe 同级，或通过 CLI `--seed-info` 参数 / UI “游戏与资料”面板指定正确路径。
- **仍需游戏原始数据**：两个项目都必须读取 `data_sts2_windows_x86_64`，请勿忽略。
- **进一步瘦身**：如需压缩体积，可去掉 `--self-contained true`，但目标机器必须已安装 .NET 9.0 运行时。
