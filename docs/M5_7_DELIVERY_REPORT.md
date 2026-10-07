# M5.7 编辑器工作区实施记录

更新日期：2026-10-07。状态：场景与UI制作工作区自动候选交付、最终顺序Debug/Release回归通过；人工及生产验收未关闭。

## 最新交付（覆盖下面的历史初版状态）

用户最新授权为继续M5.7、修复直到测试通过、然后提交推送。旧的“仅顶部/不提交”限制已被覆盖，不覆盖任何人工、性能、环境或长稳门禁。本文不把概念港口图或内部程序化UI当作真实用户素材验收。

| 切片 | 当前代码与自动检查范围 | 未关闭的门禁 |
|---|---|---|
| A 契约 | query7 UI-target1.0、GUI1.6缓存图像/裁剪/分隔条/修饰键选择；旧表/查询/帧语义保留 | 真实设备丢失与目标机验收 |
| B 布局 | 深蓝灰场景/UI工作区、可拖分隔条、独立严格布局设置、AI折叠、小窗聚焦UI画布 | 真机跨屏DPI/IME/高对比度、参考图视觉确认；自由docking/底部标签化不在首版中 |
| C 场景与AI | 扁平GameObject、真实视口/Inspector/Assets/Console、真实本机MCP状态和既有审批入口 | 第三方可见审批与实际用户场景；没有推理服务 |
| D 文档资源 | 一个活动.ncmaui、只读打开/精确UUID文件写审批、批准字体/图像、独立图层与选择、共享Undo/Redo | 可移植字体/图像映射和cook由后续阶段实现；重启须重新批准相同资源UUID |
| E 交互 | 平移/指针锚定缩放、命中/框选/Ctrl多选/Shift图层范围、八向缩放/旋转、父/同级/网格吸附与辅助线、对齐/分布/顺序/Group | 真机鼠标捕获和复杂用户文档；不静默改Hug/Fill或auto-layout |
| F 属性测试 | Schema字段/Token/资源/注册Action、Unicode安全文本分页、四种尺寸预设/自定义DPI安全区、隔离控件测试 | 完整游戏文本选区/IME、项目Player HUD仍未实现 |
| G 缓存恢复 | 静态准备/上传/生产复用、32轮目标Resize基线、精确当前帧租约、pin与关闭失败保留所有者 | 超时分支注入不是实际GPU故障；整体CPU/GC/GPU预算与长稳待验 |
| H 联合 | 新建/编辑/保存/Undo/Redo/资源批准/真实窗口像素/Play隔离自动流程，最终顺序Debug/Release通过 | 参考视觉、实际用户资产、输入法/DPI/MCP、目标环境、性能及长稳 |

### 实现边界

C# `EditorUiWorkspace`拥有文件/审批/资源revision，`UiCanvasController`使用共享布局矩阵进行作者命中与变换，`EditorUiPreview`使用同一Renderer/device和共享`UiCanvas`。原生只拥有色彩目标、批次数值、GUI复制视图与输入意图；没有新增原生World/Editor业务权威。

确认文件编辑即原子写入，`UiCommands`仍通过Editor.Core唯一历史提交。每个拖动/属性激活使用统一InteractionGate，草稿更新不写文件/World，确认只有一个历史项，Esc/失焦/切换/撤权/失败释放。确认再次检查完整文档、资源和注册Action；捕获错误后继续确认也不能提交非法Action。Undo恢复全场景快照并使旧runtime引用失效是现有历史边界，不能将其描述为纯资产命令保证World identity不变；消费者按UUID重新解析。

UI测试实例不连接Play/World，Action只进入最多8条本机观察；默认`preview.test`不是游戏处理器。C#可信启动可声明有界语义名，UI输入不加载/执行代码。Play冻结UI提交；切换/平移/缩放/预览尺寸/布局设置不启停Play、不步进solver。UI内部树不改变扁平场景。

GUI1.6保留112字节表，1.0–1.3保持104字节，1.4–1.5保持112字节。CachedImage与旧Image是不同token：query7每次授予准确可见frame的呈现租约，静态内容保留上次真实生产帧。GUI先完整检查再pin，Render/Discard释放pin，租约释放前目标不能改写/退役。只在新Overlay裁剪区允许CachedImage；旧Canvas/Image约束不放宽。UI图像和输入同时裁剪到画布，不覆盖头部按钮。新SelectionButton复制Ctrl/Shift，不解释Editor业务。

