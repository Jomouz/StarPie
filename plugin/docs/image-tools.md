# 图片获取与文本结果 API（SDK 1.13 候选）

图片服务从 SDK 1.12 起提供，旧宿主/假上下文无需增加 IPluginContext 成员。插件分别检测 IImageCapturePluginContext 和 ITextResultPluginContext。二维码插件清单与程序集元数据的最低要求为 apiVersion=1.12；宿主 SDK 仍为 1.13，增强服务按实际能力检测。已发布 beta.7 只有 SDK 1.11，缺少原始图片服务，仍拒绝该插件。

## 图片获取

Images.SelectRegionAsync(token)：用户框选，Esc/右键/Alt+F4 返回 null；token 取消则关闭窗口后抛 OperationCanceledException。捕获失败或超限抛异常。Images.CaptureAroundPointerAsync(radius, token)：当前指针周围物理像素，radius=64..1024，裁剪到虚拟桌面，支持负原点。两个方法均先检查 ScreenCapture，同一时刻只接受一次新 API 的捕获，冲突抛 InvalidOperationException。

CapturedImage 为调用方拥有的 BGRA32 byte[]，宽高以物理像素计、紧密排列，ScreenX/ScreenY 为屏幕原点。最多 4M 像素、单边 4096；框选冻结全景另限 12M 像素。这些上限不是实际工作集保证。请处理选择过大/桌面过大的失败；无需 BMP/PNG 编解码，不能持久化到配置或日志。任务结束后释放引用。

截图不执行 OCR，不读取 OCR Provider，不发起网络请求。旧 ScreenCapture.CaptureAndRecognize() 保持原约定。

## 文本结果

TextResults.ShowAsync(title, items, token) 需要 Ui；title 最多 256 字，items=1..32，单项文本最多 8192 字、格式最多 128 字。服务复制条目快照，返回 TextResultSelection，直到用户选择或关闭才结束。关闭/取消不产生复制或跳转；取消任务等待窗口关闭。

SDK 1.13 的 ShowLocalizedAsync(titleKey, items, token) 使用完整 I18n 键（如 plugin.&lt;pluginId&gt;.title），结果窗口随语言切换刷新标题与控件，保留选择和原始文本。原 ShowAsync 与已有默认成员均保留。IHostLocalizedTextResultService 是可选派生服务，明确广告宿主支持动态标题，不依赖默认实现来猜测能力。

最低要求 1.12 的消费者不可在类型/字段/方法签名、typeof 或直接调用中引用 1.13 新类型/成员，否则旧 SDK 的 JIT 会失败。二维码插件只从共享 SDK 程序集按完整类型名发现可选服务，核对实例、精确参数与返回类型，绑定每实例强类型委托；缺少增强服务时调用原 ShowAsync，并传入当前语言已翻译的标题。发现只在初始化进行，不缓存到全局，不在扫描热路径重复反射。旧宿主窗口沿用旧宿主 UI，标题按打开时语言固定；不会获得新宿主的动态标题或显示模型增强。

直接引用 ShowLocalizedAsync 的消费者仍应声明 apiVersion=1.13。默认接口成员保留源兼容不意味着 1.12 二进制包含该成员。清单声明的是实际必须具备的最低 API，不能仅凭编译时引用了较新 SDK 推高或降低要求。

静态 plugin.json 可选 nameTranslations / descriptionTranslations 语言字典让已安装插件卡片和详情无需重新加载程序集即可翻译；缺少翻译时保留原名称/说明。动作标签仍使用 ActionDescriptor 的 DisplayNameKey / DescriptionKey，在展示时解析当前语言。翻译不自动回写既有动作名称。

CopyAsync(text, token) 需要 Clipboard；OpenWebUrlAsync(text, token) 需要 Process。它们适用于 Background 动作，复用已有剪贴板/浏览器实现，并返回成功状态。打开失败交给调用者反馈，不显示阻塞性 MessageBox。URL 不写日志；SDK TextResultUrl 不接受控制符、空白、用户凭证、file/javascript/应用协议或无协议字符串。

这些服务没有沙箱能力，也不验证网页可信度。解码结果是外部输入；只能在明确用户选择后请求复制/打开，不能根据识别结果自动执行命令。显示结果不预取网页。

## 生命周期

动作声明 Background 并 await 整个捕获/解码/结果流程；不要火并忘或提前返回。停用/超时的 token 应贯穿服务及解码任务，使宿主租约保持到真实操作结束。重复点击由插件串行保护；关闭结果后才接受新扫描。

以二维码插件作为首个消费者；解码库不放进宿主或 SDK，SDK 不引入 NuGet/WPF 依赖。
