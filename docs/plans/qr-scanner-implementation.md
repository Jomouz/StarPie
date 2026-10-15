# 二维码扫描候选：SDK 1.13 + 可选官方插件

状态：扫码功能与 UI/语言优化已由用户实机验收通过。SDK 向下兼容修复及独立验证见末节；未提交、未打包、未发布。

## 基线与范围

- 主仓库工作区：G:/Users/2 Better/.codex/worktrees/qr-scanner/design
- 主仓库基线：57253a308311677e73d68b8b0446473c7e9a9de8
- 官方插件工作区：H:/starpie-official-worktrees/qr-scanner
- 官方插件基线：e3aadbbfb656a89ba68c4c211461c3b87e0caece，与主仓库固定的子模块提交一致。
- H:/design 原有 README.md、README_EN.md、assets/supporters/ 和 WPF 临时工程保留。
- 允许范围：SDK ImageTools/PluginApi、PluginContext、新图片服务与文本结果窗口、已有截屏窗口小幅兼容扩展、ActionExecutor 的私密链接及可诊断剪贴板接缝、I18n、定向验证工程和相关文档；官方仓库新增 QrScanner、同步 SDK 文件、解决方案与测试。
- 禁止：钩子与轮盘几何、配置模型迁移、旧 OCR 识别流程、默认核心引导清单、静默安装、版本发布操作。
- 本次实现预算：90 分钟；独立审查后最多 2 轮修复。不通过时保留候选及证据，按未完成收口，不无限返工。

## 设计

二维码插件独立提供 scanRegion 和 scanPointer 两个普通插件动作，ActionKind.Background。安装、初始化和打开设置不扫描、不联网、不写剪贴板。用户关闭结果或取消框选后动作结束；停用取消活动任务，租约保持到窗口真正关闭。

动作显式 TimeoutSeconds=300，包含框选和结果阅读；0 会沿用宿主后台默认30秒，不能用来表示无限等待。五分钟后取消，用户可重新触发。

SDK 1.12 通过两个可选派生上下文提供图片获取和文本结果服务，不改变 IPluginContext 与旧 OCR 接口。图片使用紧密排列 BGRA32 字节，不暴露宿主 Bitmap/WPF 类型。ScreenCapture、Ui、Clipboard、Process 分别在实际调用前检查。HTTP(S) URL 必须显式、无控制符/空白/用户凭证；不猜测协议、不预请求链接，不自动导航。

选区复用 ScreenSnipWindow；副屏坐标采用虚拟桌面物理原点。输出最多 4,194,304 像素、单边 4096；框选冻结桌面最多 12,582,912 像素，超出时建议鼠标附近扫描。该限制是分配上限，不是工作集达标证明；冻结桌面及 WPF 图像副本的实际峰值仍需测量。

解码为 ZXing.Net 0.16.11 核心包，图像灰度转换/缩放由插件处理，无图像绑定或原生 DLL。原图优先，失败后 1.5/2/3 倍重试，每次依然受 4M 像素预算限制；反色显式补测。最多展示 32 条结果。ZXing 的 ECI/字符编码处理保持原样；不使用报告中可能破坏中文的 Latin-1 强行重解释。位置为库返回的检测锚点，不能当成二维码四边形顶点。

参考报告只用于工程思路，没有采用 PixPin 专有 DLL、反编译实现或未经实测的 1–3ms/3MB/AOT 兼容性结论。报告的 32M 像素也不等于 32MB。

## 验证

主程序基线 Release 构建：0 warning / 0 error。候选验证和最终指纹另见本文件追加的证据段。

插件定向测试：tests/starpie.plugin.qrscanner/QrScanner.Tests.csproj，生成本地二维码夹具并使用假截图/结果服务，不进行真实系统动作。

宿主定向测试：scratch/qr-tools-tests/QrToolsTests.csproj，像素布局、负坐标裁剪、能力拒绝、取消，以及真实 Load → Initialize → Commit → OpenInvocationGate → Shutdown → ALC 回收；仅调用纯解码方法，不执行真实插件动作。

插件反色断言先失败，显式反色补测后通过；并发停用测试修正夹具的旧完成信号，等待本次结果进入后再取消，保留原强断言。

## 人工验收

宿主 SDK 仍为 1.13，扫码最低契约已改为 1.12 并按能力回退。先前 1.12 候选可运行扫码；已发布 beta.7 的 SDK 1.11 缺原始图片服务，仍拒绝插件。