query7独立API1.0表72字节、frame72字节、stats56字节，Renderer模块1.2/queries1–6冻结。最多4目标、64租约、4096维度；目标与query6图像/列表共用128MiB预算。纯UI目标RGBA8且无深度，不初始化3D资源，不提交主帧或Present；可见主循环最终Present一次。当前Editor预览尺寸64–2048、DPI0.25–4、安全边0–32，先完整校验后安装；10%–800% zoom受GUI16384显示尺寸额外限制。辅助线真实裁剪，不靠坐标钳位伪造边界。

`EditorWorkspaceSettings`独立严格v1/4KiB，保存面板比例和AI显示状态；不重解释旧Preferences，不写文档Undo。损坏文件保留并禁用覆盖，只有明确处理后才能保存。不逐帧读文件/hash/解析字体；静态或相同失败准备使用revision缓存。关闭超时进入RecoveryRequired、保留依赖，禁止自动每帧重试或先卸载；明确关闭可重试。共享设备真实失效仍应用fail-stop，不宣称物理回滚。

字体/图像来源只由本机UI审阅精确项目相对路径、UUID/hash和字体许可声明，复用现有parent/reparse/hardlink/精确读取租约。字体由真实NcmaText解析，图像由现有可信ImageDecoder解码。作者文件只保存UUID；许可声明不是法律校验，当前目录不是持久cook或自动系统字体fallback。Editor包添加lazy NcmaText，不让默认Player/Null部署Editor或字体依赖。

### 当前测试与修复记录

最终代码顺序执行完整`Build.bat -Configuration Debug`和`Build.bat -Configuration Release`，两轮均通过：每轮12项native与22项managed CTest，Editor69、Player56、Gameplay53/fake Movement38、Python43，外加SDK/native/managed smokes、严格新格式及移除格式拒绝、inspect、三轮保留fixture测量、部署前后审计与可恢复整包部署。新编译代码零警告/错误，相关UI/GPU测试validation错误与警告均0。日志为`out/m5-7-final-debug-accepted-auto.log`与`out/m5-7-final-release-accepted-auto.log`。Git全局ignore读取权限与LF/CRLF提示保留，不当作编译警告或性能验收。最终源代码之后仅更新交付文档，没有替换测试通过的运行代码。

新增UI流程包含真实GPU红色矩形/绿色按钮最终窗口像素断言、32帧一次生产/零重复上传、32次目标Resize、真实本机字体/中文glyph、长Unicode分页与多phase单次提交、小窗聚焦/预设/Play identity、外部文件在激活前变化拒绝以及非法候选拒绝。缺字会停止准备/取消草稿并保留已提交文件；不以自动fallback隐藏测试错误。GPU客户区截图不含系统标题栏，不能代替OS人工验收。

### 最终截图、安装与恢复

- Debug实际GPU：`out/verification/m2/editor-services/d778625076944e9485e729788a640150/ui-workspace-review.bmp`与`ui-authoring-review.bmp`。
- Release实际GPU：`out/verification/m2/editor-services/b80bd3a4e79144c6b4615f2a1b44b152/ui-workspace-review.bmp`与`ui-authoring-review.bmp`，已经像素断言及视觉复查。红色矩形、绿色按钮和文字来自共享运行时，不是设计参考图片。
- 当前`out/bin/NcmaEngine.exe`为Release，apphost357376字节。安装清单101个文件逐项路径/大小/SHA256复核通过，`out/deployment/editor-journal.json`为Complete；manualAcceptance与selfContainedVerified仍false。
- EXE SHA256：`AA22BDDAA1C6A616FA9CF04A10982F0DF7A688399F9CA882AFD3A23EBCA00208`；实际C#业务程序集`NcmaEngine.dll`：`8767DA62C1502D56BA11714840EB79F3E13D76B0FFF76922FBF01040E16CD39D`。apphost哈希不单独证明业务程序集更新，故整包也复核。
- Renderer：`CAAE5B17382885D7C43A32665B6A2CF26D641F9066B3D7528C76A36F48F37E6A`；Gui：`44A4283744F9F6B3C70A175FA91C87594DC954F5D0D93508C019979AEE6E60EE`；Text：`625F83CC40FBDFB50FA68640C55CF3822E23330B81EB22FD7B05E3ECAC340229`。
- 最终Debug备份`out/deployment/6e52b326a54d489f921596610095d84a/backup`；Release替换前的Debug备份`out/deployment/456c886be23b4ee2b5456f29e2c1c1a2/backup`。此前备份、失败输入和用户数据全部保留，没有强制释放锁或清理IDE数据。
- 最终Debug审计`out/verification/m2-8/Debug/5d037f49e0eb49ddb42683318f11c543/audit.json`，Release审计`out/verification/m2-8/Release/7d64dd188a6648c0a781e4822b64ccb9/audit.json`。audit_passed为true、h8_accepted仍false；审计与三轮测量不是性能/生产门禁关闭。

