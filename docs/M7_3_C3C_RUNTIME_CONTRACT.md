# M7.3-C3-C 正式产品联合测试合同

2026-10-14。基线 `7de102e3f8e6ddeb692f013eade47e1f23cf72e1`。本片为产品联合自动验收，不新增生产渲染算法、原生 ABI、编辑器命令、Agent 权限或兼容格式。生产改动仅为独立 UI 示例增加环境资源零计数断言与报告字段。

## 两层证据，不混淆入口

从本配置 `out/verification/m2-7/<Configuration>/packages.json` 读取刚构建的 Editor、DX11 Player 和 Null 包。先验证源 manifest，按清单复制到新的有界测试目录，再校验副本初始精确文件数、路径和 SHA。保留源包、所有失败副本及日志。manifest 是一致性校验，不是签名或沙箱。

1. 实际搬移的 `NcmaEngine.exe` 使用既有 smoke CLI；`NcmaPlayer.exe` 使用既有 ticks/report CLI，原样使用相邻插件和默认 Shader 索引。无新截图/Agent 开关。核对 Player 固定步、API 错误/警告与关闭；图形帧允许累计固定步超过目标，Headless 必须精确8步。
2. 仅测试程序集提供独立子进程探针。专用 AssemblyLoadContext 强制所有 Ncma 生产程序集来自该搬移产品，缺失即拒绝，不回退开发输出；记录实际路径与 SHA，并与 manifest 对照。测试程序集本身是唯一显式例外。进程内默认资源根指向产品并断言生效。PE 元数据区分 CLR 托管映射与原生模块，实际 Ncma/glfw 原生插件也必须来自该产品。这里是相同部署程序集中的正式共享宿主测试，不声称探针就是实际 apphost 的入口过程。

## 无源与资源身份

独立运行项目只有 `.ncmascene`、项目配置、NCP 和 C# gameplay/deps；无作者 FBX/NCA/ncmeta/ncenv/NCE/Shader。作者目录全部文件独占锁定，实际 FBX 原件在对应运行期间也独占锁定。NCP 携带精确 UUID/generation/hash 环境及模型资源；Shader 使用产品默认闭包。环境基于默认受检合成中性 NCE，不声称用户 HDR 解码。

坏默认 NCE/Shader 由真实 apphost 在 manifest 阶段拒绝。坏 NCP、错误 UUID/代次/hash 必须在 Player gameplay 创建前拒绝：session为空、tick0、frames0、正常关闭。测试不通过重算 manifest 掩盖损坏，且健康场景恢复后再次启动。

## 图像、编辑与生命周期

静态几何中心像素由独立软件 cube/mip/LUT 消费 oracle 计算，容差3个8-bit级；记录实际误差。完整 B3 软件/积分与独立CPU四权重蒙皮353项继续运行，不拿同 shader 截图自证算法。

正式 Editor 检查强度/旋转、Off、精确 Undo/Redo、窗口内视口 splitter 引起的实际目标 resize、3轮 Play/Pause/Reload/Stop 和关闭卸载。对照截图用固定预览时间隔离动画变化。真实 ASCII/Binary FBX 的第一个片段可能是静态 BindPose，因此显式选择有关键帧变化的片段并按绑定姿态包围盒构图；额外64次暂停单步确认 Play tick、动态图像、GPU skin 批次和 shadow draw。实际 Player 也跑相同无源动画包。

动态代次用独立**源项目**测试，不冒称 NCP 支持原地修改：可信夹具提前准备新不可变 NCE，通过既有 Off → 显式离帧资产刷新 → 新精确身份命令 → 绘制切换，不替换活跃 GPU 环境缓存。直接把 generation 改为不存在值的普通命令必须拒绝且保留画面；另外明确的测试故障注入模拟可信扩展破坏 live state，验证失败清屏/错误提示及显式恢复。这不是新增 Agent/编辑器写活 World 的权限，也不是自动环境重导入工具。

## 成本与轻量边界

暖机16帧后32帧无截图采样，记录 Editor 全宿主 CPU、托管分配、GPU有效标志/耗时、environment/resource/pipeline/skin统计。断言无新增环境发布/纹理上传/readback、无不可变网格/纹理上传或管线重建；不把完整 Editor UI 当作零分配渲染内核。GPU无有效样本时不能把0称实际耗时。数据只是当前机器短样本，不是完整性能门禁。

搬移 Null 不部署插件/Shader/NCE、不加载 Renderer/Platform/Pose、无呈现；独立纯UI保留18文件闭包，并在原源布局和实际搬移 apphost 中断言环境 live/resident/publications/upload 全0，API0/0。

## 未关闭门禁

HDR/EXR文件解码、用户HDR/FBX/材质、人工UI/DPI/第三方可见MCP、目标机/.NET自包含、完整性能与1h稳定性仍待验收。GpuValidated=false、内部编译器非沙箱的原合同不变。无新推理服务、Python游戏脚本或网络耦合。完整双配置成功并提交推送后只能关闭M7.3**自动候选链**；随后M7.4，仍为DX11only，Vulkan/OpenGL下一版，M7→M8→M9。
