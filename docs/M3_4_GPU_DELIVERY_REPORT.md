# M3.4 静态场景 GPU 交付记录

日期：2026-10-05。基线：`d2b9371a2d48e3f42c82e914cd96252fefb2bc1e`。
状态：2026-10-06 最终顺序 Debug/Release 完整回归均通过，静态 DX11 切片 G4 关闭。按用户选择先提交/推送本阶段，再开始 M3.5；未称完整 3D 后端或整个 M3 完成。

## 实现

- `Ncma.Scene.Rendering` 保持无 Renderer/native 依赖的注册、作者校验与已提交 World 提取；新增 `Ncma.Rendering.Scene` 适配层持有 CPU/文件和 GPU 租约。准备在可信启动、显式刷新或 Play 会话变化的帧外边界执行，不逐帧捕获/编码场景文档。
- Renderer 模块 ABI 1.2 增加独立 query v4；v1/v2/v3 的结构与 reference/resource 图片语义冻结。v4 的 scene draw/frame/pass/table/stats 分别为 256/240/16/48/72 字节；C、C++、C# 校验布局。GPU 只接收有界数值批次，不持有 World、GameObject、UUID 解析器、场景时钟或编辑策略。
- 默认公开 `Scene3DPipeline`：单主方向光 shadow → opaque/alpha-mask GGX HDR → ACES/sRGB tone；支持 PCF、近似 PCSS 和匹配的 alpha-mask 阴影。相机/灯光使用独立裁剪集合，相机外遮挡物仍可投影。法线使用对象 inverse-transpose；灯光 layer mask 同时控制直接光与遮挡物集合。
- C# 注册的 Feature、geometry/tone stage 替换和完整 pipeline 替换使用同一 typed graph；编译器检查能力、读写角色、尺寸、顺序、唯一 writer 和已初始化依赖。当前 v4 限定已注册的 scene 内建操作，不承诺任意 shader、计算 pass 或完整 RenderGraph。
- 不变资源按 UUID/kind/generation/hash/材质变体共享；Edit/Play 拥有独立提取器与管线，Play pin 原代资源。曝光/ambient 改变只更新已验证的 C# pass 参数，不重新分配 HDR/阴影图；尺寸/阴影资源变化先建候选后替换。失败准备不改 World；执行后 GPU 故障 fail-stop，不冒称玩法回滚。
- 纯空/2D 不初始化三维 mesh/rig/HDR/shadow，Player 用轻量 clear，不再展示参考立方体。Editor reference 仅保留显式无项目 preview/smoke；实际项目包括 smoke 均走场景路径。
- 项目可保存可选 `sceneCamera` UUID；有静态 mesh 的图形 Player 必须显式选择有效相机。Editor 可切换独立浏览相机/已选场景相机并显式刷新资源，缺资源保留 UUID 并显示诊断。Play 结束、刷新、恢复后不继续用旧 frame-local 对象映射。
- 只读 `ncma.render.inspect_pipeline`、`inspect_graph`、`get_profile` 返回有界 v4 数据和声明的 schemas，不新增 GPU/World 写权限。场景改动仍走现有 Editor.Core 命令/事务/Undo。
- RHI 增加反射校验的无属性全屏三角形：仅 shader-generated system inputs 允许无 vertex buffer。传统属性管线仍拒绝缺少顶点缓冲，不放宽资源/索引安全边界。

## 测试与证据

保留已有 M3.4 67 项组件/格式/CPU 资产/提取测试和 M3.3 图像语义；新增：

