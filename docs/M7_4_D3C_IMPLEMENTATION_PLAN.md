# M7.4-D3-C 正式 Editor HUD、交互与搬移联合

## 最终状态（2026-10-14，覆盖下文历史增量状态）

实现顺序1–5均已完成自动候选验证。最终64份冻结源码无Skip顺序Debug/Release均通过：各12原生/39联合测试组/Python44，加全部原格式/MCP/smokes/inspect/三轮profile/audit/恢复。Editor作者准备与选择面板已接入；New/Open权限epoch、Undo/Redo、off-frame交换和完整ASCII/Binary FBX-NCA/IBL/阴影/Bloom/HUD受检搬移联合已验证。场景引用是唯一可变发布根，先持久化不可变包，再走原EditSession/原atomic Scene Save，不引入第二份可变manifest/发布journal。

正式Release部署Complete `6cb475caadea4c0ca717d00fe9f52c6d`，实际EXE校验exit0。D3/D/M7.4自动候选链闭合，详见[交付记录](M7_4_D3C_DELIVERY_REPORT.md)。下文局部“正在运行/未接入/未交付”均为保留的历史过程，不是当前状态；人工/目标/完整性能/长期及历史间歇问题仍开放。提交推送核对远端后进入M7.5。

2026-10-14。D3-B 本地/远端基线 `5fec03226ad947a444a3cd4598c8020d223465b7` 已核对；工作区只存在四项不纳入任务的 IDE 修改。D3-B 已交付被动 Player HUD，不等于完整 UI 产品。本文不改变 D3/D/M7.4 验收范围。

## 实现顺序（同一阶段，不以局部通过替代最终交付）

1. **共享显示表面。** 在既有 `SceneUiPresentation` 上增加显式、惰性的透明缓存表面；Editor 使用现有 UI target / presentation lease / ImGui cached image 合同，3D 场景目标与 HUD 目标独立，颜色域只在场景中做 Tone。静态 HUD 只复用像素与列表，resize/DPI 变化才准备；持有显示租约时禁止修改与关闭底层资源。Player 默认仍直接 overlay，不创建额外目标；纯 2D 不加载 3D 资源。不新增 native ABI。
2. **正式 Editor 入口。** 精确项目 UiPackage 在插件/Gameplay 前执行 CPU 校验，字体/图像/布局在 Gameplay 前准备；缺少 Flat2D/错误依赖明确拒绝。把透明 HUD 合成到真正的场景视口范围，保持相机、拾取、Gizmo、UI 作者工作区和动画预览的输入边界。只有实际加载/合成/关闭都实现后才替换 D3-B pending guard。正式视口截图必须包含最终 HUD，不能只截图独立 UI target 自证。
3. **有界交互与 C# 逻辑。** 复用 UiRuntime 的 focus/capture/Unicode/slider/scroll 状态；窗口到视口坐标含 DPI/裁剪，失焦/溢出/切换工作区取消交互。明确区分 UI 捕获与场景/游戏输入；消费事件不得再次触发角色。事件复制并绑定 instance/generation/Play/world/tick/sequence；显式可信 C# 启动注册 action 路由，未知 action 启动前拒绝，无反射方法名执行、无任意代码加载。仅在安全边界接受有界提案并让固定步处理；渲染、平台、AI 回调不能直接改 World。暂停/停止/异常/Reload/Undo 后拒绝旧事件并重建当前绑定。未完成前保留交互拒绝，不声称已实现。
4. **编辑生命周期。** 原 scene 命令/Undo/Redo/Play/Reload 的唯一事务服务不变；明确区分不可变部署包与已批准的作者 UI 草稿，不能偷偷重新打开作者文件或自动改写已 pin 的包。作者内容更新必须有显式、有权限且可撤销的准备/发布路径和新代次，运行状态不写回作者文档。Play 使用独立 UI 状态，Stop 回到编辑状态；修改导致资源准备失败时不把旧图像标记成新版本。
5. **完整产品联合。** 实际 ASCII/Binary FBX→NCA→动画/skin/PBR/IBL/shadow/Bloom/最终 HUD，在正式 Editor/Player/Null 的受检搬移产品中运行，作者源被锁定/不可用。验证默认、stage、full-pipeline 定制、resize、资源代次、Undo/Redo、3次 Play/Reload/Stop、关闭，以及错误模型/环境/程序/UI闭包在 gameplay 前拒绝。严格产品程序集/资源路径与 SHA，不使用源程序集回退；保留真实 apphost 和独立产品 API probe 两种证据并区分。

## 验证与交付

- CPU：owner、完整输入批次、stale/replay/跨会话、未知 action、暂停/切换/溢出取消、事务与旧格式拒绝；不直接使用 live World 作为 UI 状态仓库。
- 实际 DX11：透明背景/半透明叠加、字形/图片像素、裁剪与颜色域，最终正式视口截图与独立 CPU 合成参考；失败原子性/资源与租约关闭/API0/0。独立后处理 oracle 不升级为完整独立光照 oracle。
- 暖态记录 CPU/分配/上传/target production/资源预算，区分整宿主与内核。无 origin-frame GPU 时间不声称逐帧独立采样，不能用静态缓存零上传代表完整性能门禁。
- 本阶段全部完成后冻结源码，顺序无 Skip Debug/Release `Build.bat`，原回归/格式/MCP/smokes/Python inspect/3轮profile/audit/部署恢复；核对清单、备份、Complete journal、实际 EXE，再提交推送并核对远端。

