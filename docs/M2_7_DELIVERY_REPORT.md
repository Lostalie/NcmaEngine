# M2.7 候选交付记录

日期：2026-10-04。状态：共享运行服务、Player/Headless、框架依赖候选包已实施；**完整 H7 未完成，正式入口未切换，旧入口未删除**。

## 实际交付

- Ncma.Application.Runtime.RuntimeSessionOwner 不依赖编辑器、GUI 或原生服务，统一文档/可信程序集预检、catalog lease、Play 和关闭。EditorSessionOwner 组合它，继续自行创建 Play 克隆和管理编辑历史/授权；受控旧桥消费者保留。
- PlaySession 增加固定会话模式及 AdvanceFixedStep，复用唯一 WorldRunner/RunStep。Headless 每次一个 h，不调用 OnUpdate、不积累墙钟债务；禁止同一会话混用 AdvanceFrame。暂停/单步原语仍保留，默认 Editor/旧桥依旧帧模式。
- Ncma.Player.App 生成 NcmaPlayer.exe，图形 Player 使用 Platform/Renderer，但不引用 Editor/Mcp/Gui。Null/physics-off 的 Headless 不加载 native/Python；physics-on 只初始化既有独立服务，不接入场景/Play 物理。
- 参数严格解析并设置 tick/h/墙钟预算。Ctrl+C 仅发取消意图；owner 在安全边界关闭。报告 JSON v1 有界，捕获 Stop 前的 tick/故障/状态摘要/模块/API 验证；异常消息不输出脚本私有内容。公开状态摘要不声称所有私人字段/随机/IO 都确定性。
- Editor 候选程序集名 NcmaEngine、WinExe；平台窗口图标和 apphost 图标沿用 NcmaEngine.ico。正式 out/bin 仍为旧 C++ EXE；此处同名候选不等于生产切换。
- Package-M2_7.ps1 从 publish 生成全新代次的 Editor、Player-Null、Player-DX11，配套样例 .ncmaproject/.ncmascene 和 gameplay DLL。文件/插件按内容集部署；包记录 SHA-256/相对路径/配置/RID/TFM、ABI 和原生库许可证。无旧桥、无源码目录寻找依赖。SHA-256 是一致性检查，不是签名。
- DeploymentManifest 校验版本、文件大小/hash、重复字段/路径、越界及 reparse；应用检测到包 manifest 时在业务启动前校验。缺 manifest 的开发输出仍可运行，不能把这个机制称为抵御恶意分发的安全认证。
- Build.bat 保持规范入口；Player 测试纳入 CTest，solution 登记新项目。个人 .vs/.vcxproj.user 未修改。H7 未通过，因此不改变 LaunchEditor.cmd/NMake 正式运行路径。

## 测试与证据

A/B 首轮：Debug 完整回归通过，29 项 CTest（8 native + 21 managed/editor），managed/native smoke、Python inspect、24 项工具测试通过。
最终结果：Debug/Release 的 Build.bat 均退出 0；每配置 29 项 CTest、22 项 Player/发布专项、managed/native smoke、Python inspect 和 24 项工具测试全部通过。编译 0 警告/错误；Git 读取全局 ignore 的权限警告单独记录，不冒充编译警告或修改用户配置。
既有 DX11 参考对比 max/mean error=0，Player 实际呈现 validation errors/warnings=0/0。证据：

- out/verification/m2-7-debug.log
- out/verification/m2-7-release.log
- out/verification/m2-7/{Debug,Release}/tests-<uuid>/results.json
- 同目录 package-dx11.json、package-null.json、physics-on.json，以及 packages.json 指向的包 manifest。

专项覆盖严格 CLI、600 tick 无 OnUpdate/债务、固定步失败中止、输入消费、30/60/144 时序的逻辑状态摘要、owner-thread/catalog lease、Headless 无 native、启动/运行故障、关闭失败、取消/墙钟预算、32 次生命周期、缺模块/未知脚本/Vulkan、报告失败、真实 apphost/无编辑器依赖。
追加共享运行服务的 factory 重入/创建期 World 修改拒绝测试；元数据预检的文档只读保护覆盖可信构造函数/Export getter，不承诺回滚私人状态或 IO。
发布专项覆盖包搬迁/中文与空格路径/不相关 cwd、只读场景文件（不冒充整个 Windows ACL 只读项目）、无 ImGui 的实际 DX11 Player 提交/呈现和 validation 0/0、候选 Editor WinExe apphost 隐藏窗口启动/关闭、篡改/越界拒绝、Headless 可选独立 Physics。
既有 DX11 参考像素对比仍由 M2.4 测试覆盖；Player 专项的帧数/API 验证不冒充它自己的独立像素基线对比。

