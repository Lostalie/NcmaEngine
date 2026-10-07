# M5.7 参考图编辑器工作区与UI制作工作区详细设计

更新日期：2026-10-07。状态：场景/UI工作区自动候选交付，最终顺序Debug/Release回归通过；整体验收仍待人工/目标环境/性能/长稳证据。

目标是在现役NcmaEngine.exe内提供统一的场景与UI制作工作区，视觉采用[用户图1](EDITOR_INTERFACE_REFERENCE.md)。保留原生Dear ImGui呈现，C#拥有工作区、选择、布局、草稿、权限和业务，Editor.Core拥有唯一文档历史。UI本体通过共享UiCanvas真实绘制，编辑辅助层独立呈现。

[M5.1至M5.6](M5_1_6_DELIVERY_REPORT.md)是自动候选底座；本轮M5.7另行接入Editor画布、专用UI离屏和字体/图片审批，不表示完整输入法或Player HUD已实现。用户最新请求授权继续M5.7、修复测试直到通过并提交推送；此前仅顶部/不提交限制已被覆盖，不关闭M4及此前人工、性能、目标环境和长稳门禁。

当前已接入统一InteractionGate、48像素菜单/深蓝灰场景与UI制作工作区、分隔条/严格布局设置、query7 UI-target1.0和GUI1.6精确缓存呈现租约、严格UI文件/资源审批、共享运行时画布、作者变换/吸附/Undo与隔离控件测试。旧表/查询/格式规则保留。以下是完整设计目标，不是每个UX细节都已验收的声明；首版响应式采用AI自动折叠、滚动面板和显式聚焦UI画布，不宣称自由docking/底部标签化。最终自动证据、实现限制与未关闭人工门禁见[M5.7交付](M5_7_DELIVERY_REPORT.md)，操作见[使用说明](M5_7_EDITOR_GUIDE.md)。

## 1 阶段目标与范围

用户应能在同一个Editor内：

- 用参考图风格的顶部工具栏、扁平对象列表、真实场景视口、Inspector、AI工具侧栏与底部区域制作场景。
- 切换到UI工作区，新建或打开严格.ncmaui v1文件，浏览独立UI元素树。
- 在真实画布上选择、多选、移动、缩放、旋转、对齐、吸附和组织元素。
- 修改布局、样式、Token、文字与资源，确认或取消草稿，经同一Editor.Core历史Undo/Redo。
- 按不同尺寸/DPI/安全区预览同一运行时语义，在隔离测试模式操作已有控件。

不新建引擎入口，不恢复Node/场景父子变换，不把原生ImGui当作游戏UI运行时。

本阶段不包含自由docking、多窗口/多文档、UI组件实例/变体、UI语义MCP写工具、Python推理服务、地形/在线协作、Vulkan绘制。组件实例属于M5.8，UI Agent写流程属于M5.9，项目Player HUD/cook/裁剪发布属于M5.10。glyph atlas、完整游戏文本选区/IME等底座缺口仍需独立补齐，不因画布改造宣称完成。

## 2 当前基础与前置缺口

| 能力 | 已有基础 | M5.7待补齐 |
|---|---|---|
| 文档 | Ncma.Ui严格v1、UUID、内部树、样式与Token | 正式新建/打开、文档索引、UI选择域和图层视图 |
| 事务 | UiCommands、AssetProjectAuthoring.Ui、UiDraft、共享Undo | 可信本机权限接线、作者交互互斥和外部变更处理 |
| 布局 | Free/横/纵、Fixed/Hug/Fill、锚点/DPI/安全区 | 作者命中、坐标变换和布局诊断，不重写布局算法 |
| 绘制 | query6/UI API1.0、驻留列表、UiCanvas | 无深度的专用UI离屏目标与GUI呈现租约 |
| 资源 | FontAsset/UiImageAsset为可信启动传入的复制数据 | 有界资源目录、精确路径/UUID/hash审批、缺失诊断 |
| GUI | ABI1.3复制视图、Image与代次/revision检查 | 角色化主题、工具栏/图层/画布输入的增量契约 |
| 偏好 | EditorPreferences v1及旧ToolbarHeight范围160至300 | 独立工作区布局设置，不能重解释旧字段 |
| 输入 | GUI捕获、UiRuntime基础控件/字符输入 | 作者模式与控件测试互斥路由，不冒充完整IME |