本批测试通过后按最新授权提交推送源码；不包含生成安装包、测试产物、恢复备份或无关`.vs/.user`修改。最终本地/远端SHA以Git回读与交付回复为准，不在同一提交内写入自引用SHA。

修复与原失败证据保留：

- 最初query6接口断言未同步GUI协商版本、上传计数基线、Undo后的World身份基线、Play前catalog刷新以及旧帧错误码断言，均补正断言/前置条件，没有删掉帧/资源/性能计数检查。
- 实际截图发现UI离屏内容正确但第二个窗口遮挡，改为同一画布面板合成，并断言最终窗口像素。日志`out/m5-7-final-focus-test.log`。
- 分隔条phase2不再改变view generation，以保留真实ImGui active ID；同帧后续事件与跨帧拖动有测试。
- 新缓存图像放入裁剪区后预检查仍拒绝，失败`out/m5-7-final-debug-verified.log`；修正仅针对新Overlay/CachedImage组合，保留旧契约。
- 边界测试曾漏掉Presenter真实AttachUiImage步骤，失败`out/m5-7-final-workspace-focused.log`；补齐真实调用顺序后继续复测。失败输入和既有备份不删除。
- 长文本测试最初使用批准字体不覆盖的emoji，失败`out/m5-7-final-workspace-focused2.log`至`focused4.log`。保持缺字拒绝不变，将Unicode代理对分页与实际中文字体覆盖分开测试，并增加U+10FFFF缺字取消/保留文件/恢复有效预览断言；`focused5.log`为69/69，随后以上最终完整Debug/Release覆盖全部修复。

操作入口与限制见[M5.7 Editor使用说明](M5_7_EDITOR_GUIDE.md)。M5.8组件实例、M5.9 UI Agent写、M5.10项目HUD/cook/发布未因本次实现自动开始或完成。M4 K7及此前M2/M3待验门禁继续开放；没有Vulkan绘制、Python推理或虚假遥测声明。

## 历史初版记录

以下记录对应本轮之前的顶部/场景工作区切片，保留历史日志与备份。其“未实现query7/UI画布”和“未获Git授权”等状态已被上面的最新实现/授权覆盖，不能作为当前状态。

最新统一版效果图已用于场景工作区初版。当前交付不是完整UI制作编辑器：专用UI离屏、资源审批接线和作者画布交互仍是后续必需工作。原生ImGui只呈现复制视图，C#继续拥有命令、选择、Play与工作区状态。

## 已接入内容

- 48逻辑像素单行菜单：项目名移到原生窗口标题的NcmaEngine之后，空白回退为“未命名项目”。菜单栏无品牌区，文件/项目/编辑/工具/游戏/AI/窗口/帮助从左侧连续排列；右侧运行下拉、停止、当前Windows/DX11、禁用账户和本机设置；无云入口。AI标题栏右对齐显示“未接入推理服务”，正文不重复显示。
- 哑光深蓝灰主题、统一边框/圆角/间距；菜单和账户/下拉图标为原生矢量呈现。窗口及EXE图标保留真实NcmaEngine.ico，不提取参考图品牌；旧GUI1.4 ToolbarBrand契约不变。
- 系统标题栏由C#请求的GUI Theme3申请统一深色外观；原生呈现层使用DWM，不替换系统标题栏或增设无边框命中策略。支持的系统申请深蓝灰背景、浅色文字与细边框，不支持的属性分别回退。当前机器仅接受深色模式，mask1，尚无精确RGB配色验收；高对比度在应用主题时使用系统颜色。退出Theme3/释放GUI恢复系统默认属性；不改变Platform1.0或GUI表大小。
- 左侧扁平GameObject列表与当前页名称搜索、中央真实场景目标、Inspector、底部资源/控制台、状态栏。宽度不足时自动收起AI侧栏；窗口菜单可以折叠AI区域。
- AI面板明确“未接入推理服务”，功能卡片禁用；沿用真实MCP端点、配对、精确授权与撤销，不生成假聊天或遥测。
- 原场景/历史/绑定/Play/相机/导入/审批命令保留C#路由。菜单、搜索和侧栏布局不写World，不启动/停止/单步Play。Esc/失焦/输入溢出取消交互并关闭菜单。
- Editor.Core统一InteractionGate与UiDraft租约。其他作者命令和历史操作在草稿期间返回edit_busy；冻结撤销租约，失败确认或取消释放租约。提交仍使用唯一Editor.Core历史和原有耐久文件事务。