## 开放条件

本文及局部测试不关闭 D3、D、M7.4。人工 UI/DPI/IME/第三方 MCP、用户素材/HDR 解码、目标机/自包含、完整性能/1h、历史 source-recovery 分配/empty-cache/File.Replace 间歇问题仍开放。Python 非 gameplay；没有新增 AI 推理或代码执行权限。本版本 DX11，Vulkan/OpenGL 下一版本。

## 当前执行证据（未完成阶段，不是最终交付）

### 最新：场景选择事务、正式发布面板与搬移验证

以下为最新实现，替代后文历史记录里的“准备尚未接入选择/正式按钮”等状态；整个 D3-C 仍未关闭。

- `SceneUiData` 是场景级不可变运行包引用值，不是新的对象树。缺省沿用项目明确的 UiPackage；显式 Off 抑制项目默认值；整场景只允许一项（包含 Off）。严格保存版本、UUID、generation、路径和 SHA，不保存运行句柄或控件状态。注册类型数 12→13，仍独立验证原来的 10 个类型。
- `EditorUiPublication` 复用原 EditSession 参与者、精确对象/组件范围和唯一历史。准备不改场景，选择只生成一条原历史记录，Undo/Redo 重验权限与固定资源引用。最多 32 个 pin、128 个参与者批准记录，不把大包塞入 16MiB 场景历史。没有新增 Agent 默认批准、执行或推理权限。
- **提交模型澄清：** 已准备 NCU 文件先成为持久、不可变资源，唯一可变发布根是场景引用。选择在原场景事务里原子提交，磁盘场景仍由原原子 Save 保存。因此不再引入先前草案设想的第二发布清单/跨文件日志：不存在第二个同步可变文件；失败、取消、Undo 后保留不可变孤立代次，不删除用户资产。准备服务自身的精确 stage 恢复仍保留。
- 正式 UI 文档面板增加新代次输入、包的项目/UUID/路径/SHA/指纹展示，输入完整指纹后才可“批准并准备”；之后另行审阅选中 GameObject 的场景引用，输入新的完整指纹才可“批准并应用”。明确显示保存场景要求、不可变包保留、Off 抑制默认值。旧点击、选中对象变更、错误指纹、冻结/Play、草稿和撤权均不得发布；作者预览仍独立。
- Editor 仅在已提交修订变化时、帧外准备完整 HUD 候选，成功才交换。失败不显示旧图像冒充新代次；Undo 可恢复有效引用。旧资源关闭失败进入显式保留/关闭恢复状态，不自动重试或卸载依赖。Player/Null 同样在 gameplay 前解析场景覆盖值与精确包。Play 准备回调用传入的已提交 Play 快照，不通过 owner getter 重入。
- 首轮真实 DX11 暴露 `Session callback reentry is forbidden`（`out/m7-4-d3c-ui-publication-first.log`），已按上述快照边界修复，未降低 owner 保护。Debug 第二轮 22 项：`out/m7-4-d3c-ui-publication-second.log`，证据 `out/verification/m2/scene-ui-publication-targeted/273f253d0cc242f289048d365b2d6e02`；Release 第一轮同样 22 项：`out/m7-4-d3c-ui-publication-release-first.log`，证据 `out/verification/m2/scene-ui-publication-release-targeted/c7d57bf4edd541f081deeef96fcd0221`。包括最终 GUI 像素、单历史/Undo/Redo/Off、错误依赖清空/恢复、3轮Play/Stop、保存重启、锁作者源 Player/Null、暖16缓存/API0/0，不代表完整性能验收。
- CPU 正式面板与选择 5 组在 Debug/Release 通过：`out/m7-4-d3c-ui-publication-panel-{first,release-first}.log`，证据分别 `5da89ab32d964d5bb24d377d3c37e4ea`、`becc2e71341044f8bbc659b85d4d98bf`（`out/verification/m2/editor-services`）。原 SceneRendering108项通过；原 Debug Editor154/154通过：`out/m7-4-d3c-ui-publication-editor-regression.log`，证据 `35e1dab811434d6faa1f0374f2b8c7b4`。这些原回归早于最后场景切换 epoch 修复。
- 标准 Skip 候选：Debug `410842ff00284899991500160e0b1a10`、Release `203bf263d5a04ff1891beb76ad390ec3`，日志 `out/m7-4-d3c-ui-publication-{debug,release}-candidate-build.log`。Skip 不部署。Debug 的正式包搬移验证395项通过：`out/m7-4-d3c-ui-publication-products-second.log`，证据 `out/verification/m2/scene-ui-publication-products-targeted/30e3cb2a8a2c46e6a6dc89c149b24cb0`。395包含文件路径/哈希，子探针另有细分记录，不是395张图。产品程序集严格来自复制清单，原项目独占锁定，不复制作者UI；真实Editor/Player/Null exe均exit0；产品API另测最终画面、3轮Play/Reload/Stop、暖16缓存/API0/0和原生卸载，Player不装载Editor/MCP/GUI。
- 搬移第一轮失败保留：测试误把图形 Player 的最低目标步数写成精确2，实际报告exit0、tick3、API0/0。按既有图形 `>=target` 合同修正夹具，Null仍严格等于2；未更改产品调度或降低已有断言。日志 `out/m7-4-d3c-ui-publication-products-first.log` 和目录 `349e76a034464d6db40027232c614ffd` 保留。
- 后续 New/Open 生命周期审查发现两项问题：旧 prepared grant 重新打开同作者文档后可能复活；参与者永久绑定初始场景代次导致新场景无法发布。准备授权现在绑定 `EditorViewStamp.Generation`，旧审阅随场景代次失效，参与者仍属于同一个 EditSession。明确重新批准后才可在新场景使用，原不可变数据及历史记录不自动删除。失败 `out/m7-4-d3c-ui-publication-replacement-first.log` 保留；修复后 Debug6组通过（`...replacement-second.log`，证据 `1d4aea1883f642ae92567c10589b1010`），准备11组回归通过（`...preparation-regression.log`，`2ab86d3c351d409f8c6c3505dc289fe3`）。上述两个候选包早于此修复，不得视作最终源码产品验证。
- 已开始独立 `SceneUiJointProducts`：实际 ASCII/Binary FBX→NCA→非BindPose动画、IBL、真实shadow、Bloom、最终HUD，严格搬移默认/stage/full pipeline、64固定步、三轮Play/Reload/Stop、Undo/Redo/resize、完整宿主暖态成本，保留原D2独立像素oracle。当前仅编译0警告/0错误，正在运行 `out/m7-4-d3c-ui-joint-products-first.log`；通过结果需核对后补录。不得把程序开始运行等同于联合验收完成。