前置缺失时不能启用“可编辑UI画布”。不能用参考PNG、占位字或样例字体伪装成真实文档预览。

## 3 总体布局与视觉

### 区域配置

| 区域 | 初始逻辑尺寸 | 内容和约束 |
|---|---|---|
| 顶部菜单栏 | 高48 | 项目名移到原生窗口标题“NcmaEngine - 项目名称”，空白回退“未命名项目”；文件/项目/编辑/工具/游戏/AI/窗口/帮助从左侧连续排列，无菜单品牌区；运行下拉、停止、Windows/DX11、禁用账户和设置在右侧，不设云入口 |
| 左侧 | 宽240，最小200 | 场景为扁平对象列表；UI为内部元素树；可折叠 |
| 中央 | 优先剩余空间，建议最小480×320 | 真实场景视口或UI画布，顶部工具行高32 |
| Inspector | 宽288，最小240 | 分组属性；窄屏改为单列，长名称不挤掉输入框 |
| AI工具侧栏 | 宽304，最小280 | 可折叠；真实状态/权限/能力/结果，不创造聊天服务 |
| 底部 | 初始约22%可用高度，最小160 | Assets与Console并列或标签；诊断与已有工具入口 |
| 状态栏 | 高24 | 项目、工作区、草稿/文件、真实运行和有效测量状态 |

这些是设计起点，不是固定像素复刻或已验收数值。分隔条约4逻辑像素，面板尺寸只保存本机偏好。

### 响应式规则

可用宽度不足时先折叠AI，再将底部改为标签，最后收起左侧列表。Inspector保持可操作宽度；低于中央最小尺寸时提供“聚焦视口/画布”。恢复窗口后重新计算布局，不重建Play。

布局随窗口/DPI/字体变化重新计算，区分OS逻辑坐标、framebuffer像素和UI文档逻辑坐标。非等比DPI必须明确处理或报告不支持，不能只取ScaleX。检查1280×720、1920×1080、2560×1440、窄窗及100%/150%/200%DPI。

### 主题和品牌

推荐拟用色板：背景#0B1420、面板#142131、输入区#101B29、边框#2B435A、强调#258CFF、文字#DFE9F3、次级文字#93A7BD；常规圆角4至6，间距4/8/12/16。这是拟采用值，不是从参考图精确采样的承诺。

产品仍为NcmaEngine，窗口标题栏与EXE继续使用engine/build/resources/NcmaEngine.ico；项目名称追加在窗口标题的NcmaEngine之后，菜单不绘制品牌或项目名。其他菜单图标与文字并列，不提取Nebula标识，不用颜色作为唯一状态提示。Disabled、Busy、ReadOnly、Faulted有独立文字/tooltip。

字体使用批准资源或可信本机设置。缺字诊断与许可声明保持真实，不默默从系统/网络补字体。无有效遥测显示“未测量/不可用”，不复制图片中的FPS/GPU示例数字。

## 4 场景工作区

左侧命名“场景对象”，以UUID选择，按名称/组件过滤，分页或虚拟化。视觉分类不保存为父子关系，删除分类不删除对象，空GameObject不自动获得Transform。

中央沿用真实3D视口、相机与诊断。Inspector沿用组件Schema、现有草稿/事务、精确删除确认和Play冻结规则。改视觉不能新增native对象存储、第二命令栈或第二Play会话。