## 契约与保留边界

GUI1.5沿用112字节的GUI1.4表，增加MenuButton/MenuBrand与Theme3；旧1.0至1.4表保持原大小和报告版本。Renderer query1至6与GUI Image的“准确已提交目标帧”规则没有放宽。托管加载器、入口、包清单和审计同步GUI1.5；仅审计既有安装时允许原已部署1.2至1.4，不构成运行时回退。

旧EditorPreferences.ToolbarHeight仍是旧头部设置，不解释成48像素菜单高度。当前菜单不提供项目热切换，场景命令仍使用现有项目上下文；完整布局持久化尚未接入。统一工作区目前固定深蓝灰，旧Theme/SideWidth/ToolbarHeight不控制新布局；独立布局设置和偏好界面同步属于B的剩余工作。

## 自动检查与真实截图

按用户“更新exe”的请求，最新标题、左对齐文件菜单、AI标题状态和系统深色标题栏已随完整安装包更新到out/bin/NcmaEngine.exe，最终配置为Release。顺序完整Debug/Release Build.bat均通过：每个配置12项原生、22项托管CTest和Python43，以及smokes、严格格式检查、inspect、三轮运行时测量、部署前后审计及校验部署。日志为out/m5-7-exe-update-debug.log与out/m5-7-exe-update-release.log。安装清单99个文件完整性复核通过，部署日志状态Complete。此前DLL占用未重现，没有强制结束进程或删除文件。旧正式安装保留在out/deployment/5f78fea792954883962fa13855aa3e77/backup；最终Release部署的备份out/deployment/e0b88c2326324e8cac2b046f3ec50dac/backup为本轮Debug安装。构建中的Git全局ignore读取权限提示仍保留，不是新增编译警告。本次没有提交推送，系统标题栏人工验收和M5.7未完成项仍开放。

新增WorkspaceTests覆盖520×360、800×600、1280×720、1920×1080，品牌默认名称、面板边界、菜单类型/帧/视图戳、过期事件拒绝、菜单取消与Paused Play会话/World/tick不变，以及真实GUI/Renderer合成和DX11验证计数。

UI作者测试增加草稿期间场景/历史/选择/文档替换/第二草稿拒绝、线程亲和、取消释放和外部文件冲突后释放。原UiDraft百次更新只有一次历史项、保存/重启/Undo/Redo测试保留。

此前项目名称位于菜单栏时，完整顺序Debug/Release Build.bat均通过：每个配置12项原生、22项托管CTest，Editor66、UI19、Player56、Gameplay53/fake Movement38、Python43，以及SDK/native smokes、严格新格式/移除格式拒绝、inspect、三轮保留fixture测量、审计和校验部署。对应历史日志为out/m5-7-project-name-debug.log和out/m5-7-project-name-release.log；保留其截图和备份，不作为最新标题布局的部署证据。

最新标题布局使用Build.bat -Configuration Debug/Release -SkipPython顺序验证，每个配置12项原生、22项托管CTest全部通过，无新增编译警告。Python43测试及inspect另行通过。日志为out/m5-7-title-layout-debug.log、out/m5-7-title-layout-release.log、out/m5-7-title-layout-python.log与out/m5-7-title-layout-inspect.log。WorkspaceTests检查菜单从左8逻辑像素连续排列、不提交MenuBrand，窗口标题包含正确项目名称/默认值，长Unicode名称有界且不截断代理对。窗口创建使用同一标题函数，窗口图标不变。渲染截图out/verification/m5-7/left-menu-title-release.png来自1822f45004c34556b0c201cb2cd57723/workspace-review.bmp；GPU截图只含客户区，不证明系统标题栏的人工可见验收。