1. 浅色、深色、钛灰，简中/繁中/英文/日文的框选与结果窗口。
2. 单屏 100%/150%/200%，混合 DPI，负坐标副屏，屏幕边缘扫码。
3. 两个动作、多个二维码、长文本、中文、小码、旋转、反色，无码提示。
4. Esc、右键、Alt+F4，扫码中停用，结果中停用，快速重复触发，取消后重试。
5. 点击复制/打开前无副作用；普通文本、Wi-Fi、file/javascript/应用协议只复制，不跳转。
6. 轮盘退场不进入截图；记录第一次加载、常见扫描耗时及临时峰值工作集。

不自动运行弹窗套件，不操作现有配置，不把源码与构建通过视为实机验收。

## 当前验收入口

主工作区 scratch/qr-tools-tests/Start-QrAcceptance.ps1 已准备。手动执行前退出日常 StarPie，以免两个实例同时监听手势。脚本用当前候选生成完整默认配置，每次创建新的 LOCALAPPDATA 验收目录，预置候选扫码插件，关闭启动更新检查并跳过核心安装引导；不改变日常配置。运行后在手势动作中选择“插件动作 → 二维码扫描 → 框选扫码 / 识别鼠标附近二维码”。测试后从候选托盘退出。

    pwsh -NoProfile -File .\scratch\qr-tools-tests\Start-QrAcceptance.ps1

默认官方插件参数指向 H:/starpie-official-worktrees/qr-scanner 的本次 Release 输出；换位置时传 -PluginDirectory。-PrepareOnly 只准备隔离目录，不显示窗口，已执行退出0。

官方插件工作区 artifacts/qr-fixtures/qr-fixtures.html 含本地 BMP 样例（网页、中文、普通/反色混合、同内容不同位置、Wi-Fi文本），无脚本或远程资源。生成命令：

    dotnet run --project tests/starpie.plugin.qrscanner/QrScanner.Tests.csproj -c Release -- --write-fixtures artifacts/qr-fixtures

## 扫码功能阶段验证记录（此前 SDK 1.12 候选）

- 主程序 Release 构建与插件 Release 构建：退出0，均0 warning/0 error；插件另启用 TreatWarningsAsErrors。
- 插件定向行为测试：54断言，退出0；本地夹具总运行约206ms，仅用于测试环境，不是实际扫码延迟承诺。
- 宿主定向行为测试：24断言，退出0，含真实注册保留300秒、Load/Initialize/Commit/门禁、私有 zxing 解析及 ALC 回收。
- 通用 --plugin-selftest --skip-invoke：使用仓库已有 ControlSelfTestFixture.dll 与当前候选，退出0、PASS。最初省略目标DLL的运行退出1；直接以QR插件作为目标也退出1，原因是自检[3g]假定样本必有必填keyMap字段。未给QR动作添加无关字段，也未削弱/修改该自检。QR自身以定向探针验证。
- 反色解码安全变异：在独立 artifacts/qr-inversion-mutation 副本移除反色解码，断言明确失败，退出 -532462766；生产源码未改变。正常/反色混合的漏码由独立审查复现后加入回归；不同位置相同内容保留。
- 现有模块源目录导入：19模块，扫码 apiVersion=1.12，contributionId=scanRegion，enabled=false；未构建发布包/catalog。
- 双仓库 SDK ImageTools.cs 和 PluginApi.cs SHA-256 一致；输出含 zxing.dll/许可证/通知，无 SDK 与主程序DLL。
- 双仓库 git diff --check 退出0；新增源码使用意图索引便于查看完整 diff，没有暂存文件内容或创建提交。
- 独立复核第1轮发现超时/混合极性两项，修复后重新构建54/24断言通过；最终动作拆分、夹具导出和验收脚本只读复核无阻断。真实活动窗口停用、DPI、主题、剪贴板/浏览器及内存门禁保持人工待验收。
- 当前候选运行时源码/测试与二进制指纹保存在主工作区 scratch/qr-evidence/source-files.sha256 与 binaries.sha256；日志在同目录及官方工作区 qr-*-final.log。

## UI 与语言优化阶段（2026-10-14）

用户已确认扫码实机验收通过，本阶段保留解码/截图/显式复制打开的行为。解码源文件 QrDecoder.cs SHA-256 仍为 08BA28E02CF755288DAF5838CDD5FC82F60D4363B90A2548CE5D9560E1133D29。