顶部以文件/项目/编辑/工具/游戏/AI/窗口/帮助菜单组织现有场景、历史、工具、Play和审批入口。运行下拉提供暂停/继续/单步/重新运行，启用状态依据真实Play状态。地形、账户、协作和未接入服务隐藏或禁用。重新布局不能重复发送运行命令。

Assets保留现有来源与授权，只展示真实可用预览。Console保持有界分页与等级过滤，不渲染无限日志或逐帧扫描文件。

## 5 UI文档和资源生命周期

### 文档和选择

只保留一个活动可编辑UI文件；场景和UI选择分开保存。UI选择键是文档UUID加元素UUID，不能作为GameObject命令目标。

新建选择批准的项目相对.ncmaui路径，分配文档/root UUID，以host proposal经UiCommands和唯一历史发布。打开先验证路径、大小、严格v1、资源闭包和权限，再替换视图；失败保留旧文档、选择和预览。只读打开不自动授予写权限。

### 保存和历史

沿用M5.2：确认编辑即一次可撤销、原子写入的文件事务。拖动只产生临时草稿，松开确认才写入；不另建“已提交但未保存”的内存历史。

“待确认”标记表示草稿。Ctrl+S确认可提交草稿或验证已提交文件；无变化不新增历史。若以后需要传统延迟保存，必须单独调整参与者/历史/磁盘策略，不在本阶段暗中引入第二存储权威。

Undo/Redo使用EditSession唯一游标，并显示即将撤销的场景/资产/UI操作名称。恢复可使场景运行时引用失效，消费者按UUID重新解析。场景New/Open改变session/代次时取消UI草稿并明确处理历史，不把UI历史迁到新会话。

### 作者资源目录

拟新增UiAuthoringResourceCatalog，由可信本机UI注册检查过的字体/图片来源、UUID/hash与内容代次，返回复制FontAsset/UiImageAsset。复用既有parent/reparse/hardlink、精确路径和读取租约检查，不扫描整个系统Fonts目录，不接受Agent路径。

首版入口为“本机审批加入字体”和“从已批准纹理/图片加入UI资源”。字体记录许可声明和是否批准再分发；图片记录尺寸与颜色语义。UI作者文件仍只保存UUID，不保存绝对路径、COM或GPU句柄。

本机资源绑定不是可移植cook。目标机缺资源应fail-closed；字体资产格式、跨项目映射、完整重导入与包发布另行设计。外部变更触发明确重新准备/授权，不逐帧做hash。

## 6 画布交互

### 尺寸和坐标

提供1280×720、1920×1080、1080×1920、纯UI小窗口和自定义预设。尺寸/DPI/安全区是预览设置，不自动修改作者参数。

文档点等于“屏幕逻辑点减图像内容原点减平移量，再除以zoom”；UI内部父空间由共享布局矩阵处理。渲染缩放不同于作者zoom，不能重复乘DPI。

zoom建议10%至800%，滚轮以指针为中心缩放，Space加左键或中键平移，F聚焦选择，提供适应文档与100%按钮。纯pan/zoom只更新呈现和辅助层，不重排文字或上传运行时顶点。

### 作者命中和选择

作者选择复用共享布局矩阵与裁剪，但独立命中所有可编辑元素；不能直接使用只面向交互控件的UiRuntime.HitTest。

按逆绘制层序选择可见未锁定元素；Ctrl切换，Shift追加/范围选择，框选使用文档空间边界。多选上限64；祖先与后代同选时归并为顶层选集，避免重复移动。锁定/遮挡项通过明确的图层操作选择，不默认穿透误选。

### 变换和布局限制