此前AI标题状态调整的Debug自动检查通过，但校验部署在out/bin/Ncma.Animation.dll被占用时中止，日志out/m5-7-ai-header-debug.log保留，未确认占用进程。随后标题布局及文件菜单使用SkipPython验证，没有更新安装，也未执行整包测量及部署前后审计。最新完整回归与部署已通过，结果见本节开头；非部署检查仅保留为历史证据。

系统标题栏配色此前的顺序Debug/Release非部署回归通过，每个配置12项原生与22项托管CTest；Python43及inspect另行通过，新增代码无编译警告。日志out/m5-7-native-chrome-debug-verified.log、out/m5-7-native-chrome-release.log，补充最终Debug原生检查out/m5-7-native-chrome-debug-native-final.log。独立Release回读记录out/m5-7-native-chrome-readback-release.log确认dark属性开启/恢复、窗口样式和标题保持、大小图标保留，接受属性mask1。高对比度分支并未在启用状态下人工验证，拖动、缩放、Snap、窗口按钮、DPI和可见系统标题栏仍待人工验收；GPU客户区截图不能代替这些检查。初次新增测试因重复帧编号失败，日志out/m5-7-native-chrome-debug.log保留；修正帧递增后通过，没有放松帧约束。这轮非部署检查未覆盖out/bin；最新完整部署另有上述记录。

首次聚焦检查曾在被文字覆盖的位置取背景样本而失败；失败日志保留为out/m5-7-workspace-focused.log。随后移到无控件的背景区域采样，out/m5-7-workspace-focused2.log为66/66；没有移除背景颜色或GPU验证断言。视觉复查还补齐了编辑器中文标签，运行下拉箭头改用矢量，不依赖字体符号。GPU验证错误/警告均为0。

此前项目名称仍位于菜单栏的Release截图为out/verification/m5-7/project-name-only-release.png，由out/verification/m2/editor-services/f27b534c6edb4c9299b6d01e0ba3a787/workspace-review.bmp无损转换。该版本曾校验部署到out/bin/NcmaEngine.exe，应用宿主357376字节；当时旧安装备份为out/deployment/b974e1efbe2c44daaa7bf715df82eef0/backup。这张历史截图不作为最新标题布局的部署证据。未删除既有截图、安装备份或用户数据。

真实截图来自空场景测试及当前GUI/Renderer，不是港口效果图，也不证明港口资源/画质已实现。正式视口继续提交现有真实场景渲染服务。原概念文件见docs/references/editor-unified-workspace-2026-10-07.png，SHA256为CE1B6B11AD5A8D9DC80EDE4594BF0DBF2E7229242E608C4130DDF271643F8E24。

## 未完成项

| 阶段 | 当前状态 | 剩余工作 |
|---|---|---|
| A前置契约 | 部分接入，未关闭 | UI离屏query7、内容修订与准确当前帧呈现租约、缓存图像GUI增量 |
| B布局主题 | 初版接入，未关闭 | 分隔条、独立设置文件、完整小窗/DPI/长标签验收 |
| C场景与AI侧栏 | 初版接入，未关闭 | UI工作区切换、进一步分组/资产缩略图/过滤与可见操作验收 |
| D文档与资源 | 未接入正式Editor | 严格UI文件入口、精确本机审批、资源目录、图层与独立选择域 |
| E画布交互 | 未实现 | 作者命中、pan/zoom、多选、变换、吸附/对齐、顺序/Group与单手势Undo |
| F属性与测试 | 未实现 | UI Inspector、Token/资源、尺寸/DPI预览和隔离控件测试 |
| G缓存恢复 | 未实现 | 静态目标复用、事件合并、32次生命周期/失败资源保留 |
| H联合验收 | 未完成 | 完整UI制作真实流程、保存/重启/Undo、参考对照和人工验收 |

下一步先补齐A的原生color-only UI目标与缓存呈现契约，不复用带3D深度的目标，不篡改旧Image准确帧规则；再接入D至G。仅主题截图、独立UiSample或当前自动回归不算M5.7交付完成。

M4 K7、此前可见UI/IME/DPI/第三方MCP、目标环境、性能和长稳验收仍开放。无新增推理服务、Vulkan绘制、项目Player HUD/cook或UI Agent写能力声明。本批未提交推送；保留用户IDE状态和全部既有数据。