仍须完成：最终场景切换修复的双配置重建/原回归、新联合测试及错误闭包/资源代次证据、正式UI可见性复核，以及全阶段源码冻结后无Skip Debug/Release、清单/备份/恢复/实际EXE、提交推送核对。正式部署仍为 D3-B Complete `a290a77b582c49cd812e7e3aeb90dc70`，本轮没有提交或更新正式exe。

### 首次共享表面迭代（历史状态）

- 已增加 `SceneUiPresentation.PrepareSurface/AcquireSurface/ReleaseSurface/CaptureSurface`：显式惰性透明目标、静态像素复用、owner 检查、视口变更失效、GUI 租约未释放禁止准备、失败资源保留与可重试关闭。直接 Player 路径不创建目标。正式 Editor pending guard 与交互拒绝暂未移除，第2–5项仍须实现。
- 专项首轮真实像素发现旧 CachedImage 把预乘目标再次乘 Alpha：预期 `[128,0,128,255]`，实际 `[63,0,128,255]`。失败 `out/m7-4-d3c-surface-first.log`、`out/verification/m2/scene-ui-surface-targeted/3ee2c162880f4d53b1dade344e0d0c5b` 保留。
- 修复 native GUI 的缓存图像混合：ONE/INV_SRC_ALPHA，图像后立即恢复原 ImGui renderer state；不修改 renderer/Shader/ABI布局，不改 SDK。新增普通半透明 CanvasRect 跟随缓存图像验证状态恢复。第二/三轮测试夹具分别漏了请求 GUI ABI1.7、CanvasEnd 唯一 widget ID；修正夹具，保留原拒绝合同和日志。
- 第四轮专项22项通过，实际合成 `[127,0,128,255]`，误差≤1；覆盖目标透明度、普通GUI状态恢复、GUI持有租约时关闭拒绝、owner/重复acquire/零帧/未准备、暖32无上传/目标production、resize/stale capture/关闭/API0/0/纯2D无3D。证据 `out/verification/m2/scene-ui-surface-targeted/582bbbf53dbf45bd9535c57576102c36`，`out/m7-4-d3c-surface-fourth.log`。这不是正式 Editor HUD 最终图像或整阶段证明。
- `Build.bat -Configuration Debug -SkipTests -SkipPython` 两轮编译通过；定向测试夹具最后两次用 dotnet 单独重编译，0警告/0错误。Skip不部署。原 UI19通过；Editor服务首调缺少仓库参数（IndexOutOfRange）记录保留，按CTest精确参数复测154/154通过；D3-B正式Player HUD336通过，证据 `out/verification/m2/scene-ui-host-d3c-targeted/563d3dcd8ae548b78e50441f5352880e`。
- 本片尚未完整Debug/Release，不提交/推送、不更新正式EXE；部署journal仍为D3-B的 `a290a77b582c49cd812e7e3aeb90dc70`。下一步实现正式 Editor 视口组合与包含HUD的最终截图，然后接通第3–5项，不将表面专项通过替代整个D3-C。

