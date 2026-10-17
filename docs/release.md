# 发布与版本审查规范

普通功能任务不得自动进入发布。本文件只在用户明确要求准备版本、打包、Tag 或 Release 时读取。

## 1. 权限边界

- commit、push、创建/更新 PR、合并、Tag、打包和 GitHub Release 是不同动作，分别需要与请求相符的授权。
- 人工验收是合并门禁；合并不等于批准发布。
- 发布只处理当前明确候选，不能夹带工作树中的其它改动。

## 2. 版本同步点

`AppVersionInfo` 是运行时版本的唯一来源。发布 `vX.Y.Z` 时检查：

1. `WinPieGestures/WinPieGestures.csproj` 的 Version、AssemblyVersion、FileVersion。
2. `WinPieGestures/AppVersionInfo.cs` 的 FallbackVersion。
3. `WinPieGestures/SettingsWindow.xaml` 的设计期版本占位。
4. `CHANGELOG.md` 顶部新增对应日期与版本。
5. `installer/build-installer.ps1` 的兜底版本。
6. `installer/StarPie.iss` 的 `MyAppVersion` 与 `MyAppNumericVersion` 兜底值。

先从候选版本制定同步清单，再修改；不要全仓搜索后盲目替换相似数字。

## 3. 构建与发布命令

示例使用变量，禁止把个人绝对路径写回仓库：

```powershell
$version = 'X.Y.Z'
$releaseRoot = Join-Path $PWD "releases\v$version"

dotnet build .\WinPieGestures\WinPieGestures.csproj -c Release --nologo

dotnet publish .\WinPieGestures\WinPieGestures.csproj `
  -c Release -r win-x64 --no-self-contained `
  -o (Join-Path $releaseRoot 'Lightweight')

dotnet publish .\WinPieGestures\WinPieGestures.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o (Join-Path $releaseRoot 'Standalone')

& .\installer\build-installer.ps1 -Version $version
```

压缩包与安装器名称遵循既有 `StarPie-vX.Y.Z-*` 规则。实际参数以脚本当前帮助为准，不凭旧文档猜测。

## 4. 插件发行边界

- 主程序发布包不得携带官方插件 DLL 或旧 `plugin/` 来源区。
- 包内只保留主程序、SDK 契约程序集和插件宿主所需文件。
- 官方模块由用户操作后从 `StarPie-Official-Plugins` catalog 下载并校验。
- 干净 `plugin-data` 启动不得联网或自动安装；已安装插件在离线时仍可加载。
- 社区扫描目录只读，安装器不得替用户创建或写入候选目录。

## 5. 发布前检查

1. 工作树和候选提交明确，无无关文件。
2. Release 构建、自检、专项测试和 `git diff --check` 通过。
3. UI、安装/升级、干净数据目录和离线场景已人工验证。
4. Lightweight、Standalone、Setup 分别启动验证。
5. 检查包内容、体积、SHA-256 和文件名。
6. 核对 Changelog、版本号和 Release 文案完全一致。
7. 发布前再次获得明确发布授权。

发现版本目标错误时停止，不通过覆盖旧 Tag 或替换已公开资产“修正”。先说明外部状态和恢复方案，再等待授权。

## 6. v2.0.0 未签名发行例外

2026-10-17，用户确认源码候选实机验收通过，并明确要求在正式 CA 证书流程完成前制作并发布 v2.0.0。此次仅发布从已合入 main 的精确提交构建的未签名 Lightweight、Standalone 和 Setup，不分发 SignPath 测试签名产物，不修改证书存储。

发布前检查三个 EXE 均为 NotSigned，核对版本、图标、包内容与 SHA-256。两种便携包用隔离数据目录和安全夹具运行 `--plugin-selftest --skip-invoke`；Setup 只做编译、资源与内容核对，真实覆盖安装保留为实机检查，不将编译成功当作安装通过。

`.github/workflows/release.yml` 仅排除精确标签 `v2.0.0` / `V2.0.0` 的自动签名发布，以免正式证书未就绪的工作流与本地发行互相覆盖；其它标签及手动签名模式继续保留原有门禁。此次使用本地发行流程：推送已验证的 main 标签、创建草稿、上传三种包与 SHA256SUMS.txt，核对后发布为正式最新版，再重新下载公开资产验证哈希。

公开说明必须写明未签名及证书仍在办理，不声称已有正式证书或完成 Windows 信任验证。后续正式签名使用新版本和新标签，不覆盖本次已公开资产。
