# M7.1-C4-C 项目 Shader 文件与发布契约

日期：2026-10-10。前置 C4-B `c115a370afc3856aebb3ef199434d7f07ae84577` 已在开工前独立核对远端 main。只支持 DX11；本文件定义实现边界，最终测试结果单独记录。

## 选择与所有权

`.ncmaproject` 的可选 `shaderPackages` 是可信启动配置，不是 Agent 输入。它包含独立 `schemaVersion: 1` 和 `packages`，每项必须显式提供 `path`、`sha256`、`profile`、`shadows`、`skinning`。未知字段、重复 JSON 字段、缺字段、未知版本/枚举、重复路径/功能键拒绝。配置进入与返回 ProjectContext 都复制数组。

- 1–5 个包；路径必须为严格大小写的 `assets/…​.ncshader`（实际路径没有省略号），1024 字符以内，不接受绝对路径、反斜线、设备名、ADS、点段或源码扩展名。
- `Flat2D` 只有一个无 shadow/skin 组合；`Scene3D` 必须成对选择 shadow off/on，允许静态一对、skin 一对或两对。可同时选择 UI。两个 skin 变体必须使用完全相同的计算字节码。
- 每文件最多 8 MiB，最多 5 个文件；独立包仍是原 NCS1 v1/每条 1 MiB 预算。独立 sha256 必须与实际文件一致，profile/features 必须与解出的完整包一致。
- 部署默认索引为 `assets/shaders/defaults.json`，最多 16 KiB，格式同上述选择。索引和所有包进入既有 deployment manifest 的路径/尺寸/hash 校验。项目显式配置完全替代默认集合；缺少所需变体直接报错，绝不回退或编译补齐。

## 文件与准入

现有 RuntimeReadPin 实现搬到 Ncma.Assets，共享给 Assets.Runtime 和 Rendering，仅授予内部可见性；没有复制另一套 Win32 IO，也没有给纯 UI 增加 Animation/Assets.Runtime 依赖。

读取前锁定文件和全部父目录，严格大小写/有界目录枚举，拒绝 reparse、硬链接和异常文件类型；文件只分享读取、目录不分享删除，拒绝写入和目录改名替换。正式部署默认选择还锁定根目录唯一的 deployment-manifest.json，将实际 pinned 索引的尺寸/hash 与 manifest 独立记录比较，再读取包；避免先验文件校验到打开索引之间的替换窗口。这仍是可信发布一致性检查，不是签名验证或代码安全沙箱。每 64 KiB 读取检查取消。包拥有复制字节；所有锁在宿主关闭 renderer 后释放，失败按逆序释放。ShaderFileSet 是 owner-thread-only，最多 5 个 immutable 包，关闭/外来线程拒绝。

先完成所有文件的纯 CPU 预检，再对整个选择逐包执行 C4-B 真正 native 反射，全部通过才一次性发布到该 RendererSession 的默认准备服务。服务必须尚未 cook/绑定过选择；owner/idle/healthy/授权/非重入仍有效。文件集合在授权回调中被关闭也不能发布。后续服务只从已选择包取字节，不重新打开文件或按需编译；GPU 安装仍使用 C1–C3 的原子创建/替换。

`GpuValidated` 仍始终为 false。独立 `BindingsValidated` 仅说明实际反射成功，不证明 shader 指令安全，也不等于 GPU 对象已创建。没有新 native ABI、Agent 编译/安装/文件发布权限、热加载协议、Python 推理或任意代码沙箱。

## 宿主和构建

- Editor 在加载插件前读取/pin选择，Renderer 创建后、GUI/业务启动前完成准入。所有共享 Scene/UI 准备取同一选择；释放 renderer 后释放文件 pin。
- 图形 Player 在任何 gameplay 加载/PlaySession 创建前完成文件预检、所需场景闭包检查和实际 GPU 绑定准入。坏包报告 tick=0、session empty。Headless 不读取 Shader 文件或初始化 GPU；仅对项目选择的结构执行配置校验。
- 已部署 Editor/Player 有 deployment manifest 时必须存在默认索引；不能因为删除索引就静默回退。源码布局测试/显式 cook 工具没有发布索引时保留原 off-frame 默认 Cook 路径。直接嵌入 PlayerRunner 的可信宿主仍负责发布根/manifest 验证；正式 apphost 保留既有启动验证。
- 新 Ncma.Shader.Cook 是构建时可信工具，输出全新目录，不覆写旧目录。它使用相同编译器、默认目录和 NCS1 Cook/Preflight；不随 Player/UI 发布，不提供 MCP 入口。
- checked Editor 包包含 UI+四种场景共 5 包和索引；DX11 Player 包只含四种场景包和索引；Null Player 不部署 Shader 包；独立 UI 只附一份 Flat2D 包和索引，原 16 个文件变为 18 个。原无 World/Physics/Scene/Animation/Editor/Gui 插件部署约束保持。
- 正式发布不加载作者 HLSL 文件、不在 tick Cook。**这不是无编译器运行时**：C4-B 的原生参考契约首次反射仍可能编译内置参考 HLSL，D3DCompiler 仍是原生依赖；共享 Rendering 中作者/编译类也没有物理裁剪。禁止将没有源码文件误称代码安全沙箱或完全去编译器。

## 验收边界

保留全部原 A/B/C1–C4-B、原生故障/超时、实际 FBX/NCA/静态/蒙皮阴影、动画/Jolt、MCP、Player/Headless、独立 apphost、部署恢复、严格格式拒绝和 Python 检查。新增路径/锁/硬链接/取消/复制/错误输出/用户 UI 像素与 Player pre-start 拒绝测试；最终提交须完整顺序无 Skip Debug/Release 和 checked deployment/hash/journal。

人工 UI/MCP、用户 FBX/材料、目标环境、自包含、完整性能和 1h 门禁仍未关闭。后续顺序 M7.2 → M7 剩余 → M8 → M9；Vulkan/OpenGL 实现留下一版本。