### 正式 Editor 被动 HUD 接入增量

- 已用实际加载/显示路径替换 Editor 的整项 pending guard：UiPackage CPU身份/hash/被动范围与Flat2D闭包在插件前检查；字体/图像/布局准备在 LoadGameplay 前。CPU-only HUD 仍明确拒绝，交互控件/非空Action仍明确拒绝。不是无条件删除保护。
- 正式场景视口合成真实透明缓存图像，尺寸与DPI来自实际视口。作者UI和动画工作区不叠加场景HUD；GUI完成后释放本帧租约，关闭先销毁GUI再HUD及包pin。CachedImage `enabled=0` 现在是真正只显示，不创建 InvisibleButton，因此被动HUD不遮挡场景拾取/相机输入。
- 新增内部测试观察器 `CaptureNextCompositeViewportForVerification`，在 `_gui.RenderGpu()` 后截取最终场景视口区域。旧的纯3D target观察器不变，避免既有光照oracle被HUD污染；新验证不能以独立HUD纹理代替最终合成结果。
- `Build.bat -Configuration Debug -SkipTests -SkipPython` 编译通过，候选代次 `234988cce1514d35b802dbdb75ec07a4`，无正式部署；随后仅测试代码补充了原事务/Undo/Redo/resize断言并定向重编译，0警告/0错误。首次联合547项通过；补充后579项通过，日志 `out/m7-4-d3c-editor-host-second.log`，证据 `out/verification/m2/scene-ui-editor-targeted/6a227a52e13b4db1893af83b981072e3`。
- 579包括保留的Player/Null检查与新Editor source/严格受检搬移、实际Editor apphost、无作者UI/字体/图片/model源、最终可见字形/图片/HUD颜色、未覆盖场景不变、暖16无上传/production/残留租约、原scoped light命令与默认Agent拒绝、Undo/Redo、真实splitter resize、3轮Play/Reload/Stop。HUD包不可变；这些周期验证的是场景隔离与被动显示，不声称交互UI状态隔离或作者UI热发布已实现。
- 接入后表面22项与原Editor154/154再次通过，日志 `out/m7-4-d3c-surface-editor-regression.log`、`out/m7-4-d3c-editor-original-regression.log`。完整双配置/冻结/部署/提交门禁仍未执行；正式EXE仍是D3-B。
- 下一步落实第3项：有界只读UI事件到C#固定步的消费合同与显式action注册、相机/游戏输入互斥和失焦/暂停/Reload取消；然后第4项已批准作者UI新代次/可撤销发布、独立Play状态，以及第5项真实ASCII/Binary FBX-IBL-shadow-Bloom-UI完整产品联合。不可因为静态HUD579项通过就关闭这些任务。

### C# 固定步输入与视口适配基础增量（第3项仍未完成）

- 新增 `Ncma.GameplayUiAction/GameplayUiInput/GameplayUiStamp`，不依赖 GUI、Renderer、UiRuntime、Python 或 native。PlaySession 的 `RegisterUiAction` 只在首次启动前接受精确动作标识和 Activate/ValueChanged/TextChanged 类型；不注册回调、不按字符串反射执行代码，也未增加 Agent 注册/执行入口。
- 可信宿主在 Running 状态显式 `BindUiInput`，获得实例、代次和随机 admission；每次观察还绑定当前 Play/World UUID、SceneRevision、已提交 tick。`SubmitUiActions` 只在 owner 非重入安全边界接受完整批次：最多128条累计、每条文本16384 UTF-16代码单元、累计65536；校验 Unicode、类型、有限归一化值、element UUID和严格递增序号。任一错误不发布前缀、不推进序号，调用者数组不被持有。
- Behaviour/System 仅在固定步执行阶段读取 `Context.UiInput`；Create/Update/清理/外部调用均为空。所有读者看到同一只读批次，按 action/element 过滤，读取不导致抢先消费。成功提交后下个固定步不重复事件；多显示帧不丢掉尚未模拟的输入。World 修改仍通过原固定步组件/命令事务，UI提交本身不改世界。
- Pause/Resume、Reload（包括预检失败）、Stop/Fault、显式取消都撤销旧 admission。世界或元数据修订变化导致尚未消费的输入在执行前被丢弃并要求重新绑定。Fault 仍为 fail-stop；不自动重试UI事件，不声称脚本私有字段或外部IO可回滚。只读副本可被用户保留，它不再是执行权限。
- `UiRuntime.CancelInput` 是宿主级生命周期撤销：清队列、焦点/捕获/组合串，递增generation，保留本实例文本/value/scroll；已有 `FocusLost` 单个输入事件保留其原有语义及原测试（不偷偷改变为清队列）。切换Play/World身份复用新取消入口。
- `UiWindowInput` 新 CPU overload 使用实际 framebufferOrigin、准备好的viewport范围与DPI，返回是否取消/键盘与鼠标捕获。包含窗口失焦/最小化/输入重置/溢出/其他UI占用/同批失焦再恢复时的整批取消；验证平台事件序列、Unicode、数值和完整批次；对合法 GLFW_UNKNOWN key 明确忽略。拖拽出界和同帧释放仍捕获；宿主必须把返回的捕获同时应用到场景相机/拾取与游戏输入。本适配器自身也绑定 owner，不能换一个线程本地UiRuntime绕过检查。
- 当前仍未实现：共享的 UiAction→GameplayUiAction 正式路由和启动前注册闭包验证；Editor/Player同帧捕获/恢复接线；独立Play UI运行状态；作者文档批准的新代次与可撤销发布；完整FBX/IBL/shadow/Bloom/HUD联合。因此正式入口仍明确拒绝交互控件/Action。这些基础接口不等于点击正式HUD已经可以控制游戏。