- 独立 CPU GGX/ACES/sRGB 像素 oracle（9 点），单方向光/相机外 caster/PCF/PCSS/alpha-mask 参考图，默认、Feature、stage 和完整替换的实际像素；稳定 ABI 提交无托管分配。
- 生产 NCA → 只读 CPU metadata/lease → 多对象提取 → GPU；相机裁剪与 light-space 裁剪分离，非均匀对象变换、normalized viewport/letterbox、8 次 Edit/Play/Stop、曝光不重分配、reimport 旧代保留、空 2D 无三维初始化与最终资源归零。
- 0/1/256/4096 实例分层日志；World 上限仍 4096，满容量用独立浏览相机测量，不提高/绕过引擎对象容量。稳定提取/编码/提交托管分配为 0；每帧一次 ABI；原生逐 draw 提交尚较重，不称高效 instancing/ECS 或作 FPS 保证。
- 原生 ABI 短表/版本/数量/布局、批次最后一项错误、外来/释放代数、矩阵/参数/资源范围、owner-thread、active-frame 释放、真实提交正例与注入 fail-stop。所有输入先完整校验；注入状态不等同真实驱动 reset。
- 真实 Player 静态场景 3 次初始化/退出，文件未改写、文件 pin 释放、显式相机、错误相机在 GPU 初始化前拒绝；本地相机按钮和只读检查的自动测试。

图像、计数与性能日志由完整构建生成在 `out/verification/m2/render-<Configuration>/scene-*`。
完整构建日志：`out/verification/m3-4/Debug-scene-final7.log`、`Release-scene-final.log`，退出码均为 0。两配置各通过 29/29 CTest（native 10 + managed/application 19），场景基础 67/67、Editor Services 42/42、Player 25 项、Assets 38/38、Import 24/24、Gameplay 53/53；managed/native smoke、严格新格式/旧格式拒绝、Python 42/42 与 inspect/preflight 通过。全包核对后部署至 `out/bin/NcmaEngine.exe`，保留 recoverable backup/journal；无 Skip 部署。

两配置 `scene-pipeline-v4.json`：PBR 9 点最大误差 1（容差 2）、相机外遮挡产生 12870 个变化像素、稳定 ABI 分配 0、validation errors/warnings 0/0。`scene-resources.json`：8 次 Edit/Play/Stop 与 reimport pin、normalized viewport 测试通过，最终资源归零。冻结 v3/reference 图片语义保持不变。

4096 实例/8 测量帧：Debug 总 506.84ms、最近 native submit 49.74ms / GPU 38.67ms；Release 总 241.25ms、最近 native submit 28.23ms / GPU 20.55ms。缓存命中提取/编码/提交分配为 0、各 8 次 ABI，copied bytes 8390784、constant upload 13110400；这是小样本设备夹具证据，不是性能验收/FPS 保证。空场景 ABI 为 0、GPU sample 无效且时长 null。

历史 CPU foundation 测量的 `gpuSceneRendering=false/g4Accepted=false` 与其 stdout 描述仍限定在 CPU 夹具，不作为全阶段判定；G4 的 GPU 门禁依据本报告及独立 scene GPU 证据。前次夹具容量/故障帧编号、编译失败与无阴影 sampler 警告日志保留，未删除/过滤失败证据；最终两配置全部重跑。

## 实际边界

本阶段是静态 DX11 场景切片，不包含 GPU 动画蒙皮、IBL、透明、多光阴影、CSM、screen-space contact shadow、Vulkan 绘制或离屏 GUI 编辑视口。现有 reference 配置的 CSM/contact 参数仍仅作用于 reference，不把它们虚报为场景参数；scene API 的 PCSS 半径以 texels 表达。
Editor 使用明确固定浏览相机位置，轨道/导航与完整 mesh/material Inspector 留 M3.6；资源引用变化通过显式 refresh 准备，尚无自动增量资源准备。
CPU 静态提取 miss 扫描/分配仍是正确性基础；完整 GUI 与 WorldRunner 不因此宣称零分配。性能数字只属于当前设备和夹具，GPU 时间为最近完成的 timestamp 样本；空场景没有样本，不借用上一帧值。
M2 人工 UI/DPI/第三方 MCP、自包含目标环境、长稳/完整性能等门禁继续未通过。
