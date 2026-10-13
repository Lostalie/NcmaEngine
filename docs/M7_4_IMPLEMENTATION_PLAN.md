# M7.4 HDR 后处理与简单定制

2026-10-14。上一小阶段属于实质进展：M7.3-C3-C 已完成并提交推送。本地/远端 `c05eaaec6391dfd6f2b6b4c79ee0497dae99eb8f` 开工前核对一致。当前只保留4项既有IDE设置变更，不纳入本片。

## 目标和分片

C#拥有效果组合、参数、注册、依赖与编辑事务；C++只执行GPU数值工作。所有效果使用同一公开契约，官方实现没有特权旁路。当前版本DX11only；纯2D不部署或分配3D后处理。不能把当前后置Exposure重复Tone示例当成Bloom或通用HDR中间阶段。

### A：颜色流程与可复用Tone预设（自动候选已完成）

明确输入为非负scene-linear RGBA16F；曝光倍率先作用于线性值，然后Tone压缩，再精确sRGB显示编码一次；输出opaque RGBA8，游戏UI/编辑器GUI在显示域合成，不再套一次Tone。

提供纯C#官方Tone声明工厂：与已有默认相同公式的ACES fitted（不是完整ACES色彩管理）、Reinhard与Linear Clamp。未知模式/Flat2D/Skinning拒绝。新增简洁有界EV转倍率，复用现有SceneToneStage曝光常量。原生Shader默认字节、原ABI和正式入口默认选项不改变。

官方预设与用户Tone都经过公开的精确目录/descriptor组合入口；仅替换Tone VS/PS，保持原Geometry/Shadow及独立共享Skin，不按源码字符串替换生产Shader。组合输出是owned immutable catalog，不是编译/GPU准入或权限凭证。实际编译、完整反射、owner/健康/非重入/离帧/可信非simulation审批、source-free NCS1与安装仍走原路径。没有新增Agent编译或任意代码执行权限。

实际GPU验证三曲线、暗部/中灰/超1HDR/曝光边界/高亮饱和、RGBA16F量化和独立标量/全图oracle；Scene3D/SceneEnvironment、shadow开关、用户stage、source-free包、失败保留旧管线、resize/close、UI显示色不受Tone、纯UI无3D，以及暖机CPU分配/上传/资源统计。原M6/M7所有测试保留。A不宣称Bloom、正式设置面板或整个M7.4完成。

### B：线性HDR Feature和Bloom真实执行（自动候选已完成）

2026-10-14进入B，开工前A本地/远端 `acffae59b14f629f3ba3578e26394f304f76db6b` 一致。B详细执行与预算见[运行合同](M7_4_B_RUNTIME_CONTRACT.md)，测试/完整回归状态见[交付记录](M7_4_B_DELIVERY_REPORT.md)。B不包含C正式宿主/配置/编辑器/AI或D产品联合验收。

独立版本化执行扩展，不挪用旧C400/ScenePass保留字段。C#描述读取/写入、线性颜色域、尺寸/格式、单写者与有界依赖；薄native执行已验证的有界batch和opaque资源。候选资源/程序完整验证和创建后才交换；失败保留active plan，异常数值阶段fail-stop不伪造回滚。

首版Bloom：线性HDR阈值/soft knee，受控降采样链、可分离模糊、上采样累加和强度，最后一次Tone。Off不创建Bloom临时纹理或dispatch。奇数尺寸/最小尺寸/resize资源生命周期和最大分辨率/level/显存预算明确；关闭后回到基线。用户registered Feature和官方Bloom同一契约，禁止普通用户直接管理D3D绑定。

独立CPU滤波oracle与亮斑扩散/暗区不产生亮度/能量变化/alpha、错误依赖/重复写/错色域/资源别名/格式/预算、候选失败原图和费用证据。源码及无源码字节都需真实反射准入；不把声明合法称为安全沙箱。

### C：正式共享宿主、配置与编辑器/AI

C拆为C1受检程序库/产品闭包、C2正式配置/共享执行、C3编辑器/AI，见[C详细方案](M7_4_C_IMPLEMENTATION_PLAN.md)。C1已通过149项检查与最终顺序完整Debug/Release，见[C1合同](M7_4_C1_RUNTIME_CONTRACT.md)、[C1交付](M7_4_C1_DELIVERY_REPORT.md)。C2正式配置/共享执行已通过67项模块/52项共享及双配置各71次正式宿主检查、完整顺序Debug/Release，见[C2合同](M7_4_C2_RUNTIME_CONTRACT.md)、[C2交付](M7_4_C2_DELIVERY_REPORT.md)。C3专用编辑器/AI目录与同一事务完成9组作者测试、正式DX11控件联动和完整顺序Debug/Release，见[C3合同](M7_4_C3_RUNTIME_CONTRACT.md)、[C3交付](M7_4_C3_DELIVERY_REPORT.md)。默认Off；C自动候选链闭合，下一片D；不替代D联合或人工验收。

增加受控版本化高层后处理设置，绑定精确注册模块和参数，不接受Shader文本/路径。Editor与Player共享已准备程序/资源；配置变更在安全边界准备，tick不编译、不读文件、不等待推理。对照图和失败诊断可检查当前world/revision/资源代次，不呈现旧图为新配置。

Inspector presets/参数/Feature顺序通过现有可撤销事务和唯一历史；AI先读有界元数据，再提案及精确审批，同一Undo/Redo。未实现效果明确unsupported。更新受检发布闭包及NCP/项目选择；原默认资源不能无证据静默切换。Headless/纯UI仍无3D插件/后处理资产。

### D：替换示例与产品联合验收

公开Feature插入、stage replacement和full pipeline replacement实际示例；用户无需接触平台API。实际静态/FBX动画/PBR/IBL/shadow/HDR/Bloom/显示域UI联合图；独立数值而非同shader自证。搬移受检Editor/Player、源目录不可用、错误闭包pre-gameplay拒绝、Undo/Reload/resize/关闭；记录CPU/GPU/分配/上传/资源成本。

GPU last-valid时间无origin frame，不算独立逐帧采样；M7.7另行补齐。M7.3-C3-C源项目故障恢复高宿主分配作为性能调查项保留，不以功能通过遮盖。目标机/.NET自包含、人工UI/DPI/第三方MCP、用户素材/完整性能/1h仍开放。

## 每片门禁

实现→定向测试/修复→冻结源码→完整顺序无Skip Debug再Release Build.bat→所有native/managed/Python/新旧格式/smokes/inspect/三轮profile/audit/部署恢复→manifest/备份/Complete journal/实际apphost校验→文档/路线图/AGENTS→提交推送并核对远端，才进入下一片。保留失败/备份/SDK/userdata/IDE。M7→M8→M9，不因一个Tone helper缩减完整M7.4范围。
