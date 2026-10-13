# M7.3-C2-C 正式 Editor / Player 环境光合同

2026-10-13。实现与定向测试完成，最终门禁以[交付记录](M7_3_C2C_DELIVERY_REPORT.md)为准。

## 启动与执行边界

`SceneEnvironmentPreflight` 在 gameplay 加载/初始化之前，核对当前已提交 World 与启动快照的环境配置、确切 UUID/generation/hash、shader profile3 双阴影变体/共享 skin 文件闭包。文件选择使用 C2-A 的整组实际反射准入；预检借用资产、缓存环境 GPU，并创建 16×16 临时场景验证真实绑定。临时绑定不绘制、不复制动画、不持有 World。资源租约一直保留到正式场景接手，释放顺序为绑定、GPU 借用、CPU 借用、缓存 Trim。

Player 使用正式 `SceneRenderSession`；每次固定更新结束后显式刷新已提交环境配置、准备视图，再提交。动态 Off/on 和强度生效；未准备的代次明确失败，不在 tick 中扫描磁盘或导入资产。已有 source-layout 启动 Cook 保留；显式项目包缺 profile/错误反射不能回退默认 shader。空场景和 Headless 无环境 GPU 初始化。

Editor 启动和 Play composition 共用预检；正式场景同步在 simulation 前。环境资源改变时显式刷新，`AssetPackage` 的显式 NCP 选择在刷新时保留。候选资源准备成功后才替换旧场景；失败保存已安装资源、去重日志和失败请求，清除视口显示并撤销 picking frame，不能将旧画面当作新 World。新的已提交资源/配置允许重试。视图准备发生在 GPU 提交前；Submit 本身不读文件或 Cook。

## 编辑与权限

环境配置仍使用原 `RenderingEditorAdapter.EnvironmentRequest`、唯一 EditSession 与权限/会话/版本校验。默认 Agent 只读；批准的事务进入同一 Undo/Redo。Undo 恢复 World 后重新解析 UUID 和资源，Play/Stop/Reload 保持 Edit 隔离。测试专用内部 friend 回调在 Present 前读取实际目标，用于自动图像断言；不形成 CLI、MCP、推理或 GPU 编译入口。

正式 CPU composition 接受 enabled 配置，但渲染宿主仍必须完成实际资产/shader/GPU 准备。不存在“命令成功等于资产/GPU已可用”的保证；失败状态由宿主报告并阻止错误画面提交。

## 验证范围与限制

43 项正式宿主自动检查覆盖用户 profile3、独立 IBL 像素 oracle、源无关 NCP、缺 profile/错误实际输出/pre-gameplay 代次拒绝、动态配置、Agent 默认拒绝、Undo/Redo、Edit/Play/Stop/Reload、失败清屏和恢复、实际 ASCII/Binary FBX-NCA 动画、独立 Editor/搬移 Player 进程、空场景与 Headless。原 B3 数值/皮肤/材质/阴影图像与 C2-B 87 项仍由完整回归执行。

尚未提供 C3 合成默认环境部署和面板；用户需显式选取环境包与 shader。没有 HDR/EXR 文件解码。CPU `GpuValidated=false`，内部参考编译仍存在，不是代码安全沙箱。新代码不改变 native ABI/shader 算法，不新增 Python gameplay 或推理服务。逐帧/按版本对象扫描和 Editor 场景候选重建仍存在，不声明动态零分配或完整性能验收。人工 UI/DPI/MCP、真实用户 HDR/FBX/材质、目标机/自包含和一小时稳定性继续开放。

本版仅 DX11；Vulkan/OpenGL 留待下个版本。C2 完成后进入 C3，整个 M7.3 仍需 C3；M7 → M8 → M9 顺序保持。
