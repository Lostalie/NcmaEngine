# M3.9 只读运行包候选

日期：2026-10-06。源码基线：`1bbb9ea033787d584f018ff41142428e1dac6894`。
状态：候选切片及最终顺序完整 Debug/Release 回归通过；不是完整 M3.9。G6/G7/G8 仍开放，G9 不关闭。

## 本轮实现

C# `Ncma.Assets.Runtime.RuntimeAssetPackage` 提供独立 `.ncpak` NCP1 运行数据包。
可信宿主在帧外通过 `SceneAssetPreparation.CreateRuntimePackage(root, project, scene, dynamicRoots)`
从**已提交且校验通过的 generation**取得依赖闭包，返回 owned bytes。该 API 不写文件、不发布项目、
不导入 FBX、不改变 World/Undo，也不是 MCP 或用户可操作的完整导出器。

入口来自启动场景的 typed mesh/skin/clip/material 引用和作者明确传入的动态 `AssetRef`。
不扫描 C# 字符串、私有脚本字段来猜测资源。模型按完整不可变 generation 保留 manifest/rig/mesh/slots/clips，
暂不细粒度裁剪同模型的其他片段；独立 materialSet → material → texture 递归闭合。
未引用的独立资产不打包；Prefab/OverrideSet 和 runtime spawn 不支持，只有已经烘焙的普通场景。

包有16字节小端 header、闭合 JSON 索引和连续数值块：文件/索引版本1、project UUID、严格排序的
asset UUID/kind/generation/hash/encoding/model/rig/offset/length。包≤64MiB、索引≤2MiB、资产≤4096。
无时间戳、job UUID、源文件路径、authoring descriptor 或运行句柄。原数值 model manifest 的
sourceHash/采样 settings、材质槽名称仍保留；不是删除所有 provenance 或用户名称的隐私过滤器。

加载先检查版本、长度/范围、重复/缺失/未知字段、排序/UUID、SHA256、typed payload、
模型角色及代数/rig/骨骼数/材料槽一致性、材质纹理语义与依赖。之后才创建只读运行资产租约，
没有 STL/托管对象/native handle 跨 ABI 的新通道。索引没有独立签名，部署 manifest 校验整个文件；
hash 是完整性/一致性证据，不是授权或安全沙箱。

现有 `.ncmaproject` schemaVersion1 增加可选 `assetPackage`，例如 `assets/game.ncpak`。
只接受规范项目内路径、精确扩展名和现有文件/reparse 检查；这是当前 reader 的显式功能，
不声称旧二进制能读取新字段。配置包后图形和 Headless Player 都在 gameplay bootstrap/原生初始化前
严格解析依赖；缺失/损坏绝不回退 authoring 或 reference cube。未配置包的现役开发项目读取路径保留。
Editor 的现有 authoring adapter 和 Player 包 adapter 共用 Scene.Rendering/Runtime lease/G4/G5 消费路径；
正式 Editor 直接编辑运行包及导出按钮未实现。

整个包只读 pin 到最后一个租约结束；拥有者先关闭不会使 Play 的已取得副本失效。
owner-thread 规则不变，返回的数据可复制但不允许跨线程使用 lease。
现有 `PinnedGenerations` 在此 adapter 计的是包文件 pin（1），不是包内模型个数。
这是启动时全包内存解析，不是 streaming/mmap、压缩、多包热替换或生产内存优化。

## 自动证据

专项 `runtime-package-first.log`：Scene Rendering 77/77，新增7项覆盖重复打包字节一致、
无 authoring/out 目录迁移、动态材质/纹理根、类型/版本/长度/hash/身份/依赖拒绝、明确包无 fallback、
取消/超预算和32轮 owned payload/文件 pin/owner-thread 生命周期。32轮租约不是32轮 GPU Play。

`player-package-fourth.log`：Player 27项通过。ASCII6100/binary7400 许可 sausage fixture
导入 NCA 后生成独立包，包内无 raw FBX/ncmeta/NCA 文件/ImportWorker/ImportKernel/
Assets.Authoring/Editor/Gui/MCP/Python，C# gameplay 使用已构建 Sample，不携带测试/导入程序集。
DX11/pose 原生插件来自已验证的 M2 独立 Player 模板，不从开发目录寻找导入内核。
实际图形 PlayerRunner 与迁移 apphost 的图形、Headless 子进程均运行真实场景；
Headless 精确4 ticks、modules 空，图形实际提交帧、API validation 0/0、关闭无错误。
所有 fixture 和 manifest 位于 ignored `out/package/m3-9/<Configuration>/<UUID>`，不外发用户私有素材。

候选 manifest 仍使用现有部署 schema1 的文件/hash/路径校验，并额外记录 asset package
格式/路径/hash/条目数及 pose ABI1/lazy。篡改包既被 payload reader 拒绝，也被整包 manifest 拒绝。
这是新建测试候选包审计，**不是**已交付 M3 专用 cook/deploy stage/backup/journal/recovery。
M2 正式部署/恢复回归保留，不能由它推断 M3 cook 发布已完成。