验证记录：

- 首次 Gameplay 专项编译因测试夹具未使用的 `Created` 字段触发 CS0649（warnings-as-errors）失败，记录 `out/m7-4-d3c-gameplay-ui-build-first.log` 保留；移除未用夹具字段，未降低警告规则。
- Debug `out/m7-4-d3c-gameplay-ui-build-second.log` 0警告/0错误，`out/m7-4-d3c-gameplay-ui-first.log` 原Gameplay53/53、Movement38/38及新增UI输入18/18组通过。Release `out/m7-4-d3c-input-gameplay-release-build.log`、`out/m7-4-d3c-input-gameplay-release.log` 同样通过。运动测试仍是原假求解器合同，不升级为Jolt角色验收。
- 输入适配初版CPU24项通过；复核增加适配器独立owner和快速焦点切换测试后，`out/m7-4-d3c-ui-input-build-third.log` 0警告/0错误，`out/m7-4-d3c-ui-input-gpu-first.log` 28项通过（原UI19项+输入9项，包括真实DX11/字体/Canvas/Undo）。Release `out/m7-4-d3c-input-ui-release-build.log`、`out/m7-4-d3c-input-ui-release-cpu.log` CPU25项通过；不能把无native参数的Release CPU25项写成GPU验收。
- `Build.bat -Configuration Debug -SkipTests -SkipPython` 本轮通过：`out/m7-4-d3c-input-build-first.log`，候选 `3ca36f354a0043378e4a2332c1ca91f6`，0新增警告、没有正式部署。该构建早于最后适配器owner/快速focus hardening，后者已定向重编译验证；候选包不冒充所有最终源码的冻结包。
- 最后源测试重新编译后，HUD表面22项通过：`out/m7-4-d3c-input-surface-regression.log`，`out/verification/m2/scene-ui-surface-input-regression/1d45114582f645cfac601ca89a1cd67b`。正式source/受检搬移/真实apphost联合579项通过：`out/m7-4-d3c-input-host-regression.log`，`out/verification/m2/scene-ui-host-input-regression/ce06301b4d5846ec9cb86d29f72c590d`。搬移检查使用上面的候选产品；正式产品未使用新交互路径，故此仍是被动HUD回归而不是交互产品验收。
- 尚未执行整个D3-C冻结源码后的无Skip Debug/Release，未提交推送。正式部署仍为D3-B `a290a77b582c49cd812e7e3aeb90dc70`（Complete），当前HEAD仍为 `5fec03226ad947a444a3cd4598c8020d223465b7`；保留全部失败和历史证据，M7.4/D/D3不关闭。
- 最后原 Editor 服务重新编译（0警告/0错误）后154/154通过，进程exit0：`out/m7-4-d3c-input-editor-build.log`、`out/m7-4-d3c-input-editor-regression.log`，图像等证据 `out/verification/m2/editor-services/97ea7a2953a542e894f796344f4c56c0`。读取核对正式EXE SHA仍为 `2BD5CD7D15C201B3889A589ADDD715468702DE19DA9EC2B14B51106EFACEFB2F`，入口DLL SHA仍为 `4358C922C92706EF45EE2E5DD38CB0576DA18466AC7C50CD23A154CB6228F688`；本轮没有更新正式部署。

### 可信动作路由与正式宿主交互增量（当前状态，尚未关闭D3-C）

实现：