本阶段范围扩展：TextResultWindow XAML/代码、ScreenSnipWindow 提示布局、I18n、静态插件显示模型、插件动作展示/新绑定自动名称、设置页动作摘要接线、SDK 可选翻译字段和可翻译结果标题、官方插件词条/manifest，以及定向呈现/实载探针和文档。不修改已有配置名称、不改核心引导、解码器或热路径。

结果窗口使用现有宿主主题和共享标题栏；单码全宽，多码宽窗口列表、窄窗口下拉。简中/繁中/英文/日文覆盖卡片、详情、动作、框选提示、标题和结果控件，热切换保留选择与原始文本。框选提示按真实文案宽度居中，选区信息在边缘限位，显示物理像素。新绑定自动名称取当前语言，已有自定义名称保留。

SDK 1.13 在保留 ShowAsync 的基础上增加 ShowLocalizedAsync 默认接口成员及 nameTranslations / descriptionTranslations 字段。新插件清单和程序集元数据均声明 1.13，避免旧 1.12 二进制宿主接纳后出现缺失方法；SDK assembly identity 仍为 1.0.0.0，主程序版本号不变。两仓三份 SDK 文件保持相同。

新测试均不展示窗口、不截图、不写剪贴板或打开浏览器。离屏 PNG 来自 WPF 内容渲染，不能替代原生窗口交互/DPI 验收。

| 验证 | 命令（各自仓库根） | 退出码 / 结果 |
| --- | --- | --- |
| 主程序 Release | dotnet build WinPieGestures/WinPieGestures.csproj -c Release --nologo | 0，0 warning / 0 error |
| 插件 Release | dotnet build src/StarPie.Plugin.QrScanner/StarPie.Plugin.QrScanner.csproj -c Release --nologo /p:TreatWarningsAsErrors=true | 0，0 warning / 0 error |
| 离屏主题/语言/布局 | dotnet run --project scratch/qr-presentation-tests/QrPresentationTests.csproj -c Release -- scratch/qr-evidence/ui-previews | 0，322 断言 |
| 实际宿主注册/显示/卸载 | dotnet run --project scratch/qr-tools-tests/QrToolsTests.csproj -c Release -- H:/starpie-official-worktrees/qr-scanner/src/StarPie.Plugin.QrScanner/bin/Release/net8.0-windows | 0，52 断言 |
| 解码与假服务会话 | dotnet run --project tests/starpie.plugin.qrscanner/QrScanner.Tests.csproj -c Release | 0，54 断言 |
| 兼容自检 | --plugin-selftest scratch/plugin-controls-tests/Fixture/bin/Release/net8.0-windows/ControlSelfTestFixture.dll scratch/qr-evidence/ui-selftest-report.txt --skip-invoke | 0，PASS；LOCALAPPDATA 为隔离 ui-selftest-profile |
| 源目录导入 | pwsh -NoProfile -File build/Import-ModuleRegistryFromSource.ps1 -OutputPath artifacts/qr-ui/module-registry.json | 0，19 模块，QR 1.13 / scanRegion / enabled=false |

离屏验证包含浅色、深色、钛灰 × 四语 × 单/多码及实际最小 480×420，普通文字/链接、同步选择、热切换、关闭订阅释放与至少 4.5 的文字对比度。窄窗口内容不足和深色主按钮对比度先失败后修复，原失败日志保留。复核提出 480×360 高度风险，新增测试证明失败；最低高度调整为 420，正文保留可读高度，失败证据为 ui-minimum-tests.log。

首轮独立复核发现设置页动作摘要仍使用缓存语言，已改为 PluginActionPanelText 的当前语言模型入口；同轮补新绑定名称及自定义名称保留行为测试。第二轮只读复核独立重跑 322 / 52 / 54 断言与双仓 diff --check，均退出 0，无新的阻断；查看最小窗口英文多码、日文多码、繁中单码预览，无正文/按钮裁切。两轮预算已用完。真实窗口交互与视觉仍待用户复查。

本阶段日志在 scratch/qr-evidence/ui-*.log 及官方工作区 qr-ui-*.log。离屏预览位于 scratch/qr-evidence/ui-previews；新候选指纹另存 ui-source-files.sha256 / ui-binaries.sha256，保留先前功能验收候选指纹。没有执行 GUI 套件或自动启动人工验收窗口。

