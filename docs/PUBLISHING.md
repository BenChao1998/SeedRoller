# 发布与打包

## CLI 单文件发布

`powershell
# 生成自包含的 x64 单文件版本
dotnet publish src/SeedRollerCli/SeedRollerCli.csproj 
  -c Release 
  -r win-x64 
  --self-contained true 
  /p:PublishSingleFile=true 
  /p:IncludeNativeLibrariesForSelfExtract=true
`

产物位于 src/SeedRollerCli/bin/Release/net9.0/win-x64/。将以下文件放在同一目录后即可运行：

- SeedRollerCli.exe
- config.json（可选，用于批量参数）

> seed_info.json 已内嵌到 CLI；若需要测试新的资料，可在命令行使用 --seed-info 指向任意文件。

## WPF UI 发布

`powershell
dotnet publish src/SeedRollerUI/SeedRollerUI.csproj 
  -c Release 
  -r win-x64 
  --self-contained true 
  /p:PublishSingleFile=true 
  /p:IncludeNativeLibrariesForSelfExtract=true
`

发布目录会在 src/SeedRollerUI/bin/Release/net9.0-windows/win-x64/publish/。

部署建议：

1. 创建一个顶层文件夹（例如 SeedRollerUI/），把发布目录全部拷入其中，并额外创建 cli/、game_libs/ 子文件夹存放 CLI 与游戏 DLL。
2. 在顶层旁边放置一个快捷方式 SeedRollerUI.lnk，目标指向 SeedRollerUI/SeedRollerUI.exe，Working Directory 同样设置为 SeedRollerUI/，便于用户一键双击。
3. 将 src/SeedRollerUI/seed_info.json 同步到 SeedRollerUI/seed_info.json，并复制一份到压缩包根目录，方便需要手动编辑的玩家。

GitHub Actions (ui-build.yml 与 elease-build.yml) 已自动完成上述操作，打包出的 zip 结构如下：

`
SeedRollerUI.lnk
seed_info.json           # 方便查看/编辑，程序仍会优先使用子目录或内置版本
SeedRollerUI/            # 实际运行目录
├─SeedRollerUI.exe
├─seed_info.json         # CLI/UI 默认使用的版本
├─cli/                   # SeedRollerCli 可执行文件
└─game_libs/             # 必需的 sts2.dll、0Harmony.dll
`

## 常见问题

- **运行后显示英文名称**：确认 SeedRollerUI/seed_info.json 存在；若文件缺失，程序会使用内嵌版本，但若你正在测试自定义资料需要把它放回同名路径。
- **仍需游戏原始数据**：两个项目都必须读取 data_sts2_windows_x86_64，请勿忽略。
- **进一步瘦身**：如需压缩体积，可去掉 --self-contained true，但目标机器必须已安装 .NET 9.0 运行时。
