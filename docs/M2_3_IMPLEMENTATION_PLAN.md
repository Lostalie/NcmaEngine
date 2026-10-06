# M2.3：C# 主循环、C++ GLFW 薄适配与原生 GUI

更新日期：2026-10-04。状态：候选实施及自动 H3 测试通过；真实输入法、DPI、焦点/窗口人工验收待完成。依赖 H1/H2；完整 DX11/GUI 绘制在 M2.4。
本阶段只产生候选程序，不覆盖当前 out/bin/NcmaEngine.exe。交付、精确预算和测试记录见 [联合报告](M2_3_4_TEST_REPORT.md)。

## 1. 目标与职责

C# 同步 Main 拥有主循环；NcmaPlatform 负责 GLFW 窗口/事件，NcmaGui 负责 Dear ImGui 控件展示。
确认采用“C# 主循环 + C++ GLFW 薄适配”，不采用 C# 直接绑定 GLFW 回调，也不采用 C++ 主循环驱动托管引擎。
新 Ncma.Editor.App 不启动 hostfxr、不调用 EditorApplication.Run，也不在 native 再创建主循环。
基线 GUI 保留 C++ 1.91.9b，不引入第二个 ImGui Context 或不同版本 cimgui。

依据 GLFW 的主线程和非重入限制，窗口/事件/销毁都在入口线程：
[GLFW 官方线程约束](https://www.glfw.org/docs/3.4/intro_guide.html)。
后台错误回调只记录原生有界诊断；禁止调用 CLR、World、Editor 或销毁窗口。

### 1.1 所有权与调用边界

| C# 应用与业务 | C++ 平台/GUI 薄适配 |
| --- | --- |
| 同步 Main、单调时钟、主循环与服务生命周期调度 | GLFW 初始化、窗口与系统事件处理 |
| 输入映射、玩法输入、编辑器命令与安全边界 | 原生回调记录、有界输入批次、窗口状态 |
| PlaySession/WorldRunner 调度、暂停与恢复策略 | DPI/焦点/尺寸/关闭状态的采集与平台操作 |
| GUI 视图与意图处理、默认/自定义渲染流程 | Dear ImGui 展示暂态与原生后端连接 |

C++ 适配只提供平台机制，不拥有 World、游戏逻辑、编辑器业务或第二套应用调度器。
C# 只调用 NcmaPlatform 的版本化 C ABI；产品模块不另行绑定 GLFW、注册托管 GLFW 回调或初始化另一份 GLFW。
窗口由 module-owned opaque handle 表示；GUI/Renderer 必需的平台原生对象通过窄 native-private 服务取得，不传给托管业务。
输入、状态和 GUI 意图通过固定布局 POD、显式长度、调用方缓冲与复制数据交换，不跨 ABI 传 CLR 对象、委托或 C++ 异常。

### 1.2 单一入口线程与事件安全边界

同步 Main 保持 GLFW 调用在线程入口；不得用 Task.Run 或未保证线程归属的异步续体迁移窗口操作。
后台 IO/任务只返回有界结果或发出唤醒，由入口线程在安全边界应用；不直接修改 World 或销毁窗口。
GLFW 回调仅记录输入/状态/诊断，不在回调内处理游戏、编辑器事务、重入 PollEvents 或执行销毁。
不能假定回调只发生在 PollEvents 内；其他平台操作触发的事件也进入同一队列，下一收集边界按 seq 复制。
错误回调可能发生在其他线程，其诊断缓冲须有明确同步和生命周期规则，仍不得调用 CLR。
普通窗口操作在指定 owner 线程执行；原生入口校验线程与重入，错误转为结构化状态，不能依赖调用者自觉。

### 1.3 帧流程与 M2.4 衔接

1. C# 调用平台 PollEvents/事件收集，接收有界批次与窗口状态，优先处理关闭、失焦、最小化和尺寸变化。
2. 原生 BeginGuiFrame 更新输入与捕获信息；C# 按明确的首帧捕获规则过滤玩法输入。
3. C# PlaySession 推进固定步和只读更新，不增加 native Tick 或另一套固定步控制。
4. C# 构造 GUI 视图与不可变渲染数据，处理先前 GUI 意图时重新检查身份、generation 与 revision。
5. M2.3 验证 GUI 输入/意图与 CPU draw data；M2.4 完成后由 C# 管线组织渲染，原生执行并合成 GUI，最终一次 Present。

M2.3 与 M2.4 方案 A 保持一致：C# 控制应用和渲染流程，C++ 负责 GLFW/ImGui 适配及 GPU 执行。
性能目标是固定少量 ABI 调用和缓冲复用，不为每条输入事件跨语言回调；C++ 主循环不作为未经测量的性能捷径。

## 2. 分步实施

### A. 单一平台实例与窗口 API

Platform API 建议覆盖 Initialize/CreateWindow/GetWindowState/PollEvents/SetTitle/SetIcon/RequestClose/Destroy/Shutdown。
WindowState 返回逻辑尺寸、framebuffer 像素尺寸、contentScale、focus、minimized、closeRequested、sequence。
窗口身份为 module-owned handle，不把 GLFWwindow/HWND 暴露到 Runtime/Scene/Gameplay。
同进程一个 GLFW 生命周期；Gui 后端要链接同一 GLFW 实例。
建议仅设置 GLFW_LIBRARY_TYPE=SHARED 并明确部署 glfw DLL，避免两个 DLL 各静态链接一份 GLFW；
若合并 Platform/Gui 物理 DLL，也必须维持两个窄 API 和一个 module lease，不能产生两套初始化。

### B. 输入记录与边沿复制

native 回调记录按键/鼠标/滚轮/文字/焦点/resize，poll 返回 caller-owned POD 批次。
保留同一次 poll 内 press+release，使用单调 seq 与 owner 线程；建议最多 4096 事件。
溢出返回明确状态及最新 held/focus：清 transient、重同步 held、取消活跃 GUI/draft，不能假装完整消费。
文字按严格 UTF-8 复制到 GUI，不进入 Gameplay key bitmap；输入法合成能力先测试再声明。
C# 输入适配保持 M1 session UUID、seq、finite pointer、按位 held/pressed/released；
零步暂存、首成功步消费、多步边沿只一次、Pause/Resume/失焦清理不改变。

### C. GUI 可复制协议

C# 构造 GuiFrameView：frame/viewGeneration、documentGeneration/revision、panel ID、稳定 widget ID、文本/数值/范围/enable、选中 UUID、viewport handle。
native Draw 不可持有 CLR 引用，只保留 active widget/text 光标等纯展示暂态，不保存场景/脚本真值。
GuiEvent 包含 view generation、widget ID、intent kind、复制值、activate/change/commit/cancel、捕获键鼠状态。
所有事件下一安全边界应用；绘制不回调 C#。事件丢失/代次错误取消交互，不更新权威数据。
视图建议 2MiB/8192 项、输出 256 条/64KiB（已实施的精确上限），列表按可见范围请求；完整授权提案不可截断后批准。
widget ID 使用 panel+对象 UUID+字段稳定键，不用数组索引，让对象增删不会把编辑值套到其他对象。
不做任意 HTML/脚本/控件可执行指令，不把 GUI 协议升级为通用远程 eval。

### D. 输入捕获与双阶段 GUI 帧

BeginGuiFrame 更新原生输入及 WantCapture；C# 随后过滤玩法输入，再推进 Play，最后 PresentGuiView 输出新事件。
焦点丢失取消编辑草稿；键鼠捕获只影响玩法通道，不吞关闭/系统输入。
不能用上一帧捕获状态覆盖所有本帧事件；定义活跃控件接管首帧的策略并用点击/文字测试验证。
Gui 需要平台 handle 和 Renderer 的 native-private 后端适配，C# 不获得图形设备指针。
在 Renderer 未完成时，测试 GUI 输入/控件事件与 CPU draw data，不宣称已显示可用编辑器。

### E. DPI、字体、图标、布局

以 framebuffer 像素与逻辑点显式转换；跨 DPI/resize 不让视口矩形与鼠标坐标混用。
字体加载/atlas 重建在 owner 安全边界，采用明确字体来源与 fallback；中文/高 DPI 不以缺字方框作为通过。
图标沿用 engine/build/resources/NcmaEngine.ico，同时验证窗口小/大图标和最终 apphost 资源图标。
布局/主题保存本机偏好；核对现有 engine/config ini 用途，禁止在 cwd 自动生成第二份旧 NcmaEditor.ini。
固定布局和恢复默认先完成；真正 docking 必须另锁 vendored 分支/version 并全回归，不能仅开启不存在的 flag。
M2 不默认 multi-viewport，避免新增窗口/GPU生命周期复杂度。

### F. 第一条托管窗口链

候选 Editor：读取项目 → 创建服务 → 加载模块 → GLFW 窗口 → 单调 clock →
事件复制 → fake/candidate GUI → 关闭清理。
命令行完整解析，避免 EditorMain.cpp 当前 substring 匹配；非法参数/renderer/路径有稳定退出码。
最小化时不忙等，使用有界等待；后台 post-empty-event 只唤醒主线程。
不使用 ExitProcess 掩盖 Dispose/关闭问题；smoke 也必须完整走 finally/关闭路径。
启动/帧循环/关闭均由 C# 调度，native API 不包含会长期接管应用循环的 Run/Tick 回调入口。
按依赖关闭：停止新业务/帧提交 -> GUI 关闭 -> 已接入 Renderer 的 GPU 完成与释放 -> 窗口销毁 -> GLFW Shutdown -> module lease/卸载。
M2.3 尚无 Renderer 时跳过该依赖，不伪造渲染完成；所有路径保留 finally 清理，确保原生回调不再可能访问已释放状态后才卸载模块。

## 3. 最低测试矩阵与 H3

- 无窗口假平台主循环；隐藏窗口反复创建/销毁、初始化失败、线程/重入负例。
- 候选程序由同步 C# Main 调度；不运行 native EditorApplication/hostfxr 主循环，不存在第二套 GLFW 初始化或托管 GLFW 回调。
- 事件发生于 poll 及其他平台调用时均可按序收集；错误回调跨线程安全，回调不修改 World、不重入事件处理、不销毁资源。
- 同 poll press/release、滚轮/UTF-8、事件溢出、held 纠正、焦点、ImGui capture 与 Pause/Step。
- DPI 100/150/200%、逻辑/framebuffer 坐标，resize 0/非零、最小化恢复、关闭优先。
- GUI 视图非法长度/未知 widget/旧 generation、对象被删、帧晚到、输出满，不误修改。
- window/GUI/font 资源和 module lease 归零；不能靠进程退出代替循环销毁。
- 记录 ABI calls/bytes、事件数、批次复制耗时和托管分配；输入压力下不出现逐事件托管回调或无界队列，复用输入缓冲。
- 手工中文/输入法/图标/焦点截图和结果补充，自动合成输入不等于真实键鼠验收。
- Build.bat Debug 全矩阵通过，新代码零警告。

H3 后仍不迁移默认启动器；H4 给出实际绘制，H5 才完成所有现有面板业务。

## 4. 不在本阶段开发

不做完整UI制作/运行时UI、Animator 节点编辑、完整资产浏览器、音频/手柄全 SDK、跨平台支持或多窗口 docking。
