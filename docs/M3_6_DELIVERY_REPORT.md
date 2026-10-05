# M3.6 资产工作流与离屏视口候选交付

日期：2026-10-06。基线：M3.5 `7391d36341baa52874f85c3f2eaf837745652ce0`。
状态：候选自动切片及最终顺序 Debug/Release 完整回归通过；不是完整 M3.6 或整个 M3 完成。G6 保持开放，M3.7–M3.9 未实施。

## 实际实现

- 正式 C# Editor 在配置 assets 项目后创建本地 `EditorAssetWorkflow`，浏览 `.ncmeta` root/subasset UUID、类型、来源、代数、依赖数；根列表/子列表每页16项，报告每页8行、最多8192行。缓存随 asset revision 更新，不逐帧读盘/解析模型。
- FBX 源计划在后台只读校验路径/锁定/哈希；本机用户核对 source/UUID/metadata 后，才给精确路径和身份授权、排入已有受控 worker。一条可见任务提供背压，完成/取消后需要显式 Forget 才开启下一任务。不是新增 MCP 批准工具，也不是任意程序执行接口。
- 候选报告显示 worker phase/source bytes（不是总体百分比）、数值计数、source/derived SHA256、身份变化/诊断和受影响的内置 mesh/clip 对象 UUID。不是任意用户组件的完整反向引用索引。generation pins/保留字节与 Undo/Redo 数量可检查。
- 导入发布走现有 asset command participant 和唯一 EditSession 历史；后台准备并 pin generation，owner 只消费完成结果。取消/失败/项目关闭不发布候选；迟到计划重新核对 session/document/asset revision。源和场景操作共用 Undo/Redo。
- Character/static root 可拖入视口，或用明确 XYZ 准备放置；拖放只准备，不直接写 World，用户确认后一次事务创建最多16个独立扁平对象、最多64项操作。角色使用 Transform/SkinnedMesh/首个 ClipPlayback；保存只存 UUID/值设置。没有父子场景结构。
- 类型化 mesh/materialSet/clip 按钮通过同一事务修改已选对象。clip/skin 校验 rig/root；mesh 自动选择 manifest 配对材料。已有派生材料集合的 slot 覆盖在提交前通过缓存验证，不读 NCA，也不让错误先进入 GPU。独立 `.ncmaterial`/`.ncmatset` 尚未进入该浏览器，已有严格运行时解析/authoring 契约未变。
- GUI ABI additive **1.3** 保持 table104/item96/event72/frame48 字节布局；Image 使用 renderer-owned opaque target identity，不向 C# 传 COM/SRV/ImTextureID。AssetButton 只拖放 canonical UUID，不传源文件路径。详见 [图像契约](M3_6_GUI_IMAGE_ABI.md)。
- 正式场景先画到 color/depth 离屏 target，native GUI 解析有 pin 的 image，合成到 swapchain，最后一次 Present。DPI/framebuffer 尺寸变化才替换 target；最小化释放 target，Begin/Destroy 丢弃未提交 draw pins。无 skin 的空 clear 不初始化 PBR 内核。
- 点击使用当前提交帧/World identity+revision/view identity 的 CPU ray/bounds；static 为局部 OBB，animated 为正权重 source influence bounds 经当前 palette 的保守联合包围盒，不是精确表面命中、不做 GPU 读回。独立数学与 CPU posed-vertex oracle 验证包围关系。
- Orbit/Pan/Zoom 属 C# 浏览状态，数字/按钮控制，不隐式写场景相机或 Undo。Edit scrub/pause 属独立非持久时钟；Play 不接受 Edit scrub/修改，已有 FBX CPU/Action Lab 仍各自隔离。既有数值 Inspector 草稿事务保留。

## 自动验证

新增 native/managed 检查覆盖图像 target 类型、foreign/stale、未提交/错误帧、64 pins/溢出/双重释放、target busy、32次 resize/release、离屏与 GUI 合成中心像素误差≤1、单次 Present、geometry/texture uploadedBytes 不增长以及 DX11 validation 0/0。

`Ncma.Editor.Services.Tests` 增至44项：真实静态 ASCII 与 binary 角色 FBX → 受控 worker → NCA → 单一 Undo/Redo →放置/类型选择 → 保存/关闭/重启；拒绝未批准 UI 事件、路径越界、caller 改写 placement DTO、旧文档/计划、Play frozen；取消保留已安装 metadata；锁住 metadata 后连续32次 browser build 无读盘；固定阻塞后台 prepare 的锁时，已发布 generation 用量/manifest/slot 读取必须先完成，未准备代不泄露。正式 `CandidatePresentation` 运行新项目3帧离屏 DX11并关闭。隐藏/短帧运行不代表真实键鼠/输入法/DPI 验收。

最终完整日志：`out/verification/m3-6/Debug-final3.log`、`Release-final2.log`。顺序执行完整 `Build.bat -Configuration Debug/Release`，无 Skip、退出码均0；每配置执行31次 CTest（native11 + managed/application20，含保留重跑项）、Python43/43、managed/native smoke、严格新格式与旧格式拒绝、inspect、三轮保留 runtime profile 与部署前后审计。Editor Services44/44；GUI1.3的32次target循环/图像断言与DX11 validation0/0均通过；最终构建无新增编译警告。Release全包hash/路径核对后部署 `out/bin/NcmaEngine.exe`，备份 `out/deployment/8e28a7b4212f46a4acb60687b5572031/backup` 与 journal保留。`audit_passed=true` 不等于 `h8_accepted`，后者仍false。

发现并修复 Cancel立即改变可见状态但后台清理尚未结束时的 Forget竞态：`CanForget` 是完成状态检查，不阻塞，UI禁用/业务再次拒绝；固定被阻塞的后台 prepare锁夹具验证已发布索引读取不等待该锁。`Debug-final.log` 的 `asset_import_busy` 失败记录保留；`Debug-final2.log`、`Release-final.log` 是非阻塞索引修复前的通过结果，不作为最终源码证据。其他中间 `Debug-initial.log`、`Debug-integrated.log`、专项日志同样保留。

证据存 `out/verification/m3-6/`，像素图像存 `out/verification/m2/render-<Configuration>/gui-offscreen-*.bmp`；不提交 ignored 二进制/包/备份。对GUI合成图做了图像检查，但它不代表真实窗口人工验收。

## G6 未闭合项

- 共享设备、受控批量离屏缩略图与 ignored 缓存**未实现**；不把 FBX CPU wireframe 或整场景截图称为资产缩略图。
- 独立 material/materialSet 文件的 typed browser/编辑器工作流仍待接线；当前类型选择限已 cataloged 的派生 subasset。
- 真实键鼠拖放/焦点/IME、跨屏 DPI、最小化恢复/Inspector 一致性需填写 [人工清单](M3_6_MANUAL_ACCEPTANCE.md)。本轮自动环境没有完成这些项目。
- 不是完整 gizmo、精确 surface picking、完整用户组件引用索引或性能验收。animated conservative bounds 只用于选取，未恢复未经验证的 animated GPU 裁剪。
- M2 人工第三方 MCP、自包含目标环境、长稳与生产性能 pending 不因此关闭；Vulkan 绘制/完整2D管线/完整 Animator 未增加实现声明。

提交允许保存已测试候选源代码，不等于 G6 通过。按原阶段依赖，G6 的剩余功能/人工证据或用户明确允许候选并行开发之前，不推进 M3.7 为已验收阶段。