- `Ncma.Application.Runtime.SceneUiActions/SceneUiInput` 负责纯C#应用层组合。Application.Runtime只增加Ui元数据依赖；通用Ui/Ui.Rendering没有Gameplay依赖，纯2D样例不因此拉入游戏逻辑、编辑器或额外原生插件。
- `SceneUiActions` 是复制且有界的可信启动表，不是项目JSON/Agent可写的执行表。启动前校验Button/Slider/TextInput的动作与kind，拒绝未知/空动作、被动元素伪造动作、初始空TextInput无字体；`PlaySession.RegisterUiActions` 新增整批原子注册，不因后一个冲突留下前缀注册。作者UI拿到同一份精确动作名副本，不再与运行时表脱节。
- `SceneUiInput.Observe` 在平台输入前绑定实例/代次和当前Play/World/tick/revision；正常固定步保留focus/capture，越过正常tick修订步幅的外部世界/元数据改变会取消。映射时再次检查UiStamp、element对应的Action和原输入kind，整批转成只读GameplayUiInput。另一个producer的admission不能被静默夺走或取消。
- PlayerRunner增加显式可信 `uiActions` 启动参数，先做包/动作闭包验证、后加载插件/游戏逻辑；PlayerPresentation的真实Pump路径在固定步之前处理窗口事件、准备动态文字/布局、提交提案，并屏蔽已消费的键鼠位和鼠标增量。Null只校验CPU闭包和动作表，没有UI/3D原生模块。
- Editor的可信启动构造同样接入动作表。Play启动前准备独立UI实例；编辑态实例不消费游戏输入；Play的控件文本/值不写回编辑实例或作者文档。Pause/单步/Reload/Stop/失焦/工作区及视口改变取消旧输入，快捷键与物理输入在同一帧遵守HUD捕获。Stop销毁Play目标/租约并回到原Edit HUD。默认CLI没有任意动作加载开关；未提供可信C#表时仍明确拒绝交互包。
- 为真实输入验证增加内部、受限的测试接缝：Editor把有界WindowState/InputEvent送入原输入累加器/SubmitInput，Player调用原Pump使用的ProcessInput。实际Gui、DX11场景/HUD、ScriptCatalog与固定步均不替换。`Ncma.Gameplay.UiFixture` 是测试专用SDK-only脚本程序集，仅在隔离测试项目里装载，不能混入正式产品包。该证据是合成平台事件的宿主接线验证，不等于操作系统真实输入/IME/DPI人工验收。
- 暴露只读入口取得独立UiRuntime后，缓存表面还检查运行时PresentationRevision，拒绝在widget状态已改而画布未重新准备时复用/截取旧表面。
- 首轮真实Editor Play测试暴露Text插件的单context `Busy`，不是可以放宽的ABI约束。新增 `UiTextResources`：一个惰性owner-thread文字服务共享给作者预览/Edit/Play画布，控件状态、文本run、目标与租约仍各自拥有；所有font/run释放前不能关闭TextService，不能切换plugin root。无字体HUD仍不加载文字插件；不新增第二个native文字context，不改native代码/ABI。

当前证据：

- Application新增路由9组与原17项在Debug/Release通过：`out/m7-4-d3c-route-third.log`、`out/m7-4-d3c-route-release.log`；最后相应构建0警告/0错误。前两次仅测试代码编译错误（把SDK运行时/UUID包装类型用于Guid参数、单元素params目标推断）日志保留，改为明确fixture UUID；未更改运行时权限或压低断言。
- 原生宿主交互测试先有测试命名空间Vector3歧义编译失败，修正别名；`out/m7-4-d3c-interaction-first.log` 为真实Text Busy失败，保留。共享文字修复后第二轮51项通过；加入正式PlayerRunner、失焦与共享资源关闭检查后第三轮59项；统一作者动作名后第四轮60项通过：`out/m7-4-d3c-interaction-fourth.log`，`out/verification/m2/scene-ui-interaction-targeted/c955568b0cf041eb86623aa91cbf2491`。
- 60项包括真实C#脚本读取UI事件、点击/Space不重复触发物理键鼠、文本/slider固定步结果、GUI最终图像变化、默认拒绝、Null无native、graphical PlayerRunner、单Text context/提前卸载拒绝、3轮Editor Play/Pause/单步/Reload/Stop、Edit文档/UI隔离、原生API0/0和Play目标/租约回收。仍为source宿主探针；`interaction-results.json` 明确 `relocatedInteraction=false`、`wholeD3Accepted=false`。
- 源缓存表面24项通过：`out/m7-4-d3c-route-surface-regression.log`，`out/verification/m2/scene-ui-surface-route-regression/6d841e8520304d22a0a403f711caa79c`。原被动HUD source/受检搬移/实际apphost 579项通过：`out/m7-4-d3c-route-host-regression.log`，`out/verification/m2/scene-ui-host-route-regression/2fcb2255da9342a98b97e401950fe85e`，不能改写为新交互的搬移证明。
- 标准Debug `Build.bat -Configuration Debug -SkipTests -SkipPython` 通过：`out/m7-4-d3c-route-host-build-first.log`，候选 `c007863375f0494bbe1c5761fdd97001`，未部署。此候选早于最后作者动作名同步；后者定向重编译、source交互复测通过。完整冻结双配置无Skip仍未执行，原人工与长期门禁仍开放。

下一项保持原方案范围：作者UI明确批准、资源闭包与可撤销的新代次发布到场景HUD（不能自动覆写pin住的部署包）；然后受检搬移的交互及ASCII/Binary FBX→NCA动画/IBL/shadow/Bloom/HUD完整联合。还须补新路径完整生命周期/成本记录和最终冻结Debug/Release/清单/恢复/实际EXE检查，之后才提交推送。不能把60项source交互通过当成整个D3-C完成。

### Release 补证与搬移交互（仍未关闭 D3-C）