最终冻结源码顺序完整 `Build.bat -Configuration Debug`、`Release`，无Skip、退出码均0。
日志为 `out/verification/m3-9/Debug-final3.log`、`Release-final.log`；独立managed CTest副本
`Debug-final3-ctest.log`、`Release-final-ctest.log`。每配置31次CTest（native11 + managed/application20）、
Python43/43、managed/native smoke、inspect、新格式严格校验/已移除格式拒绝、三轮保留profile和
部署前后审计全部通过；新增代码无编译警告。Scene Rendering77/77、Player27项、Editor Services62/62、
Import25/25、M2部署恢复8/8，原冻结GPU/Runtime/Physics/Gameplay等负例未删除或放宽。

最终两配置新增 Player 验证在上述专项基础上：每种FBX包各32轮实际创建/Play/Stop（共64轮/配置），
每轮有实际帧、tick推进、API validation0/0、renderer/pose关闭的资源租约guard通过，且包文件
独占写句柄可重新打开。场景bytes始终不改。原生关闭guard和文件pin释放不等于完整driver/进程内存
统计或一小时长稳；另有32轮纯runtime租约测试，二者不混算。损坏包的图形/Null预检均在
tick0/modules空状态拒绝并释放；迁移apphost的图形与精确4 ticks Null模式均通过。
最终候选排除PDB和可选XML文档，不把开发符号一起复制到此运行包。

最终Release许可fixture包（非生产发行包）：

- ASCII：`out/package/m3-9/Release/69e13477a880432b9ef19669e26f1537`，`game.ncpak` 119063 bytes。
- Binary：`out/package/m3-9/Release/9cf0dda0c0674705b0274b488fe6eab4`，`game.ncpak` 114075 bytes。

Release checked Editor整包已部署到 `out/bin/NcmaEngine.exe`，可恢复备份为
`out/deployment/f6cb62e69fe9444d91ae0bb700e531ad/backup`；Debug备份为
`out/deployment/15135ee33f2141f89fd7541aa784e8e9/backup`。部署前后audit_passed=true、h8_accepted=false，
M2人工/目标环境/完整性能/长稳pending项不改。候选数据/包/备份全部ignored，不提交GitHub。

## 发现的失败

直接 apphost 的第一轮 Player 在既有 NCA 读取阶段失败（`player-package-first.log`），
private RuntimeReadPin 的原始 CreateFileW 路径超过 MAX_PATH。私有 Open 现在使用规范化
扩展长度 Windows 路径，保留父目录 pin、canonical casing、reparse/hardlink/文件类型检查。
没有缩短 UUID/hash、恢复旧 host 桥或加入公开设备路径 API；本地验证不等于 UNC 目标机验收。

第二轮新测试复用了已存在的报告路径，触发 PlayerReports 的正确禁止覆盖规则，导致
预期 dependency_failed 的断言提前失败（`player-package-second.log`）。为损坏包分配独立报告后通过；
未放宽生产规则或拒绝断言。失败日志保留。

复核时又发现原始材质槽数0与1均映射到有效默认槽数1，会让不匹配的模型闭包通过。
`raw-slot-before-fix.log` 用重新计算hash的mesh反例复现；现在比较原始mesh payload的
MaterialSlots与MAT1名称数组长度，而非展示层的fallback count。`raw-slot-fixed.log` 77/77通过。
此前Debug-final/final2的通过结果均不作为最终修复版源码的门禁证据。

## 未实现 / 待验收

- A：清空缓存后从许可源+settings+importer/target profile 冷重建、统一 cook recipe/CLI/GUI、
  全部输入/依赖 cache key、资产许可 inventory 与受控回收未实现。现有 generation 打包的确定性不等于 cold cook。
- B：Prefab发布/实例覆盖、runtime spawn、动态资源的 gameplay service、streaming与纯2D裁剪包未实现。
- C：M3 专用资产包 stage/backup/journal、重复/中断恢复、wrong plugin/真实device fault 联合验收待补。
  本轮未改正式部署业务、不清理旧策略原型、不改用户文件。
- D：M3.6 缩略图/独立材质浏览、M3.7 发布/覆盖/同步、M3.8 异步写任务/联合授权未实现。
  用户真实角色/贴图/预期画面未提供，真机输入/DPI/第三方可见 MCP 修改闭环、自包含目标机、
  约定完整性能和一小时工作流未验收。G9 不能由候选包或隐藏/短帧测试关闭。

交给后续的仍是 typed immutable lease、committed-tick 片段时钟和只读根位移；没有 Physics Step、
World 根运动写入、Animator、Vulkan 绘制、Python AI transport 的新实现声明。