- Free布局允许移动和八向缩放。auto-layout内拖动优先变为插入顺序，不无声转成绝对定位。
- Hug/Fill轴的尺寸手柄提示“由布局控制”，切为Fixed需明确操作。
- 旋转默认15度吸附，修饰键关闭吸附。旋转裁剪继续拒绝，不伪造stencil能力。
- 对齐/等距分布首版以同一父空间为范围；跨父空间明确拒绝或要求显式重组，不能近似写错坐标。
- 吸附按父容器、选集边界、网格优先，阈值约6屏幕逻辑像素并按zoom转换；辅助线不写运行时文档。
- 图层支持新建、重命名、显示、锁定、顺序、删除、Group。Group仅组织UI，不是组件实例；锁定不同于运行时Enabled。
- UI内部删除可删除准确确认的子树，root不可删除；场景单对象删除规则不与UI内部树混用。
- 分组/重父级先验证当前几何语义可保持、无循环、符合预算。无法保持的组合明确拒绝。

### 草稿和互斥

Idle → 捕获选集与起始戳 → Draft更新 → Confirm或Cancel。每个手势只有一次Editor.Core提交，Update不写磁盘、不新增历史、不操作World。

Esc、失焦、工具/工作区/文档切换、Play冻结、撤权、外部文件或revision变化取消草稿。错误候选显示诊断并禁止确认。每个安全边界最多合并一次更新，不对每个鼠标事件序列化完整4MiB文档。

UiDraft已接入统一InteractionGate：场景/资产/UI同时只允许一个作者交互；其他本机和已授权远端写请求返回busy/conflict。Gate不持有第二历史，租约不暴露为Agent能力；确认前重新检查授权和全部戳。冻结、取消、无变更确认或提交失败结束租约，不覆写外部变更。

## 7 Inspector与控件测试

Inspector按名称/类型/UUID、布局、样式、Token、文字、图片和Action分组。对应现有UiDefinition，不增加未实现的Gradient/BlendMode/组件变体枚举。

多选显示一致值或“混合”，一次修改编译成有界候选。root、锁定、只读、布局控制轴和超预算操作正确禁用。Token检查Color/Scalar类型及引用；移除被引用Token不得留下坏文档。Action只绑定已注册C#语义名称，不执行输入代码。

作者模式下按钮不触发运行时事件。进入“控件测试”创建隔离UiRuntime，互斥停用选择/拖动工具；事件进入有界本机观察列表，不转给活动Play/World。退出恢复作者状态，不写作者文件。Preview Reset只重置UI测试实例。

Play中作者提交仍服从EditSession冻结，可以查看/缩放，不能由测试模式绕过冻结，也不能为编辑UI自动Stop Play。工作区切换、折叠或Resize不推进solver。

工具文字字段使用原生ImGui输入框，其输入法验收与游戏TextInput的完整IME不是同一门禁；M5.5/M5.6底座限制继续展示。

## 8 业务模块和状态

以下是拟新增或扩展模块，不是当前实现声明：

| 模块 | 职责 |
|---|---|
| EditorWorkspaceLayout | 响应式区域、折叠、分隔条和本机设置 |
| EditorThemeModel | 角色化颜色/尺寸/图标映射 |
| EditorUiWorkspace | 活动文件、选择域、内容revision、命令编译与唯一历史路由 |
| UiCanvasController | 坐标、作者命中、选集、手势、吸附和辅助层 |
| UiPreviewSession | 隔离测试实例、尺寸、资源租约和诊断 |
| UiAuthoringResourceCatalog | 精确资源来源/内容代次及复制数据供应 |
| EditorInteractionGate | 跨场景/资产/UI的作者交互互斥，不新增Undo |
| EditorAiToolsPanel | 真实状态/能力/审批/结果，不拥有推理或授权权威 |
| Ncma.Gui与GuiPlugin | 复制控件视图、输入意图及呈现 |
| Ncma.Ui.Rendering与Renderer | 共享运行时UI本体、离屏批次/目标/图像pin |

Editor.Services依赖Editor.Core/Assets.Authoring并适配Ui/Ui.Rendering/Text。Ncma.Ui和Player不反向依赖Editor/MCP/Gui，不新增原生Editor业务服务。