- 上轮原 Editor 回归最终154/154通过：`out/m7-4-d3c-route-editor-regression.log`。Release标准Skip候选构建通过：`out/m7-4-d3c-route-host-release-build.log`，代次 `f76aeeef8bf44cd59225e395b83dfe33`；没有替换正式部署。
- 用该Release插件验证：source交互60项（`out/m7-4-d3c-interaction-release-first.log`，证据 `46aa5e65ef5941cfb548ce72810abee5`）、表面24项（`out/m7-4-d3c-surface-release-first.log`，`4091e0f1fb744585bd0db4beec0a95c0`）、被动source/搬移/实际apphost579项（`out/m7-4-d3c-host-release-first.log`，`caae614273414aa391a4c9c9eee2fa7c`）均通过。证据目录位于各自 `out/verification/m2/scene-ui-*-release-targeted`。
- 新增独立 `NcmaRenderingTests.SceneUiRelocatedInteraction`，不挤入原120秒source测试。复制清单列出的正式Editor/DX11 Player/Null产品，再复制独立runtime项目并核对每文件SHA；原作者源、字体和原runtime项目均独占锁定。子进程复用严格ProductContext，Ncma程序集只能来自受检产品；记录实际加载程序集SHA、实际原生路径及清单SHA、关闭后无原生模块。
- 启动动作表由测试的可信C#组合硬编码，不来自JSON/Agent；使用产品API和内部有界输入观察接缝，不冒充新增自定义CLI apphost，不代表OS硬件输入/IME人工验收。SDK-only测试脚本只放进测试游戏项目，不进入引擎产品清单。Player子进程不得装载Editor/MCP/GUI程序集；Null没有plugins目录或渲染帧。
- Release搬移第二轮407项通过：`out/m7-4-d3c-relocated-interaction-second.log`，`out/verification/m2/scene-ui-relocated-interaction-targeted/51e761d9ef754a30b665fe580b3d1a22`。覆盖真实固定步click/Space/text/slider、物理输入不重复、失焦/暂停取消、Editor三轮Play/Reload/Stop、Edit隔离、最终GUI图像、资源/租约关闭/API0/0；407包含路径/哈希检查，不是407张图像。
- 首轮失败为测试夹具把默认拒绝和可信启动写到相同报告路径，违反既有报告CreateNew保护；分别使用独立报告路径修复。`out/m7-4-d3c-relocated-interaction-first.log`及对应目录保留；没有修改产品保护或降低断言。新增测试两轮编译均0警告/0错误。
- 作者发布路径代码审查确认：现有UiCommands只负责`.ncmaui`作者文档事务；RuntimeUiPackage不可变且pin住，不能把作者保存直接等同于运行包发布。后续必须复用原EditSession参与者/Undo权限和持久日志，显式生成新代次及切换选择；需要处理大包与16MiB历史预算、旧代次pin、失败补偿/恢复，不能另建独立历史或覆盖运行包。此项尚未实现。
- 新搬移交互仍是静态测试场景，不替代完整ASCII/Binary FBX→NCA动画/skin/IBL/shadow/Bloom/HUD联合、作者发布、整宿主性能、最终冻结无Skip双配置和人工门禁。本阶段保持IN PROGRESS，未提交推送。

双配置后续核对：

- Debug标准Skip重建成功：`out/m7-4-d3c-relocated-debug-build-first.log`，候选 `83c898365ba04bd78996138171205bde`。随后仅在测试中补实际CLI拒绝用例并重编译Debug/Release（`out/m7-4-d3c-relocated-{debug,release}-tests-build.log`），均0警告/0错误；产品源码未变。
- 实际复制的Editor EXE在没有可信表时退出4且报告精确的动作注册缺失；DX11/Null Player EXE退出7、tick0、空session与空native模块。产品API的可信交互成功和实际CLI的默认拒绝分别记录，不能混称“CLI已支持任意交互动作”。
- 最后Debug412项通过：`out/m7-4-d3c-relocated-interaction-debug-first.log`，证据 `out/verification/m2/scene-ui-relocated-interaction-debug-targeted/d13d44c3542d4286a8597426e2b410bc`。Release412项通过：`out/m7-4-d3c-relocated-interaction-third.log`，`out/verification/m2/scene-ui-relocated-interaction-targeted/b75cc9f4e547445bb07ae8c9364e536d`。原source交互60项再次通过：`out/m7-4-d3c-relocation-source-regression.log`，`out/verification/m2/scene-ui-interaction-relocation-regression/12c33238207c4245a2313cf9f1f85901`。
- Release原UI28、Gameplay53/Movement38/UI18组、Application17/路由9组通过：`out/m7-4-d3c-route-release-{ui,gameplay,application}-regression.log`。Movement仍为deterministic fake，不升级为Jolt角色验收。
- 标准Build的CTest筛选包含新测试；本轮没有执行完整无Skip。正式部署journal仍为D3-B Complete `a290a77b582c49cd812e7e3aeb90dc70`；继续作者批准发布和完整联合，阶段不关闭。

### 作者 UI 运行包准备基础（未接入选择事务）