## 当前候选包使用

先执行 Build.bat -Configuration Release，读取 out/verification/m2-7/Release/packages.json 的 playerNull/playerDx11/editor 路径。
所有路径是本次构建的代次目录，旧代次保留验证证据，不使用时间戳猜配置，不提前 promote 到 out/bin/out/player。

```powershell
$packages = Get-Content -LiteralPath 'F:\NcmaEngine\out\verification\m2-7\Release\packages.json' -Raw | ConvertFrom-Json
& (Join-Path $packages.playerNull 'NcmaPlayer.exe') --project (Join-Path $packages.playerNull 'sample\sample.ncmaproject') --headless --ticks 120 --report 'F:\NcmaEngine\out\verification\player-run.json'
& (Join-Path $packages.playerDx11 'NcmaPlayer.exe') --project (Join-Path $packages.playerDx11 'sample\sample.ncmaproject')
& (Join-Path $packages.editor 'NcmaEngine.exe') --editor --project (Join-Path $packages.editor 'sample\sample.ncmaproject')
```

报告默认不覆盖已有文件；示例报告如已存在，改为新的 .json 名称。自定义项目仍只使用 JSON v1 的 .ncmascene，并须有可信组件注册和已支持的脚本 Export。

## 尚未完成的门禁

1. M2.5 完整预览/面板业务与 H3/H4/H5/M1 的 UI/输入法/DPI/第三方 MCP 人工验收仍未通过。
2. 自包含 .NET 8 runtime pack 缓存与无预装 .NET 的独立目标环境尚未具备；未自动下载/安装，不将 framework-dependent 成功称为 self-contained 通过。自包含发布及缺适用共享运行时的隔离环境测试尚未完成。
3. 默认路径切换、部署 journal/恢复与文件锁注入测试未实施；它们只能在前置门禁完整后推进。候选包验收不代表正式 Rider/Explorer/MCP 全业务验收完成。
4. GUI 模式控制台行为采用 Player Exe；Editor WinExe 的重定向 smoke 已覆盖，可见启动失败提示/实际 Explorer 图标与窗口人工确认仍待补齐。
5. 完整资产 cook/FBX GPU 蒙皮/Animator/HUD/网络/运行时 AI/Vulkan 不在本次实现范围。

H7-Candidate 只完成当前可执行的自动子集，H7-Ready/H7-Production 均未通过；不能标 M2 完成，也不能进入旧入口统一删除。

## M2.5-E FBX 切片追加

Editor 候选包增加按需加载的 plugins/NcmaNative.dll（角色资源 ABI 2）和 ufbx 许可证，
deployment manifest 用 resourceKernels 单独记录，不把它冒充统一 NcmaPlugin 1.x 模块。
Null/DX11 Player 不部署此 DLL。部署回归追加搬迁到中文/空格路径后的 FBX 数值导入/采样，
参见 [FBX 切片](M2_5_FBX_DELIVERY_REPORT.md)。这不改变 H5/H7 未完成状态或生产入口。

2026-10-04 本轮最终 Debug/Release 候选包及搬迁回归均通过，Player/package 专项各 22 项；
包索引为 out/verification/m2-7/{Debug,Release}/packages.json。
本轮完整门禁为每配置 29 项 CTest、编辑器专项 25/25、Python 25 项及 managed/native smoke；
证据 out/verification/m2-5-fbx-{debug,release}.log。详细核对见 [M2.5 交付记录第 6 节](M2_5_DELIVERY_REPORT.md)。

后续 M2.5/H5 批次：Editor 的 resourceKernels 同时记录 character ABI 2 与 animation ABI 2（同一 NcmaNative.dll、按需加载）；GUI 1.2 / Renderer 1.1 元数据已同步。
候选业务/偏好/Console/独立预览迁移见 [当前 H5 记录](M2_5_H5_DELIVERY_REPORT.md)，manualAcceptance/selfContainedVerified/默认入口切换仍未通过，不能提升为生产包。