面板、zoom、折叠、最近UI文件等保存到out/user/editor的独立有界严格版本工作区设置。无效文件保留并报告，不写场景/UI文档或文档Undo。旧EditorPreferences字段不改变含义；确认为废弃的字段/消费者按精确清单清理，不增加旧类型别名。

## 9 GUI和渲染契约

### GUI增量协议

建议GUI ABI1.4，保留1.0至1.3的布局/意义，新增角色化主题、工具栏分组、分页/虚拟图层行、画布手势和有界辅助几何。不得给旧reserved字段塞新语义，旧消费者不能猜测新事件。

事件校验明确绑定frame、view generation、edit session/document generation、scene revision、project/asset generation与revision、UI文档UUID/content revision、稳定widget ID。可用C#路由表绑定较大戳或新增固定POD envelope；不能把Scene revision当作UI revision。

手势后续事件使用主机校验的捕获token/起始戳，继续核对最新视图。旧workspace/doc、重放、禁用控件、重复commit均拒绝。事件buffer溢出取消整批，不应用前缀。Interop、Gui、插件表、package manifest与测试消费者一起更新。

### UI离屏和静态图像

建议Renderer模块保留1.2，通过独立query7协商UI-target API1.0；query1至6冻结。增加RGBA8无深度目标、目标提交、测试读回和图像呈现租约。尺寸/数量/驻留字节同时有界，候选失败保留旧资源，不初始化3D资源。

Editor共享现有Renderer/device，不能创建第二个全局Renderer。C#编排场景/UI目标和GUI合成，同一可见frame最终只Present一次；不能照搬独立UiCanvas样例的Present循环。

GUI1.3要求exact submitted frame，不能把上一帧静态UI直接当作本帧重绘。新契约区分last produced frame与本帧presentation lease：只有device/target/content generation仍匹配才授予本帧采样权。旧1.3语义不放宽，不改写lastRenderedFrame伪造重绘。

建议首版最多4目标、默认最大2048平方、预算检查后最高4096维度；总目标字节与图片/列表预算共同检查，旧加候选的峰值也计入。参数是待实现限制，不是性能证据。

Resize/退役在安全边界进行，GUI pin归零后释放。关闭超时保留所有者，明确RecoveryRequired，禁止先卸载插件或逐帧偷偷重试。UI资产/布局错误不影响Play；共享GPU真实失效按应用fail-stop处理，保留已提交tick，不宣称solver回滚。

## 10 AI工具侧栏

展示实际MCP配对/端点、已注册能力、批准scope/有效期、等待审批与真实结果/错误。无推理服务显示“未接入”；无UI写工具显示“将在M5.9提供”。

审批继续调用现有可信控制器，折叠/展开不批准请求或扩大共享audience scope。不提供假聊天发送按钮、伪造回复、自动场景生成或新增Agent审批接口。可以提供本机说明和能力搜索。

侧栏只消费有界缓存/异步结果，没有渲染或simulation tick中的同步AI/IPC等待，没有任意Python执行或直接World/GPU访问。

## 11 性能和故障策略

静态UI内容、尺寸和资源不变时，layout/text/display-list重建计数不增长。选择/pan/zoom只更新辅助层和呈现变换。GUI即时模式仍可能逐帧编码，不能由UI缓存推断整个Editor零分配。

属性修改在安全边界合并准备；字体不重复读取/解析，文件不逐帧hash，列表/日志不全量扫描。沿用4096元素、64层、256操作和历史字节预算；不足明确拒绝，不拆成不可撤销前缀。

分域记录CPU时间、allocation、上传/编码字节、缓存命中、有效GPU样本与资源数。没有独立帧ID的旧GPU样本不当作新样本，不承诺任意硬件FPS或性能领先。

文档/草稿失败保留已提交内容。关闭失败保留会话和依赖，进入RecoveryRequired；旧目标、跨设备或旧文档图像租约拒绝，不能以静态截图掩盖故障。