- `UiAuthoringResourceCatalog.CookPackage` 仅复制已明确批准的字体/解码图片，按UUID构建精确NCU1资源闭包；不会在cook时重开作者源或发现系统字体。字体再分发标记仍是必需条件，显示许可不提升为打包许可。`FontAsset.CopyBytes`、`UiImageAsset.CopyRgba`返回副本，不暴露内部存储。
- `EditorUiPackagePreparation` 将项目根目录/UUID、当前作者文档/资源/edit stamp、不可变候选字节与SHA、60秒本地review指纹绑定。只在无草稿且可写时接受明确批准；默认被动UI，交互UI必须使用同一个可信C#动作表及精确类型验证。未新增Agent工具、代码执行或推理权限。错误项目、过期、资源变化、外部作者文件变化、撤权都拒绝。
- `UiPackageGenerationStore` 是纯C#受检存储；UI语义解码由可信组合层注入，不依赖或初始化原生插件。文件名包含项目、文档UUID、generation、SHA；只创建不可变新代次，已有同代次不同内容拒绝。每包64MiB、保留32个/256MiB，预算包含失败准备文件，超限要求显式处理，不清理用户数据。每个文件及父目录受检pin，拒绝覆盖/重命名正在使用的包。
- 准备采用独占新stage→flush→重验批准/取消→不覆盖rename→读回hash与语义→再次批准→pin。失败前后均保留证据；rename前失败必须通过精确stage路径和重新批准显式恢复，rename后失败可显式重新校验同一包。没有任意路径扫描删除、自动覆盖、兼容格式或隐藏fallback。恢复只承诺不可变包准备，不声称场景选择或历史已经提交。
- **重要边界：上述服务目前没有挂到正式Editor按钮，没有新增场景/项目选择格式，没有写活World，没有切换HUD，也没有独立Undo历史。** 后续仍必须实现受批准的代次选择参与者、原EditSession原子提交/补偿/持久日志、原Undo/Redo，以及off-frame候选HUD交换。准备成功不能被当作D3-C作者发布完成。
- 专项新CTest `NcmaEditorServiceTests.UiPackagePreparation` 独立进程运行，验证CPU-only。最初8组、加恢复后10组Debug/Release通过；再加动作类型后11组通过：`out/m7-4-d3c-ui-package-preparation-fourth.log` / `out/m7-4-d3c-ui-package-preparation-release-third.log`，证据目录分别 `out/verification/m2/editor-services/70dfc1441c5a45e8af4fac9ad4c30730`、`aeec08cb72fa41ea82c46b13f695c2f8`。包括完整bytes/hash、默认拒绝、pin、history/World不变、过期/撤权/stale、owner/reentry、失败两侧保留/恢复、32代次预算、导出副本与动作kind。
- 两项测试夹具问题保留：单参数params目标类型推断CS8752（`out/m7-4-d3c-ui-package-preparation-build-fourth.log`）改为显式`SceneUiAction`；预期被动拒绝返回NotSupportedException但测试Reject未列入（`out/m7-4-d3c-ui-package-preparation-third.log`）补为明确预期类型，未放宽产品拒绝。之后双配置编译0警告/0错误。
- 原Editor154/154通过：`out/m7-4-d3c-ui-package-editor-regression.log`，证据 `out/verification/m2/editor-services/960cc469fe634b5a88b33200b9575810`。既有UI工作区测试另加真实字体检查：未声明可分发的Windows字体不得cook；获批准的ProggyClean在源文件独占锁定时仍通过复制资源生成有效NCU1。该154运行早于最后构造时项目根目录/UUID补充校验，新增边界另行最终复测。
- 最后项目根目录/UUID校验补齐后，标准Debug Skip构建通过：`out/m7-4-d3c-ui-package-debug-build.log`，候选 `f7a2a28dd81249ac98eb87b90822aa30`，没有正式部署。随后最终Debug11组通过：`out/m7-4-d3c-ui-package-preparation-fifth.log`（证据 `0838c78ca1954fcdad9171f6c57b9847`）；Release重编译0警告/0错误，11组通过：`out/m7-4-d3c-ui-package-preparation-release-fourth.log`（`4742d4c790d94b37951b6e2ec5c22241`）。Release产品包尚未重新生成，不能用旧f76包代表此次新准备服务；源码专项是当前版本。
- 最终补证：最新Debug候选搬移交互412项通过，`out/m7-4-d3c-ui-package-relocated-regression.log`，证据 `out/verification/m2/scene-ui-relocated-preparation-regression/5fe280833e3c40efafb95cf3d659cb2c`；最终源码Release原Editor154/154通过，`out/m7-4-d3c-ui-package-editor-release-regression.log`，证据 `out/verification/m2/editor-services/12f00bba9c3f49618e571d6979a9f3f0`。进程exit0，未修改正式EXE，SHA仍为 `2BD5CD7D15C201B3889A589ADDD715468702DE19DA9EC2B14B51106EFACEFB2F`。未进行全阶段无Skip/提交/推送；本基础增量不关闭作者发布或完整D3-C。
