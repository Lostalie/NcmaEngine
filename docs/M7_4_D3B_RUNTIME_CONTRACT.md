# M7.4-D3-B：正式 Player 被动 HUD 合同

## 所有权与入口

项目 `uiPackage` 是可选显式配置：`version=1 / documentId / generation / path / hash`。路径必须为规范项目相对路径，例如 `assets/ui/hud.ncupak`，不是 OS 字体路径、作者资源发现或任意脚本入口。配置未设置时不创建 UI Canvas，也不加载 Text 原生模块；图形 Player 产品携带可选惰性 NcmaText.dll，无字体 HUD 不加载它。Null 产品没有原生插件，只检查有界 CPU 闭包，不做字体整形/GPU 校验。

C# `RuntimeUiPackage` 拥有文档及复制的资源，owner-thread 访问；读者不能修改内部字典或文档。共享 `RuntimeReadPin` 在包生命周期内锁定文件/祖先目录，拒绝硬链接、重解析和大小写别名。独立 `SceneUiPresentation` 拥有 Text loader/service 和 Canvas，关闭顺序为 Canvas → Text → loader，失败即停止释放依赖并保留资源供关闭重试。它不拥有 World，也不接收 Agent 或 Python 回调。

`PlayerRunner` 先验 UI 包，再创建正式图形宿主；`PrepareBeforeGameplay` 完成真实字体建立、文字栅格化、图片/列表与布局准备，之后才加载 gameplay/start Play。每帧开始在模拟步外按视口准备；静态状态复用列表。先提交场景（或明确清屏），再提交显示域 UI overlay，再截图/Present。UI 不参加 HDR Bloom 或第二次 Tone。新的视口准备失败走既有清屏/故障/关闭，不冒用旧 HUD。

图形 Player 的受检 Shader 库在原 8 个场景变体之外追加既有 Flat2D，变为 9 个；无新 Shader 算法或 ABI。用户显式 Shader 选择缺少 Flat2D 时，配置 HUD 将在图形宿主创建前拒绝，不临时编译或回退。纯 UI 独立产品仍只有 Flat2D + index，不引入 3D 程序/环境/后处理资源。

## NCU1 v1

独立有界容器，不修改 NCP1/NCS1/场景 JSON/native ABI，不支持旧格式回退。little-endian 40 字节头：magic `NCU1`、uint32 version=1、16 字节 project UUID（.NET Guid byte layout）、uint64 generation、int32 文档字节数、int32 依赖数；随后是现有严格 UiCodec v1 JSON。

每依赖为：16 字节 UUID、uint8 kind（1 字体/2 图片）、uint32 width/height、uint8 redistribution、int32 licenseBytes/payloadBytes、UTF-8 license、payload。字体宽高必须零、redistribution 必须 1、显式非空许可证≤2048字符；图片仅原始 RGBA8、空许可证/redistribution=0、尺寸≤4096且精确字节数。分发声明不是法律授权证明，发布者必须自行核实权利；不复制系统字体到产品。

整包≤64 MiB、文档≤4 MiB、依赖≤64、单payload≤32 MiB、license编码≤8192字节。文档 UUID/项目/代次与外部选择逐项相等；复制输入后检查外部 SHA-256，再解析。hash 是完整性检查，不是签名或信任授权。恰好包含文档引用的字体/图片，不得缺失/多余/重复或同UUID跨类型；Image 和非空文本必须显式 UUID。拒绝错误版本、未知元数据、截断及尾随字节。无运行时 handle、作者路径或可执行文本。

## 明确未完成

- 只接通被动 HUD：Frame/Group/Rectangle/Text/Image。交互控件和非空 action 启动前明确拒绝；没有伪装已实现的点击、游戏输入消费或行为调用。
- Editor 原 UI 编辑器/资源审批/可撤销命令不变。Editor 正式场景 HUD 视口、编辑后刷新/Play/Reload 联动是 D3-C 工作；现阶段显式 UiPackage 进入 Editor 会在插件加载前拒绝，不静默缺图。
- 依赖预算限制资源加载，不是恶意字体解码安全沙箱；依旧是可信本地项目。Null 的纯 CPU 检查不证明字体内容能被 DirectWrite 接受；图形宿主会在 gameplay 前验证。
- D3-C 完整 FBX/IBL/shadow/Bloom/UI 搬移联合、整个 D3/D/M7.4、人工/目标机/自包含/完整性能/1h门禁未完成。
