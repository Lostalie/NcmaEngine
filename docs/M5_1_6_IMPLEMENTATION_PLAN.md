# M5.1至M5.6 UI基础实现方案

2026-10-06。用户已授权实施M5.1至M5.6，修复失败并完成测试后提交推送。本批不实施M5.7工作区、不关闭M4及此前人工、目标环境、性能和长稳门禁。

## 阶段职责

| 阶段 | 实施范围 | 自动退出检查 |
|---|---|---|
| M5.1 文档 | 删除旧C++ UiDocument及唯一测试消费者；C# UUID文档、内部元素树、样式、类型化Token、严格.ncmaui JSON v1和独立运行时副本 | 旧格式、未知/重复/缺失字段、整数枚举、非法Unicode/数值/资源、循环/深度/数量、线程和副本别名拒绝 |
| M5.2 事务 | 纯候选操作、主机准备的有界proposal、精确文档/文件/资源授权；复用Assets.Authoring磁盘事务与Editor.Core唯一历史；草稿更新、取消和一次提交 | 默认拒绝、过期/撤权/重放、外部文件变更、Undo/Redo、保存重启、三个磁盘发布故障点的恢复 |
| M5.3 布局 | Editor/运行时共享Free/横/纵布局、Fixed/Hug/Fill、padding/gap、min/max、锚点、逻辑坐标/DPI、安全区、裁剪、可见/禁用继承与缓存 | Hug/Fill循环拒绝；相同比例/DPI结果；动态隐藏重新流排；4096元素；静态布局不分配 |
| M5.4 绘制 | 公共Renderer query6的独立UI数值API；驻留顶点显示列表、相邻有序批次、RGBA图片、透明叠加、圆角/裁剪；明确纯UI无默认深度及3D资源初始化 | 实际DX11像素/叠加顺序/裁剪、API validation、重复帧/代次/非法批次、pin和释放、故障fail-stop；静态帧无重复上传 |
| M5.5 文本 | 独立NcmaText数值插件及C#字体资源；显式内存字体、DirectWrite整形/换行/组合字符；灰度RGBA文字覆盖纹理、匹配的帧外测量与缓存 | 中文/组合字符实际覆盖像素、缺字拒绝、非法字体、字体pin、CPU-only生命周期、缓存测量不重复整形 |
| M5.6 运行时 | Button/Slider/TextInput/ScrollView、命中/捕获/取消/焦点/键盘/字素编辑、composition状态、复制动作队列；C#文本/值/可见/禁用API；共享UiCanvas及独立纯UI样例 | 保存文档到真实HUD/文本/控件绘制和输入闭环、队列上限/旧身份/重放、动态文本/flags、Resize、静态刷新、样例smoke |

## 所有权与边界

C#拥有文档、布局、编辑权限、运行时控件、缓存选择和事件策略。原生只执行有界GPU批次或字体整形/栅格数值，不保存World、编辑历史或执行脚本。UI内部树不成为GameObject场景父子关系。

UiCommands只提交可信主机预先准备的proposal UUID，不接受Agent提供的文档、文件路径、代码或自我批准。原有场景/资产配对授权不自动授予UI文件权。大候选不通过64KiB能力请求搬运；Editor.Core仍持有唯一历史，Undo/重放重新检查原授权。

UiRuntime没有World引用或游戏回调。主机用当前UI实例/代次、Play/World和committed tick核对复制事件，在安全边界读取；改变身份清队列，旧tick事件不跨新tick套用。presentation setter不修改作者文档或自动执行游戏命令。

UiCanvas.Prepare在提交和simulation之外执行。相同文档/控件状态/viewport不重复布局、整形或上传。改变文本、布局或资源后重新准备有界候选；关闭失败保留所有者与资源，禁止依赖卸载，不把GPU完成等待放入simulation。

## 有界契约

- 文档4MiB、4096元素、64层内部深度、256 Token、每次候选256操作。
- Renderer query6/UI API1.0；原query1至5及模块1.0至1.2表不变。驻留显示列表最多65536顶点/4096有序批次；图片/列表/总字节另有显式上限，非法候选不截断。
- Text模块kind5、ABI1.0；4个字体、单字体32MiB/字体总64MiB、128个文字run/总128MiB，栅格最大4096平方。数据和测量归C#，字节不含GPU或字体COM指针。
- UI输入单批256、有界128动作队列；容量不足明确背压，不把未处理事件当作已执行。

## 当前基线与后续工作

本批使用缓存的完整文字run覆盖纹理，不宣称已完成glyph atlas、SDF/彩色emoji、多字体混排自动fallback或完整文本选择编辑。缺字明确失败，不从操作系统或网络偷偷补字体。字体的许可声明不是实际再分发权证明；样例读取本机Windows字体，不复制到发布资源。

矩形scissor不能表达旋转裁剪，当前显式拒绝该组合，不伪造stencil支持。UI显示列表可独立提交或合成到活动3D帧；专用UI离屏纹理与正式Editor画布接线仍需后续工作区阶段验证。

composition状态机可自动测试；当前GLFW平台适配器只收到已提交Unicode字符，不宣称真实Windows IME预编辑/候选窗完成。Player项目加载/动作样例HUD、资源cook/裁剪发布、glyph atlas与高级文本能力应在后续阶段补齐并保留明确状态，不由本批样例smoke替代。

## 验证与交付

Build.bat完整顺序Debug/Release，保留全部native/managed CTest、smoke、严格格式拒绝、Python inspect/测试、包与部署恢复。新代码零编译警告，DX11检查零validation错误/警告。失败修复后重新运行完整回归；不跳过失败项，也不强制清理锁定文件。

交付记录见[M5.1至M5.6记录](M5_1_6_DELIVERY_REPORT.md)。整体编辑器视觉/布局仍按[M5.7方案](M5_7_IMPLEMENTATION_PLAN.md)执行，本批不复制参考图的品牌、Node场景树、假AI服务或示例性能数字。