Start-QrAcceptance.ps1 -PrepareOnly 在最终 SDK 1.13 二进制上退出 0，生成隔离 acceptance-b7c821f7d462468689e4d49d9e4fb741；未显示窗口。主仓库基线、官方仓库基线及 H:/design 原有脏文件保持不变。

### 新视觉复查

退出日常 StarPie 后重新运行本文件的 Start-QrAcceptance.ps1，会使用最新已构建宿主和插件建立新隔离验收目录。先前已创建的验收目录不会更新。用户在后续消息中已确认本轮 UI 与语言人工验证通过。

## SDK 向下兼容阶段（2026-10-14）

用户明确选择兼容 SDK 1.12 候选、旧插件继续受支持；不要求缺原始图片能力的 SDK 1.11 宿主运行扫码。此阶段由 Codex 直接实现，13:50 UTC 开始、15:20 UTC 截止，独立审查最多两轮；两轮已完成。发布执行与监测仍由 Gemini 在后续授权范围内处理。

允许范围：ImageTools/PluginApi/PluginManifest 两仓契约、宿主文本服务接口广告、扫码展示适配器及最低 API 清单/元数据、旧 SDK 二进制探针和有关文档。禁止下载/分发新版SDK DLL到旧宿主、绕开版本门禁、改变旧 OCR 或截屏实现、真实系统动作及 Git/发行操作。

宿主 API 保持 1.13，SDK 身份仍为 1.0.0.0，所有旧接口保留。IHostLocalizedTextResultService 是新增可选派生服务；扫码只用字符串类型名与精确签名探测增强，通过每实例委托调用。插件 API 最低 1.12；没有增强服务时原 ShowAsync 得到当前语言已翻译标题，旧宿主窗口沿用旧能力，不会获得新宿主 UI/热切换增强。

第一轮独立复核发现此前两仓同步误删官方 SDK 1.11 已存在的 PluginManifest.Features 和 PluginFeature，共 20 个公开签名。现已恢复，并由真正 SDK 1.11 编译的旧插件读取/写入全部属性，在新宿主中真实 Load/Initialize/Commit/Shutdown/ALC 回收。SDK 1.11 的 859 项、1.12 的 915 项公开签名检查均为 removed=0。

原有 UI 与解码源码不变，用户前述人工验收仍作为本次功能/视觉基线。1.12 回退流程由实际旧 SDK 二进制加假宿主服务验证；未自动启动 GUI 或执行剪贴板/浏览器。

| 当前候选验证 | 退出码 / 结果 |
| --- | --- |
| 主程序及插件 Release 构建 | 0，0 warning / 0 error |
| 离屏呈现 suite | 0，322 项 |
| 宿主 suite，加 SDK 1.11 旧插件第二参数 | 0，61 项 |
| 官方扫码 suite | 0，57 项 |
| Run-SdkCompatibility.ps1 | 0，10 项真实旧 1.12 SDK 二进制假会话 |
| SDK 完整公开签名棘轮 | 0，1.11 859 / 1.12 915，均无删除 |
| ControlSelfTestFixture --plugin-selftest --skip-invoke | 0，PASS；隔离 LOCALAPPDATA |
| 双仓差异卫生与 SDK 三文件镜像 | 0 / 一致 |

第一轮复核后恢复旧公开成员，第二轮独立重跑矩阵/宿主、确认旧二进制属性访问与回收通过，无剩余阻断。PE 元数据独立检查确认新扫码 DLL 没有 IHostLocalizedTextResultService TypeRef 或 ShowLocalizedAsync MemberRef；不是依赖较新 SDK 中的默认成员来冒充旧二进制兼容。

失败证据保留：旧服务收到未翻译键的 qr-compat-red.log；独立复制源码恢复直接 1.13 调用导致 MissingMethodException 的 qr-compat-mutation.log；旧插件绑定缺失 PluginFeature 导致 TypeLoadException 的 compat-abi-red.log；原 UI SDK 被完整签名棘轮检测缺 20 项的 qr-compat-abi-red.log。变异仅在 artifacts 独立副本进行。

当前日志为 scratch/qr-evidence/compat-*.log 与官方 qr-compat-*.log，新指纹另存 compat-source-files.sha256 / compat-binaries.sha256。Gemini 发布计划为主工作区之外、仅本地的 H:/design/.ai/tasks/SP-QR-UPLOAD-001/gemini-execution-plan.md，由用户人工转交；不提交 .ai 或测试产物。