## 12 实施顺序

| 小阶段 | 交付 | 退出检查 |
|---|---|---|
| M5.7-A 前置契约 | 戳/InteractionGate、UI离屏/呈现租约、GUI增量及消费者同步；现GUI1.4工具栏/1.5菜单主题，后续缓存图像独立增量 | POD、旧ABI不变、真实离屏像素、单Present、旧代/帧/内容、pin/关闭失败 |
| M5.7-B 布局主题 | C#区域计算、主题/图标、分隔条和设置文件 | 小窗/DPI/长标签无裁切，工具栏不被覆盖，设置不改文档/历史 |
| M5.7-C 场景与AI侧栏 | 真实视口/Inspector/Assets/Console及能力面板 | 原命令有效，无Node/假服务，工作区切换不推进Play/Physics |
| M5.7-D 文档与资源 | 新建/打开、精确本机权限、资源目录、图层和选择域 | 保存重启、缺资源/外部变更/default-deny/关闭撤权，选择不混用 |
| M5.7-E 画布交互 | pan/zoom/命中/多选/变换/吸附/对齐/顺序/Group | 父空间/auto-layout/祖孙多选/root保护，手势一次Undo，取消冲突无写 |
| M5.7-F 属性和测试 | 当前Schema Inspector、Token/资源、尺寸/DPI、隔离控件测试 | 共享预览语义，测试事件不入World，冻结不绕过，缺字/坏布局诊断 |
| M5.7-G 缓存恢复 | 静态缓存、事件合并、目标退役和RecoveryRequired | 实测计数、32次Resize/切换/关闭基线，错误保留所有权 |
| M5.7-H 联合验收 | 真实UI资产流程、参考图对照、完整回归/待验项 | 自动/人工分开，完整Debug/Release、包/恢复，不关闭其他门禁 |

按依赖补齐并逐片修复、测试。当前A至G已接入自动候选，H以最终顺序Debug/Release和人工门禁分别记录；任何局部自动测试均不关闭整个A至H。最新请求已授权测试通过后的Git提交推送。

## 13 测试矩阵和退出门禁

自动检查包括：

- 大窗/小窗、DPI/字号、分隔条/折叠/最小控件/长标签。
- 同一.ncmaui在独立运行时与Editor相同逻辑尺寸下布局/文本/像素语义一致；数值几何oracle加真实GPU图像，不仅同一实现互比。
- 图层/锁定、任意元素选择、祖孙多选、Free/auto-layout/anchors、变换/吸附/顺序/Group/裁剪限制。
- 多次拖动仅一个历史项；Esc/失焦/切换/撤权/外部文件/revision无写；混合场景/资产/UI历史重新检查原授权。
- 精确路径/UUID/依赖、未知proposal、旧view/frame/UI revision、重复commit和buffer/操作/历史预算拒绝。
- 单Renderer/Present、静态采样租约、旧代/跨设备/已释放token、关闭失败和32轮资源基线。
- 工作区/preview操作不改变Play identity/tick/World；设备故障不抹去已提交数值结果。
- M1至M5基础、native/managed smoke、严格格式、Python inspect/测试、包和可恢复部署保留；完整顺序Build.bat Debug/Release，新代码零编译警告，相关DX11 validation零错误/警告。

人工检查参考图视觉、真机Resize/DPI/跨屏、图标/工具栏、中文字段输入法、鼠标捕获/键盘焦点、UI预览、Play/Stop/Reload和真实第三方审批。GPU归因、性能预算、目标机和长稳无实测继续待验，不用内部测试推断通过。

退出要求是现役Editor的两个工作区和UI制作真实可用，有保存/重启/Undo、输入、帧和资源证据。仅主题截图、控件清单或独立样例不算交付。交付源代码、ABI/schema、自动测试、截图与docs/M5_7_DELIVERY_REPORT.md；记录在实施后生成，不提前填通过。
