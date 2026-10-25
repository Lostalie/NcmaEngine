# ECS 重构实施合同（E0–E6）

2026-10-25 最新实测：E6 当前本机 Debug/Release 完整回归各 131/131 通过，无失败、超时或跳过；双配置全托管编译 0 警告 / 0 错误。正式 Release 编辑器已另经用户授权更新。偶发原子保存根因、SDK8/VS 宿主验证与 E5-D 目标机性能仍未关闭。[详细修复、证据与边界](E6_UNIFIED_VALIDATION_PLAN.md)。下方标题和历史失败记录不覆盖本条最新实测结论。

<a id="ecs-e6-b"></a>

## E6-B 元数据与访问代码生成（2026-10-24，便携分析器、字段规则及注册聚合收敛，整体统一首轮测试未通过）

最新实测：五项前置门禁落实，Debug/Release完整托管63工程与原生全目标均零警告/错误。两配置CTest均实际执行100入口，各89通过、9失败、2超时，不能宣称E6完成。B4-B构建/发布、生产字段差分、注册聚合用例通过；CodeGen内存fixture各22/25，仍需处理manifest诊断、引用移除输出和JSON拒绝预期；Runtime主组83/84，调度指标被资源线程规则拒绝。Services/SceneRendering等入口在资产准备异常处提前失败，后续E6用例尚未完整覆盖。正式EXE不变，未运行整条Build脚本、无正式部署/提交/推送；E5-D继续延期。[完整失败清单与复测要求](E6_UNIFIED_VALIDATION_PLAN.md)。后文“未运行”均为此前状态。

顺序：E6-A 实现收尾 → E6-C → **E6-B** → 整体 E6 统一测试。E5-D 延期。本节不把编译成功视为功能、增量失效或性能验收。

### B4-B本机双配置实测（2026-10-24最新）

按用户指出的未执行缺口补跑，不再以编译替代合同验证。当前源码新建Debug/Release验证入口，各执行16条构建合同和2项真实Editor/Player隔离发布，共36条子命令全部符合预期；8条预期拒绝命中指定诊断，没有意外失败或超时，未修改测试条件/产品代码。四轮success=true、正式EXE哈希C50742AD不变，所有进程已正常退出。

涵盖干净/no-op/编辑/声明移除恢复、程序集引用冲突/移除/修复、最后有效DLL保留、netstandard2.0/Roslyn4.8 PE核验、编译/发布依赖泄漏负例与真实发布审计。实际SDK9.0.315；SDK8及此次VS矩阵未执行，新增字段差分/注册聚合内存用例和其余E6回归未运行，因此B4跨宿主和整个E6验收仍未完成。未启动/部署/提交/推送，E5-D延期。详见[完整范围与证据](E6_UNIFIED_VALIDATION_PLAN.md#b4-b实际执行结果2026-10-24)；`out/verification/ecs-e6/b4b-execution-first/execution-result.json`关联四轮原始回执。下方未执行说明保留为历史。

### 注册聚合与唯一组合入口（2026-10-24）

`ComponentGenerator`为有本地注册或引用模块的程序集生成唯一`<AssemblyName>.Generated.ComponentModule.Module`，并附带编译期模块manifest；纯组合程序集也生成依赖聚合。程序集名不是合法命名空间时使用稳定hash名称。源码里的`[GenerateComponent]`自动加入；暂不适合该有界元数据子集的组件，在原单类型注册方法上用`[ComponentRegistration]`显式加入，保留原schema、validator、nullable metadata、无默认值和runtimeAttachable，不凭空生成默认资产UUID或开放JSON支持。新增`NCMA009`拒绝错误方法签名、畸形模块manifest和占用的生成类型名。

唯一工厂为`Ncma.Runtime.CompositionRegistry.Build(params ComponentModule[] roots)`。无参数只包含Runtime，维持低层依赖方向；完整宿主使用自身生成根：Editor的实际程序集名为`NcmaEngine`，Player为`NcmaPlayer`。调用方不再排列组件或手写模块依赖清单。Build先复制根、展开依赖，按ordinal身份确定顺序并去重；同ID不同模块实例、环、空项或超过128模块/根预算在注册回调前拒绝。之后在一份新建的原`ComponentRegistry`上依次注册；保留原重复TypeId/CLR类型、元数据、回调、冻结及扩展规则，无全局可变目录、不运行时反射扫描程序集。失败不返回半成品，可信回调的外部副作用不承诺回滚。

已删除`ComponentRegistry.CreateDefault`、`RenderComponentRegistry.CreateRegistry/Register`、`CharacterComponents.Register`、`RenderConfiguration.CreateRegistry`及UI/Shadow/PostProcess三个私有`CreateRegistry`。全仓生产/测试调用者改为Build；渲染类只剩schema/数值规则工具，改名`RenderComponentRules`，不保留旧兼容工厂。三个私有解码器使用所在程序集聚合，其公开Read/Decode入口本就按具体类型过滤，没有必须维护的注册子集，因此不增加第二份过滤注册表。底层独立工具可选择显式生成根；低层`ComponentRegistry.Register<T>`仍是原可信扩展原语，不是另一份引擎类型清单。

完整宿主目标清单为19类型（Runtime1、Animation4、Scene.Rendering9、Characters4、Rendering1），其中4类型保留生成字段metadata。新增3组CodeGen与3组Runtime用例登记未运行：生成+手写聚合/声明顺序/删除失效、错误声明拒绝、完整19类型与原挂载限制、菱形去重/复制/重复构建隔离、冲突/预算先行拒绝、原重复注册和失败隔离。既有身份菱形测试更新为允许一个聚合入口、禁止重复本地注册；原数值/负例/性能负载不缩减。正式EXE不变；验证限静态与编译，整体E6末才运行测试。证据目录`out/verification/ecs-e6/pre-composition-aggregate-first`保留改前托管源码和状态文档及编译日志，非全仓冻结；未部署/提交/推送。

本片最终隔离Debug编译10个完整测试工程（同时覆盖现役Editor/Player依赖），均0警告/错误：CodeGen14.66s、Runtime3.66s、Services13.47s、Rendering19.20s、Scene.Rendering8.13s、Editor.Core3.70s、Scene3.52s、Gameplay4.71s、Animation4.48s、Player6.88s。首次Editor引用项目命名空间而非实际AssemblyName导致1错误37.01s；动画测试发现两处既存作者World/旧Play构造引用导致2错误4.16s。两轮失败与对应源码保留；前者修正生成根名称，后者使用现役Bake得到真正runtime，保留原tick/clock/postcommit fault断言并追加作者bytes不变检查。原始托管源码及状态文档823份已归档，选定当前源码/日志/产物receipt并非全仓冻结。Editor/Player依赖与产物无CodeGen/Roslyn运行依赖；未运行测试、未做本片SDK8/VS/Release宿主验收。

### 字段契约唯一规则源（2026-10-24）

`Ncma.CodeGen.Shared/FieldContract.cs`以链接源码同时进入生成器和Runtime，不引入运行时Roslyn依赖。两侧仅提取符号/反射事实；共享层负责以下顺序：

1. 类型JSON自定义属性拒绝 → enum → 精确CLR标量kind表 → Entity/地址/引用/ref-like排除 → 值对象白名单与集合排除。
2. 所有实例字段/属性（包括私有和本来会忽略的成员）先拒绝`System.Text.Json.Serialization`属性；静态成员忽略。公共readonly字段拒绝；属性要求公共getter和setter，getter-only/indexer忽略，公共getter+非公共setter拒绝。反射不实例化自定义属性。
3. CamelCase路径、对象路径与叶子一起展开；原16层/128字段限制不变。原`FieldSchema`继续负责闭合schema；字段ID/路径/flags/完整覆盖与最终ordinal排序共用一份实现。未扩大受支持JSON子集，flags仍不是访问授权。

新增`FieldContractTests`三组，登记到原CodeGenTests入口但**尚未运行**：

- 扫描生产工程的真实源码属性符号，逐组件运行生成器模型与实际`ComponentMetadata.Prepare<T>`；运行时定义独立从属性读取并反向输入，逐项断言ID、顺序、kind、flags、CLR及JSON路径相等。当前涉及Transform/Character/Camera/DirectionalLight四个声明。源清单与已引用产品程序集清单必须完全一致，新模块未加入测试引用或程序集过期时失败，不静默漏测。
- 全标量类型及enum、嵌套对象/叶子、flags和ordinal排序、只读属性/静态/indexer忽略；独立预期值防止两侧共享错误后一起“通过”。
- 私有成员/公共成员/类型上的JSON自定义、readonly字段、私有setter、地址/Entity/string/DateTime拒绝差分。

仅测试工程新增Characters依赖以及两侧对CodeGen.Tests的friend可见性；生产端不扫描程序集、不扩MCP或运行时写权限。隔离Debug完整编译CodeGen.Tests **26.39s**、Runtime.Tests **3.55s**、Services.Tests含Editor **45.61s**，均0警告/错误。首次恢复TLS错误与一处测试可空性错误已保留后修复；编译不代表上述差分测试或功能验收通过。证据`out/verification/ecs-e6/pre-field-contract-shared-first`为选定源码/日志/产物记录，非全仓源码冻结。正式EXE不变；未测试/部署/提交/推送。

### 分片与交付边界

| 分片 | 实现目标 | 当前状态 |
| --- | --- | --- |
| B1 显式组件声明和生成基础 | 增量生成器、稳定 TypeId/version、原注册目录、受检 typed World/调度访问；首先迁移真实 TransformData | 已接线，测试待整体 E6 |
| B2 字段元数据 | 显式稳定 FieldId（不从声明顺序派生）、嵌套字段路径、默认值、编辑/持久化/运行访问标记；schema 与字段一致性检查；接入同一 ComponentRegistry | 持久化组件子集已实现，测试待整体 E6 |
| B3 消费方迁移 | Inspector/序列化/AI schema 消费同一元数据；代表性引擎与脚本类型迁移；调度声明生成但仍由原图编译器验证 | 约定有界子集及消费审计已实现；待整个E6统一测试，非所有组件/脚本迁移 |
| B4 构建与闭环 | 构建宿主版本合同、跨程序集重复 ID、发布依赖审计、增量/干净生成一致性及失效负例；补齐整体测试入口与完整消费者矩阵 | B4-B本机SDK9双配置36条命令实测通过；SDK8/此次VS复测及其余E6回归待执行，不是完整跨宿主或E6验收通过 |

B2/B3 必须区分作者 UUID/事务与运行 Entity/World；生成访问不成为编辑写入口，不绕过原命令、权限、预览、批准、Undo、generation、只读、线程、候选步、预算或唯一提交。AI 仅获得已授权原 capability；不新增自动批准/运行写权限。旧兼容类型不恢复。

### 构建宿主修正：netstandard2.0分析器，无SDK固定（最新）

按用户最新决定，删除根`global.json`及`NcmaRequireCompilerHost/NCMAHOST001`，不再限制SDK9.0.315或拒绝全框架MSBuild。生成器改为`netstandard2.0`，包引用`Microsoft.CodeAnalysis.CSharp 4.8.0`并设PrivateAssets=all；测试程序降为net8，直接引用同版包，三个CTest路径同步。生成器自身及生成代码使用C#12，最低编译器API为Roslyn4.8（SDK8/9、VS17.8+），不是任意旧VS均兼容。参见[官方包目标框架](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/4.8.0)与[Roslyn生成器说明](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md)。

没有重写JSON解析器或放宽schema：保留System.Text.Json，明确包版本8.0.6/PrivateAssets=all；仅将非宿主依赖作为Analyzer项交给Roslyn加载器。普通CodeGen项目引用只返回自身程序集，避免netstandard依赖混入net8测试ReferencePath；编译器自有Roslyn/Immutable/Metadata不作为附加分析器传入。产品引用/发布隔离守卫继续生效。9条诊断登记在未发布规则表并启用扩展分析器规则，没有关闭警告压制问题。

HashData/ToHexString替换为标准SHA256实例+相同大写十六进制输出；Stack.TryPop、Dictionary.TryAdd/GetValueOrDefault、ASCII判定、Range/ToHashSet改为netstandard2.0可用等价写法，保留共同FieldSchema的预算/重复/闭包规则。内部IsExternalInit仅作生成器record编译标记，不成为产品兼容层。

构建fixture移除两个SDK固定相关命令，现16条；新增生成器PE中`.NETStandard,Version=v2.0`及Roslyn4.8程序集引用检查，泄漏负例改用NuGet依赖。原增量、移除、冲突与发布用例不删减；测试仍未运行。

证据边界：SDK9下CodeGen.Tests最终Debug8.86s、完整Services.Tests含Editor Debug42.31s均0警告/错误；VS2022 Community/Roslyn4.14全框架MSBuild共享编译真实Gameplay.Sample（含Runtime组件生成和脚本Export生成）3.96s、0警告/错误。实际产物确认netstandard2.0，隔离Editor/Sample的文件与deps无CodeGen/Roslyn运行依赖。SDK8未安装，未实测；VS非共享模式因CS8034/0x80131515未通过，独立只读Zone查询显示F:程序集URL为Internet，未修改信任策略。不能将共享模式结果冒充所有VS设置或SDK8验收。

首次普通还原TLS失败；获准官方NuGet还原后出现11条分析器规则错误，再1条规则表格式错误，随后6条net8/netstandard引用冲突警告；均保留日志，依次补齐未发布规则表与分离分析器依赖，未用NoWarn绕过。后续Editor/VS普通还原也遇到TLS，官方还原成功后才编译。17份改前原文（含已删除global.json）、失败相关源、全部日志及选定receipt位于`out/verification/ecs-e6/pre-codegen-portable-first`，非全源码冻结。正式EXE C50742AD未变，无功能测试/冒烟/性能/发布/部署/提交/推送。

下一步仍是整个E6统一验证，补齐SDK8环境及宿主矩阵后分别记录，不自动安装SDK、不把本机结果扩大成所有宿主通过。下方SDK固定内容为历史，已被本节取代。

### B4-B 真实构建与发布验证设施（历史，已实现/编译，未运行）

新增两个显式测试模式及CTest入口：`NcmaCodeGenTests.BuildContracts`、`NcmaCodeGenTests.PublishContracts`。普通build不运行；原Build.ps1前缀分组覆盖二者。前者准备18条SDK/MSBuild命令，后者隔离发布实际Editor/Player；均未执行。宿主/依赖守卫增加稳定错误码NCMAHOST001/NCMADEP001/NCMADEP002，负例必须匹配预期码而非任意非零退出。

干净/no-op/编辑/声明移除恢复、组合引用替换/移除/修复、最后成功DLL保留、SDK真实解析/不可用固定版本、原编译器引用泄漏与Content发布泄漏、恢复后正常发布均已登记。通过PE元数据读取实际manifest/Generated类型和程序集依赖，不加载被测程序集；发布产物另查apphost/runtimeconfig/deps，保留文件哈希。不宣称无关旧.g.cs调试残留是有效编译输入，不删除旧文件掩盖失效。

新鲜UUID隔离目录、reparse/越界拒绝、fixture改前历史、命令/PID/输出/binlog与失败回执、单子进程180秒期限、仅持有的子进程树清理及正式EXE前后检查已接入；不部署、不运行游戏。发布是net8/win-x64/framework-dependent，仍不裁剪/AOT/单文件；不是M9完整资源包验收。选定源码pins不是全冻结。

核对A/C/B测试注册与当前CTest入口，新增[整体E6统一验证清单](E6_UNIFIED_VALIDATION_PLAN.md)。本片实现设施而非跑验收：CodeGen.Tests首轮Debug10.40s、审阅后Debug4.23s，最终同源Debug10.83s/Release10.39s，全部零警告/错误、终态，仅编译；测试、发布、GUI、IPC、性能均未运行。7份改前文件、审阅前设施源、完整日志和局部哈希保存在`out/verification/ecs-e6/pre-codegen-b4b-first`。正式EXE未变，无部署/提交/推送。

**下一步：整个E6统一验证**，先准备当前双配置全部依赖并核对完整入口，再运行A/C/B及邻接回归；保留失败、修复后重测，不把历史编译绿色复用成验收。E6尚未验证通过，E5-D继续延期。

### B4-A 编译宿主、跨程序集身份与入口（历史，实现待统一测试）

**宿主合同**：根目录`global.json`固定SDK **9.0.315**（不自动roll forward、不用预览）；`managed/Directory.Build.targets`在构建/编译前要求Core MSBuild及同一SDK版本。从任意工作目录启动`Build.ps1`都会在仓库根运行并在退出时恢复目录。产品仍是net8，生成器及其专用测试为net9；没有将Player/游戏运行时升级为net9。全框架VS MSBuild不属于支持宿主；Rider等应选仓库固定的dotnet/Core MSBuild。SDK升级须同时审核global.json、宿主守卫及编译器合同，不能隐式漂移。

**身份检查**：每个成功生成的组件附带程序集级`GeneratedComponentIdentity(TypeId,Version,Type)`声明。编译器仅读取Roslyn元数据，不加载组件程序集/执行组件构造，不建立运行目录。检查引用图中的manifest，并按程序集身份去重，允许菱形引用；本地与引用冲突、仅引用之间冲突均报`NCMA007`，不同schema版本也不能共享同一TypeId。畸形manifest、外来类型、重复类型或与原显式声明不符报`NCMA008`；原本地重复仍为`NCMA002`。生成器依赖CompilationProvider，引用替换会使检查失效重算。

**覆盖边界**：统一targets给managed下所有普通工程直接接入analyzer-only引用，包括没有本地组件声明的Editor/Player组合工程；移除四个重复手写ProjectReference。生成器与其专用测试排除该自动引用以免循环。仅覆盖当前编译器可见、带生成manifest的声明；不声称穷尽任意手写注册函数、未引用的动态插件或旧二进制。原ComponentRegistry的重复ID/类型拒绝、加载及权限检查仍是运行期最终边界，不新增兼容目录、自动扫描注册或AI能力。

**依赖与测试入口**：构建ResolveReferences及发布ComputeFilesToPublish后拒绝普通工程的Ncma.CodeGen/Microsoft.CodeAnalysis引用/文件；生成器和CodeGen.Tests本来需要编译器依赖，明确例外。此守卫是构建卫生，不是恶意改名DLL的安全扫描。`Build.ps1`补上CodeGen.Tests的构建、原生测试排除及托管测试包含；CMake测试固定仓库工作目录。没有执行Build.ps1、CTest或发布/部署脚本。

新增5组CodeGen用例**登记未运行**：跨程序集本地冲突（含不同version）；无本地声明的组合冲突与引用顺序稳定性；菱形/相同叶类型名不同ID正例；畸形manifest拒绝且不崩溃；引用替换/移除/恢复后的增量driver与干净生成一致性。这些是编译器内存fixture，**不是已经完成的真实MSBuild增量/发布验证**，原有用例及阈值不变。

本次仅编译与静态检查：CodeGen.Tests Debug **11.10s**、完整Services.Tests含正式Editor Debug **41.71s**、完整Player.Tests含Player Release **34.42s**、Gameplay.Sample Debug **9.86s**，全部零警告/错误且终态。PS/XML/JSON静态解析正常；检查上述隔离Editor/Player/Sample输出及deps，没有CodeGen/Roslyn运行依赖；已检查四个真实组件生成manifest。这不代表测试、GUI、实际发布或增量失效验收。13份改前原文与完整编译日志、局部哈希保存在`out/verification/ecs-e6/pre-codegen-b4a-first`，非全源码冻结。样例未安装到out/managed；正式EXE C50742AD未变，无部署/提交/推送。

**下一步B4-B**：补齐真实MSBuild干净/增量/声明删除/引用替换产物矩阵、错误宿主与编译器依赖泄漏负例、隔离发布依赖及总体验收入口审计；先完成这些待运行验证设施与剩余实现，再在整个E6末统一运行所有测试。B4尚未整体收尾，E5-D继续延期。

### B3 消费一致性审计收尾（历史，实现待统一测试）

本次补齐两个实际消费缺口：B3-B组件仍只有JSON控件，Export控件没有使用目录标签且固定±1000范围。并发现脚本目录generation不属于EditorViewStamp，需要在事件提交前单独检查。没有重写持久化格式、扩大AI授权或移除现役扩展入口。

| 消费链 | 唯一声明来源/当前实现 | 审计结论及边界 |
| --- | --- | --- |
| 组件生成注册、typed及调度访问 | Generated.Register → 原ComponentRegistry；Generated访问 → 原World/ScheduleContext | 原TypeId/version/schema/验证；生成声明不授予权限，未改造系统不冒充任务 |
| 作者保存/加载、Prefab和Bake | SceneDocument.NormalizeComponents → Registry.Decode/EncodeObject/ValidateEncoding；原Codec与原事务 | 同一注册规则；JSON保存不加入FieldId/Metadata，Bake仍为独立运行实例边界 |
| Transform与代表性组件Inspector | 原ComponentMetadata.Field/ReadSerializedField | Transform保留熟悉布局；其他组件仅在全部叶字段为number/integer/bool且<=16时生成完整表单；稳定FieldId+TypeId/version定位，不从顺序猜字段 |
| 不支持表单的组件 | 原完整分页JSON；没有声明就不猜字段/默认值 | 非标量/超过16项/无元数据不截断，不是旧格式兼容；完整组件编辑仍受原schema/校验 |
| 环境/后处理/阴影/UI/动画专用面板 | 现役类型化领域值、模块目录和原事务/批准流程 | 资产/模块/跨字段语义不能由简单字段标记替代；未虚称全部面板自动生成，也不绕过领域批准 |
| 组件AI类型页 | 原Registry descriptor + 显式7项字段投影 | 分页及252KiB+4KiB封套不变；不返回元数据默认值/场景值，Editable/RuntimeWrite不是授权 |
| 脚本加载/作者绑定/闭包 | 原ScriptCatalog核对生成公共Export → EditorSessionOwner.Describe深拷贝 → 原EditSession/ScriptBindingClosure | 同一Name/Kind/标签/默认值；持久化名字不改；默认读取仍可执行受信构造/getter，不是无副作用沙箱 |
| 脚本Inspector/Attach | 原ScriptCatalog按TypeName+Name+Kind匹配，动作携带catalog generation | 标签使用Category/DisplayName；缺失/Kind不符显示不可用；事件先核对当前目录、上帧目录和Edit目录，阻止刷新前旧事件及旧draft |
| 脚本AI类型页 | 既有ncma.engine.behaviour_types → 同一Edit目录快照 | 原公共默认值本来就返回，保持行为，不与组件字段页的无默认值承诺混淆；不返回私有ElapsedSeconds或live脚本对象 |
| Play运行检查 | 原受批运行页/Entity代际/World/Play/共享受众与到期守卫 | 本次未改动，声明/作者表单不是运行读写权限 |

新组件数字控件使用JSON数值文本，不凭元数据猜min/max，不把整数转double再写回；integer要求可表示的精确int64/uint64数字文本，number有限，bool仅0/1。整组件JSON仍经原手势草稿、完整验证、单次Commit及Undo/Redo。Editable只限制此控件，不能替代原完整组件API的授权规则。字符串、数组及更多字段继续完整JSON入口。

Export数字控件使用不依赖区域设置的文本与原float/double/int/bool验证，不再固定±1000或把1.5静默取整。脚本持久化值本来是double；保留该合同，不声称任意精度脚本数字。原每页8绑定/16Export限制不变。目录换代在Apply中检查，旧操作经过共享异常路径取消draft；附加脚本也检查目录一致性，不授予新的live访问。

新增4组Services审计用例登记未运行：真实Inspector与component_types路径一致、uint高位/范围外原合法数值/非法小数/checkbox/Undo/字节往返；无元数据JSON与Editable/冻结/旧事件；真实脚本Inspector与behaviour_types显示/默认合同、整数上限/非法小数/历史；目录替换在下次Build前拒绝Export及独立Attach事件、显式刷新后的正例与缺失声明。原用例/负载/阈值不删改。

完整隔离Services.Tests含正式Editor Debug初次 **41.52s**、补齐AI/独立Attach断言后最终 **10.64s**，均0警告/0错误且终态。未运行测试/GUI/IPC/冒烟/性能，编译不构成功能验收。记录 `out/verification/ecs-e6/pre-codegen-b3-audit-first` 保留9份改前原文（含未改的既有metadata fixture）、审阅前新增测试源和局部哈希，非全源码冻结。正式EXE C50742AD未变，无部署/提交/推送。

**B3在上述明确边界内实现收尾；下一步B4构建宿主与跨程序集冲突、干净/增量/失效负例、发布依赖和整体测试入口闭环。测试仍仅整个E6末，E5-D延期。** 全组件/全脚本迁移、继承及非数值Export生成、所有面板自动生成、无反射启动、零分配及全字段安全策略不在B3已完成范围。

### B3-C 脚本Export生成与原目录接入（历史，实现待统一测试）

本片以现有公共 `[Export]` 为唯一成员合同，显式 `[GenerateExports]` 选择生成访问。**成员名继续是原持久化身份**，不改绑定JSON/Kind、目录响应或闭包哈希内容结构；没有把组件FieldId强加到脚本导出格式。

- 新增增量 `ExportGenerator` 和 `NCMA101` 诊断。首批支持public sealed partial、顶层非泛型、直接继承Behaviour、有public无参构造器且无required成员的类。只生成public实例可写float/double/int/bool字段/属性；拒绝private/static/readonly/init/ref/indexer、继承/嵌套/泛型等不支持形状，不静默漏掉声明。类型名512字节、成员/标签/类别128字节的既有UTF8上限保持，声明最多1024项，与原绑定容量一致。
- 生成 `IGeneratedExportSource` 显式实现，提供冷启动数组及无实例捕获的typed Read/Write委托。`ExportNumbers` 复用原finite/float溢出/int整数范围/bool0或1约束；getter/default仍可能执行用户代码，不声称纯函数/零分配。没有生命周期派发器、自动注册副作用、平行目录或全局脚本类型缓存。
- 原ScriptCatalog在原候选加载/busy范围内创建原型，冷反射核对完整公共Export集合的Name/Kind/DisplayName/Category，再使用生成getter取默认值，实例绑定用生成setter。缺marker或实现、缺失/重复/额外条目和元数据不符均拒绝；显式生成类型失败不退回反射。普通未声明生成的当前Export扩展仍沿原路径运行，不把它称为废弃兼容入口。
- 原快照、ScriptBindingClosure、构造/候选切换/外来candidate拒绝/owner-thread/重入/lease/collectible unload流程保持；原PropertyInfo异常包装保留。委托随目录候选所有权释放，没有进程级委托缓存。它们是受信本地脚本装配API，非live Inspector/AI写能力，不暴露私有状态。任意受信构造器/访问器的外部副作用不保证回滚，未实现脚本沙箱。
- 实际 `Ncma.Gameplay.Sample.RotatorBehaviour` 的Clockwise、DegreesPerSecond、Multiplier迁移；ElapsedSeconds保持非Export。默认值、成员名、标签/类别、旋转逻辑不改。示例项目仅加入分析器引用，产品仍net8；Inspector/AI沿原ScriptCatalog快照消费，没有新的capability或写权限。

新增4组生成器用例已登记未运行：四种类型的生成/编译/动态实例值与非法数值拒绝、私有字段不进入输出、不支持形状/元数据预算诊断、同driver修改/移除/失效与确定性/空类。新增3组Services目录用例已登记未运行：真实生成示例目录/默认/绑定/私有状态缺席、非法kind/范围/成员与线程、外来/弃用/重载candidate和lease边界。已有目录卸载、Play与绑定业务用例保留，正式测试仍等待整个E6末。

完整隔离Debug编译 `CodeGen.Tests` **10.15s**、`Gameplay.Sample` **9.64s**、`Services.Tests`含正式Editor **39.31s**，均0警告/0错误并已终态。只静态检查生成源码（仅三个公共Export）及Sample/Editor运行依赖（无CodeGen/Roslyn），不代表动态生成测试、实际目录加载、GUI/IPC、卸载回收或性能通过。记录位于 `out/verification/ecs-e6/pre-codegen-b3c-first`，10份改前文件及选定哈希，不是全源码冻结。示例只构建到隔离目录，未覆盖out/managed；整体E6测试前必须按正式测试流程重建示例。正式EXE C50742AD不变，无测试/冒烟/性能/部署/提交/推送。

**本片结束时下一步（历史）：B3序列化、其他Inspector/AI与组件/脚本声明消费一致性审计；然后B4构建与完整门禁闭环，再整个E6统一测试。E5-D继续延期。** 最新审计见上方。继承/泛型/非数值Export生成、脚本新FieldId序列化、无反射装配及所有脚本迁移不在本片完成范围。

### B3-B 代表性组件与类型化访问（历史，实现待统一测试）

本片完成有现成合法默认值的三个实际组件，不代表所有组件、所有调用点或运行系统均已生成/迁入任务图。

- `CharacterData`（13字段）、`CameraData`（10字段）、`DirectionalLightData`（7字段）声明显式FieldId/CLR路径/JSON路径，在原CharacterComponents/RenderComponentRegistry调用生成Register；没有平行目录。原TypeId、v1、schema属性及required顺序、默认值、验证函数均保持，原持久化JSON不增加FieldId或元数据。
- Character仍为`runtimeAttachable=false`，字段用途为Persistent/Editable/RuntimeRead；相机/灯光另含RuntimeWrite。用途标记仅描述意图，不是授权；生成Write依旧调用原World.Set，物理域配置冻结、唯一提交、候选步、线程、generation、只读等实际边界不变。没有依据标记放宽写权限，也没有宣称所有标记都是字段级安全限制。
- 正式CharacterPlayRuntime的角色启动读取、RenderSceneExtractor相机和主方向光读取改用生成类型化访问；生成Query只能冷建并缓存，现有缓存/组合查询和数值提交保持原实现。ReadAccess/WriteAccess/Channel/ReadScheduled/WriteScheduled保留原图编译与受检context；只新增对应验证用例，不把未改造业务系统宣称为调度任务。
- Characters与Scene.Rendering显式引用仅构建用分析器，仍net8产品，SDK9/net9生成器宿主。两次独立编译产出的3份生成文本逐字节相同；Editor/Player运行deps与输出目录没有CodeGen/Roslyn依赖。此处不是完整增量失效或部署验收，B4继续负责。
- FollowCamera、mesh等需要对象/资产上下文的类型不伪造UUID默认值；Health当前没有原默认声明，含字符串类型也不强行放进unmanaged子集。继续原手写注册，元数据明确缺失，不恢复旧兼容类型。

新增5组Scene.Rendering用例登记未运行：独立原schema配方/默认值/字段一致性；typed校验/只读/线程/foreign/generation/query失效；原调度提交/跨阶段拒绝/过期context；角色运行挂载拒绝及原子回滚；作者字节往返、显式bake隔离与真实渲染提取数据。原有负载/阈值/业务测试未降低。

完整隔离Debug编译：首轮Scene.Rendering.Tests失败1处CS1503（新增用例调用不可见内部ReadOnly，31.54s），保留精确失败源码/完整日志；改用公开固定步提交观察者触发真实只读边界，无新增friend/API。第二轮完整Scene.Rendering.Tests **9.23s**、完整Services.Tests含Editor **39.28s**，均0警告/0错误、全部终态。只做编译与源码/产物静态检查，未运行任何测试/冒烟/GUI/IPC/性能。11份改前原文及选定源码/日志/产物哈希见 `out/verification/ecs-e6/pre-codegen-b3b-first`，不是全源码冻结。正式EXE C50742AD未变，无部署/提交/推送。

**本片结束时下一步（历史）：B3-C脚本Export生成声明与原目录消费；补齐序列化、其他Inspector和AI消费一致性审计；然后B4及整个E6统一测试。E5-D继续延期。** B3-C最新实现见上方记录。

### B3-A 首批消费方（历史，实现待统一测试）

本片只完成 Transform Inspector、序列化默认值和既有类型查询的字段声明页，不代表全引擎/脚本迁移。

- ComponentRegistry 新增按 TypeId 的直接 Describe/TryDescribe 以及 DefaultSnapshot（要求精确版本和已有元数据）；无平行目录。默认快照来自注册时的规范化默认值，应用仍须原命令/权限/验证。没有改场景保存格式、写入 FieldId 或绕过完整组件校验。
- ComponentMetadata 在构造时建立只读 FieldId 索引和 JSON 路径段缓存。Field/ReadSerializedField 只读调用者已有JSON，不读取World、不验证或授权整个payload；没有逐字段 LINQ/反射/路径Split。交互写JSON仍走原冷事务路径，未声称整个Editor零分配。
- 正式 Inspector 的 Transform数值/标签/Editable标记来自原元数据，UX仍按10项熟悉顺序排列；动作携带FieldId，不再由数字索引猜测JSON组/坐标。修改完整组件JSON后进入原Begin/Update/Commit交互事务、原stale/frozen/权限/Undo/Redo边界，不引入运行写回。局部Editable检查只限制这个控件，不是安全授权，也不宣称禁止原完整组件API修改该字段。
- Add Transform 使用注册默认快照，不再每帧硬编码Identity再序列化。没有默认声明时明确显示不可用；没有字段声明的现有手写组件仍使用原完整JSON编辑入口，不猜测FieldId。
- 既有 `ncma.engine.component_types` 保留ReadOnly风险与原宿主接入，无新增capability。显式提供typeId才返回 `fieldPage`，offset>=0、默认16/最多32项；offset/limit必须伴随typeId，未知/非字符串typeId或额外输入拒绝。无typeId仍为原目录/schema查询。
- 字段页只投影FieldId/JsonPath/ValueKind及4个用途布尔值，标记available、metadataVersion、总数/偏移/返回数/下一页/完成状态、defaultsIncluded=false、usageIsAuthority=false。不序列化带默认值的字段对象，不返回默认快照、场景/Play值或私有脚本状态。已有原schema文本照常返回，其中原有schema注释不被重新定义为秘密过滤。
- 原252KiB数据+4KiB封套预算保持，描述/输入/输出schema同步修改；原元数据JsonIgnore保持。权限配对/批准/传输协议入口未改，编译成功不是实际MCP/IPC或GUI通过。

新增4组Core+2组Presenter用例已登记未运行：分页一致性/无默认或场景值、闭合输入/旧会话和revision/只读不授写、序列化默认值原事务及往返/Undo/Redo、缺声明诚实返回；真实Presenter自定义默认值、FieldId映射改变后仍读写同一字段、陈旧/冻结事件及历史。保留原负载与阈值。

完整隔离 Debug 编译 Core.Tests **13.22s**、Services.Tests含Editor **40.67s**，均0警告/0错误、终态成功；较早Core编译12.71s为中间源。新查询schema源码经JSON静态解析；12份改前原文复核、局部产物/日志哈希在 `out/verification/ecs-e6/pre-codegen-b3a-first`，不是全源码冻结。未执行测试/冒烟/GUI/IPC/性能；正式EXE C50742AD未变，未部署/提交/推送。

**本片结束时剩余B3（历史）：B3-B代表性引擎/游戏组件与typed访问迁移；B3-C脚本Export生成声明/原目录消费；再完成序列化、其他Inspector及AI声明消费的一致性审计。** B3-B当前进度见上方最新记录。之后B4构建闭环，再整个E6统一测试；E5-D延期。

### B2 字段元数据合同（历史，实现待统一测试）

`ComponentField(FieldId, MemberPath, JsonPath)` 明确声明每个序列化字段及中间 object 路径。FieldId 是**组件 TypeId 内的稳定标识**，不从 CLR 名称、JSON 名称、反射顺序或声明位置计算；改名时由开发者保留 ID，删除/复用 ID 的历史迁移检查尚未实现。Transform 已声明13项，包括 Position/Rotation/Scale 与其数值子字段，原 `ncma.transform` v1/schema 未改。

- 唯一注册入口仍为 ComponentRegistry.Register；生成代码提供定义数组与默认工厂。元数据附着原 ComponentDescriptor，不新增第二目录。手写注册未提供声明时 Metadata=null，不自动猜测 ID。Metadata 标记 JsonIgnore，避免原外部响应无意扩大；Inspector/AI 明确投影消费留 B3。
- Flags 包含 Persistent/Editable/RuntimeRead/RuntimeWrite；默认 Persistent+RuntimeRead。当前只描述原持久化序列化字段，拒绝 Persistent=false、未知位及 Write无Read；不引入“隐藏字段仍被保存”的假过滤。标记不是权限或安全隔离，也尚未接通字段级写入限制；原能力/原事务/World/调度检查继续有效。
- 生成器和运行注册共用有界 schema 规则源码；NCMA006 拒绝缺失/重复ID、错误路径/类型、未完整覆盖、未知标记、开放object、required不匹配、重复schema键、非法本地引用及不支持的schema关键字。显式 schema 仍由类型作者维护，不宣称已经从字段自动生成整个 schema。
- 每组件最多128项（含object路径），FieldId 1..128 ASCII字符，路径最多256字符、CLR展开深度16，JSON/引用深度32、原schema/default 64KiB上限保留。支持原camelCase字段映射、数值/布尔/字符/Guid、枚举、显式列出的 Numerics 值类型及简单用户值类型组合；非公开setter、readonly字段、自定义JSON属性、隐式框架转换器/集合等失败关闭，不冒充完整STJ支持。
- 注册先复制并完整检查字段定义，之后才调用默认工厂与原验证函数。默认值规范化后按原schema验证；私有Prepared元数据再捕获不可变JSON快照，各字段共享快照视图而非全值重复克隆。DescribeFields 是冷路径新数组；注册元数据不可通过它或只读Fields集合修改。
- 注册回调期间拒绝重入注册或构造World冻结目录；异常不发布类型，正常返回才填入原双索引。默认工厂/验证函数仍是可信本地代码，外部IO/其他对象副作用不承诺回滚，注册目录不因此变成多线程容器。Generated.Default 也经原验证函数；目录保存注册当时的快照，不声称有副作用的工厂每次返回同值。
- Runtime新增4组、CodeGen新增4组用例已登记未运行：默认值/只读隔离/原wire不扩展、调用者数组与规范化、错误预算/路径/schema/标记先拒绝且不发布、重入/冻结；编译诊断、字段改名保ID及使用标记失效、隐式转换器/readonly字段拒绝。B1用例沿用，仅正例声明补齐闭合schema/显式字段。

最终完整隔离 Debug 编译：Runtime.Tests **7.40s**、CodeGen.Tests **7.65s**、Editor.Services.Tests含Editor **38.85s**，均0警告/0错误。中间各1处测试源码编译错误（原始字符串插值括号、泛型推断）已修正，失败原文/完整日志保留。静态核对原Transform schema未变、两份干净生成文本一致、Runtime没有生成器/Roslyn运行依赖；这不是功能/增量失效/性能验收。

证据 `out/verification/ecs-e6/pre-codegen-b2-first`：9份改前原文，另2份项目文件重建改前内容并与B1精确哈希核对；中间源码/失败日志/选定产物固定，不是全源码冻结。正式EXE C50742AD未变，无测试/冒烟/GUI/IPC/性能/部署/提交/推送。**下一步 B3 消费方迁移，然后B4，整个E6末统一测试；E5-D延期。**

### B1 已落地合同（历史，字段边界以上方B2为准）

- `Ncma.CodeGen` 实现 Roslyn `IIncrementalGenerator`。仅处理明确的 `GenerateComponent` 声明，无运行时程序集扫描/自动注册；为同编译单元重复 ID 诊断使用冷 Collect，不声称逐类型生成完全独立。
- B1 范围是顶层、非泛型、readonly partial、unmanaged 的 IComponent；递归拒绝 Entity/地址/指针字段。托管字段、脚本 Export 和字段级元数据留到后续合同，不默默降级。
- 显式 TypeId、正 schema 版本、schema、验证函数和默认属性；NCMA001–005 错误阻断无效身份、重复 ID、不支持类型、错误成员、无效/超 64 KiB schema。当前仅检查 schema 根 object/JSON 深度32/字节预算，**未生成 FieldId，未做全部字段/schema 一致性诊断**。
- 生成嵌套 `Generated`：注册、默认值、World 读/写/查询、ScheduleAccess 读/写、通道、ScheduleContext 读/写。均调用原受检 API；查询创建是冷操作，应缓存原计划；没有裸池引用、反射热路径或新目录。
- `TransformData` 为第一真实迁移类型。原 `ncma.transform` v1、schema、Identity、Validate 保留，`ComponentRegistry.CreateDefault` 必须调用生成注册，没有手写回退。schema 文本仍显式声明，不宣称由字段自动推导。
- 顺序确定的生成输出，hint 名由全限定类型 SHA256 限长；生成文件只在 obj 中，不提交生成快照。声明删除/更名/改 ID 失效用例已准备，尚未执行。
- 编译工具暂以本机 .NET SDK 9.0.315 的 Roslyn 4.14 为准；生成器及其测试宿主 net9.0，产品 Runtime/Editor 仍 net8.0。SDK Roslyn 仅为编译引用，Runtime 项目以 Analyzer + `ReferenceOutputAssembly=false` 引用；无代码生成器运行依赖。尚未承诺 SDK8、其他 SDK/全框架 Visual Studio analyzer 宿主兼容；B4 需正式固定/验证工具链。

### B1 统一测试登记（本轮不运行）

- Runtime.Tests 新3组：注册身份/默认/冻结、World 验证/只读/线程/跨World/失效Entity、生成调度访问/真实提交/过期Context及跨阶段拒绝。
- CodeGen.Tests 新6组：正例生成后的编译、源顺序确定性、同driver变更/无效化/删除、重复ID、身份/schema、类型/嵌套Entity、成员签名负例（部分合并在同组）。CMake `NcmaManaged` 构建目标和 `NcmaCodeGenTests` 入口已登记，CMake 尚未重新配置/执行。
- 保留既有 E6-C 严格0B负载和阈值；本片不新增0B/整体性能通过声明。生成访问仍须在整体 E6 验证时覆盖权限/生命周期/冷启动与完整 Editor/Player 消费链。
- 初次构建失败为 SDK Roslyn/net8 引用版本冲突，第二次为 Attribute.TypeId 命名冲突；原失败源码和完整日志保留。修正为编译宿主 net9、属性 `ComponentTypeId`，不隐藏系统 Attribute 成员。

本片最终完整隔离 Debug 编译：Runtime.Tests 7.44s、CodeGen.Tests 7.70s、Editor.Services.Tests（含Editor）41.69s，均0警告/0错误、终态成功。静态核对原 Transform schema 文本未变，两份干净编译生成文本相同，Runtime deps/输出无生成器或 Roslyn 运行依赖；这些不是用例执行或完整发布依赖验收。

证据目录：`out/verification/ecs-e6/pre-codegen-b1-first`（8份原文复核，局部源码/产物/日志固定，不是全源码冻结）。正式 EXE SHA256 C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61 未变，不运行测试/冒烟/GUI/IPC/性能采样，不提交推送。下一步 B2。

## E6-C 完整实现合同（2026-10-24，实施收尾、统一测试待执行）

故障来源退役修正（2026-10-24，实现/未测试）：`WorldRunner.ResetFault()` 从 `if(IsFaulted)` 改为 `if(IsFaulted && _scheduleFault)`，使用既有 `scheduleFailed && World.Tick==stepStartTick` 分类。只有调度源提交前故障退役活跃图；普通System在调度前抛错、任务完成后外层提交前失败、提交后observer故障不因此退役健康图。仍清累积器、故障标记和LastError，非阻塞收集已有退休资源；Play.RecoverScheduleFault资格不放宽，ClearSchedule/Stop/Reload显式退役不变。提交后故障保留已提交步，私有回调状态/IO不回滚，不自动恢复或重放。

新增2组已注册未运行用例：串行/双worker × 调度前System/调度后提交前/提交后observer，检查原图引用保留、零新增回收债务、失败步World和资源提交状态、Reset后原图可继续；真正调度异常仍退役、旧执行器不能重新挂接且不重放。原超时worker隔离/回收测试保留。完整隔离 Debug 编译 Runtime.Tests 5.52s、Gameplay.Tests 25.11s、Editor.Services.Tests（含Editor）44.36s，均0警告/0错误、终态成功，非功能验收。证据 `pre-schedule-fault-origin-first` 保留4份原文、完整日志及局部哈希，非全源码冻结。测试仍在整个E6末统一运行，E5-D延期；正式EXE不变，无部署/提交/推送。

跨阶段组件读取编译拒绝（2026-10-24，实现/未测试）：`ScheduleGraph` 合同升为v3，规范化声明校验后、构建阶段锚点/边前，用两遍声明扫描建立每种组件最早写阶段并检查后续访问。若同组件存在 `Write@P` 且后续任务 `Read@Q`（Q>P），抛出 `cross_phase_component_read_requires_resource`，附组件、writer/reader任务及阶段，提示显式 `Resource + ReadOutput`。现有Write包含Read权限，因此后阶段Write声明也同样拒绝，不能靠改声明模式绕过。同阶段快照读取、跨阶段纯Read、先Read后Write及独立Resource命名空间保留。显式Before/After不豁免此检查；不按实体范围细分，也不引入阶段中间提交或自动组件同步。合法图哈希含新合同版本，无v2兼容分支。

新增2组图用例覆盖Read/Write后阶段拒绝、阶段间隔/显式依赖/输入排列稳定诊断及允许边界；新增1组串行/worker执行用例验证组件生产者通过资源输出向后阶段传值，ReadResource仍读输入、ReadOutput读前驱输出。原跨阶段组件冲突正例改为资源等价例；密集图保留原128任务、8128/8129边与8192预算断言，并新增跨阶段组件拒绝，不缩小负载掩盖失败。全部已注册未运行，测试仍在E6整体末尾统一执行，E5-D延期。完整隔离 Debug 编译 Runtime.Tests 5.52s、Gameplay.Tests 25.72s、Editor.Services.Tests（含Editor）45.13s，均0警告/0错误、终态成功，非功能验收。证据 `pre-schedule-cross-phase-first` 保留5份原文、完整日志及局部哈希，非全源码冻结；正式EXE不变，无部署/提交/推送。

本轮新建对象写入预检（2026-10-24，实现/未测试）：所有任务完成后、任何普通/结构 staging 前，先收集整轮 `Create` UUID 集，并按原合并128结构操作上限计数；随后普通组件写集若命中新建集合，立即抛出 `scheduled_write_to_created_object`，提示使用 `Add<T>` 结构操作，附任务ID、UUID和组件类型。集合先完整收集，writer排在creator之前或之后都按同一规则拒绝。合法 `Create + Add<T>` 不属于普通组件写，不被本规则误拒；原唯一提交/冲突/权限/代次和预算不放宽。

源码事实限定：当前公开 `Write<T>(index)` 只能写捕获时存在的行，全新UUID的 `Find` 返回-1，直接Write通常先被行号检查拒绝；未声称公开API已复现“未知实体”路径。新增3组待运行用例：内部受损输出注入（未开放产品接口）在串行/worker及creator前后顺序下均命中明确诊断，验证所有Stage探针为0和World字节/tick/revision不变；公开Create重复UUID+现有行Write的合并预检；跨任务合法Create+Add初始化成功。用例已注册未执行，E6末统一测试，E5-D延期。完整隔离 Debug 编译 Runtime.Tests 5.73s、Gameplay.Tests 27.66s、Editor.Services.Tests（含Editor）44.82s，均0警告/0错误、终态成功；编译不等于功能验收。证据 `pre-schedule-created-write-first` 保留4份原文、完整日志及选定哈希，非全源码冻结。正式EXE不变，无部署、提交或推送。

诊断面板索引与分页修正（2026-10-24，实现/未测试）：统一 `SchedulePageSize=8`，任务/边偏移上限分别为 `ScheduleGraph.MaxTasks-SchedulePageSize`、`ScheduleGraph.MaxEdges-SchedulePageSize`，移除120/8184硬编码；上一页、下一页可用性及页面读取同用该页大小。显式刷新构造 `SchedulePanel` 时一次性创建 Ordinal `Dictionary<string,ScheduleTaskReport>`；每行 `TryGetValue`，不再 `report.Tasks.FirstOrDefault` 扫描及创建捕获谓词。索引随快照失效/关闭丢弃，缺失报告仍显示未执行/N/A，不自动读取运行中数据。新增实际Presenter用例覆盖128任务全页/首尾禁用、相同快照重复绘制/翻页复用同一索引、刷新替换索引、Tick失效及只读文档不变，已注册未运行。只优化任务报告查找；原图任务/边副本及界面字符串等仍有分配，不宣称整个面板零分配。完整隔离 Editor.Services.Tests（含Editor）Debug编译36.50s，0警告/0错误、终态成功；证据 `pre-schedule-panel-index-first` 保留4份修改前原文、完整日志及局部哈希，非全源码冻结。本轮无测试、部署或提交推送，正式EXE不变，E6末统一测试/E5-D延期保持。

任务字节诊断（2026-10-24，实现/未测试）：`ScheduleTaskReport` 增加可空 `Bytes`、`AllocatedBytes`，本地只读诊断面板在每个任务行与 `timestampTicks` 并列显示；未执行/报告快照时未完成/测量不可用显示 N/A，不伪装成0B。没有兼容旧构造签名或新增远程权限。

- `Bytes`：当前任务已被预算接受的输出预留 + 资源复制读取字节。重复同一脏行不重复计费，重复资源读/写按原预算计费，拒绝配额不计入；共享列捕获容量不分摊到各任务，类型化读取不逐次计费。因此不是对象总大小、总读写带宽、堆分配或最终提交字节，失败任务可以有非零成功预留。
- `AllocatedBytes`：在实际 owner/worker 执行线程上，用 `GC.GetAllocatedBytesForCurrentThread()` 包围同步 `Context.Invoke`，包括回调、访问检查及异常路径的托管分配。排除执行前的列捕获/Context构造、队列、执行后的合并/报告及原生内存/子线程分配；不是整帧/整个调度器分配，也不是内存保留量。仅采样该线程计数，不使用进程全局差值、强制GC或新增NoGCRegion；异常负差值标不可用，不截为0。

数据先由执行线程完成，再通过 Done 发布，owner 只读取已完成指标。失败报告包含全部已派发任务；超时仍未结束的任务保留 `unfinished/null/null`，不读正在写入的Context计数。报告为当时冻结快照，旧worker随后退出不改写旧报告。原单次提交、故障/回收和面板显式刷新/失效隔离不变。

新增2组Runtime用例：串行/双worker、8步预热后空回调/Span输出读取/稀疏写严格0B与显式32KiB分配的分离、精确输出/资源复制配额；失败任务保留字节/分配但不提交，跳过任务无伪零报告。扩展原超时用例检查未知指标，并扩展实际Presenter用例核对未执行N/A、执行后同一行指标及口径说明；全部已注册未运行。完整隔离 Debug 编译 Runtime.Tests 5.59s、Gameplay.Tests 29.99s、Editor.Services.Tests（含Editor）43.41s，均0警告/0错误、终态成功。证据 `pre-schedule-task-metrics-first` 保留9份修改前原文、完整日志及选定哈希，非全源码冻结。这里提供运行时观测能力，不宣称已证实0B或整体性能通过；整个E6结束再统一测试，E5-D延期。正式EXE不变，无部署、提交或推送。

共享配额修正（2026-10-24，实现/未测试）：`ScheduleBudget.Charge` 改为 CAS 循环：先读取已用值，按剩余容量判定，通过后才原子增加；CAS 竞争失败重新读取。拒绝不写计数，不出现先超额再减回的瞬间污染；负数拒绝，0B允许，恰好达到原16MiB上限允许，构造时校验初始预留范围。使用减法检查余额后再相加，避免大额请求的算术溢出。成功预留仍按原 attempt 生命周期保留，不引入退费接口或失败任务自动恢复。完整隔离 Debug 编译 Runtime.Tests 5.46s、Gameplay.Tests 27.49s、Editor.Services.Tests（含Editor）44.31s，均0警告/0错误、终态成功；仅编译通过，不是功能/并发/性能验收。正式EXE未变，未部署、提交或推送。

新增3组待统一执行用例：连续4096次超额请求后仍可精确用尽余额、负数/极值/零额/初始预留边界；4路并发混合合法与非法请求，成功总额必须精确等于剩余8192B；共享预算下读/写超额不污染另一Context合法8B请求、拒绝读不改目标缓冲、失败Context继续保持sticky拒绝。这里验证的是共享计数隔离，不是失败后继续提交整帧；原调度图任一任务失败即失败、禁止部分提交的规则不变。用例已注册未运行，本轮仅静态/编译检查，E6整体末尾统一测试，E5-D延期。证据目录 `out/verification/ecs-e6/pre-schedule-budget-cas-first` 保留修改前原文及局部编译证据，非完整源码冻结。

UUID 行索引修正（2026-10-24，实现/未测试）：保留 `ScheduleContext.Find<T>(Guid)`，不再 `Array.IndexOf` 扫描。每列的两个输入 bank 各持有私有、可复用的 `Dictionary<Guid,int>`；Capture 受检扩容时预留容量，随后 Clear 并在原 UUID/值捕获循环中同步填充。查找平均 O(1)，连续 N 次查找平均 O(N)，而不是旧 O(N²)；不承诺哈希表最坏 O(1)。捕获/清空仍随行数或保留容量线性处理，不是整帧零分配结论。删除、组件移除、行号移动和同 UUID 新 generation 在刷新时更新；未刷新的另一 bank 保留自己的快照。缺失 UUID（含空 UUID）仍返回 -1，权限/线程/取消/Context 失效检查仍先经过 Column<T>，没有 live World 查询。

索引容量与列数组一起计入原保留16MiB预算；行保守估算由 `sizeof(T)+64` 改为 `sizeof(T)+128`，原 attempt 逻辑输入预算同步计费，上限不提高。这是保守载荷/索引计费，不是 CLR 总堆硬上限。只有 owner 在回调开始前重建，原超时 bank 租约规则保持，不在 worker 读取期间清空或跨执行器复用。

新增3组已注册未运行用例：4096行反向逐一查找、串行/worker三步、预热后查找严格0B；双bank独立/删除/组件移除恢复/同UUID换代/清空/预算拒绝；Find未声明/跨线程/失效拒绝。原4096行捕获+稀疏/密集写的严格0B用例及其次数/阈值保持不变，也尚未运行。完整隔离 Debug 编译 Runtime.Tests 5.54s、Gameplay.Tests 29.07s、Editor.Services.Tests（含Editor）43.29s，均0警告/0错误，全部终态成功；不等于功能或性能验收。测试仍留到整个E6结束；本轮不部署或提交推送，E5-D继续延期。证据目录 `out/verification/ecs-e6/pre-schedule-column-index-first` 保存5份修改前原文、完整编译日志与选定哈希，非全源码冻结。

列双缓冲与稀疏写修正（2026-10-24，实现/未测试）：每个执行器绑定一次 `EntityQuery` 与两组 `Entity/UUID/T` 列缓冲，按 attempt 交替使用；稳定成员/容量下刷新数组内容，不重建查询计划、数组或列映射。每组仅在首次使用/容量增长时受检扩容，逻辑 Count 与容量分离，Read/ObjectAt/Find 都限制在有效行；增删、同 UUID 的新 generation 与 World 替换分别按原查询成员版本/身份规则处理。**仍逐帧刷新所声明列的值，是 O(行数) 的快照读取，不是零拷贝或增量捕获。**

可写任务×列不再分配整列 Values/bool Dirty。每个 bank/任务/列冷创建一个可复用稀疏写器，使用行号→稀疏槽索引、紧凑脏行号及 T 值；首写新增行，重复写同一行覆盖该值；容量仅随历史脏行高水位增长。每步复位稀疏索引/数量，合并前按原行号排序，避免回调写入顺序改变发布顺序；目标枚举改为 Count/TargetAt，不创建 yield 迭代器。写入仍暂存，原冲突检测、代次校验、原生保留写权及唯一提交不变，Read仍观察输入快照而非自身写集。

预算分两层：原 attempt 的16MiB逻辑读/写预算继续计费；另外对执行器保留的查询成员缓存、两组列容量及全部稀疏写器容量统一限16MiB（不是每bank各16MiB）。扩容前 CAS 预留，超预算不扩容、捕获异常仍污染任务。类型化新增脏行的 `sizeof(T)+sizeof(row)` 也计入原单任务1MiB输出和全步预算，重复覆盖不重复计费。两层不能误称为整个进程只有16MiB：CLR/对象头、上下文、临时结构和扩容时旧数组等待GC等均不在硬堆上限承诺内。

未完成 worker 仍占用当前 bank，执行器拒绝下一次 Run；故障退役不把缓冲交给新执行器或全局池，只有 worker 退出后才释放内部持有。过期 Context 无法访问后续复用的数组，外部仅得到值副本，没有新增 live World/ref/Span 租借。原共享 completed 视图及序列号方案保留，不恢复整表复制。新增 `ReadResource(id, Span<byte>)`、`ReadOutput(producer, resource, Span<byte>)`：写入调用者拥有的目标区，返回有效字节数，容量不足/权限失败先拒绝、不部分复制。原 byte[] 返回版本仍明确分配隔离副本，资源输出 `WriteResource` 仍分配，不能以新重载覆盖这些成本。

新增6组待运行用例：4096行双bank与稀疏/全脏写器预热后局部8轮严格0B；成员增长/缩减/代次和World替换；4096行×64单行写者、串行/worker四步；保留预算拒绝；Span资源/前驱输出预热1024后4096轮严格0B及短目标原子拒绝；超时旧写器保留与新执行器四步独立缓冲。原类型化Read/Write0B用例次数/阈值不变，仅迁移冷构造。所有用例仅登记/编译，未测试；没有新增 NoGCRegion、缩负载或放宽0B。**完整调度步0B仍未达成**：Context/许可字典、完成表、CTS/定时器、任务完成对象、wave/LINQ、合并集合/报告/结构JSON及资源分配等仍须后续整体收敛，不能以这些局部断言代替验收。

编译记录：首次 Runtime.Tests 因局部变量与参数重名报3错误/0警告（2.71s），精确失败源码及日志保留；修正后完整 Runtime.Tests5.26s、Gameplay.Tests16.99s、Editor.Services.Tests（含Editor）40.89s，隔离 Debug 均0警告/0错误、终态成功。证据 `pre-schedule-sparse-columns-first` 为7份修改前原文、失败源码/完整日志/选定产物哈希，不是完整冻结或功能/性能验收。测试留到整个E6末尾；E5-D延期，正式EXE不变，无部署/提交推送。

完成结果视图修正（2026-10-24，实现/未测试）：移除每任务 `new(completed, StringComparer.Ordinal)`；每个执行 attempt 只创建一份 `ScheduleCompletionBuffer`（固定 Context/序列号槽位与一个 ReadView），任务ID→槽位使用执行器冷构造的 FrozenDictionary。每个 Context 仅持共享只读视图和启动批次的完成发布序列号上限，绑定开销不再随已完成任务数增长；完成表及每任务绑定合计为 O(T)，不再整表复制产生 O(T²)/帧引用条目。这里只描述完成表，不宣称整个调度步线性或零分配。

结果由 owner 在任务 Done 且成功后只追加一次：先写 Context，再 Volatile 发布该槽位序列号，最后更新全局完成水位。worker 通过 acquire 读取槽位序列号，普通可变字典不再一边 Add 一边被 worker 读取。`ReadOutput` 只检查原前驱权限、按 producer 定位槽位、判断 `0 < sequence <= Context cutoff`、查询该生产者资源键；不扫描/复制全表，不接受失败、未完成、之后才完成或无前驱关系的结果。每个 attempt 独立槽位，不跨帧清空复用；超时回调持有旧视图直至按原回收合同退出。这里的“完成发布序列号”不是 World 提交序列：输出仍暂存，只有原固定步唯一提交后才对 World/资源正式发布。

原声明/线程/Context失效/取消/ReadBudget保持；Context改为单次执行，不能在输出发布后再次运行修改字典。ReadOutput返回字节仍按原规则复制，保留数据隔离及字节预算，不因共享表暴露上游可变数组。新增4组用例：截止序列/返回值隔离/前驱拒绝、owner追加与worker读取、未完成/失败/重复/跨attempt/跨线程发布拒绝、128任务跨16阶段的串行/worker两步共享视图与新帧独立性。原严格0B类型化Read/Write用例仅迁移构造参数，次数/阈值不变；全部未运行，留到整个E6结束统一验证。

完整隔离 Debug 编译：Runtime.Tests首次5.35s、负例断言静态收紧后3.20s；Gameplay.Tests16.69s、Editor.Services.Tests（含Editor）38.80s，均0警告/0错误、终态成功。负例需真实捕获拒绝异常，不能以 Context 吞入任意测试异常冒充拒绝通过。证据 `pre-schedule-completion-view-first` 保留6份修改前原文、中间用例源码、完整日志及局部哈希；编译不等于功能/并发/性能验收。正式EXE不变，本轮无部署/提交推送，E5-D继续延期。

阶段图修正（2026-10-24，实现/未测试）：`ScheduleGraph` 合同升为 v2，阶段不再生成任务两两边。对 P 个非空阶段，仅建立 P−1 个 `$phase:<完成阶段>` 锚点；任务连向本阶段完成锚点，下一阶段任务等待该锚点，相邻锚点串链。每个锚点必须等全部入边完成，非“任意任务完成即放行”；空阶段跳过、单阶段/空图不创建锚点。阶段边数为 O(N+P)，预算仍为原8192，不提高阈值掩盖膨胀。128任务均分16阶段时，阶段边按结构由7680减至254、锚点15个（这是结构推导及待运行用例断言，不是实测性能）。当前128任务的合法旧任务DAG至多8128条唯一边，原问题在当前上限内并非已证实的8192超限；此修正消除冗余并为扩容留空间。

跨阶段正向 Before/After 仍保存在原声明与哈希中，由锚点链实现，不另建冗余边；反向阶段依赖直接拒绝，未知/自依赖/同阶段环仍拒绝。同阶段显式依赖及读写/结构冲突保持原语义；跨阶段冲突已由锚点保证。`CopyEdges()` 的端点现在可能为合成锚点，新增 `CopyPhaseAnchors()` 提供辨识；`CopyTasks/CopySerialOrder` 仍只包含真实任务。任务标识符不允许 `$`，调用者不能伪装/引用锚点；v1不提供兼容分支。

Executor 按紧凑直接入边放行任务、在 owner 上自动完成锚点，不创建锚点 kernel/Context/输出/worker槽，不在运行时重新展开阶段执行边。原冷构造 `ReadOutput` 祖先缓存继续穿透锚点保存真实任务祖先关系（最坏仍可为二次存储，不宣称整个编译器/调度器线性或零分配）；输入仍是原提交快照。前阶段任何失败不释放后阶段，同阶段独立 worker 可并行，唯一提交及回收合同不变。只读 Editor 面板辨识阶段完成锚点，不增加 AI 权限。

新增3组图+2组执行用例，覆盖128×16线性边/确定哈希/拷贝隔离、稀疏阶段/显式正反依赖、128同阶段8128边与127+1两阶段8129边、串行/worker同阶段并行与跨阶段输出祖先、失败不放行，均未执行。完整隔离 Debug 编译 Runtime.Tests5.40s、Gameplay.Tests21.88s、Editor.Services.Tests（含Editor）39.39s，均0警告/0错误、终态成功。证据 `pre-schedule-phase-anchors-first` 保留8份修改前原文、完整编译日志及局部哈希；不是功能或性能验收。测试仍在整个E6结束统一执行，E5-D延期；不部署、不提交推送，正式EXE不变。

超时恢复修正（2026-10-24，实现/未测试）：将执行故障与资源回收债务分离。`WorldRunner.ResetFault()` 不再等待 worker，永久退役故障图、撤销其提交资格和 Context，并可继续无图固定步。旧回调/缓冲/线程留在 Runner 的有界退役列表，`CollectScheduleResources()` 仅非阻塞回收；`WaitForScheduleResources()` 是卸载门禁，未退出抛专用 `ScheduleCleanupPendingException`，不是重新判定执行故障。每个 Runner 活跃图加未回收图最多4份（最多16个对应 worker），挂接时预留退役容量；满额禁止新图但不禁止恢复或无图运行。已退役执行器即使回收完毕也不能重新挂接，不自动重放失败任务。

Play 新增本地 `CanRecoverScheduleFault/RecoverScheduleFault`：仅允许原调度回调提交前失败、World身份/tick/revision未变且无耦合/组合生命周期的会话；恢复保留已提交 World，清空旧输入/命令回执、更新 Play SessionId 使旧授权及句柄作用域失效，回到 Paused。不得宣称回滚 Behaviour 私有字段、用户闭包或文件/网络副作用。原生耦合、普通 Behaviour 与提交后故障仍不可用此入口恢复。可以在暂停边界明确挂接新图。Stop/Reload 先退役并等待回收，再释放脚本/原生资源；待回收异常保持原 Faulted 或 Paused，不触发新的 coupled fault。Editor 只读面板分开展示执行状态、可恢复性及刷新时回收债务，没有新增恢复按钮、AI 授权或 MCP 接口。

新增3组 Runtime、2组 Gameplay 回归用例，另更新原超时用例：覆盖未退出时恢复、新图执行/迟到输出隔离、容量耗尽、World替换清理、Play epoch、Stop/Reload卸载门禁与耦合 fail-stop。均仅注册/编译，未运行；证据 `out/verification/ecs-e6/pre-schedule-recovery-first` 为局部原文/日志/哈希，不是完整源码冻结。测试仍在整个 E6 结束统一进行，E5-D继续延期，正式EXE不更新。

本次修正完整隔离 Debug 编译：Runtime.Tests 5.27s、Gameplay.Tests 14.30s、Editor.Services.Tests（含 Editor）36.57s，均终态成功，0警告/0错误；不能据此宣称超时恢复或功能测试已通过。

热路径修正（2026-10-24）：`ScheduleContext.Allowed` 原先每次捕获 `kind/id/write` 执行 `Access.Any`，确有逐行委托/闭包分配风险。现构造时一次性展开私有 `(Kind, Id) → Mode` 许可字典，Read/Write 校验仅查表，不再扫描声明或构造谓词；保持 Write 包含 Read、类别隔离及拒绝后任务失败。新增3组许可用例，其中正常GC下 owner/worker 各预热后4096次类型化访问严格0B断言，均未运行，不新增 NoGCRegion 豁免。完整 Runtime.Tests Debug 编译0警告/0错误5.27s，证据 `pre-schedule-permissions-first`；下方六工程结果属于该修正前源码。这里只移除已识别的热访问分配源，构造/捕获/资源复制/合并等仍分配，不能据编译或此局部用例声明整个调度步0B。

本节取代下方历史 C1-only 进度。C1–C5 的实现范围为：稳定声明/冷图、受检串行执行、原固定步接线、有界隔离工作线程、只读诊断与生命周期。下一阶段为 E6-B，测试仍留到整个 E6 实现结束；**实现完成不等于测试通过或性能达标**。

### 执行与数据合同

- `ScheduleGraph` v2 复用现有组件 TypeId/schema，以阶段完成锚点链保证跨阶段顺序；先解析明确依赖，再添加同阶段确定性冲突边。调用者得到深拷贝声明、锚点、依赖边、真实任务串行顺序和合同哈希。E6-B 生成同一合同，不建立第二套注册表。
- `ScheduleExecutor` 绑定一个真实 World 和一个 Runner。默认 `workerCount=0` 按编译顺序串行执行；显式选择 1–4 个固定后台线程，仅运行 Worker 任务，OwnerThread 任务不离开所属线程。
- `ScheduleComponent.Of<T>()` 目前只支持 `unmanaged IComponent`。固定步开始捕获声明涉及的类型化列和 UUID，不捕获完整场景 JSON，不提供 Entity/World/池引用。带引用的复杂组件需要先由 owner-thread 转成受限数据通道，不能绕开此限制。
- Context 校验任务声明、回调有效期和线程；即使调用者捕获异常，未声明读写、逃逸 Context 或通过闭包直接访问 live World/Query 仍使任务失败。任务只读原已提交值，自己的写集也不改变输入；显式依赖不会偷偷发布上游组件写入。`ReadOutput(producer, resource)` 仅可读取已完成前驱的不可变输出。
- 外部资源是复制的 byte 数据通道，不是物理求解器/原生对象句柄。Write 包含读；一个资源只有一个输出生产者，多阶段结果使用不同通道。组件输出和结构命令按任务固定顺序合并，同一 UUID/组件重复写、destroy 与其他写混用、旧 System 与任务写冲突均拒绝，而非完成顺序决定结果。

### 与正式固定步的边界

`输入/生命周期 → Behaviour 与原 System（owner-thread） → 受检任务图 → 原候选预检 → 原生耦合域执行 → 原唯一提交 → 提交后观察`

- 接入 `WorldRunner` 的原 `_executeStep`，因此 Editor 和 Player 的共用 `PlaySession` 自动使用同一个挂接点；未显式注册图时保留原有执行路径，不虚构任务。
- Play 中结构命令转入原 `RuntimeCommandBuffer.SubmitBatch`，保留 E2 验证、待提交句柄、绑定元数据与回执。`LastScheduleSubmission` 仅表示提交票据，是否提交必须查询原 `Commands.GetReceipt`。
- 类型化输出仍走原组件校验与原生组件写权限；声明 Write 不授予物理/动画宿主保留的写权。普通 Runner 使用 World 的原暂存结构入口，不产生第二个 World 或第二次提交。
- 资源结果以实际固定步 attempt + tick 的提交身份发布。提交前故障丢弃，物理/观察者在提交后抛错则保留该已提交结果并 fail-stop，不能把已提交 tick 伪装成回滚。
- Behaviour、输入/信号、编辑事务、动画/物理/渲染等现有系统仍是不可并行穿越的 owner-thread 边界；本片没有给任意旧 System 自动标记安全并行，也不把 Python/推理/IPC 放进固定步同步等待。

### 预算、取消与生命周期

- 冷图保持 128 任务、每任务 64 访问/64 显式链接、128 资源、16 阶段、8192 边。
- 计算通道与输出使用每attempt 16 MiB逻辑计费预算，单任务新增输出（含类型化新增脏行）1 MiB、单资源64 KiB；执行器另有统一16 MiB保留容量预算，覆盖查询成员缓存、双列bank及全部稀疏写器容量。结构输出合并后最多128操作、完整序列化64 KiB。两层预算都不是整个托管堆的硬上限：字典/线程/CLR/用户私有分配与GC临时峰值仍须在整体性能验证中另测。
- 队列最多 128 项；一次图执行采用共同 deadline（默认 2 秒，可配 1 ms–5 秒），Context 支持协作取消。超时取消并使 Context 失效；未退出的 worker、输入与回调仍由退役执行器持有，禁止复用或修改资源。执行故障可显式隔离恢复，资源卸载仍须 drain，二者独立。
- 不强杀托管线程；可信 owner-thread 回调若永久阻塞仍会阻塞 owner，协作 deadline 不是硬实时抢占/恶意代码沙箱，也不能撤销任意文件/网络等副作用。
- Stop/Reload 在卸载脚本及原生资源前先退役解除图绑定并 drain，失败保留所有权、要求显式重试。执行器由调用者拥有，Runner 保留未回收借用；解除绑定后 Dispose 仍可能因未退出而抛资源待回收异常，不会强杀线程。完整 World 替换使旧执行器失效，但仍允许安全解除/回收。

### 诊断及 E6 末尾验证清单

- Editor 场景、UI、动画图工作区提供“任务调度 / 本地只读诊断”：显式刷新，分页任务/边、声明、冲突原因、执行时间戳计数和故障；Play/World/tick/revision/状态变化隐藏旧内容。无图时明确显示未注册，不生成伪任务或性能值。
- 冷图/报告提供复制的只读数据，可供受信任工具适配；没有新增 MCP 注册、AI 批准或远程改图权限。当前诊断面板不是节点式调度图编辑器。
- 新增准备 16 组 Runtime、6 组 Gameplay、2 组 Presenter 用例，另有前片 8 组冷图用例，均未运行。覆盖声明/环/预算、串并等价、上下文/捕获 World、原结构回执、原生写权、超时保留/显式回收、Stop/Reload、提交前后故障及真实 Presenter 失效。
- 4096 对象 × 8 步的串/工作线程诊断用例保留原始耗时/全进程分配差值，标记 `historicalPerformanceAcceptance=false`；不是旧 E3/E5 配对合同、0B 或目标机性能通过。小场景和真实领域吞吐/尾延迟、完整宿主/GUI/IPC/双配置与资源长稳门禁仍在 E6 统一验证时执行。
- 本轮仅做静态及隔离编译；所有编译失败源码和日志保留在 `out/verification/ecs-e6/pre-schedule-execution-first`。正式 EXE 不更新，不部署/提交/推送，E5-D 延期债务不改判。

编译交付记录：Runtime.Tests 6.57s、Gameplay.Tests 15.02s、Editor.Services.Tests（含 Editor）55.84s、Rendering.Tests（含 Editor/Player）63.14s、Player.Tests 48.30s、Scene.Rendering.Tests 49.48s，全部完整相关工程与依赖隔离 Debug 编译终态成功、0警告/0错误。中间主构造参数捕获1错误、测试 Transform 值类型写法1错误、测试作者字节API2错误、诊断缺 Core 命名空间4错误（两个宿主构建各报4处）均修正，原源码/完整失败日志保留；不删失败、不重写为通过。新增24组及冷图8组未执行；只读静态归档校验重核16份原文，正式 EXE C50742AD保持不变。局部哈希记录不是完整源码冻结或功能验收。

## 历史：E6-C 优先实施分片（2026-10-24）

1. **C1 声明与冷图编译**：本轮已实现基础，未验证；只规划，不执行。现有注册表解析组件身份，未来生成代码复用相同合同。
2. **C2 受检串行执行**：owner-thread 执行作用域，组件/外部资源访问必须落在声明范围；禁止裸 World/池引用逃逸、跨 tick/await 或把声明当作写授权。未声明访问即失败，不能仅靠开发者承诺。
3. **C3 正式固定步接线**：梳理 Behaviour、输入、信号、结构命令、动画/物理及提交阶段；复用现有暂存、预检和唯一发布点，显式依赖不隐式发布上游写集；外部求解后异常仍 fail-stop。不能直接给任意旧 System 标记“安全并行”。
4. **C4 有界隔离工作线程**：仅接收不可变输入/受检任务输出，声明完整且可证明独立的纯计算任务允许并行；不移交 live World，队列/取消/故障有界；确定性合并后统一提交。owner-thread 任务仍留原线程。
5. **C5 诊断与收尾**：复制的任务图、冲突来源、执行/故障诊断、生命周期收敛；准备串并等价、访问漏报、环、取消、结构合并及性能用例，不在 C 小片提前运行。

C 全部实施后开始 E6-B：生成稳定身份、访问器、元数据和同一任务声明合同，检查无效声明与生成物失效。**全部 E6 实现结束后才统一运行测试、性能与实际宿主门禁**；C1 编译通过不代表 C 已完成，也不改变 E5-D 延期状态。

2026-10-24 顺序调整（用户最新决定）：**E6-A 实现收尾 → E6-C 声明读写依赖调度 → E6-B 元数据与访问代码生成 → E6 整体统一测试**。E5-D 继续延期；小阶段只实施、静态检查与编译，不运行测试/冒烟/性能采样。E6-C 先依赖现有 ComponentRegistry 的稳定 TypeId/schema 版本，E6-B 后续生成同一声明合同，不新增平行类型目录。

E6-C1 首片已实现但未运行验证：新增 ScheduleAccess/ScheduleTask/ScheduleResource 声明与不可变 ScheduleGraph，支持组件读/写、外部资源读/写、owner-thread 结构变更、阶段和显式 before/after；显式依赖先拓扑排序，冲突边再按确定性顺序添加，拒绝未知/重复/schema 不符/环/超预算声明。限制为128任务、每任务64访问与64显式链接、128资源、16阶段、8192边；返回复制后的任务/边/串行顺序及确定性合同哈希。新增8组回归用例已登记未执行，完整 Runtime.Tests Debug 编译0警告/0错误（4.18s）。这是**冷任务图基础，不是已接通的调度器**：未接 WorldRunner/PlaySession，尚无访问拦截、任务执行、工作线程、性能或功能验收；Worker 声明不授予跨线程 World 访问。正式 EXE 未更新，本轮未提交推送。下方旧 E6-B → E6-C 顺序为历史，以上述顺序为准。

2026-10-24 E6-A 收尾实现（待整个 E6 统一验证）：本地保留 Play 差异面板现从统一事件分发处隔离后台控件/画布，并消费 UI 预览键盘输入，打开时取消已有交互。启动前新增资源与脚本绑定依赖预检：复用现有资产加载器的实际闭包，记录根引用、成员 UUID/类型/代次/哈希，拒绝缺失资源、错误租约和作者预览；纯逻辑/UI 场景不借用旧 3D 资源。脚本解析对已加载目录执行无构造器的类型/Export 检查（包含禁用绑定），记录模块标识与确定性合同哈希，并检查启动回调前后目录代次。Editor 记录来源/镜像哈希/依赖回执，Stop/Reload 清除；Player 先准备冷启动依赖，再实例化 ECS。既有 UI、Shader、物理依赖仍由对应宿主预检负责；脚本记录不是完整程序集文件/原生/操作系统依赖清单，也不代表 M9 发布包验收。

本轮新增 10 组回归用例已登记、均未执行。最终完整 Editor.Services.Tests（含 Editor）、Scene.Rendering.Tests、Player.Tests（含 Player）、Rendering.Tests 的隔离 Debug 编译均为 0 警告/0 错误；中间脚本模块误用内部 ReadOnly 的 1 项编译失败已修复，原源码与日志保留。局部证据在 out/verification/ecs-e6/pre-runtime-change-input-first 和 pre-bake-dependencies-first，非全量源码冻结或功能验收。E6-A 当前实现覆盖已接通，仍须在 E6-B/C 实现后统一验证功能、GUI/IPC、生命周期、资源与性能；不以编译替代验收。E5-D 继续延期，正式 EXE C50742AD 未变。本轮未测试、部署或再次提交推送；前一开发快照 adcddab 已提交推送。下一步 E6-B 元数据与访问代码生成，E6-C 随后实施。下方旧的“依赖闭包/输入隔离待实现”等条目为历史进度。

2026-10-24 开发中提交记录：E6-A 的本地 Play 差异界面已接入场景、UI 和动画图工作区。编辑/运行对象分别分页，共享 UUID 选择，支持运行时删除及新建对象；完整 before/after 差异与原命令分段显示，逐页审阅、逐 UUID 删除确认和最终 Stop/应用确认均独立。来源变化隐藏旧数据并撤销确认，确认面板打开时禁用场景快捷键和 HUD 输入。新增6组 Presenter 用例已登记但未执行；首次服务测试工程编译因错误引用不存在的计数属性失败2项，修正为既有只读诊断方式后完整 Editor.Services.Tests 及 Editor 依赖 Debug 编译0警告/0错误（6.12s）。日志与失败源码保留于 out/verification/ecs-e6/pre-runtime-change-ui-first（本地忽略目录，不随提交发布）。本次按用户要求提交开发中源码，不代表完整 E6 或性能验收；不运行测试、不部署 EXE。E6-A 仍需资产/脚本依赖闭包及整体验证，E6-B/C 尚未开始；测试统一留到整个 E6 结束，E5-D 继续延期。下方“界面尚未接入”等文字为历史进度。

E6-A 变更桥受检执行服务已接通（实现待统一测试）：暂停运行差异可显式预检为原 transaction/delete 命令批次；本地确认接口要求同一最新票据、精确指纹及全部删除 UUID，确认后先按原生命周期 Stop，再进入唯一 EditSession/Commit/Undo。Stop 回调期间作者文档只读，失败不提交；确认一次消费，陈旧/替换/跨宿主票据拒绝，不自动重试。权限限定于审阅对象、创建对象、组件与绑定，未新增 MCP 入口或自动写回。32 个对象差异展开后仍受原128操作/64KiB完整输入限制；归一化仅允许既有组件排序，不得悄悄改变审阅值，变化时要求另行审阅。新增9组服务用例已登记未运行，覆盖混合变更与精确Undo/Redo、完整删除确认、失效/线程/回调、停止失败、组件绑定移除、空差异、160操作展开拒绝及归一化变化。首次编译因新测试缺 Runtime 命名空间出现3错误，源码/日志保留；修正后最终Core.Tests与完整Services.Tests及Editor依赖Debug编译0警告/0错误（2.11s/5.89s），不是功能验收。局部证据在 out/verification/ecs-e6/pre-runtime-change-apply-first，非全量冻结。**正式编辑器差异展示/人工确认按钮尚未接入，完整变更桥与资产/脚本闭包仍未完成**；随后推进E6-B/C，整个E6结束统一测试。E5-D继续延期，正式EXE C50742AD不变，无测试/冒烟/性能测量/部署/提交/推送。下方旧“执行连接未实现”为历史进度。

E6-A 变更桥执行基础新增（待统一验证）：Core 提供本地受检原命令批次预检与提交，组合的仍是 ncma.scene.transaction 和独立 ncma.scene.delete_object；原 JSON 操作白名单未添加 delete/restore，也未注册新远程能力。预检可在 Play 冻结时只读执行；提交必须解除冻结、来源/版本/归一化结果吻合，再经原 Commit 一次安装、一条历史。删除逐 UUID 要求原删除能力及目标授权，完整目标随历史和请求缓存保留，Undo/Redo/重放再次校验；创建后删除等净零变化仍保留授权要求。工作区本地历史取得完整目标，现有单目标远程审批明确拒绝混合删除历史，不能降级为普通事务。128 个原操作与64 KiB完整命令输入限制不放宽。新增8组Core+1组工作区用例已登记未运行，Core.Tests和完整Editor.Services.Tests及Editor依赖Debug编译零警告/错误。**运行差异审阅到人工确认提交的连接与界面尚未实现，变更桥仍未完成**；下一步完成该连接、资产/脚本闭包，再推进E6-B/C。所有测试留到整个E6实现结束；E5-D延期，正式EXE C50742AD未变，无部署/提交/推送。

E6-A 显式变更桥首片（只读审阅基础，未完成执行）：EditorSessionOwner 记录实际 Play 烘焙来源，新增 EditorRuntimeChanges，仅在暂停且作者来源未变时手动比较选定 UUID。按对象列出创建/删除/重命名、组件增删改和完整绑定/Export 配置差异，保留前后值；上限 32 对象、128 差异、64 KiB 完整响应，超限拒绝不裁剪。审阅绑定 Edit/作者/Play/运行 World/revision/tick/目录代次，复制结果无可变别名，重载/Stop/作者变动或新审阅使旧票据失效；无新权限、无自动同步、无写回、无 MCP 写入口。**人工差异界面、确认执行及原事务/破坏性删除授权接线仍未实现**，不能标记变更桥完成。现有批事务不支持删除，不将其伪装成普通修改或直接 Restore 文档；后续须保留原删除权限与 Undo/Redo 校验。新增 6 组用例已登记但未运行；完整 Editor.Services.Tests + Editor 依赖最终 Debug 编译 0 警告/0 错误（6.00s），首次服务编译将脚本目录 Guid 误作 ulong 的 3 错误已修正并保留失败源码/日志。下一步完成原事务执行合同与界面、资源/脚本依赖闭包，然后 E6-B/C；全部测试仍在整个 E6 实现结束后统一执行，E5-D 延期。正式 EXE C50742AD 未变，无部署/提交/推送。

E6-A 本地运行属性详情已接入正式 Editor（实现待统一验证）：对象行可单独查看组件、脚本绑定及 Export，每次读取一个完整条目，沿用 252 KiB 数据预算，长 JSON 分段展示而非裁掉字段；显示 Play/运行场景/World/version/revision/tick，状态改变隐藏旧值并要求明确重新读取，Reload/Stop 后须重新选择。仅手动读取时序列化，不逐帧捕获完整场景；本地查看不创建远程读取授权，不写作者数据或运行组件，不声称读取任意脚本私有状态。新增 4 组用例已登记但未执行，包含真实运行值与作者值不同、分页、陈旧代次/重载/候选步/线程、完整大字段与 Unicode、GUI 与 AI 权限隔离。完整 Editor.Services.Tests 及 Editor 依赖最终 Debug 编译零警告/错误；中间一次测试代码缺泛型参数导致 1 错误，失败源码和日志保留。原 EXE C50742AD 未变，未测试/冒烟/性能测量/部署/提交/推送。下一步 E6-A 资源/脚本依赖闭包和显式 diff/原事务变更桥，E6-B/C 仍未开始；整个 E6 实现结束后统一测试，E5-D 继续延期。

E6-A 最新进度：剩余 27 个渲染消费方文件已完成作者/运行接口迁移（后处理/管线/阴影 9、UI 7、构造/交换/生命周期等 9、自定义管线与 M6 综合夹具 2）。完整 Ncma.Rendering.Tests 及依赖隔离 Debug 编译成功，0 警告/0 错误（7.87s）；此前 144→75→33 错误的编译日志均保留。作者操作使用 UUID 和原事务；运行变更、跨 World 身份、恢复、固定步与渲染断言使用真实烘焙实例/实际宿主投影，另核对作者字节不变，没有恢复 Document.World 兼容入口。原像素、负例、严格 0B、多帧率、128 次循环和资源阈值保留但均未运行。**未执行任何测试/冒烟/性能采样，编译成功不是功能验收**。27 份改前源码已复核，33 项局部源码/日志/程序集/EXE 哈希记录在 out/verification/ecs-e6/pre-authoring-render-final-first/compile-result.json，不是全量冻结。下一步补齐 E6-A 资产/脚本依赖闭包、显式 diff/原事务变更桥及本地运行属性详情；E6-B/C 未开始，整个 E6 实现结束后统一测试，E5-D 继续延期。正式 EXE C50742AD 未变，未部署/提交/推送。下方 272/27 等为历史编译缺口。

E6-A 最新进度：8 个动画夹具、6 个图形恢复夹具及已有辅助文件已迁移。作者对象使用 UUID；动画推进、根运动、故障回滚继续检查真实 Play ECS。恢复用例区分“作者文档保持”“来源未变时保留预览 World”“来源已变时重新烘焙”，不使用虚假的编辑 World。完整 Rendering.Tests Debug 编译仍失败：**272 处错误、27 个文件待迁移**（此前 545/41），本批最后编译未报告错误，但不能标记程序集成功。首次恢复编译新增的 2 处辅助函数作用域错误已修正，失败源码/日志保留。**未执行测试或性能采样**；下一步处理后处理、UI、管线和生命周期等剩余夹具，再补齐 E6-A。E6-B/C 未开始，整个 E6 实现后统一测试，E5-D 延期。正式 EXE 未变，无提交推送。

本轮证据：15 份改前文件复核未变，当前 15 文件、27 个剩余编译来源、3 日志、1 份失败探针源码及正式 EXE 共 47 项局部哈希在 out/verification/ecs-e6/pre-authoring-render-animation-first/compile-result.json；恢复源码与日志另存 pre-authoring-render-recovery-first。三个完整编译终态分别为 310/274/272 错误，均 0 警告（7.54s/7.39s/7.71s）；仅据此确认迁移编译缺口缩小，不是全量冻结、程序集生成或测试验收。

独立中断动画预览核对实际 RuntimeSceneInstance 的 tick 与检查点不变，并核对作者字节；编辑器预览使用现有 EditorSceneProjection。恢复夹具读取实际宿主持有的投影，来源未变检查保留真实派生 World，Undo 改变作者 revision 时检查旧投影失效并显式重建；原作者文档、唯一历史、Play/脚本状态仍保留。未降低原 512/1024、32 次启停、30/60/144Hz、0/1/8/32 对象与像素/数值/分配限制；所有运行验证仍待 E6 整体结束。

E6-A 最新进度：10 个共享渲染/环境夹具文件已迁移，新增显式作者到运行场景的烘焙辅助函数。编辑预览使用现有来源戳检查，作者修改后拒绝旧拾取并显式重建投影；Play、环境变更、边界和恢复检查使用实际 ECS，另核对作者数据不变。完整 Rendering.Tests Debug 编译仍失败：**545 处错误、41 个文件待迁移**（从 734/51 降至此值），本批文件未报告错误，但不能宣称程序集已编译成功。**未执行任何测试或性能采样**，原像素、帧数、严格 0B 和资源释放断言保留。下一步迁移动画及图形恢复等剩余夹具，再补齐 E6-A；E6-B/C 未开始，整个 E6 实现后统一测试，E5-D 延期。正式 EXE 未变，无提交推送；下方 734/51 与 608/44 为历史候选。

实现细节与证据：SceneResources/SkinScene 共用作者夹具只保留 UUID；编辑预览调用实际 EditorSceneProjection.RequireCurrent，准备新投影及资源后才关闭旧会话，保留 immutable GPU 共享检查。动画预览同时检查投影运行 tick=0、来源有效、作者字节不变；环境预检与共享渲染会话检查真实运行检查点，不能用作者数据静止替代运行状态。0/1/256/4096 的场景绘制负载在显式空烘焙后直接建立运行值，不是作者模型性能测试。32 帧预热、8 帧测量、严格 0B、像素和租约阈值未放宽，均未执行。

两个完整编译终态失败已保留：第一次 608 错误/44 文件/0 警告（7.59s），第二次 545 错误/41 文件/0 警告（7.49s）。10 份改前源码已复核，11 个当前源码、41 个剩余来源、2 日志和正式 EXE 共 55 项局部哈希位于 out/verification/ecs-e6/pre-authoring-render-shared-first/compile-result.json；原始文件分存该目录及 pre-authoring-render-environment-first、pre-authoring-render-environment-second。这不是全量冻结或测试通过证据。

E6-A 最新进度：Player 的 8 个旧作者夹具文件已迁移至 UUID/组件接口，真实 FBX/静态场景、角色、根运动、战斗和生命周期断言仍使用独立运行 ECS。完整 Ncma.Player.Tests 隔离 Debug 编译零警告/错误，**未执行任何测试或性能采样**；原帧率、固定步、32 次启停及 0/1/8/32 角色矩阵保留。渲染测试工程上次定位的 734 处错误（51 文件）尚待迁移，本轮未重新编译；下一步处理显式编辑预览投影与渲染夹具。E6-A 其余边界及 E6-B/C 未完成，所有测试留到整个 E6 结束，E5-D 延期。正式 EXE 未变，未提交推送；下方 Player143 为历史状态。

证据：out/verification/ecs-e6/pre-authoring-player-fixtures-first 保存 8 份改前源码、完整编译日志、当前源码/测试工程产物/正式 EXE 的 15 项哈希及改前文档；原始源码复核未变。该记录仅证明局部编译，不是功能/性能验收或全量冻结。没有运行 Player、原生插件、IPC 或测试程序，运行断言将在整体阶段末统一执行。

E6-A Gameplay/Scene.Rendering 迁移（2026-10-23，最新）：完成 23 个旧夹具文件迁移，另新增 EcsDiagnosticState 和 SceneRuntimeFixtures 两个辅助类型。Gameplay.Tests 完整隔离 Debug 编译 0 警告/0 错误（2.24s），Scene.Rendering.Tests 完整隔离 Debug 编译 0 警告/0 错误（4.73s），**没有运行测试、冒烟、IPC 子进程或性能测量**。

Gameplay 的冷作者构建改为 UUID/规范化快照；查询、固定步、密集池、Movement、元数据提交和回滚继续使用独立运行实例。查询腿保留作者字节不变对照，编辑分页腿只改作者文档并在测量外检查运行数据不变；检查点使用实际运行格式，不把静止的作者数据当作运行正确性证据。原对象数、循环数、采样窗口和严格分配阈值保留，诊断格式显式标记 E6 与 historicalPairedContractCompatible=false；旧配对合同只提供负载/来源输入，新报告与正式配对合同仍须阶段末审计，不能沿用 E3/E5 性能结论。

Scene.Rendering 的事务、精确 Undo/Redo、资产/Prefab、保存与组成校验继续使用原作者文档管线；提取器、错误组成、查询缓存、线程边界和恢复失效检查使用实际运行 World。修改后独立 bake 返回运行实例，无兼容 Document.World 门面。编辑 Undo 的过期检查改为旧作者 revision 拒绝烘焙，运行恢复仍检查旧 Entity 失效；原 4096 对象和严格 0B 分配断言保留，尚未执行。

当前候选的后续编译检查：Player.Tests 143 处错误/8 文件，Rendering.Tests 734 处错误/51 文件，均 0 警告；完整失败日志保留，**不能宣称全解决方案或所有测试工程可编译**。23 份原源码已归档并复核；当前 25 源文件、59 个待迁移来源、4 日志及 10 测试工程产物与正式 EXE 的局部哈希记录在 out/verification/ecs-e6/pre-authoring-gameplay-rendering-docs-first/compile-result.json，不是全量冻结或功能验收。一次只读产物计数脚本失败已记入该记录，随后按实际观察到的精确路径读取产物，未重跑构建或测试。正式 EXE SHA C50742AD 未变，无部署/提交/推送。

下一步迁移 Player/Rendering 冷作者消费者，随后补齐 E6-A 依赖闭包、显式变更到原事务的桥接、本地运行属性详情；E6-B/C 尚未开始。**统一规则适用于所有后续整体阶段：小阶段仅实施、静态检查与编译检查，测试在所属整体阶段全部实现后统一执行；未通过时修复并重新验证，不以编译成功替代测试通过。** 当前为 E6-A → E6-B → E6-C → E6 统一测试，E5-D 继续延期。下方 Gameplay82/SceneRendering153 等仅属历史候选。

E6-A Core/Application/Transport 迁移（2026-10-23，最新）：11 个夹具文件已移除旧作者 World 依赖，完整 Editor.Core.Tests、Application.Tests、Editor.Transport.Tests 的隔离 Debug 编译均零警告/错误，**没有执行测试、IPC 冒烟或性能采样**。Core 的场景页独立对照改为 model/modelVersion/documentSessionId/documentRevision，冷/热读取均检查零全量捕获；保留权限、重放、草稿冲突、完整 Undo/Redo 和回调重入断言。schema 无效的写入应在发布前拒绝，另加“未完成组成仍可检查、验证拒绝、授权事务补齐后验证允许”用例，不用旧可变 World 注入无效编辑状态。

Application 的恢复保护和 UI 输入路由现在使用实际 RuntimeSceneInstance：固定步更名检查运行 World，并额外核对作者文档不变；运行状态的外部修改仍触发输入撤销。编辑器二次启动检查不同运行 World。Transport 保留真实管道/MCP 子进程、人工配对/批准、原 100/1000/4096 对象与 12+100 采样矩阵、预算、审计和完整文档 oracle；仅冷作者初始化改为一次性快照构建，Undo/Redo 检查作者 revision 而非不存在的 Entity 句柄。三种成本报告升级 schema 3，标记 ncma.authoring-scene v1、workloadContractId 与 historicalPerformanceAcceptance=false；旧合同仅提供工作负载输入，不复用旧 E5 输出验收或性能结论，相关最终报告审计仍待 E6 统一验证。

编译失败证据全部保留：Core 初次剩余 1 处 GetUuid 漏迁，修正后通过；Application/Transport 迁移前 21/25 处错误已消除。另一次当前候选编译明确 Gameplay.Tests 仍有 82 处错误（16 文件）、Scene.Rendering.Tests 仍有 153 处错误（7 文件）；Player/Rendering 其他冷作者夹具尚未重新编译验收。11 份改前源码、完整日志、3 个测试程序集和 23 个失败来源文件哈希在 out/verification/ecs-e6/pre-authoring-core-fixtures-first 及 pre-authoring-app-transport-fixtures-first；记录是局部编译证据，不是全量冻结。所有构建终态，正式 EXE C50742AD 未变，无部署/提交/推送。下一步迁移 Gameplay 与 Scene.Rendering，然后其余消费方及 E6-A 依赖闭包、显式变更桥、运行属性详情；E6-B/C 未开始，整个 E6 结束统一测试，E5-D 继续延期。下方 Core122 等属于历史状态。

E6-A 场景/编辑服务夹具迁移（2026-10-23，最新）：完成剩余 13 个服务夹具文件及 7 个场景夹具文件迁移。编辑对象只使用文档 UUID/组件接口；Play、检查点、数值求解和渲染断言继续检查真实运行 World。动画/蒙太奇独立性与显式烘焙的实际 World 比较，并检查作者来源戳，而非将文档 SessionId 当作 WorldId。角色三次启动检查运行 World 各自独立及源文档未变；4096 对象、70×62000 字符、原采样/动画帧数、碰撞阈值、权限及精确 Undo/Redo 断言保留。

场景读用例按新架构调整 14 组：冷/热分页均不得全量捕获，动态 getter 只能在显式写入时取值；元数据归一化、schema 和跨页总体字节预算在发布前拒绝，失败保留原数据与 revision；读回调在候选写入期间拒绝重入；删除立即清除绑定，同 UUID 重建不恢复旧脚本。删除旧编辑 World 的观察/动态 getter/句柄失效语义，不恢复兼容入口。原“Play 文档克隆”用例改为实际运行实例和原运行元数据提交协议。

编译证据（未运行任何测试）：完整 Ncma.Editor.Services.Tests Debug 编译零警告/错误；第一次缺少 3 处命名空间引用的失败日志与源码已保留，补齐后第二次成功。完整 Ncma.Scene.Tests Debug 编译零警告/错误。随后编译 Ncma.Editor.Core.Tests 仍失败：122 处错误（Program 103、ReadQueryTests 19），因此不宣称所有测试工程或全解决方案可编译；Gameplay/Player/Rendering 中其余冷作者夹具也仍需迁移和当前候选编译，不复用之前版本的绿灯。20 份修改前源码逐一与本轮编辑前读取内容复核一致，日志/当前哈希/失败源码及汇总记录位于 out/verification/ecs-e6/pre-authoring-fixtures-remaining-first 和 pre-authoring-scene-fixtures-first；compile-result.json 是局部源码及产物记录，不是全量冻结或测试验收。正式 EXE C50742AD 未变，无部署/提交/推送。下一步先迁移 Editor.Core 两个文件，再继续其余消费方与 E6-A 依赖闭包、显式变更桥、运行属性详情；E6-B/C 未开始，整个 E6 完成后统一测试，E5-D 继续延期。下方 100/198 处错误及其他旧编译结果仅属历史候选。

E6-A 编辑夹具迁移追加（2026-10-23，最新）：13 个服务夹具文件改用纯编辑文档 UUID/组件接口，覆盖 Prefab、Low 画质、环境与后处理 Inspector、工作流、模型放置、资产权限、工具栏及 UI 选择/布局。原有对象/字节/操作预算、权限拒绝、回调只读、精确 Undo/Redo 和类型化引用检查保留；Prefab 的 4096 空对象和 65×60000 字符负载改为一次性构造作者快照，未减少数量或阈值。编辑态不再伪造 World/tick；断言使用文档会话、精确内容/历史以及未启动 Play，预览状态通过真实派生 World + 作者来源戳检查，Play 的错误 World 拒绝仍覆盖。五个 Prefab/画质/环境/后处理文件及正式 App 依赖的隔离 Debug 编译通过（零警告/错误）；完整服务测试工程仍编译失败，错误从 244 降至 **100 处，位于剩余 13 个文件**，其中主入口 Program 55、角色/UI/动画夹具 45。没有运行测试；13 份原始文件、完整编译日志及当前源码哈希保存在 out/verification/ecs-e6/pre-authoring-fixtures-first。下一步迁移剩余夹具与主入口，然后继续 E6-A 的依赖闭包、显式变更桥和运行属性详情；E6-B/C 尚未开始。正式 EXE C50742AD 未变，无部署/提交/推送，整体 E6 实现完成后才统一测试。

E6-A 正式 Editor/Player 接线追加（2026-10-23，最新）：编辑视口已改为显式烘焙的 `EditorSceneProjection`，来源戳分别记录编辑会话、文档会话、documentGeneration/revision，运行实例仍使用自己的 Scene/World 身份；没有恢复 `Document.World` 或建立后台双向镜像。变更来源、外来会话、运行值/tick 或未提交状态会使预览失效；即使快捷键在本轮同步后改文档，旧预览也不能继续用于渲染、动画推进、拾取或 MCP 当前场景读取。GPU 准备失败仍保留待释放对象，未改文档的图形恢复复用原预览运行实例/动画时钟；旧文档来源必须重新烘焙，Inspector 区分当前失败请求与旧安装 World。作者态名称/组件/对象列表、HUD、Prefab 之外的剩余 App 读取已切换；Player 冷输入与动作诊断示例也使用 UUID 文档构建。两个正式托管入口及依赖 Debug 编译零警告/错误；新增 5 组用例与前轮引用选择用例通过隔离的编译库检查，**均未执行，不代表完整测试工程通过**。完整 Editor.Services.Tests 编译先被示例 24 处调用阻挡（已修复），当前在 26 个旧夹具文件报 244 处错误；Scene.Tests 先前 198 处仍未复核。13 份改前源码、全部编译失败及成功日志保留于 out/verification/ecs-e6/pre-authoring-app-first，最终编译证据 compile-result.json；未降低警告/审计。下一步迁移作者态/运行态夹具，继续补齐依赖闭包、显式 diff/原事务变更桥和本机运行属性详情。E6-A 尚未完成，B/C 未开始，测试仍在整个 E6 实现结束后统一执行；正式 EXE C50742AD 未变，无提交/推送/部署。

E6-A 服务层迁移追加（2026-10-23，最新）：8 个正式服务文件已移除编辑文档的 World 依赖；编辑会话不再建立 SceneWorld 门面，动画图预览经显式 BakeRuntime 创建独立运行实例，资产/Prefab/UI/环境/画质操作使用文档 UUID、只读作用域和原有事务。Prefab 检查戳改为 DocumentSessionId，不冒充运行 World。资产引用选择保留创建顺序、缺少 Transform 和冲突组件的联合报告；原有 2 组用例改用编辑文档与独立快照 oracle，保留 4096 对象/删除重建/恢复覆盖，未编译或执行该测试工程。Editor.Services 及依赖单独 Debug 编译零警告/错误；完整 Editor 构建进入 App 后仍有 32 处旧 World 调用（9 个文件），不能标为完整编译通过。首次错误使用 --no-restore 导致新隔离目录缺少 assets 文件，失败记录保留；随后正常还原编译，未关闭审计或警告。9 份改前源码与结果保存在 out/verification/ecs-e6/pre-authoring-services-first，完整 App 失败输出另存 compile-second.log。下一步迁移正式编辑视口派生运行实例及恢复生命周期、其余 UI 作者态读取，然后补齐夹具/依赖闭包/显式变更桥。E6-A 未完成，B/C 未开始，全部测试仍仅在整体 E6 结束后执行；正式 EXE 未改，无提交/推送/部署。

E6-A 作者端去 World 首片（2026-10-23，最新）：`SceneDocument` 内部改为一份规范化序列化编辑记录和仅保存位置的 UUID 索引；没有 World、Entity 索引/代次、组件池、运行 tick 或 GameObject 门面。删除旧 `World` 属性及其 revision 观察、运行查询/序列化证明缓存，不提供兼容包装。新建/删除/重命名对象、类型化/序列化组件读写以 UUID 为边界，registry 共享描述与校验但不建立运行存储；内部只保存 JSON，不让自定义组件 getter 的后来变化暗改已写入编辑值。只读作用域、owner-thread、重入、原子候选预算/回调隔离与 revision 保护独立于运行 World。显式 `BakeRuntime` 才产生独立运行镜像/实例，GameObject 仍属于运行 ECS。

`PrepareRestore` 先规范化全部组件、复制数据、检查整份文档预算、分配 UUID 索引并运行隔离回调，之后一次发布；旧 prepared 提交不可重放。普通可信构建操作仍先准备完整编辑候选，当前写路径会整份复制/检查，**不宣称冷编辑成本/分配达标**，后续统一性能检查不得复用旧 World 作者端结果。分页读取直接复制所需编辑记录，没有每帧全场景 JSON 往返要求。EditSession 唯一 Undo/Redo、提案/参与者/交互/读取保护已改为文档作用域；`ncma.scene.inspect` 明确返回 `model=ncma.authoring-scene`、`modelVersion=1`、`documentSessionId`、`documentRevision`，删除旧 `worldId/tick`。Prefab Extract 的最后参数改为真实作者端 SessionId；后续 DTO/调用者须同语义迁移，不能把旧 WorldId 字段静默填文档 ID。

新增 7 组 AuthoringStoreTests（无 World/Entity API、冻结序列化值、原子/规范化失败与回调隔离、陈旧候选/线程/只读、顺序/绑定复制/有界读取、预算失败、运行烘焙隔离）登记但未执行。Scene 库 `compile-authoring-store-first`、Editor.Core `compile-authoring-core-first` 隔离 Debug 编译零警告/错误；完整 `compile-authoring-scene-tests-first` 退出 1：198 错误均位于七个旧用例文件，新文件没有本轮编译诊断不等于测试程序集成功。完整 Editor 编译 `compile-authoring-editor-first` 退出 1：当前阻塞在 EditorAnimationGraphPreview/EditorAssetWorkflow/EditorSessionOwner/EditorPrefabWorkspace/EditorUiPublication/RenderingEditorAdapter/EditorEnvironmentWorkspace/SceneQualityEditorAdapter 共 53 错误；这是首批消费者边界，不是所有下游只剩 53 错误。完整四份日志与十四份当前源码哈希记录、十三份原文归档在 `out/verification/ecs-e6/pre-authoring-store-first`。首次 Delete+Add 同路径补丁请求被工具拒绝且五份源码哈希未变，之后使用 Update 实际修改，拒绝记录保留；不是产品测试失败。

下一片先迁移上述八个正式服务消费者：删除作者端 SceneWorld facade，独立预览显式烘焙并拥有可丢弃的派生运行实例，配置/资产/UI/Prefab 操作使用文档 UUID 与原唯一事务；再处理 Editor App、Player 冷输入与旧用例。仍需完整资产/脚本闭包、显式变更桥、本地运行属性展示。**没有执行测试/冒烟/性能测量，完整 Editor 当前不能从此源码构建**；正式 EXE C50742AD 保持不变，无部署/提交/推送。E6-A 未完成，A/B/C 整体结束后统一测试。

E6-A 通用运行检查宿主接线（2026-10-23，最新，未执行测试）：新增 `RuntimeInspectionService` 与闭合工具描述，正式 Editor 初始化注册 `ncma.runtime.state` / `ncma.runtime.inspect`，后者提供 objects/components/bindings/exports 四类分页。前者仅在有授权时返回当前已提交版本戳和获批 UUID；分页须精确匹配 Play 会话、运行实例会话、World、版本、revision、tick。只读注册容量由 18 明确增加到 20 容纳两项能力，不扩写既有写权限或 IPC 消息上限；data 最多 252 KiB，保留原统一响应的 4 KiB 包装预算，页最多 128 项。远程对象列表的名称、计数和分页仅来自批准范围，组件载荷使用注册 TypeId/version 解释，不执行其中内容。

授权只能由本地界面审阅后建立：1–32 个精确对象、60 秒、显示完整配对受众；绑定私有真实 Entity 代次、Play 对象/会话、运行 Scene/World、编辑会话/文档代次、端点实例引用和 AudienceRevision。相同 UUID 销毁重建、Reload/Stop/新 Play、重新配对或端点替换不能复活旧授权。待审阅票据保存私有副本和指纹且同样 60 秒过期；工具不提供批准、续期、写入、推进模拟、Undo 或推理入口。批准范围明确包含所选对象在有效期内的全部组件值/脚本绑定/Export（含后续新增字段与变化值），数据可能含业务内容或其他引用，**不是秘密脱敏接口，也不是逐客户端 ACL**。本地面板目前用于对象分页和审阅；完整本地运行属性钻取展示仍待补齐。

新增 7 组宿主用例已登记：真实 IPC 四种分页/契约与双模型不变、过期 tick/严格输入/候选步拒绝、批准过期/配对 epoch/端点替换、对象同 UUID 重建/Reload/Stop、新 252 KiB 外包装预算失败与小页读取、真实 MCP stdio tools/list/同 request ID 权限重查、GUI 禁用批准/人工勾选/旧 Play 票据失效。**没有运行任何用例、MCP 进程、界面冒烟或性能测量**。两次隔离 Debug 编译 `compile-runtime-host-inspection-first/second` 均退出 0、零警告/错误；完整编译 stdout、十份最终源码/程序集哈希记录及六份修改前原文在 `out/verification/ecs-e6/pre-runtime-host-inspection-first`，原文 SHA 已重核。正式 EXE C50742AD 未变，无部署/提交/推送；E5-D 继续延后。E6-A 尚需作者端去 World 与消费者迁移、完整资源/脚本依赖闭包、显式变更桥和本地运行属性展示，B/C 尚未开始；只在整个 E6 实现结束后统一验证。

E6-A 动画夹具迁移（2026-10-23，最新）：剩余 AnimatorInterruptionSceneTests、AnimatorRootSceneTests、BlendSpaceJointTests、BlendSpaceSceneTests、LayerSceneTests、M6FinalAcceptanceTests、MontageSceneTests 七文件共 25 处 Play 构造，均改为显式 `BakeRuntime` 加载独立运行实例。位置/斜坡跳跃/旋转、Reload World 身份、动画上下文、渲染会话与快照、失败量子字节断言读取 `Play.Scene`，不以未变化的编辑模型充当运行 oracle。运行 Entity 在运行 World 解析；源 Entity 仅用于读取稳定 UUID，不能跨 World 传递。保留独立编辑预览、源格式打包、非法组合/资源负例；原数值门槛、循环次数和 GPU/求解器断言不变。

七份修改前源码在 `out/verification/ecs-e6/pre-render-animation-migration-first/manifest.json` 按 SHA 归档并重核；`compile-render-animation-first` 完整 Rendering.Tests 与引用工程隔离 Debug 编译退出 0、零警告/错误、7.87 秒。`compile-result.json` 记录命令、工具结果与七份原文/当前源码及最终程序集哈希，**是结构化编译记录，不是完整 stdout 日志**；旧 25 错误及此前失败日志保留。没有执行任何测试或性能用例；不能据编译声明 GPU、功能或整个引擎验收通过。正式 EXE C50742AD 不变，无部署/提交/推送，E5-D 仍延期。下一片接正式宿主的通用运行检查及人工授权/生命周期边界，后续仍须移除编辑模型 World、闭合资产/脚本依赖与显式变更桥；E6-A 未完成，A/B/C 完成后统一测试。

E6-A 渲染宿主迁移进行中（2026-10-23，最新）：24 个 `Ncma.Rendering.Tests` 文件改用运行 Scene；实际 Editor/Player/独立宿主的图形恢复、HUD 输入、场景提交、环境/阴影/后处理动态设置读取真实运行 World。独立 Player 预准备、脚本目录与 Play 使用同一烘焙实例，恢复状态字节采用运行检查点；HUD 位置 oracle 不再读取未变化的编辑文档。资源发布/源场景保存/编辑 Undo 保持作者端输入。AnimatorScene 的 RuntimeSessionOwner 改为显式运行实例，未加兼容入口。

修改前 24 份源码归档于 `out/verification/ecs-e6/pre-render-host-migration-first/manifest.json` 并重核；初次 `compile-render-host-before-first` 有 141 错误、零警告，其工具输出被截断，`compile-before-captured.log` 明确仅保存截断输出，不冒充完整日志。迁移首轮 `compile-render-host-first` 有 26 错误，其中一处是将 Editor 启动错误替换成 Player 显式实例调用，修正后 `compile-render-host-second` 仍退出 1、零警告、25 错误，完整失败日志保留。剩余文件：AnimatorInterruptionSceneTests、AnimatorRootSceneTests、BlendSpaceJointTests、BlendSpaceSceneTests、LayerSceneTests、M6FinalAcceptanceTests、MontageSceneTests；下一片须保留数值/GPU/求解器/故障断言并迁移构造与状态读取，不能只替换构造后仍读编辑 World。**Rendering.Tests 尚未成功编译，没有执行测试**；正式 EXE C50742AD 未变，无部署/提交/推送，E6-A 尚未完成。

E6-A Editor 宿主迁移与权限边界（2026-10-23，最新）：`CharacterEditorChecks`、`CharacterInspectionTests`、`AnimationRuntimeInspectionTests`、`EnvironmentInspectorTests`、`PostProcessAuthoringTests` 和服务测试 `Program` 改用 `Play.Scene`，运行字节断言使用检查点；独立角色/图故障会话先显式烘焙，编辑文档/Undo、真实绘制、只读审批/过期/撤销/重载覆盖保留。第一次隔离编译 36 处旧接口错误保留于 `pre-editor-host-migration-first/compile-before.log`，迁移后 `compile-editor-host-first` 全项目及依赖 Debug 编译零警告/错误。

代码复查另发现既有角色读取授权只比较连接 ID，撤销再配对可能恢复同组 ID。现与动画服务一致：审阅指纹和有效授权同时绑定 Endpoint 对象及单调 AudienceRevision，界面显示 epoch；准备“中间不读 Grant，撤销并重配相同连接，旧批准/旧审阅拒绝，重新人工审批允许”用例。没有新增能力、自动批准或放宽对象范围；通用运行分页尚未注册 MCP，不宣称完整运行检查交付。修改前六份原文与之后三份源码版本分别保存在 `pre-editor-host-migration-first`、`pre-character-audience-first`，全部重核；最终 `compile-character-audience-first` 全 Editor.Services.Tests 及依赖 Debug 编译零警告/错误，**没有运行任何测试**。正式 EXE C50742AD 不变，无部署/提交/推送；继续 E6-A 剩余架构工作，A/B/C 实现结束后统一验证。

E6-A Player 测试图迁移（2026-10-23，最新）：Player 生命周期/RuntimeSessionOwner、角色、根运动、战斗、联合渲染与模拟成本夹具改为显式烘焙独立运行实例；会话服务不持有编辑文档，没有恢复旧 `Document` 入口。运行句柄按 UUID 重新解析，失败量子/提交后故障/Reload/帧率一致性断言使用实际运行 World 和运行检查点。场景文件与资源包输入仍由编辑模型冷构建，保留源文档隔离断言。战斗故障比较不再误读未变化的编辑文档；FBX 角色状态变化使用运行启动快照哈希，禁止用编辑格式与运行格式天然不同来证明运动。Player schema 2 报告增加独立固定步运行哈希 oracle；K7 诊断报告升 schema 2 并标明运行格式，旧性能证据不重标。

七份修改前源码归档于 `out/verification/ecs-e6/pre-player-runtime-migration-first`，主 manifest 六份、skinned-manifest 一份，全部重核哈希；原先 72 处旧接口编译失败保留于 `compile-before.log`。`compile-player-runtime-first` 和补齐哈希断言后的 `compile-player-runtime-second` 均为整个 Player.Tests 项目及依赖的隔离 Debug 编译，退出 0、零警告/错误，日志保留；**没有运行测试或性能采样，不代表全解决方案或 E6-A 完成**。正式 EXE C50742AD 未变化，无部署/提交/推送。下一步继续其余宿主测试消费方、运行只读检查正式授权/生命周期接线、作者端去 World、资源/脚本依赖闭包与显式变更桥；E6-A/B/C 实现结束后统一测试，E5-D 继续延期。

E6-A Gameplay 测试图编译恢复（2026-10-23，最新）：剩余 UI 输入、增量编码/提交、两种成本基准和 PressureProfiles 均接入 `RuntimeSceneInstance`。测试专用 `RuntimeSceneTestTransactions.CommitMetadata` 在复制元数据上准备修改，经真实 `World.BeginStep/PrepareStepView`、运行元数据预检及原顺序发布，不直接写运行字典，不创建编辑文档或另一 World。Runtime/Scene 对既有 Gameplay.Tests 增加精确 friend 声明，仅支持这些内部协议用例，没有新增公开 writer。保留外部生产者启用/类型更新、缓存失效、非规范 Export 顺序、检查点身份恢复及 UI 旧戳丢弃；UI 分支现在实际修改 Export 并检查原 World/对象名不变。

基准迁移不延续错误证据归属：Play 三种负载的转换在测量循环外完成，操作及结果校验均针对运行 World，冷摘要采用运行检查点。输出改为 `ecs_e6_diagnostic_*`，显式记录实际状态格式/版本并标记不能兼容历史配对合同；v2 内存观测与原始测量循环次数/断言保留，没有改为强制 GC 或放宽阈值。旧 E3/E5 common 配对入口在测量前拒绝当前模型，应使用归档旧产品回放历史；读取类诊断合同未在本片迁移。新 E6 正式配对合同及性能验收仍未完成，所有新基准本轮未运行。

修改前 5+6 份源码在 `pre-gameplay-metadata-migration-first/manifest.json` 和 `pre-runtime-baseline-migration-first/manifest.json` 归档并复核哈希。第一次 `compile-gameplay-metadata-first` 编译退出 1，保留剩余 7 处旧构造错误；完成基准迁移后 `compile-gameplay-runtime-first` 全 Gameplay 测试项目及依赖隔离 Debug 编译退出 0、零警告/错误。两份日志保留在对应归档目录；不是测试通过，也不证明全解决方案可编译。正式 EXE C50742AD 未变，没有测试、部署或提交推送。后续继续 Player/其他宿主调用迁移，以及运行检查正式接线、编辑模型去 World、资源/脚本闭包和显式变更桥；E6-A 尚未完成。

E6-A 有界运行检查实现记录（2026-10-23，最新）：新增 `RuntimeSceneInstance.Inspection.cs`。`GetInspectionStamp` 给出版本、运行实例 SessionId、WorldId、revision 与 tick；`InspectObjects/InspectComponents/InspectBindings/InspectExports` 必须提交精确当前戳，旧戳、跨实例、非 owner-thread、未提交候选及重入读取拒绝。该戳不是 Play 生命周期票据或权限，不替代宿主的批准/可见范围/连接代次检查；本片没有新增 MCP 入口或自动授权。

每页最多 128 项，完整 JSON 响应（含戳、对象/绑定身份和分页信息）最多 256 KiB；过大响应明确失败，可缩小页数，不静默删字段或截断。对象页按创建顺序，组件页按 TypeId，绑定和 Export 保留运行存储顺序。查询仅缓存有界 Entity 身份，返回仅持久 UUID 与复制值；不返回 Entity/裸存储。组件读取最多编码单对象的 64 个既有有界组件，不构造全场景快照；绑定概览与 Export 分开分页，避免单个大绑定强制完整输出。编码位于 World 只读与实例重入保护内，输出归一化并拥有独立数组/JSON；没有第二个 World 或编辑 Undo。

新增 `RuntimeInspectionTests` 8 组（分页顺序/无完整快照、独立副本与负零、各戳字段拒绝、元数据/World/tick/恢复失效、分页与身份边界、响应预算、线程/候选、编码回调保护），累计新增 E6 用例 26 组，均未执行；原迁移的 11 组元数据覆盖不算新增。`EcsJointCases` 的 15 种对象规模/写负载组合迁移到真实运行实例、分页检查及冷检查点往返，另检查原作者文档字节不变，不再创建 EditSession 检查运行状态。

修改前三份源码与编译日志保存在 `out/verification/ecs-e6/pre-runtime-inspection-first`。`compile-runtime-inspection-first` 场景测试工程隔离 Debug 编译退出 0、零警告/错误；`compile-runtime-inspection-gameplay-first` 全 Gameplay 测试工程退出 1、零警告、13 处旧构造调用错误，剩余 IncrementalGameplayCases、IncrementalCommitCases、GameplayUiCases、EcsBaseline、EcsBaselineV2、PressureProfiles 六文件。未运行任何测试、未替换正式 EXE、未提交推送；E6-A 尚未完成，后续继续正式宿主接线及剩余架构边界，不以限定编译结果冒充阶段验收。

E6-A Gameplay 测试夹具迁移记录（2026-10-23，最新）：`Program.cs` 生命周期用例、`StructuralBatchCases`、`MovementCases`、`SimulationCostCases` 改为先创作后转换，Play 仅接收运行实例。结构/Movement 的持久 UUID 在运行 World 重新解析，不携带作者端 Entity；回滚字节比较与恢复采用运行检查点。编辑隔离用例保留原 EditSession/历史/编辑字节并直接烘焙，额外检查运行位置变化而编辑位置不变；Movement 的权限隔离检查改为从同一不可变镜像加载独立实例，不用编辑格式反序列化运行数据。成本测量次数、严格分配断言和数值故障边界不变，所有用例未执行。

修改前四份源码分别归档在 `pre-gameplay-batch-migration-first/manifest.json` 和 `pre-gameplay-lifecycle-migration-first/manifest.json`。全 Gameplay 测试项目隔离 Debug 编译 `compile-gameplay-migration-first` 退出 1，零警告、14 处类型错误，均为尚未迁移的 7 个文件传入 SceneDocument：EcsJointCases、IncrementalGameplayCases、IncrementalCommitCases、GameplayUiCases、EcsBaseline、EcsBaselineV2、PressureProfiles。四个已迁移文件没有报告编译错误，但没有成功生成整个测试程序集；原始编译输出保存在生命周期归档 `compile-first.log`。下一片保留元数据变更和联合检查语义继续迁移，尤其运行检查不能恢复成 EditSession 包裹运行 World。正式 EXE 的 C50742AD 哈希保持，无测试、部署、提交推送；E6-A 未完成。

E6-A 旧运行校验清理记录（2026-10-23，覆盖下方旧状态）：已删除 `SceneDocument.RuntimeValidation.cs`，原 11 组 `RuntimeValidationTests` 覆盖迁移至直接使用运行实例事务的 `RuntimeMetadataTests`。删除前的三份源码保存在 `out/verification/ecs-e6/pre-runtime-document-removal-first/manifest.json` 对应归档，可恢复。场景测试项目隔离 Debug 编译零警告/错误，输出位于 `compile-runtime-metadata-migration-first`；**未执行测试**。这 11 组是旧覆盖迁移，不计作额外新增测试；此前 18 组新增 E6 用例仍未运行。Gameplay/Player 等旧测试调用尚待迁移，不能声称所有测试工程编译完成。E6-A 尚未完成，所有小阶段测试仍统一在整体阶段结束后执行。

E6-A 正式运行消费者切换记录（2026-10-23）：`PlaySession(RuntimeSceneInstance)` / `PlaySession.Scene` 取代原编辑文档 API，旧构造入口/属性直接删除，未加兼容别名。固定步命令、状态/成本、UI 输入、恢复检查及 Movement 均引用运行模型；耦合及动画组合的启动恢复使用会话限定检查点。`RuntimeSessionOwner` 不再持有文档，独立管理线程/脚本目录租约与 Play 生命周期；Editor 在源文档只读保护下将已提交 revision 转为不可变运行镜像，加载后才启动 Play，保持失败资源保留和唯一 EditSession/Undo。Player 的编辑格式读取收束到 `PlayerSceneLoading` 冷边界，模拟、图形与最终状态摘要不引用该编辑对象。共享 `ISceneContent` 只描述拥有独立副本的冷快照，渲染/资产/角色/UI 校验无需把运行格式转回编辑格式；没有新增双向同步 World。Player 报告 schema 升为 2，`StateSha256` 对 `ncma.runtime-scene` v1 状态编码求哈希，不与原 authoring 哈希混称等价。

编译记录：`pre-play-cutover-first/manifest.json` 修改前归档 63 份产品源码。第一次 Player 编译遗留 `CaptureBytes`、第二次从 Player 调用内部 `ReadOnly`；Editor 第一次编译有两处 Presenter 的旧 `Document` 属性；已修正并保留输出/诊断，没有恢复旧 API。最终 Player `compile-play-cutover-third`、Editor `compile-editor-cutover-second` 两个隔离 Debug 托管图编译均退出 0、零警告/错误，日志在同一归档目录；未运行任何测试或完整原生构建，正式 EXE 未替换。旧测试夹具仍调用被删除 API，接下来须保留覆盖语义迁移它们；不得以产品编译冒充所有测试工程可编译。`SceneDocument.RuntimeValidation` 产品已不再使用，但旧测试尚依赖，下一片迁移并删除；作者端 World 移除、依赖闭包/显式变更桥/有界检查仍待做。E6-A 未完成，E5 延期与 E6 末统一测试规则不变。

E6-A 第二片实现记录（2026-10-23）：运行模型新增独立 `RuntimeSceneSnapshot` / `RuntimeSceneCodec`、会话限定不可变检查点与显式 revision 受检恢复；组件仍只存于该实例的唯一 World，检查点不是同步镜像或编辑 Undo。`RuntimeSceneInstance.PrepareRuntimeStep` 对脚本元数据归一化、UUID/Export 唯一性、负零及总体编码预算做候选校验，按实际运行格式计算包络，不逐 tick 生成完整快照。`World.PreparePublicationGuard` 检查具体已提交候选对象/编码引用、变更号、World 身份及 revision，使放弃批次不能仅凭相同 revision 发布元数据。恢复会话检查点会令旧 Entity 失效；跨会话恢复拒绝。加载也会重新校验注册表归一化后的组件+绑定总体预算。

共享 `BehaviourBindingRules` 从编辑文档校验器提取，现有作者端/运行命令消费者已改用新规则，旧方法及常量移除，无兼容转发。7 份受影响源码在修改前保存在 `out/verification/ecs-e6/pre-runtime-state-first/manifest.json` 对应归档中。新增 `RuntimeSceneStateTests` 8 组并登记，累计 18 组 E6 用例未执行。两个隔离 Debug 编译（场景测试项目及 Gameplay）均零警告/错误，输出分别为 `compile-runtime-state-first`、`compile-runtime-state-gameplay-first`，不是测试通过，不复用旧 E5 结论。下一片正式迁移 PlaySession/RuntimeSessionOwner、Movement 的启动恢复、渲染的冷快照消费者及 Player，随后移除旧运行文档路径；目前仍有这些依赖，不能宣称 E6-A 完成。E5 测试延后、E6 整体结束统一测试规则不变，未部署或提交推送。

E6-A 首片实现记录（2026-10-23）：新增 `managed/Ncma.Scene/SceneDocument.Bake.cs`、`RuntimeSceneImage.cs`、`RuntimeSceneInstance.cs`。转换只接受当前已提交 revision，组件归一化/总体字节预算和原组成策略保留；额外运行组成策略只收到隔离副本，不能写源 World/文档。运行内容保留 UUID/对象顺序、组件及脚本绑定，独立 format/version/content hash，不包含编辑会话戳或 Entity；来源戳单独返回。加载创建独立 World 并再次校验注册表，无有效实例的部分替换；脚本元数据不与已销毁/复用 UUID 的不同 Entity 混用。此片未引入文件格式兼容入口，也不接管 Undo。

验证状态：新增 `RuntimeSceneBakeTests` 10 组用例已登记、未运行；`dotnet build managed/Ncma.Scene.Tests/Ncma.Scene.Tests.csproj --configuration Debug --artifacts-path F:/NcmaEngine/out/verification/ecs-e6/compile-model-separation-first --nologo` 编译退出 0、零警告/错误，不代表任何测试通过。登记前按原 SHA 归档唯一被修改的现有测试入口到 `out/verification/ecs-e6/pre-model-separation-first`，原 E5 源码证据保留。未部署、未提交推送。下一片迁移运行脚本元数据事务、PlaySession/运行服务及 Player，随后移除编辑模型中的运行权威；不得以 SceneDocument 包装器冒充分离。完整资产/catalog 依赖闭包、变更桥与有界运行快照尚待实现；此片不是完整 E6-A 验收。

2026-10-23 最新用户决策（覆盖下方历史执行顺序）：E5-D 剩余测试延期，立即转入 E6 开发，不以 E5 收尾作为 E6-A 的启动门槛。所有小阶段测试统一放到所属整体阶段实现结束后；E6 按 A → B → C 实现并准备测试，再统一冻结候选、运行各片及联合测试、修复与复测。开发期间仅做必要静态/编译检查，不将其标为测试通过。既有 E0–E5 证据和失败记录保留，延期测试及性能/目标机/人工债务不改判；历史80983运行中状态已由用户要求中断取代。当前规则与范围见 [路线图](ROADMAP.md)。

2026-10-16。用户已批准按方案实施。顺序：M8.2 自动候选交付 → 本重构 → M8.3 及后续 → M9 Windows。
本文件规定目标和门禁；各项是否实现以第 7、8、9 节的状态和证据为准。M8.2 基点为 `51010c33755ab35ac5fa3f45bc297c074ea3addb`。

当前E5-D目标预算已由用户确认，见[11.27](#1127-e5-d-用户确认目标机预算)。11.24–11.26中的“目标预算待确认”是当时的历史状态，不再代表当前待决项；历史合同和测量证据不改写。

2026-10-23 路线图追加：用户要求新增编辑模型与运行模型分离、元数据与访问代码生成、声明读写依赖的任务调度；已作为 [路线图 E6 架构演进](ROADMAP.md#e6-架构演进新增未实现) 的 A/B/C 三片规划，完整能力均未实现。计划在 E0–E5 收尾后、M8.3 前实施。E0–E5 仍遵守下述单一运行权威、owner-thread、唯一发布和历史合同；E6 须另立合同及源码冻结，不在当前测试中修改模型或引入多线程，不追溯改写既有验收证据。

## 1. 不变边界

- C# `Runtime.World` 是唯一实体/组件权威；不得新建与其镜像同步的第二个 World。
- GameObject 是 Entity 的高层组合式入口，不要求继承；脚本与批处理 System 使用同一份组件。
- 扁平对象组织、可选 Transform、单对象删除。骨骼/动画/UI 自身层级不成为场景父子树。
- C# 承担游戏逻辑。Python 仅可选独立模块，异步接收有界快照并返回提案；不得等待推理或 IPC 后再完成 tick。
- C++ 保留数值/渲染/物理等性能插件；版本化批量 C ABI 不携带托管引用、STL 或 ECS 内存地址。
- UUID 是持久身份；运行时句柄不得进入 SceneDocument、资产、Agent 提案或网络持久数据。
- 仅一个 EditSession/Undo；编辑事务继续允许完整快照。网络独立，无 GameObject RPC/复制标志。
- 保留现行 `.ncmascene`/组件 schema，不恢复旧格式、旧入口或兼容空壳。

## 2. 身份与公开 API

目标 Entity：`WorldIdentity + Index + Generation`，默认值无效。World 身份变化、删除、槽位复用、恢复、越界和跨 World 都必须拒绝陈旧引用。
代次溢出时退役槽位或明确拒绝，不绕回。恢复只在完整准备成功后更换身份；失败不能使有效句柄失效。

第一版保留现有对象的 UUID；UUID-less 瞬态实体不在此次范围。UUID→Entity 为冷路径索引，查询/批量更新不逐组件反查 UUID。
GameObject 惰性创建；不能为每个 Entity 强制分配包装对象。包装相等性依赖实体身份，不依赖引用相等；旧包装不得重新绑定新实体。
现有 `Ncma.Runtime.GameObject` 和 `Ncma.GameObject` 消费者迁移后收敛为单一公开门面，不长期保留两层逐对象包装。

程序集依赖须保持单向：低层 Runtime 提供 Entity/World/组件访问，不引用 Managed 或 Gameplay。现有用户侧 `Ncma.GameObject` 可直接持有 World 服务和 Entity；移除其中的 `Ncma.Runtime.GameObject` 二次包装。Runtime/Scene/渲染等低层消费者改为 World + Entity API；Behaviour 上下文仍由 Gameplay/Managed 服务注入，不能为了门面合并把脚本生命周期搬入 Runtime。实施时统一迁移 Create/Find/Resolve/GetObjects 消费者和两个 ObjectReference 表达，不添加旧签名转发壳。

## 3. 存储与 Query

首选强类型 Sparse Set：每种 `T` 独立 dense entity/value 数组及 sparse 索引。热循环不 `object` 装箱；字符串等现有合法值成员仍支持，不能草率改为全量 unmanaged 限制。
名称/注册元数据为冷数据；Behaviour 生命周期由 Gameplay 持有；资源使用有生命周期约束的句柄。

候选页/COW 与脏写集用于 E3，具体页大小须实测；E0 仅冻结原实现基线，不能从原实现数据推断最优页大小。
查询支持有界 All/None、常见多组件组合；复用查询计划，不缓存可跨提交使用的裸指针。成员版本、数据版本和 World 身份分开。
默认按创建序号确定性遍历；既有 UUID 排序消费者保持语义。dense swap-delete 顺序不成为公开承诺。
结构变化后更新成员缓存；未提交创建的实体不可出现在公开查询。公共 API 返回副本/受检写入；不提供不受限 `ref`、跨 await/tick 的可写租约。

## 4. 固定步与失败语义

顺序：Begin → 输入/信号 → Behaviour/System 暂存 → 全批预算/依赖/权限预检 → 必需时物理预检及求解 → 元数据/实例/回执/插值准备 → 最终验证 → 发布 tick/状态 → 只读观察者。

- 读取仍看已提交状态，后一个 System 不隐式看见前一个 System 的暂存写入。
- 同批重复目标拒绝；跨批按确定性顺序 last-write-wins。无效结构命令即使被调用者捕获，也毒化整个候选步。
- 外部写权限、运动权威和冻结成员资格不能被直接组件访问绕过。
- 所有可能失败的验证、分配、扩展回调在发布之前；发布点不执行任意扩展/IO。
- 移除每帧完整文档不能移除它承担的验证：类型/schema、归一化后 payload、对象/组件/绑定预算、引用依赖、全局精确编码预算均须保留。
- 可缓存每项精确编码/长度和聚合预算；跨对象验证器需明确依赖。无法增量化的可信验证器保留显式有界冷路径，不能静默跳过。
- 提交前失败回滚托管候选组件/结构/信号消费/回执；私有 Behaviour 状态与外部 IO 不回滚。
- 原生求解进入 `ExecuteAndStage` 后不可声称数值回滚；故障 fail-stop，必须显式恢复。
- 提交后观察者出错只故障 runner，不能撤销已提交 tick。编辑持久化补偿失败保留证据并冻结。

## 5. 分阶段工作及放行

| 阶段 | 交付 | 必须通过 |
| --- | --- | --- |
| E0 | 本合同、消费者/测试映射、可重复原实现性能基线及差距清单 | 100/1000/4096 对象读/联合筛选、固定步零/稀疏/全写、结构操作；真实领域成本证据索引；固定数据和原始样本；噪声说明 |
| E1 | World 原地替换身份与强类型池；UUID 映射；GameObject 同实体门面 | 失效/复用/溢出/恢复/跨 World/线程；可选组件/字符串；严格场景往返；所有受影响消费者编译；此阶段可保留旧全量提交机制 |
| E2 | Query 计划与确定性顺序；有界结构批次/待提交句柄和回执 | All/None/组合交集、成员变化、迭代失效、命令预算、全批预检、没有前缀提交 |
| E3 | 增量固定步写集、绑定/实例/信号/回执/插值 | 原有事务/权威/私有状态/观察者/物理故障范围等价；保留全部预算；稳定帧不构造完整 SceneDocument/World JSON |
| E4 | 角色/动画/渲染/编辑/Agent 消费者迁移 | 真实 Editor/Player/Headless 联合回归；保留批量 C ABI 与唯一命令历史；Agent 不增加写权限 |
| E5 | 精确旧机制审计、完整双配置回归、测量对照、交付记录 | Debug/Release 无 Skip、原格式/ABI/权限/包/备份恢复/无 Python 门禁；确认收益与小场景退化范围；提交推送后才进入 M8.3 |

每片先测试再提交推送；不通过先修复并保留失败证据。旧类型在替代消费者就绪的片内删除，不拖到 E5 才删除；E5 是残留审计，不是保留兼容层的理由。
E0 不修改正式存储或部署 EXE。后续运行时代码改动必须建立新源码冻结，不能沿用 M8.2 的 906 文件冻结作为新源码证据。

## 6. 性能判定协议

- 同机同配置，固定输入、工作量、语义输出；Release 独立进程至少三次，保留原始样本和环境/程序集身份。
- CPU 分项和整体固定步同时记录 P50/P95/P99、线程分配、GC；内存采样注明进程累计峰值与局部值区别。
- GPU/上传/原生驻留须从实际领域工作负载获取，不能用 CPU 合成场景替代。
- 查询预热后无逐实体分配/装箱为目标；有结构修改/输出复制时分开核算。
- 原方案的“热点 ≥20% 改善、小场景 P95 ≤5% 退化”为待测目标，不是当前已达成或噪声不明时机械通过的数字。E0 三轮波动超过阈值时增加样本/隔离测量，不能放宽阈值冒充收益。
- 容量仍为现行 4096 对象和各域既有预算。不得单独增大 ECS 容量而绕过文档、Agent、插件、资源约束。
- 人工、真实用户素材、目标机、自包含、完整性能、1h 与历史故障调查独立保留。

## 7. 实施状态

E0 合同/自动基线门禁完成并已提交推送 `936b51f7f38e072fb554be9c820ddf24752cb3ba`。原实现基线工具位于 `managed/Ncma.Gameplay.Tests/EcsBaseline.cs`；详细证据、时间噪声和仍开放的正式性能门禁见 `ECS_E0_BASELINE.md`。

E1 分两个可独立回归的实施片，整体门禁不变：

- **E1-A 强类型存储**：在唯一 World 内替换逐对象 `Dictionary<Type, object>`，建立按类型 dense 值数组/slot 数组及有界 sparse 索引；保留现行公共身份和完整快照事务。内部 slot 不是公开 Entity，不能序列化或用于绕过 World 校验。整池首次写复制是此片的过渡提交策略，不是 E3 的分页/增量实现。
- **E1-B 身份与单一门面**：引入 WorldIdentity/Index/Generation、代次溢出退役、失败候选句柄永不复活、确定性创建序号；迁移所有消费者和两种 ObjectReference；用户侧 GameObject 直接访问同一 Entity，删除被替代的 Runtime.GameObject，不添加旧 API 转发壳。信号/批次目标同样校验完整身份；注册/序列化负例覆盖组件内嵌套运行时 Entity，不能因为它是值类型便允许持久化句柄。代次保留/烧号不能随候选回滚而使旧候选引用复活。

E1-A 已实现并通过同源码双配置各 89 项 CTest 及补充验证，提交 `92a1b3084736a20ae45db6310d511b0786c3621f` 与远端一致，见 `ECS_E1_A_DELIVERY.md`。

E1-B 已完成两个增量：第一增量 `0fa22943ae8e955ebd3e67a44ac51a0ad580c9e1` 引入 Entity 代次、失败候选烧号/退役、两种旧 ObjectReference 移除及信号/批次完整身份；第二增量 `2926bdbfc7c314665ebf5d9d18972730389ecdfc` 迁移所有低层消费者到 World.CreateEntity/FindEntity/GetEntities 和显式组件访问，删除 Runtime.GameObject 与旧签名，仅保留共享同一存储的 Ncma.GameObject 高层门面。第二增量同一 913 文件冻结通过 Debug/Release 各 89 项 CTest、补充冒烟/Python 与最终文件核验，身份测试 15/15；失败修复和全部证据见 `ECS_E1_B_DELIVERY.md`。未保留兼容壳。

用户随后要求更新 EXE：该 E1-B 源码另行完成无 Skip Release Build.bat，13 原生 + 76 托管/产品 CTest、Python 45、三轮历史 profile、前后审计与受检部署通过。正式版本为 E1-B Release，Complete `7f3d3822dd39431b9c203e17194afb8c`；148 个安装文件和 148 个旧版备份文件核验通过，实际 apphost 校验 exit 0。证据 `out/ecs-e1b2-exe-full-release-first.log`、`out/ecs-e1b2-exe-verification-first.log`。这不是 E5 新旧配对性能或完整 ECS 验收。

## 8. E2 分片与 Query 契约

E2-A 查询基础与 E2-B 结构批次分别测试、交付。E2-A 已实现并提交推送 `5d46ef0596566cfff85370c84e3a36e5b84c58ca`，本片开始时精确核对本地与远端 main 一致。E2-B 已通过同源码完整双配置回归和最终交付核验，E2 自动候选门禁完成；E3–E5、M8.3 尚未开始。本片只构建候选，不替换用户刚更新的正式 E1-B EXE。

### E2-A 已实现的查询边界

- `World.Query<T>()`、`Query<T1,T2>()`、`Query<T1,T2,T3>()` 和 `CreateQuery(all, none, capacity)` 返回调用者持有的 `EntityQuery`。Runtime 不引用 Managed/Gameplay，也不在 World 中无限保留用户计划。
- All/None 合计最多 16 个注册类型；重复、交集、null、未注册类型拒绝。结果预算 1–4096，默认 4096。空 All 包含没有组件的实体，None 按类型存在与否排除，不按组件字段值排除。
- 多组件查询从最小 All 池扫描，做交集和排除后按创建序号排序。dense swap-delete、添加组件的顺序或槽位复用不会改变公开顺序。既有 UUID 排序消费者未迁移，语义不变。
- `World.Identity`、`MembershipVersion`、既有数据/提交 `Revision` 分开。MembershipVersion 是最近一次结构发布的 revision，不是结构操作计数。改名、既有组件值写入、空 tick 不改变成员版本；任何成功结构修改保守地使该 World 的查询缓存失效，包括最终成员相同的 remove/add。
- 固定步暂存结构不更新公开成员版本，不出现在查询中；成功提交才更新，失败或 Abort 不影响旧缓存/迭代器。恢复成功更换 World 身份，旧计划不能静默绑定新 World，须重新创建；恢复失败不使有效计划失效。
- 普通 `foreach` 使用受检 ref-struct 迭代器，不走 IEnumerable 装箱、不跨 await、不暴露池或可写 ref。每次 MoveNext/Current 检查所属 World、线程与成员版本；值写入后仍可迭代并通过 Get 读取提交值。结构提交后即使重新读取过该计划，旧迭代器也必须拒绝。
- `Count`、枚举或 `CopyTo` 超过计划预算直接拒绝，不返回截断结果；CopyTo 目标过小或计划刷新失败时目标不变。返回的 Entity 副本仍受原代次/World/写权限/冻结成员检查约束。
- 缓存只保存 Entity，不保存裸指针或组件值；预热且成员不变时遍历/读取/复制无需逐实体分配。建计划、结构失效后的重新筛选排序及完整 tick 的成本与热循环分开，不宣称 E3 或整帧加速。

```csharp
// 在所属 World 的 owner thread 创建并保留计划；World 恢复后重新创建。
EntityQuery moving = world.Query<TransformData>();
foreach (Entity entity in moving)
{
    TransformData value = world.Get<TransformData>(entity); // 值副本、已提交状态
    // 写入仍通过 World.Set 和既有权限/固定步规则，不提供直接池写入。
}
```

### E2-B 有界结构批次契约

2026-10-18 实施。`IRuntimeCommands.SubmitBatch(ReadOnlySpan<StructuralCommand>)` 接入既有 RuntimeCommandBuffer，由同一个 PlaySession/World 固定步拥有最终提交权。声明式命令包括 Create/Destroy/Rename/Add/Remove/Attach/Detach/Enable；不是执行任意委托的公开 API，也不是新编辑历史、Agent 写入口或 Python 调度器。

- **完整身份和创建依赖**：已有目标用 `StructuralTarget.Existing(Entity)`，校验完整 World/Index/Generation 后定位候选；不能用过期 Entity 命中复用槽位。`CreatedAt(i)` 只能指向当前批次中更早的 Create。`PendingCreation(handle)` 只能引用同一 session/generation/固定步 attempt 内已接收、尚未删除的创建；上一步已提交对象应通过 `Resolve(handle)` 获取 Entity，再用 Existing。
- **重复/冲突**：同批按“对象 + 变更字段”去重，name、每个 component type、每个 binding UUID 各为一个字段。因而 Create → Rename/Add/Attach 是合法依赖，不被误判为重复对象；同一组件 Remove+Add、同一 binding Enable+Detach、两次 Rename 拒绝。Destroy 在同批内对整个对象独占；创建后删除可在两个有序批次中执行。跨批以提交调用顺序执行，改名 last-write-wins，组件/绑定仍须满足每一步前置条件。
- **共享预算**：每固定步所有单命令与批次累计最多 1024 条、1 MiB 命令 payload；不能通过拆批重置预算。空批、超限、序号溢出拒绝。新批次每条额外计入 128 字节预算，字符串按 UTF-8，组件和绑定使用有界 JSON 编码；组件按原始/归一化后批次编码总量中较大者计费。单组件 64 KiB、每对象 64 组件、4096 对象、绑定/Export 容量和完整 World/SceneDocument 4 MiB 验证不放宽。输入 JsonElement、绑定 Export 数组复制；输出数组不暴露。
- **全批准备**：先做输入预算/目标/依赖/冲突预检，再在同一个 World 的当前 step 上建立 metadata/slot/组件池 COW 候选分支。逐条执行现有 Stage 权限、类型/schema、归一化、runtime-attachable 和冻结成员检查，最后执行完整 World/SceneDocument 编码及 binding 全局验证。所有可能失败的复制/回执队列分配发生在接入 step 候选之前；失败恢复此前候选根和绑定表，不能留下批次前缀。代次预约、创建序号、已预约 token 不回滚，避免失败身份复活。
- **失败仍毒化整个 step**：拒绝后即使调用者 catch，也不能提交此前其他单命令、组件写入或批次。输入/信号消费、tick、公开查询成员和成功回执只在原有最终提交点发布。后续 Behaviour factory、跨域最终准备仍可能拒绝已接收的批次；`StructuralSubmission` 不是成功回执。私有脚本状态、外部 IO 不回滚，原生物理仍遵守 fail-stop。
- **待提交句柄**：删除旧 `PendingObject` 及其消费者声明，使用不公开候选 Entity 的 `PendingEntity`，包含只读 ObjectId/Token 与内部 attempt/完整 Entity。旧单命令 SpawnEmpty 同样返回此类型，保留其余单命令现行业务语义，不增加旧类型兼容壳。该句柄不授予实时 World 写权限，不能通过普通 World.Resolve 提前获得实体。
- **回执生命周期**：`CommandReceipt` 增加完整 `Entity?` 和 WorldIdentity，成功 step 后才可 GetReceipt。创建同一步又删除得到 removed/null；只有 committed 且完整身份匹配的创建可 Resolve。删除/槽位复用、恢复 World、Stop/Reload、错误 session/generation、失败 step、淘汰回执均不能重新解析旧创建；缓存仍只保留最近 1024 条，不持久化运行时身份。
- **保留边界**：owner-thread-only；OnUpdate/只读准备拒绝写入；可信 validator 重入被拒绝且无法通过 catch 绕过毒化。Runtime 不依赖 Gameplay/Managed。组件读写仍经过 World 权限，没有直接池写、裸指针、不受限 ref 或多线程写入。

```csharp
// 仅所属 PlaySession 的固定步回调；组件值是受注册 schema 约束的副本。
StructuralSubmission batch = commands.SubmitBatch([
    new StructuralCommand.Create(Guid.NewGuid(), "Projectile"),
    new StructuralCommand.Add(StructuralTarget.CreatedAt(0), transformSnapshot)
]);
PendingEntity pending = batch.Created(0); // 尚不能 Resolve/GetReceipt
```

保存该句柄，在**整个固定步成功提交之后**的 owner-thread 回调中读取（不是在上述提交回调中紧接着执行）：

```csharp
Entity entity = commands.Resolve(pending);
CommandReceipt receipt = commands.GetReceipt(batch[0]);
```

此片仍保留全量快照验证和整池首次写复制；每批额外准备全量候选有成本，不声称它比逐条命令更快。E3 才处理增量写集/编码缓存；E5 才给出新旧配对性能结论。

### E2-B 验证与交付记录

- 新增 `StructuralCandidateTests` 8 组：候选根/池值无前缀、失败代次保留、提交前不可见、元数据失败、权限与冻结成员、多批 COW 隔离、捕获验证器写入、完整 4 MiB 世界预算、失效准备动作；`StructuralBatchCases` 26 组覆盖创建依赖、全部结构/绑定命令、重复/冲突、空/默认/外来/过期输入、原始/归一化/聚合预算、1024/1025 边界、迟发 factory 失败、输入回滚、回执淘汰、删除/复用/Stop/恢复/重试、复制输入、重入及 owner/read-only/序号溢出。
- 定向 Debug 最终 `out/ecs-e2b-runtime-debug-first.log` 和 `out/ecs-e2b-gameplay-debug-sixth.log` 通过：Runtime 13、storage 14、identity 15、Query 17、新候选 8；Gameplay 53、新批次 26、Movement 38、UI 18、E0 fixtures 11。Movement 此处仍是确定性 fake，不是新 Jolt 角色验收。
- 定向失败保留：Gameplay Debug second 是新增测试误用 InputFrame 参数类型导致编译失败；third 为首次获取焦点会按原契约丢弃按键边沿，而测试未先建立焦点，修正为先聚焦再提交边沿；fifth 是新增 fixture 两处 nullable 编译错误，改为明确检查已产生的提交。没有削弱产品输入规则、断言或预算；原日志全部保留。
- 全回归源码冻结 `out/verification/ecs-e2b/source-freeze-first.json`：920 文件，SHA256 `B6D3B8393373F18FDB94A6520ABAC211A87E5DB8A9BB983742D5E97BB6CD3BC7`。顺序执行两配置 Build.bat -SkipTests（不部署）后各完整 89 项 CTest，再做托管/原生冒烟、Python 45/inspect 和包/正式安装/备份/E0 原始程序集核验。
- Debug/Release 第一轮标准构建均零编译警告，完整 CTest 分别 **89/89，1057.84 秒**、**89/89，935.32 秒**。两配置新候选 8/8、新批次 26/26、原 Runtime 13/storage 14/identity 15/Query 17/Gameplay 53/Movement 38/UI 18/E0 fixtures 11 全通过；smoke/Python 45/inspect 全通过，构建前后均核验同一 920 文件冻结。顺序会话 23322 已终止 exit 0，没有待轮询构建或测试。
- 日志 `out/ecs-e2b-build-{debug,release}-first.log`、`out/ecs-e2b-ctest-{debug,release}-first.log`、`out/ecs-e2b-ctest-{debug,release}-details-first.log` 和 `out/ecs-e2b-{supplemental,smoke,python,inspect}-{debug,release}-first.log`。本轮两个完整配置均首轮通过；先前定向失败证据照常保留。
- Debug 候选 `e577b51b849049b3979b16c54ed56395`；Release 候选 `47f4cceb6906453e9ba274442320fde8`。两配置各五包的产品身份、路径、size/SHA256 与闭包核验通过；Editor/Null/DX11/Null-Physics/DX11-Physics 清单分别 144/56/113/67/116 文件，沿用原有有界 Editor 日志白名单。
- 最终 `out/ecs-e2b-delivery-verification-first.log` exit 0，SHA256 `8A2FD7C2A8877345DC791EB1D9671B40D7AA8EB80B7DD2195854F75A231BB439`：冻结相同、十包完整；正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 的 144 清单/148 实际文件及 148 备份完整，E0 原始 18 个程序集 SHA 不变。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未改变。
- 本轮没有新的无 Skip 部署、三轮历史 profile 或 E5 新旧性能配对验收；人工、目标素材/目标机、自包含、完整性能、1h 和历史故障调查仍开放。保留四项无关 IDE 设置，不将 out 产物/日志加入源码提交。下一步 E3 增量固定步，不进入 M8.3。

### E2-A 验证记录

首轮 Debug `out/ecs-e2a-runtime-debug-first.log`：原 Runtime 13/13、存储 14/14、身份 15/15，新增 Query 17/17 通过。覆盖 All/None/多类型、缓存更新、迭代失效、恢复、线程、权限/冻结、预算无截断、默认/越界迭代器、固定种子 600 次结构修改的独立创建列表对照、revision 溢出，以及 4096 实体预热后 20 轮遍历/Get/CopyTo 线程分配 **0 字节**。该分配结果不是整帧、GPU、真实领域收益或 E5 配对性能验收。

最终源码冻结 `out/verification/ecs-e2a/source-freeze-first.json` 共 915 项，SHA256 `02B257274860A1661F0288456F3613B08530B6CE406350226BC5A7E9FD0A42FC`。两配置 Build.bat -SkipTests 构建零编译警告，再顺序执行全部 89 项 CTest 和原生托管冒烟、Python 45/inspect。不部署、不覆盖 E0 原始 18 文件基线，不关闭人工、目标机、自包含、完整性能或长稳门禁。

首轮完整 Debug 为 **88/89，1109.42 秒**：`NcmaSceneDocumentTests` 的完整场景保存用例在未改动的 `SceneDocumentFiles.Save` 第 60 行 File.Replace 抛出 IOException（无法删除要被替换的文件）。原保存代码、测试断言和 E2-A 冻结均未修改；使用测试自身新生成的独立目录单独复测原用例 **1/1** 通过，不能据此声称已证明故障根因或关闭历史 File.Replace 调查。保留 `out/ecs-e2a-ctest-debug-first.log`、`out/ecs-e2a-ctest-debug-details-first.log`、`out/ecs-e2a-scene-recheck-debug-first.log` 及原测试目录；不删除失败目标、不采用非原子覆盖或吞掉异常。随后同源码完整 Debug 第二轮，再顺序执行 Release，最终结果另记。

最终结果：

- Debug 第二轮 **89/89，1110.90 秒**；Release 第一轮 **89/89，1000.85 秒**。两配置 Runtime 13/13、typed storage 14/14、identity 15/15、Query 17/17、Gameplay 53/53、E0 fixture 11/11 全部通过；4096 实体 × 20 轮热查询分配均为 0 字节。场景文档 36/36（包括严格新格式/旧格式拒绝）通过。
- 日志 `out/ecs-e2a-build-{debug,release}-first.log`、`out/ecs-e2a-ctest-debug-second.log`、`out/ecs-e2a-ctest-release-first.log`；完整用例输出 `out/ecs-e2a-ctest-{debug-details-second,release-details-first}.log`。补充验证 `out/ecs-e2a-{supplemental,smoke,python,inspect}-{debug,release}-first.log`；两配置 Python 均 45 项通过。
- Debug 候选 `3a411795b39f42ddb4da5d4b0f5e4b83`，Release 候选 `9e1fb6d5571f4e459e606d154ef9d0ec`。两配置各五个包的产品身份、路径、size/SHA256 和闭包核验通过；Editor/Null/DX11/Null-Physics/DX11-Physics 清单各 144/56/113/67/116 文件。未修改原有有界日志白名单。
- 最终 `out/ecs-e2a-delivery-verification-first.log` 通过，SHA256 `D21682225976424589E05D86C0569A9A3360C7FB71D2FAF714D2FD81EB568481`：源码冻结相同；正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 安装 148 文件（144 清单）和备份 148 文件保持完整，E0 原始 18 个归档程序集逐一匹配。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未改动。
- 最终顺序回归会话 57598 exit 0，所有构建/测试结束。第一轮失败会话 44267 exit 1 和失败证据保留。本片未执行新的无 Skip 部署或 E5 新旧配对性能门禁；只关闭 E2-A 自动候选门禁，不关闭整个 E2/ECS。

## 9. E3 增量固定步实施顺序

2026-10-18，E2-B 本地及 GitHub main 精确核对为 `cb50b4f25e9acfaa85ddf9568717ec30e1e254ea`。E3 要同时消除热点整池复制、稳定固定步全量 World/SceneDocument JSON、以及与变更量无关的高层重复准备。不能只完成分页存储就宣告 E3 完成；编辑 Undo/持久化冷路径仍允许完整文档。

| 分片 | 改动和依赖 | 放行条件 |
| --- | --- | --- |
| E3-A 存储候选 | 按测量选择值页大小；稀疏/实体索引结构性 COW；替换整值池首次写复制，保留当前完整提交检查 | 多分支共享/撤销隔离、跨页 swap-delete/清空引用、所有池准备后再删除、实际旧 DLL 读路径对照、页粒度测量、双配置完整回归 |
| E3-B 增量验证 | 建立候选写集和受限的内部已准备视图；逐项精确编码长度与聚合预算代替稳定帧完整 JSON；统一 World/SceneDocument 预算，不改变场景格式 | 原始与归一化后大小、对象/组件/绑定/Export、schema、UUID/代次/引用、跨域权限、溢出、失败恢复全部保留；自定义序列化/可信验证器无法证明可缓存时保留明确有界冷路径，不能静默漏检 |
| E3-C 高层提交 | Commands.Begin 不再捕获整文档；绑定与实例按变更准备；信号/回执按完整 Entity 检查存活；插值从候选强类型 Transform 读取，不经 JSON 解码 | 固定顺序、提交前读取旧值、单一提交器、生命周期/信号/输入一次性消费、删除/复用、观察者错误与原生 fail-stop 语义保持；不新增 AI/网络写权限 |
| E3-D 联合验收 | 对 A–C 做无写/稀疏/全写/结构变化与真实领域测试，检查稳定固定步没有完整文档构造 | 精确计数/负例证据、完整 Debug/Release 与包闭包；同输入旧实现/候选配对数据。人工/目标机/完整性能/长稳仍归原门禁，不用局部基准代替 |

每片先测试再提交推送；当前 E3-A/B 自动候选通过，下一步 E3-C 剩余高层提交，随后 E3-D 联合验收。信号过滤在 B 的视图迁移中已接入完整 Entity，但不代表 C 完成。E3–E5 通过前不启动 M8.3。

### E3-A 混合分页存储

- 生产值池使用 `PagedValues<T>`，当前默认每页 32 个值。固定容量仍为 4096；未写页不分配值数组。Fork 共享目录及页，分别更换父/子写入所有权；首次修改只复制目录及被修改页，不靠不可靠引用计数，也不暴露可写 ref。
- 稀疏索引和 dense slot 索引仍是连续数组，只有 Add/Remove 等结构变化才复制；普通值写入不复制这两组索引。结构性新容量/页分配在修改数据之前准备。RemoveAll 先对所有参与池做 PrepareRemove，再开始删除，避免后半池分配失败造成前缀删除。
- 读操作、公开 Entity、Query 缓存/顺序、权限、恢复和唯一 PlaySession 最终提交器不变；此片仍执行现有完整 World/SceneDocument 验证。没有新增并发写入、公开底层数组或 ABI。
- 删除已不再使用的整值数组复制实现。`WholePoolBaseline` 仅在测试程序集保存 E2-B 已提交的池算法，改名并补无分配 PrepareRemove 方法，用于局部比较；它不是运行时兼容类型、后备存储或第二 World，不进入产品包。

### E3-A 选择依据与测试状态

- 初版将索引也分页：功能测试通过，但局部读取间接访问显著增加，因此未作为交付布局。保留 `out/ecs-e3a-pool-release-preliminary-{first,second,third}.log`：第一轮未关闭分层 JIT，受预热阶段变化影响，不用于跨页策略的最终结论；后两轮明确关闭分层 JIT。最终采用连续索引/分页值，仍不声称消除了所有直接池读取成本。
- 新增 PagedStorageTests 10 组：懒分配/页大小边界、双向分支隔离、只复制触及值页、跨页删除、发布前准备/发布零分配、跨池准备失败无前缀、8000 次多分支随机映射对照、热读写零分配、分支分配不随对象数增长、引用清空。原 E1 字符串清空断言只改为检查分页值的所有 4096 个位置，没有删除断言。`out/ecs-e3a-runtime-debug-third.log` 全通过；此前 second 的测试参考类机械改名误改接口名导致编译失败，修复后通过，失败日志保留。
- 三个独立进程测量目录 `out/verification/ecs-e3a/measure-first`，每轮 90 个池样例（100/1000/4096 × 整池参考及 16/32/64/128/256 值页 × 读/空 fork/1% 写/全写/1% 结构变化），每项 100 个原始样本，每样本 16 次操作。32 页在小规模稀疏分配、较大规模写入和目录/页对象数之间折中，不声明所有工作负载最优。
- 同目录额外保存两组各四文件的隔离闭包，只有 Runtime DLL 不同：E2-B 来自其 Release 候选 `47f4cceb6906453e9ba274442320fde8` 且 SHA 匹配原清单；E3-A 来自本片 Release 构建。**同一个新测量程序**只调用两版本共有的 World/Query API，三轮交替先后顺序，每轮 6 个读取负载、每项 300 样本 × 16 次。它不是两套不同测试程序的历史时间比较；源码/输入哈希和原始结果保留。
- 最终测量使用 `DOTNET_TieredCompilation=0`，这是局部算法比较条件，不改变产品配置，也不能与 E0 未固定分层 JIT 的历史数字直接计算收益。最终交付核验已确认 925 文件冻结不变，标准 Release 构建的 Runtime/测试程序集与测量输入逐字节一致。
- 测量观察：混合池 fork 在 100/4096 元素下均 **144 B**；4096 元素、约 1% 均匀值写入每次分配 **54,632 B**，整池参考为 **196,728 B**。三轮同项 P50 为 **3.48–4.04 μs** 对 **45.21–47.70 μs**，只包括池算法，不包含 World 验证/文档/完整固定步。直接池读取仍慢于连续整值数组；实际 World/Query 读取全为 0 B，但 P95 波动明显，包括个别退化样本，**未证明 ≤5% 退化门禁或 E5 完整性能通过**。
- 本片源码冻结 `out/verification/ecs-e3a/source-freeze-first.json` 共 925 文件，SHA256 `3DE0E5D5C46BC97BD2BCBB32957EA1184E68963F7E38479C00B4B18B82565351`。标准双配置 Build.bat -SkipTests 零编译警告，完整回归和最终交付核验通过；正式 E1-B EXE 不部署、不替换。
- Debug 第一轮完整 CTest **89/89，1065.54 秒**；Release 第一轮 **89/89，981.89 秒**。两配置详情确认 PagedStorage 10/10，100/4096 元素池 fork 均 144 B，以及原 Runtime 13/storage 14/identity 15/Query 17/候选 8/Gameplay 53/批次 26/Movement 38/UI 18/E0 fixtures 11 全通过。托管/原生冒烟、Python 45/inspect 和前后 925 文件冻结复核通过。顺序会话 78237 已终止 exit 0，测量会话 13169 已终止 exit 0，无待轮询任务。
- 构建/测试/补充日志 `out/ecs-e3a-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-first.log`；CTest 完整输出 `out/ecs-e3a-ctest-{debug,release}-details-first.log`。Debug 候选 `724ea17c9c5b4877855b4ea18bf43f88`，Release 候选 `e9457c62902544deb8dfd8902d814fdb`。两配置各五包 Editor/Null/DX11/Null-Physics/DX11-Physics 清单分别 144/56/113/67/116 文件，身份/路径/size/SHA256/闭包通过；原有有界 Editor 日志白名单不变。
- `out/ecs-e3a-delivery-verification-first.log` exit 0，SHA256 `A33862C13D0D6A4E52ED2E931AEED970F456AC24A82E349072854AB58807F83A`：冻结/十包通过，正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 的 144 清单/148 实际/148 备份保持完整，E0 原始 18 个程序集保持原 SHA。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未变。
- 测量核验 first 失败：Windows PowerShell 5 的 ConvertFrom-Json 返回数组被额外 `@(...)` 包成单项，核验脚本误报闭包数量。仅修正 out 内辅助脚本的数组接收方式，不修改数据、产品、断言或预算。保留 first 失败日志；`out/ecs-e3a-measurement-verification-second.log` exit 0，SHA256 `26026528ADEBAD1BF1617A9BE8E710DB1D9845387182BEC6668262A47BD4DEC9`，确认原 E2-B 算法参考一致、归档 8 文件不变、标准 Release DLL 与测量输入一致、三轮池各 90 × 100 原始样本和旧/新公开读取各 6 × 300 样本及环境/实际加载 DLL SHA 一致。
- 仅 E3-A 自动候选门禁完成，不代表整个 E3/ECS 完成。没有新的无 Skip 部署、三轮历史 profile 或 E5 整帧性能验收；人工/目标机/自包含/完整性能/1h/历史故障调查仍开放。下一片 E3-B 增量精确验证；四项 IDE 设置与 out 产物/证据不提交。

### E3-B 增量精确验证（自动候选完成）

E3-A 已核对本地/GitHub main 为 `3d0a1fb329fbdae77bc5729a9878c9218e28e0e4`。此片不移除持久化、Undo 或完整快照格式；目标是让固定步预检使用同一 World 的受检候选和精确预算，不依赖整份 JSON 往返。

- World 派生编码缓存按槽/代次保存对象与组件编码、精确字节数，不成为第二组件存储。写集追踪对象及具体组件；普通值写只重新编码被改组件，改名复用组件编码。编码器实际输出的单项长度加原封套/逗号计算完整 4 MiB 预算，算术为 checked；不估算 Unicode、转义、数字或组件类型名长度。编码组件仍受 64 KiB 边界和注册 schema 检查；原始/归一化输入、结构批次 1 MiB 和聚合预算继续检查。
- 缓存资格由引擎保守判定：纯值字段/自动属性及递归稳定类型。属性不只看 CompilerGenerated 标签，还检查 getter 是否严格读取自己的一个实例字段；JSON 属性/自定义转换器/未知访问器走有界冷路径。可信值验证器仍在每次写入执行，没有新增可由 Agent 输入的“跳过验证”选项。动态编码结果每次预检重新测量并检查 payload/schema，不因没有 World 写入而跳过。
- 缓存仅随最终 World 提交安装；编辑直写和 Restore 的 revision/identity 变化会失效。候选回滚、分支丢弃、跨槽复用不污染已提交缓存。准备动作包含 attempt/变更序号/候选根检查；捕获编码/检查异常后仍毒化整步。结构分支还保留自己的脏集及已准备编码版本，避免丢弃分支后复用错误缓存。
- 内部 PreparedStepView 只提供受检元数据、完整 Entity 存活检查和字节计数；没有底层数组、可写 ref 或新权限。线程、写入、撤销、提交、替换及枚举恢复均检查有效性。需要旧 DTO 的现役消费者显式取得新数组壳，不能改动缓存。信号过滤已按候选完整 Entity 检查，不再先构造 WorldSnapshot 的 UUID 集。
- SceneDocument 的运行时预检已移除整文档 Encode/Decode；对象/文档封套和 Behaviour 编码按内容复用，组件数组长度来自同一受检候选。所有跨对象绑定 UUID、Export 类型/有限值/容量仍每次验证；缓存持有私有数组副本。双精度比较使用位模式，不能把 JSON 长度不同的 +0/-0 当作相同缓存项。元数据安装仍在唯一 World 提交后且校验目标 identity/revision，不引入第二事务或编辑历史。
- 现役 Play/结构批次已改用候选视图。**E3-C 尚未完成**：Commands.Begin 仍读取整文档；实例、回执和插值仍共用每步一个最终 DTO；World 元数据复制和信号/绑定遍历等成本仍在。本片测试不宣称完整固定步无全量文档或 E5 性能通过。
- 新增 Runtime 18、Scene 7、真实 Play 3 组验证；包括编码器实际字节 oracle、Unicode/转义、精确世界/文档边界、归一化超预算、组件级脏写、动态转换器/伪造属性标记、冷路径 schema、过期/跨线程视图、丢弃分支、缓存别名/回滚、负零、重复绑定和真实 Play 失败。原 Runtime 13/storage 14/identity 15/Query 17/结构候选 8/Paged 10、Scene 36、Gameplay 53/结构批次 26/Movement 38 fake/UI 18/E0 fixtures 11 保留。
- 定向 Debug/Release 已通过：`out/ecs-e3b-{runtime,scene,gameplay}-{debug,release}-tenth.log`，新增真实 Play 的最终定向结果为 `out/ecs-e3b-gameplay-{debug,release}-eleventh.log`。原始 second 的测试静态字段未初始化编译警告、sixth 的迁移调用点未改完编译失败、eighth 的枚举器最后一项恢复漏检失败均保留；修复产品失效检查后通过，不降低断言。早期 SSH 沙箱只读访问失败后，经正常授权核验精确远端 SHA 成功，没有改安全设置。
- 完整门禁冻结 `out/verification/ecs-e3b/source-freeze-first.json` 共 931 文件，SHA256 `7F9CBF58AA36E298DB0432466DFC0AB094BE0F60A08015FBD258594AF3216D42`。顺序 canonical Build.bat -SkipTests、全 89 CTest、smoke/Python45/inspect 进行中；会话 81334，不启动重复构建。完整门禁/最终十包与正式安装/备份/E0 核验未完成前，不提交推送或开始 E3-C。正式 E1-B EXE 不变。
- 深度专项复查追加：字节长度可以分项相加，但 JSON 最大嵌套不能直接沿用单项限制。World/Scene 在组件 Data 之前增加五层容器，因此候选编码显式保留 `MaxDepth - 5` 的 Data 嵌套限制；不依赖当前 schema 检查恰好更严格。新增第 19 组运行时测试，用深度 1/26/27/28/31 与完整 serializer 的接受/拒绝结果对照。第一份冻结因此作废，原 Debug 二进制仍按原会话完成，不并行改其程序集；第一轮完整结果不作为修复后的验收。结束后另建 second 冻结和两配置全回归，保留 first 全部证据。
- 第一轮 Debug 二进制 **89/89，1102.43 秒**，smoke/Python45/inspect 通过；由于上述源码补充，门禁在回归后的冻结复核处按预期拒绝，81334 已结束 exit 1，未启动第一轮 Release。不能把该轮当作最终同源码通过。补充测试实际覆盖对象和数组嵌套，并与完整编码后的严格 32 层 JSON 读取共同对照；两配置 Runtime 19/19（thirteenth）、Scene 7/7 与 Gameplay 3/3（twelfth）通过。
- 第二份冻结 `out/verification/ecs-e3b/source-freeze-second.json` 931 文件，SHA256 `89FE13F9D9AA66DB81F56C0B5A8A7082B40CE7452B3CE502782E21EBFE622C91`；第二轮完整门禁会话 32308 正在执行。日志后缀为 second，检查脚本 `out/verification/ecs-e3b/verify-delivery-second.ps1`。完整两配置及最终核验完成前，E3-B 仍未交付，不进入 E3-C。

### E3-B 最终验证与交付记录

- 上述进行中记录为过程历史，以此处最终结果为准。第二份 931 文件冻结前后完全一致；两配置标准 Build.bat -SkipTests 均零编译警告，Debug 第二轮 **89/89，1076.11 秒**，Release 第二轮 **89/89，982.44 秒**。顺序会话 32308 已结束 exit 0；旧 81334 已结束 exit 1（源码补充导致冻结拒绝），不再轮询或重启。
- 两配置新增 Runtime **19/19**、Scene **7/7**、真实 Play **3/3**，原 Runtime 13/storage 14/identity 15/Query 17/结构候选 8/Paged 10、Scene 36、Gameplay 53/结构批次 26/Movement 38 fake/UI 18/E0 fixtures 11 全通过；托管/原生冒烟、Python **45**、inspect 全通过。完整日志 `out/ecs-e3b-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-second.log`，详细用例输出 `out/ecs-e3b-ctest-{debug,release}-details-second.log`。
- Debug 候选 `caa4790ddf74492abf53a5d2bbdcb983`；Release 候选 `25eaacb8544d42a6808861d51701293c`。两配置各五包 Editor/Null/DX11/Null-Physics/DX11-Physics 的清单分别 144/56/113/67/116 文件，产品身份、路径、size/SHA256 和闭包检查通过。未改变既有有界 Editor 日志白名单。
- 最终 `out/ecs-e3b-delivery-verification-second.log` exit 0，SHA256 `C7CFA27066C323B212AA65FF72EC2C80B7F6BF0574181A3305DBD7F2FAA04945`：冻结及十包通过；正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 保持 144 清单/148 实际文件、148 备份完整，E0 原始 18 个程序集逐一匹配。另核对 E3-A 归档测量 8 文件原始 SHA 未变。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未改变。
- 本轮只是 E3-B 自动候选，不部署、不执行新的无 Skip/三轮历史 profile 或 E5 整帧配对性能门禁。C# 候选视图、编码计数和完整 codec oracle 是正确性/路径证据，不是完整性能验收。保留所有早期失败、两份冻结、首轮 89/89 但冻结失效的记录、历史 File.Replace 调查和四项 IDE 改动。下一步 E3-C，不跳到 M8.3；人工/目标素材与目标机/自包含/完整性能/1h 仍开放。

### E3-C 高层提交（自动候选完成）

E3-B 本地及 GitHub main 精确核对为 `a430a2ab43daafa6d5c4e7db1af5e9402071205a` 后开始。此片移除运行步的完整文档/快照消费者，不删除现役作者编辑、Undo、持久化、Start/Reload 冷路径的完整格式。

| 消费者 | E3-C 路径 | 保留约束与成本 |
| --- | --- | --- |
| Commands.Begin | 有版本元数据副本；成功步复用根、绑定写入时 fork | 文档/World 变化失效；单命令和批次共享 1024/1 MiB |
| Scene 元数据准备 | 逐对象精确预算检查；未改根复用 | 全局绑定 UUID/Export 验证仍遍历；失败不安装 |
| Behaviour 实例 | 稳定根复用；变化时以类型和完整 Entity 匹配 | UUID/绑定排序、延迟生命周期、退休清理；仍检查存活 |
| 成功回执 | 候选 UUID 解析为完整 Entity | 仅提交后可读；旧代次/世界、删除和淘汰拒绝 |
| 插值 | 按值读取候选 TransformData | 仍遍历 Transform、准备输出；没有 GPU/整帧性能结论 |
| 作者编辑/Undo/持久化/Start/Reload | 原现役完整快照冷路径 | 不是旧格式兼容层，不删除也不伪称已增量化 |

- Commands.Begin 只在新 lineage、文档 revision 或 World identity 改变时复制私有 Behaviour/Export 元数据；成功步保留自己的元数据根。单条结构命令只在第一次修改绑定时复制字典，数组始终替换；批次仍是原子的候选分支，纯改名/组件批次不复制绑定根。失败候选不安装到已提交缓存；其他受信运行生产者删除对象后，提交准备过滤已失效的绑定 UUID。
- SceneDocument 保留原有全局绑定 UUID/Export 和精确文档预算验证；元数据相同时复用私有字典根，改名、删除、创建或绑定变化时才准备变化后的根。没有共享可写数组给公开 API、Behaviour 工厂或 Agent；工厂得到自己的 Export 数组副本。
- 内部受检候选视图增加 UUID → 完整 Entity 解析和按值强类型组件读取；提交前公共 World.Get 仍读旧值。视图仍由 attempt/mutation/root/线程检查保护，写入、丢弃分支、撤销、提交、替换后失效；没有可写 ref 或新公开权限。
- PlaySession 不再在固定步开始 CaptureSnapshot，也不再在最终预检 CopySnapshot。稳定绑定/实体状态直接保留实例数组；结构变化按 UUID/绑定 ID 原有顺序准备，类型与完整 Entity 相同才复用实例。检查发现原分支在相同绑定 UUID 改类型时会提前移除旧实例、漏掉清理；现在保留其退休记录，下一边界恰好 Disable/Destroy 一次。新实例的 Create/Enable 仍在下一步执行，失败工厂不发布任何高层状态。
- 回执通过受检候选解析存活 Entity，保留原提交后可见性、容量及世界身份检查；插值直接读取候选 TransformData，不解码 JSON。新对象、无 Transform、删除、暂停和旧值插值规则不变。初始化/Reload 插值也使用强类型候选；两者作者快照仍是明确冷路径。
- 无写/稀疏步骤的测试收紧为**没有最终 validation snapshot 复制**（原 E3-B 测试明确允许每步一个），并检查实例和绑定根复用。两配置定向 Runtime 增量 22/22、Scene 9/9、真实 Play 3/3、新高层提交 10/10，原 Gameplay 53/结构批次 26/Movement 38 fake/UI 18/E0 fixtures 11 全通过。新增覆盖完整身份/值副本/分支与线程失效、绑定复制别名、作者编辑/Restore 失效、启停及类型替换生命周期、工厂修改 Export/失败回滚、插值和确定性工厂顺序。
- 首次 Runtime 定向编译失败为测试误写 struct 返回值的 Position.X，改成完整值替换后通过，不改产品约束或断言。证据 `out/ecs-e3c-runtime-debug-first.log` 保留；最终定向日志 Runtime Debug second、Scene Debug first、Gameplay Debug second，Release 三项均 first。首次 Gameplay Debug 仅含旧及增量 Play 三组，新增十组以 second 为准。
- 932 文件冻结 `out/verification/ecs-e3c/source-freeze-first.json`，SHA256 `7BE7DAD2383F4D5510C7DC974D58A4A792A43B97BC1E1BE3B8E9B94355A3DEB1`。顺序标准 Build.bat -SkipTests、两配置全部 89 CTest、smoke/Python45/inspect 会话 65547 正在执行；不能提前标记全门禁通过或提交。正式 E1-B 安装/备份和 E0 原始基线待最终核验，不部署。
- 仍有 World 元数据复制、全局绑定身份验证、实例存活检查、Transform 遍历/插值数组与回执处理成本；移除整文档不是零分配承诺或整帧性能结论。E3-D 联合验收、E4 领域迁移、E5 性能与既有人工/目标机/自包含/1h 门禁保留。
- 第一轮期间的等价性复查发现：作者用 CopyBindings 会排序 Export，不能用于保留已恢复文档顺序的运行时复制。修正为保序数组副本，增加完整文档字节 oracle 和真实 Play 无写步骤测试。另对旧 JSON copy 的异常 UTF-16 规范化保留**绑定元数据专用**有界冷路径；规范化后的重复 Export 名仍拒绝。正常绑定保留纯数组复制/跨步复用，不重新序列化组件；新提交的非规范字符串仍按原语义在下一 Begin 规范化，不提前更改当前 factory 输入。新增 Scene 2 组/真实提交 2 组尚待重新构建。源码已与 first 冻结不同；65547 原 Debug 结束后应在冻结核验处拒绝、不执行 first Release。该轮及 first 冻结全部保留，修正后须重新跑 second 同源码完整双配置。
- 第一轮已结束：Debug **89/89，1082.12 秒**，补充 smoke/Python45/inspect 通过；随后源码冻结检查按预期拒绝，会话 65547 exit 1，未运行 first Release，不作为最终验收。最终修正源码重新完成两配置定向 Runtime 22/Scene 11/真实 Play 3/提交路径 12 组及原用例，日志 `out/ecs-e3c-{runtime,scene,gameplay}-{debug,release}-final-first.log`；定向会话 81486 exit 0。第二份 932 文件冻结 SHA256 `4DCCBEA037329B171672F659E3008E86BA27480E53F44911C34CA66160A27FC9`，路径 `out/verification/ecs-e3c/source-freeze-second.json`。第二轮完整两配置门禁开始；正式入口不变，全部首轮/定向失败证据保留。
- 第二轮 Debug **89/89，1079.76 秒**，smoke/Python45/inspect 及前后同 932 文件冻结复核通过；候选 `b7e40b7191314de7bc148e6e9f708b1a`，正式入口不变。顺序会话 14376 继续执行 Release 标准构建与完整回归，不启动重复任务。尚未完成双配置最终验收和交付核验，不提交。

### E3-C 最终验证与交付记录（2026-10-19）

上述进行中段落为过程历史，以本段为最终状态。E3-B 基点已核对本地/远端 `a430a2ab43daafa6d5c4e7db1af5e9402071205a`；E3-C 不代表整个 E3/ECS 完成。

- 同一第二份 **932 文件**源码冻结在双配置前后保持一致，SHA256 `4DCCBEA037329B171672F659E3008E86BA27480E53F44911C34CA66160A27FC9`。标准 Build.bat -SkipTests 两配置零编译警告；Debug 第二轮 **89/89，1079.76 秒**，Release 第二轮 **89/89，968.99 秒**。顺序会话 **14376 exit 0**，全部构建和测试结束，不再轮询。
- 两配置增量 Runtime **22/22**、Scene **11/11**、真实 Play **3/3**、高层提交 **12/12**；原 Runtime 13/storage 14/identity 15/Query 17/结构候选 8/Paged 10、Scene 36、Gameplay 53/结构批次 26/Movement 38 fake/UI 18/E0 fixtures 11 保留并通过。托管/原生 smoke、Python **45**、inspect 全通过；没有以 fake Movement 代替新的真实 Jolt 角色验收。
- 完整日志 `out/ecs-e3c-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-second.log`；详细输出 `out/ecs-e3c-ctest-{debug,release}-details-second.log`。Debug 候选 `b7e40b7191314de7bc148e6e9f708b1a`，Release 候选 `748552c447514c89a50b86b7ddae835c`；两配置各五包 Editor/Null/DX11/Null-Physics/DX11-Physics 清单 **144/56/113/67/116** 文件，身份、路径、size/SHA256 与完整闭包核验通过。有界 Editor 日志白名单未修改。
- 最终 `out/ecs-e3c-delivery-verification-second.log` exit 0，SHA256 `0FC5849505CA90D9896C64E48B4C4065907CC141309F66170A508D596AB6D59D`：源码冻结和十包通过；正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 的 **144 清单/148 实际/148 备份**完整；E0 原始 **18** 个归档程序集、E3-A 原始 **8** 个测量输入与 **9** 份测量日志均逐一匹配。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未变。
- 第一轮 Debug 89/89 及补充通过，但追加保序/Unicode 修正使冻结失效；65547 已结束 exit 1，**不算最终验收**，没有 first Release。首次定向测试 struct 返回值赋值编译失败、两份冻结及全部日志保留。最终定向会话 81486 已结束 exit 0。四项 IDE 改动、out 候选/失败/备份不提交。
- 本轮只完成 E3-C 自动候选；无新无 Skip 部署、三轮历史 profile 或 E5 整帧配对性能验收。保留所有人工/目标素材与目标机/自包含/完整性能/1h/历史 File.Replace 调查门禁。按既定门禁提交推送并核对远端后，下一片为 **E3-D 联合验收**，其后 E4/E5，通过后才恢复 M8.3，最后 M9 Windows。

### E3-D 联合验收（进行中，2026-10-19）

E3-C 本地和 GitHub main 已核对为 `28aecf523c6dfc6b2634549f7ab4cd67cf592dc6`。本片同时检验 A–C 的路径不变量、真实产品领域回归和同驱动旧/新工作负载，不能只用局部池测试或冒烟代替阶段验收。

- 新增 `EcsJointCases`：100/1000/4096 对象 × 无写、稀疏写、全写、组件成员变化、1% 创建替换，共 15 组。实际 PlaySession 预热后四步检查完整 World 捕获、validation snapshot 复制、SceneDocument 捕获计数均不增加；无写/稀疏/全写检查精确组件编码增量。公共读取保持已提交视图，查询/最终值/暂停插值与完整 codec 冷路径对照一致。计数器私有，不构成新的公开写权限或性能 API。
- Debug/Release 定向 second 日志均通过联合 15/15、原 Gameplay 53/53、增量 Play 3/3、高层提交 12/12，以及结构批次、Movement fake、UI 和 E0 fixtures。证据 `out/ecs-e3d-gameplay-{debug,release}-second.log`。这是定向验证，不是完整双配置门禁；真实物理/动画/渲染/Editor/Player 组合仍须完整 CTest。
- `Measure-EcsPaired.ps1` 用同一个新测试驱动分别加载 E2-B 已交付 Release 与当前候选六个真实产品程序集；每侧闭包为六 DLL 加驱动 DLL/deps/runtimeconfig 九文件。校验原包身份、清单和复制 SHA/size、实际已加载程序集路径/哈希，并在工作负载外用完整场景编码摘要比较最终语义。测量模式三轮交替顺序，各 33 项 × 100 样本，保留原始时间/线程分配/GC/分层记录；TieredCompilation=0 是明确记录的局部实验条件，不是产品默认设置。
- 原 E0 工作负载计时区、12 次预热和 100 次采样不变；只增加计时外语义回调。初始化/Start/Reload/Undo 仍是冷路径，不宣称完全增量化。CPU 工作负载不代表 GPU、物理、推理、整个引擎帧或 E5 验收。
- 配对 smoke third 已完成：`out/verification/ecs-e3d/537c74946bda4ad78b7300dec6e5fa59`，旧 E2-B 与已交付 E3-C 各 11 项语义相等。**此处 E3-C 只用于证明驱动兼容，不是最终 E3-D 候选或正式测量。** 最终需标准 Release 构建后新归档并测量。
- 输入负例 second **11/11** 正确拒绝，原输入不变：`negative-8a6e4ba3ebd24042b872b32a9a05f4ef`，包括缺失/损坏 DLL、哈希/大小、重复/越界名、schema/variant、额外文件、坏 JSON 和多模式参数。失败前不能运行工作负载。
- 负例 first 保留：PowerShell 单项参数被展开成字符，缺失程序集在旧顶层入口 JIT 时抛出未捕获异常；修正为显式 string[] 参数与独立 BCL 入口捕获、被调用入口 NoInlining。原会话 79529 仍有三个已核对的辅助进程 17524/12936/7156，常规停止 17524 报错后未升级终止。已请求用户许可；未获答复，不强杀、不删除失败副本。正式测量须等这些进程结束后再进行，不能隐藏环境污染。
- 下一门禁：冻结最终源码 → 标准 Debug/Release Build.bat -SkipTests（不部署）及全部 89 CTest、smoke/Python45/inspect → 同驱动真实配对测量与摘要分析 → 同源码/十包/正式安装/备份/E0 与 E3-A 原始证据核验 → 文档与受限提交推送。任一失败保留证据、修正后重跑受影响门禁；目前不得标记 E3-D/E3/ECS 完成。

#### E3-D 全域回归范围与证据边界

已核对 CMake 的现役测试注册：下列领域包含在完整 89 项中，不能用 Gameplay 定向结果替代。表格是门禁映射，不表示正在执行的本轮测试已经通过。

| 领域 | 现役测试入口 | 本片依赖的证据；不能推导的结论 |
| --- | --- | --- |
| 唯一存储、查询、结构事务、回执 | NcmaManagedHeadlessTests、NcmaGameplayTests、NcmaSceneDocumentTests | E0–E3 断言及新增 15 组；不代表整帧性能 |
| 真实原生数值边界 | NcmaPhysicsNativeTests、NcmaCharacterNativeTests、NcmaPhysicsTests、NcmaPoseManagedTests | 原生插件/托管消费仍兼容；Gameplay 的 38 组 fake Movement 不代替这些入口 |
| 场景、动画、DX11、2D/UI | NcmaSceneRenderingTests、NcmaRenderingTests、SceneUiJointProducts、ShadowJointProducts、UiCostJoint、SceneCostJoint 四档 | 场景与真实渲染/输出组合、原有精度与资源约束；不新增 Vulkan/OpenGL 实现声明 |
| 世界替换与恢复 | GraphicsRecoveryJoint 四档、EditorGraphicsRecovery、PlayGraphicsRecovery、PlayerGraphicsRecovery、EditorRecoveryLoop | 完整 Entity 在世界恢复后的失效与现役恢复路径；不是 1h 稳定性验收 |
| 编辑/历史/资产/独立 Player | NcmaEditorCoreTests、NcmaEditorServiceTests、NcmaAssetTests、NcmaAssetImportTests、NcmaPlayerTests、NcmaEditorDeploymentTests | 原持久化、Undo、资产与包约束；不是手动 UI 或干净目标机验收 |
| 可选 Python 与传输隔离 | NcmaModuleTests、NoPythonProducts、Grpc、RelocatedClient、NcmaEditorTransportTests，另 Python45/inspect | 原可选模块与命令边界不退化；不是接入推理服务 |

静态冷/热路径复核：Gameplay 的完整捕获仅在组成生命周期注册、Start/Reload；MovementCoordinator 仅在构造和 `ICoupledStepParticipant.Start` 核对启动文档；RuntimeSessionOwner 的捕获属于脚本候选 Preflight（加载/重载）；SceneAnimatorRuntime 的捕获属于 Compose。它们不能被计入固定步收益，也不能以删除启动校验来改善数据。当前没有在这些模块的现役固定步发现完整捕获调用；该搜索是补充证据，动态 15 组计数检查和完整领域回归仍不可省略。

本轮第一份冻结 937 文件，SHA256 `498B12B4F35F5EF36255B6A75C2009E0260940B73FE64308432E834AC5972F3B`。Debug 标准 SkipTests 构建已通过，候选 `0397270b82dd4c0786267ea94b81c214`；第一轮完整 **89/89，1101.03 秒**，补充原生/托管 smoke、Python45/inspect 及前后源码冻结核验均通过。详细日志含 Runtime 增量22/Scene增量11/提交12/新增联合15组及原有领域断言。会话 11250 已进入 Release 标准构建，尚无双配置最终验收、最终配对测量或交付核验；不得以 Debug 单配置通过替代 E3-D 完成。

#### E3-D 双配置回归及包核验结果（配对测量待完成）

上述进行中记录由本段更新：会话 **11250 已结束 exit 0**。同一 937 文件冻结在完整 Debug/Release 构建、回归前后均一致；两配置标准 SkipTests 构建零编译警告，Debug **89/89，1101.03 秒**，Release **89/89，978.60 秒**，两边原生/托管 smoke、Python **45**、inspect 全通过。Runtime22/Scene11/提交12/新增联合15组与全部旧领域测试保留通过，不把 fake Movement 代替真实数值插件或将 CPU 合成检查冒称完整性能验收。

- Debug 候选 `0397270b82dd4c0786267ea94b81c214`，Release 候选 `881d1a0599444fe488d8ee47dbe1378e`；日志 `out/ecs-e3d-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-first.log`，详细测试输出另存 `ctest-{debug,release}-details-first.log`。
- 最终回归/闭包核验 `out/ecs-e3d-delivery-verification-second.log` exit 0，SHA256 `33796E7024A6B9DC0E238B2088715C850F35F7463021F6923A5907372228D2E2`。十包清单各配置144/56/113/67/116项通过；正式 E1-B 的144清单/148实际文件、148备份，E0原始18个程序集，E3-A原始8输入与9日志均保持原SHA/size。正式 EXE 仍为 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`，未部署。
- 交付核验 first 失败来自辅助脚本机械替换后误指向 E3-A 的 first 失败日志，旧正确 second 成功日志 SHA `26026528ADEBAD1BF1617A9BE8E710DB1D9845387182BEC6668262A47BD4DEC9` 实际不变。保留首轮日志与辅助脚本；second 仅修正历史证据路径，不修改产品、断言或历史文件。
- **本轮标准 Release** 新配对冒烟归档 `out/verification/ecs-e3d/11b670bfc7424fd0845c57bc8ca08153`：E2-B/最终候选各11项语义一致。对该新归档的负例11/11通过，目录 `negative-3956fc6f210a40c8878b614874ddb3e1`；日志 `out/ecs-e3d-canonical-paired-{smoke,negative}-first.log`。它仍是冒烟，不是33项×100样本×三轮的正式测量。
- 唯一当前待处理步骤：旧负例三个精确辅助进程仍存活，常规停止失败后的终止许可尚未收到。未杀进程、未删除失败文件。正式三轮配对测量须等待处置完成，之后复核新旧输入/原始样本/语义与全部分位数、分配和GC数据，再完成E3-D文档、受限提交推送。当前 **E3-D/E3仍未完成**；不跳到E4/M8.3，不把本轮回归通过等同E5性能/人工/目标机/自包含/1h验收。

用户随后明确批准仅终止三个已核对辅助进程。执行时17524/12936已自行退出，7156的完整命令行、镜像、父进程、精确创建时间及持有句柄的StartTime均匹配；仅对7156执行一次Kill，WaitForExit返回成功。原辅助脚本最后以CIM枚举记录是否消失为断言，遇到已退出记录残留而报错；此失败保留，没有重试更强终止。后续两次只读实际进程枚举均确认三者不存在，旧会话79529已结束exit0（不据此宣称旧负例测试成功）。证据 `out/ecs-e3d-approved-helper-close-first.log` 与 `out/ecs-e3d-approved-helper-exit-first.log`；所有文件保留。上述处置阻塞已解除，937源码冻结再次匹配，正式三轮配对会话88153开始；测量完成前仍不标记E3-D完成。

#### E3-D 三轮配对完成：发现全写回退，暂不收尾

会话 **88153 已结束 exit 0**。最终 Release 候选与原 E2-B 使用同一驱动、TieredCompilation=0，顺序为旧/新、新/旧、旧/新。归档 `out/verification/ecs-e3d/1977744a247f47769370bcb095f87dc2` 包含18份输入、6份运行日志和输入/完成清单；每轮每侧33工作负载，每项100原始时间及分配样本，共 **19,800 个时间样本和19,800个分配样本**。三轮各33个最终场景摘要完全相等，程序集实际加载路径/哈希匹配，输入前后未改变。

独立复核 `out/ecs-e3d-paired-verification-first.log` exit0，SHA256 `D41DE712DBACC312D6645FB1F3BD77B2772935B92B0171931FD0225B9661F0A3`，逐一验证九文件闭包、6轮日志、33个工作负载键、预热12/样本100、读取16遍归一化、原始数值和nearest-rank分位数。测量后的 `out/ecs-e3d-delivery-verification-third.log` 再次通过，SHA仍为 `33796E7024A6B9DC0E238B2088715C850F35F7463021F6923A5907372228D2E2`；同937源码、十包、正式安装/备份及原始基线均不变。

以下为 **4096对象、三轮各自P50的最小–最大范围**，单位毫秒；不是置信区间，也不是GPU/整帧/FPS结论。P95比值为同轮候选/旧版。

| 工作负载 | 旧版 P50 ms | 候选 P50 ms | 候选/旧版 P95 |
| --- | --- | --- | --- |
| World 无写 | 36.176–37.611 | 0.048–0.058 | 约0.002 |
| World 约1%写 | 36.512–37.000 | 1.490–1.574 | 0.054–0.060 |
| **World 全写** | **55.773–56.938** | **129.144–132.864** | **2.198–2.294** |
| Play 无写 | 230.946–231.404 | 1.512–1.632 | 0.009–0.010 |
| Play 约1%写 | 229.475–233.313 | 3.147–3.313 | 0.019–0.020 |
| Play 全写 | 251.254–252.211 | 133.113–136.074 | 0.504–0.567 |
| 每对象 Behaviour 全写 | 290.381–293.652 | 135.143–138.373 | 0.493–0.514 |
| Play 约1%对象替换 | 233.014–235.156 | 5.839–5.910 | 0.030–0.032 |

- 无写/稀疏/结构变化与高层Play的全量文档消除收益明显，但**不能只报收益**：直接World全写在100/1000/4096均回退，三轮P95比值总范围1.994–2.534；4096每步线程分配从20,152,776–20,153,088 B升为34,214,440 B。其100样本观察的Gen0回收154–155次变为232次；Gen1/Gen2减少不代表该路径成本改善。当前不得将E3-D/E3标为通过，不提交推送、不进入E4。
- Play无写4096线程分配从约77.30 MB降至2,219,048 B，稀疏写降至2,959,872 B；它们仍不是零分配。Play全写降至36,136,784 B，但不能掩盖其底层World全写回退。全部三轮原始分配/GC/分层统计保留在复核报告。
- `read_transform*` 负载有意沿用E0的 `GetEntities()` 复制枚举而非E2-A强类型Query；每遍5712/52512/213504 B在两版本相同。其P95比值存在跨轮波动（最高约1.511），不能用它宣称新Query退化或5%小场景门禁通过；E2-A的预热强类型Query零分配属于另一明确边界。
- 初步代码审查发现待验证热点：每次 `Set` 的Validate已序列化检查payload；提交期脏组件还要有界编码/解析/Clone、schema/depth校验与组件计量，并对脏对象重新计量不变头部。E2-B池Encode是直接SerializeToElement，当前为有界输出后Parse/Clone。**这只是候选原因，不是已证明根因**；不能直接移除64KiB/4MiB/depth/schema/归一化、每次可信验证器或unknown getter/converter保护来取悦基准。
- 下一片保持在E3-D：对上述成本做有界定向分项，优先消除稳定值重复编码和不变头部的重复计量；确保批次分支、重复写、删除复用、abort/restore、未知编码器及失败毒化语义不变，再用新冻结重跑定向、完整双配置和同驱动三轮配对。当前937版本及此次退化数据作为不可覆盖的对照保留；修复前不重复提交或部署。

#### E3-D 全写回退修复候选（定向通过，完整门禁尚未启动）

2026-10-19按用户“开始下一步”继续在E3-D修复，不进入E4。

- 先对原标准Release `881d1a0599444fe488d8ee47dbe1378e` 的真实Runtime DLL做隔离分项诊断，三轮各20,000调用：递归Schema校验约12.9–13.8微秒/3032 B，写入验证约4.4–4.9微秒/1280 B，提交期有界编码/Parse/Clone约4.7–4.8微秒/1768 B，组件计量约2.0–2.6微秒/184 B，对象头计量约0.46–0.54微秒/496 B。最大热点是反复解析Schema及其字段字符串/集合分配，重复序列化是另一项可消除成本。日志 `out/ecs-e3d-profile-first.log`；辅助源码在 `out/verification/ecs-e3d/profile-first`，记录实际程序集/MVID。这是隔离调用诊断，不是整步各项可精确相加的耗时占比。
- 新 `ComponentShape` 在可信注册时预编译既有闭合对象/标量/local-$defs检查。每次仍检查所有数据字段、重复/未知/必需字段、数值有限性和Int64范围；仅避免反复解析同一Schema。每对象最多32字段、展开最多4096节点、深度32；不支持、畸形、过大或循环Schema回到**未修改**的原递归检查，不添加新Schema方言，也不放宽原来对额外字段的拒绝。
- `World.Set` 原本已生成并验证的JSON，仅对现有IL证明稳定的值类型保留到当前候选步；可信验证器仍每次调用。缓存跟随候选分支复制/恢复/安装，包含代次，重复写取最后一次；组件增删使相应项失效，删除对象清理其项，abort全部丢弃。未知getter/converter仍走原有有界冷编码。Schema错误仍在准备提交时拒绝，没有提前到Set或跳过。独立组件值仍只在原强类型池中保存，不产生第二World或新写权限。
- 不变的Entry引用可复用原对象头精确字节数；重命名、替换、编辑/restore仍触发重新计量。组件64KiB、World4MiB、完整JSON深度、原始/归一化校验和失败毒化均保留。
- 新6组Schema等价测试包含1000个确定性随机payload，使用原未修改递归检查作为接受/拒绝oracle；另覆盖UTF8/转义名称、重复字段、嵌套引用、Int64/有限数边界、32字段末位和fallback。新增3组增量测试覆盖重复写与规范化、延迟Schema拒绝、分支丢弃/安装/abort、同槽不同代次和同代次移除再添加；原精确聚合边界、JSON深度、未知转换器与权限测试保留。
- Debug/Release定向均通过：Runtime13/storage14/identity15/Query17/结构候选8/Paged10/增量25/Schema6，Scene原36/增量11，Gameplay53/增量Play3/提交12/联合15/批次26/Movement38 fake/UI18/E0fixtures11。日志 `out/ecs-e3d-repair-{runtime,scene,gameplay}-{debug,release}-targeted-first.log`，会话99567已结束exit0，无编译警告。第一次缓存实现误将代次写为uint，CS1503保存在 `out/ecs-e3d-repair-runtime-release-second.log`，改为实际ulong后通过；没有放宽测试。
- 局部整步诊断每规模仅30样本：仅Schema优化的4096对象P50为80.8185ms，加入编码/头部复用后P50为50.2337ms、P95为66.7157ms、样本范围40.5535–70.3877ms，线程分配约14.081MB。日志 `out/ecs-e3d-profile-{second,third}.log`。早先进度消息44–48ms只引用最后两个样本，**不能作为完整分布或正式收益范围**。这些数据没有同轮旧版配对、不是最终标准候选，也未包含最后补充的删除缓存清理；不据此关闭原来的2倍回退或宣称E5通过。
- 完整门禁审批连续两次遇到服务容量错误：`Automatic approval review failed: Selected model is at capacity`；两次命令均**未执行**，不是产品失败或安全性否决。没有绕过审批降级执行；`source-freeze-repair-first.json` 和新gate日志均尚未生成。忽略目录中已准备 `freeze-source-repair-first.ps1`、`run-gates-repair-first.ps1`、`run-supplemental-repair-first.ps1`、`verify-delivery-repair-first.ps1`，等待审批恢复后按原流程执行。历史E3-A正确second测量证据路径保留，不作全局first/second替换。
- 下一步仍须新939源码冻结、标准双配置构建和全部89CTest/补充检查、最终标准候选配对冒烟与负例、三轮正式配对、十包/正式安装/备份/历史证据核验。全部通过前不提交推送、不部署、不进入E4；第一份937源码/全部退化样本、失败记录和四项IDE改动均保留。

#### E3-D 修复完整验证：首轮原子替换异常与同源码重测

用户要求“进行完整验证”后，审批成功；939文件冻结 `out/verification/ecs-e3d/source-freeze-repair-first.json`，SHA256 `47905BDD40B61E63ED8B89D5DF5D8EABC6E329D735C3C364DCB8442FCE288BFC`。第一轮Debug标准构建通过，候选 `4acbffd0e59d418699cdf696fcf8c0c7`；完整CTest **88/89，1089.95秒**，仅第75项部署回归失败，未开始Release。会话95930已结束exit1，日志与详细用例保存在 `out/ecs-e3d-*-debug-*-repair-first.log` / `out/ecs-e3d-ctest-debug-repair-first.log`，不能记作全通过。

- 失败位置：未修改的 `scripts/EditorDeployment.ps1:77` 的File.Replace，IOException “Unable to remove the file to be replaced”。目标为独立测试目录 `out/verification/deployment-tests/baa25fc617d54fc48a81ee91dc4e85c3/success`，不是正式 `out/bin`。部署自身回滚后journal为RolledBack；只读核验该测试bin的原始3文件与journal记录的SHA/size全部匹配，未对失败目录做删除、强制替换或恢复重试。
- 同一部署CTest在脚本正常新建的全新GUID目录中单独复测 **1/1，17.01秒**，会话18342 exit0。日志 `out/ecs-e3d-deployment-repair-isolated-first.log`，详细输出 `out/ecs-e3d-deployment-repair-isolated-details-first.log`。不修改源码、断言或原子写入策略，不将此次成功视作根因已解决；文件系统异常的根因仍未确证，历史调查保留。
- 源码冻结再次匹配；第二轮顺序完整门禁会话83142已启动，`run-gates-repair-second.ps1`重新执行标准Debug/Release构建、全部89CTest和补充检查，日志使用 `-repair-second`，原失败证据不覆盖。正式三轮配对及最终交付核验仍须等待本轮结束，不能由单项复测通过推导阶段完成。
- 另验证原937版本退化归档18输入/6测量日志及原始冻结、原配对复核报告SHA均未改变，记录 `out/ecs-e3d-regression-archive-verification-first.log`。当前构建目录的DLL正常更新，不把可变的构建产物当作历史测量输入；历史归档始终保留。

#### E3-D 修复完整验证最终结果：功能通过，尾部性能仍未通过

2026-10-19本次“进行完整验证”已执行完毕。**验证流程结束不等于阶段验收通过：E3-D/E3仍不收尾，不提交推送、不部署、不开始E4。** 本轮未修改已冻结的产品或测试源码。

- 同一 **939文件**冻结 SHA256 `47905BDD40B61E63ED8B89D5DF5D8EABC6E329D735C3C364DCB8442FCE288BFC` 前后匹配。标准Build.bat -SkipTests两配置零编译警告；Debug修复第二轮 **89/89，1124.47秒**，Release **89/89，1001.41秒**，两边托管/原生smoke、Python45、inspect通过。完整门禁会话83142已结束exit0，不再轮询。日志 `out/ecs-e3d-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-repair-second.log`，详细用例日志 `out/ecs-e3d-ctest-{debug,release}-details-repair-second.log`。
- Runtime增量25/Schema6、Scene增量11、增量Play3/提交12/联合15与原Runtime13/storage14/identity15/Query17/结构候选8/Paged10/Scene36/Gameplay53/批次26/Movement38 fake/UI18/E0fixtures11均通过。部署8组在完整Debug/Release中均通过（17.51/17.20秒）；首轮88/89和单独1/1记录不丢弃，原子替换异常根因仍未确证。
- 最终Debug候选 `585453061b454867a566ba192224dafe`，Release候选 `be823509e2ee4a84b3bd593897715dc4`。两配置各五包清单144/56/113/67/116文件；实际146/57/114/68/117，差额是清单和现役允许的Editor日志，既有白名单未更改。所有身份/路径/size/SHA及闭包核验通过。
- 正式E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 保持144清单/148实际文件、148备份完整；EXE SHA `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`未变。E0原始18程序集、E3-A原始8测量输入和9日志保持原哈希。测量前后交付核验 `out/ecs-e3d-delivery-verification-repair-{first,second}.log` 的SHA相同：`0E485981DF8803A0E4043FF0A2B5B02ECA3C5C4724E9A2A6344F79F33075C15F`。
- 最终标准候选配对冒烟归档 `4f380fec3daa417ca15ad02437644879`，旧/新11项语义一致；负例目录 `negative-33480195240148479d9affbf07887335` 的11项均正确拒绝且原输入不变。日志 `out/ecs-e3d-paired-{smoke,negative}-repair-first.log`。
- 三轮正式配对归档 `out/verification/ecs-e3d/ff40e2cf71514201943d7304632ed924`，旧/新、新/旧、旧/新交替执行，33项×100样本×6进程，共 **19,800个耗时样本和19,800个分配样本**。同驱动、真实产品程序集、TieredCompilation=0，三轮各33个完整场景摘要全部一致，18输入和实际加载九文件闭包前后哈希一致。独立分位数复核 `out/ecs-e3d-paired-verification-repair-first.log`，SHA256 `AD1D1576A153921469B619D879F8FCC94F67F9AD74416ED1B7688C193E87A2E9`。后续核验会话41850已结束exit0，无剩余构建/测试/测量进程会话。

**World全写重点结果**（各轮P50/P95的最小–最大值，ms；区间不是置信区间）：

| 对象数 | 旧版P50 | 修复候选P50 | 旧版P95 | 修复候选P95 | 同轮P95候选/旧版 |
| --- | --- | --- | --- | --- | --- |
| 100 | 1.2110–1.2203 | 0.9365–0.9547 | 1.8391–1.9065 | 1.4552–1.4800 | 0.774–0.805 |
| 1000 | 12.9827–13.3305 | 10.5785–11.3219 | 15.2221–17.7173 | 14.9121–16.0881 | 0.891–1.057 |
| **4096** | **55.5210–56.7901** | **52.9296–54.7862** | **62.5545–65.0129** | **70.6210–75.3443** | **1.102–1.159** |

4096全写三轮P95分别 **+12.895%、+10.202%、+15.891%**，三轮都回退，不能当作一个偶发样本忽略。每步线程分配从20,152,112–20,152,600 B降为14,080,720 B，约下降30%；100样本观察的Gen0/1/2计数也由153–156/119–122/64–67降至98–100/73–75/24–25，但回收次数少不证明停顿短，不能据此解释或关闭尾部回退。先前约2倍的严重回退已显著缩小，**不等于性能问题完全修复**。

其他4096工作负载仍有明显收益：World无写P95比0.00184–0.00275、约1%写0.0281–0.0291；Play无写约0.0093–0.0101、全写0.271–0.282、Behaviour全写约0.233、1%对象替换0.0238–0.0261。它们不能掩盖直接World全写的尾部问题。`read_transform*`继续沿用E0的GetEntities复制枚举而非强类型Query，分配不变；小场景P95波动最大约1.387，未证明5%门禁或整帧收益。

原937退化归档18输入、6日志及原冻结/配对证明再次保持不变，`out/ecs-e3d-regression-archive-verification-second.log` SHA `60C50262ED36425E019B2AD3D63FF9E253AAFBC4E269B8DA08F22C1CF12F3418`。全部失败目录、首轮日志、原始数据和四项IDE改动保留。

**验证结束时的原建议（被下述用户排期决定覆盖）**：先在E3-D定位密集写的回调、准备、安装耗时及瞬时分配/GC停顿/派生缓存存活期，再修复。尚未证明GC或某个缓存是根因；不得通过反复抽样挑选一次通过、放宽阈值、删除边界校验或退回完整快照来收尾。任何产品修改仍须新冻结、完整双配置和配对；人工/目标机/自包含/整帧性能/1h门禁保持开放。

#### E3-D 用户确认提交与性能债务延期

2026-10-19用户明确要求：**E3-D先提交，记录性能未达标状态，在后续阶段统一修复。** 本次作为“功能回归通过、性能未达标”的源码检查点提交，不代表E3/ECS完整验收通过；用户随后明确授权推送，并标记“功能验证通过、性能未达标”，不部署；本轮只调整交付状态与排期，产品/测试源码保持已验证的939文件冻结不变。

下一开发阶段可推进E4领域迁移；下列债务纳入E5统一性能修复与验收，在恢复M8.3前处理。该决定覆盖上文以及历史记录中“必须先修复才允许提交/进入E4”的排期限制，不覆盖测试、安全、权限或性能判定标准。

| 跟踪项 | 已知证据与状态 | 后续修复与关闭条件 |
| --- | --- | --- |
| ECS-PERF-01 密集全写尾延迟 | 未达标；4096对象三轮P95回退12.895%、10.202%、15.891%；约30%分配下降不能抵消尾延迟退化 | E5定向分析回调/准备/安装及GC停顿、缓存存活期；修复后新冻结、完整Debug/Release回归、同驱动三轮配对及原始分位数/语义/闭包核验，按既定性能门禁验收 |
| ECS-PERF-02 小场景读性能 | 未证明5%门禁；E0复制枚举样本P95最大比值约1.387，不是typed Query测量，也不直接断言为已确证退化 | E5控制测量噪声、区分复制枚举与typed Query，对100/1000/4096分别复核耗时、分配和GC；存在回退则修复，不挑样本或放宽标准 |

完整整帧性能、真实素材/目标机、自包含、人工和1h门禁继续独立开放。File.Replace首轮失败及根因未证实的调查保留。提交不覆盖原939修复与937退化归档、失败日志、正式安装或备份。

## 10. E4 子系统迁移：实施拆分与 E4-A 消费者审计

2026-10-19用户授权“开始E4阶段”。基线为已提交推送的 `6f4d666ab5686b15cafda29edf23d8facc5fb230`（E3-D：功能验证通过、性能未达标）。本片 **E4-A仅完成消费者审计、迁移合同与静态验证，不修改运行时代码，不宣称E4整体完成**。ECS-PERF-01/02继续由E5统一修复；不恢复历史“禁止进入E4”的排期限制，也不将延期视为性能通过。

### 10.1 E4实施顺序

| 分片 | 具体交付 | 放行条件 |
| --- | --- | --- |
| E4-A 消费者审计 | 可核对的调用清单；区分显示/启动/编辑/保存；明确排序、身份、缺失与冲突语义 | 清单与源码逐项一致；漏项/重复/行号/源码/分类负例拒绝；验证既有源码、候选包、正式安装与备份未变 |
| E4-B 角色与动画 | 角色/碰撞体、根运动、Animator启动查询；跟随相机与Player启动精确UUID查找；保留冻结成员与原生资源映射 | 创建顺序及既有UUID顺序分别等价；组件缺失仍拒绝；不存在的相机仍返回原有结果；恢复/换World/暂停/重启/故障/成员冻结联合回归 |
| E4-C 渲染提取与设置 | 复用按World身份绑定的查询；静态/蒙皮几何并集、灯光、设置读取；Editor/Player共同数据读取但保留各自错误处理 | 不漏诊断、不变绘制和FrameIndex顺序；独立阴影集合、资源代次、插值、2D隔离、DX11参考/API/恢复测试 |
| E4-D 场景、编辑与既有AI查询 | 对象列表/属性读取及导入影响列表；元数据成员观察；现有分页能力的有界读取 | 唯一EditSession/Undo；UUID、文档代次、revision、授权与大小限制不变；编辑撤销/重做、选择/换场景、陈旧请求与MCP权限负例 |
| E4-E 联合验证与残留核对 | Editor/Player/Headless跨领域组合；逐项复核已替代消费者与必须保留的冷路径 | 同一新冻结下完整Debug/Release、原生/托管smoke、Python/inspect、格式/ABI/权限/无Python产品/包与备份核验；不代替E5性能验收 |

每片先验证再提交推送。E4-B及后续凡修改产品/测试/构建脚本，必须建立新源码冻结，不能沿用E3-D的939文件验证结论。替代消费者就绪时删除对应旧实现，不引入兼容壳；显式复制API、编辑快照等现役合同不是“旧类型”，不一概删除。正式EXE仍为E1-B，本次不部署。

### 10.2 枚举调用清单（E4-A基线，非性能采样）

机器可核对清单：[ECS_E4_CONSUMER_AUDIT.json](ECS_E4_CONSUMER_AUDIT.json)。扫描 `managed` 下C#，排除 `bin`、`obj`、`*Tests`、`*SmokeTest` 和 `Ncma.Samples`；匹配 `GetEntities(` 共 **22行/19文件，其中1行是API定义，21处是调用**。该数字是词法入口盘点，不是完整调用图或热点耗时证明。当前生产扫描中 `CreateQuery(`/`.Query<` 只出现在Runtime的查询定义/转发实现，不能把E2查询基础已完成等同于领域消费者已迁移。

下列路径均相对 `managed/`；精确基线行号和原文见JSON，后续迁移不要为保持清单数字而保留旧调用。

| 消费者 | 执行时机 | 处理与必须保留的语义 |
| --- | --- | --- |
| Ncma.Characters/CharacterPlayRuntime.cs:41 | 启动 | E4-B：Character OR BoxCollider并集、去重、UUID顺序；不能把OR改成AND |
| 同文件:48 | 启动 | E4-B：FollowCamera成员集合，包括空集合的冻结 |
| 同文件:160 | 显示帧FollowView | E4-B：精确UUID读取，缺失返回null；不缓存可跨恢复使用的Entity |
| Ncma.Characters/RootMotionSet.cs:34 | 启动 | E4-B：All RootMotion / None Animator，保留创建顺序及资源/组件缺失拒绝 |
| Ncma.Characters/GraphRootMotionSet.cs:33 | 启动 | E4-B：RootMotion与Animator交集，保持同一动画时钟与资源代次 |
| Ncma.Characters/ActionController.cs:47 | 启动 | E4-B：Health与ActionDefinition并集，保持UUID顺序、动作32预算和根运动要求 |
| Ncma.Scene.Rendering/SceneAnimatorRuntime.cs:49 | 启动 | E4-B：Animator查询后验证SkinnedMesh，不能把缺少蒙皮的Animator静默过滤；保持UUID顺序、32角色/32768骨骼预算 |
| Ncma.Player.App/PlayerRunner.cs:69、91 | 启动 | E4-B：RootMotion/Animator存在性及精确相机UUID，保留Headless资源准备与启动失败 |
| Ncma.Scene.Rendering/RenderSceneExtractor.cs:58 | 缓存未命中的显示提取 | E4-C：几何并集和灯光筛选；缺Transform、双几何、多主灯诊断；创建顺序、FrameIndex与独立阴影集合 |
| Ncma.Scene.Rendering/SceneEnvironmentState.cs:19 | 启动/显示共享入口 | E4-C：缺省Off、重复设置拒绝、精确World与已提交边界 |
| Ncma.Scene.Rendering/ScenePostProcessState.cs:12 | 启动/显示共享入口 | E4-C：缺省Off，两个Off也属于重复；不能只查Enabled |
| Ncma.Scene.Rendering/SceneShadowData.cs:31 | 启动/显示共享入口 | E4-C：缺省null与显式Off不同，保留重复拒绝 |
| Ncma.Rendering.Scene/ScenePipelineStartup.cs:38 | 启动 | E4-C：设置唯一性、快照与World一致性、工厂只读边界 |
| Ncma.Player.App/PlayerPresentation.cs:140 | 每次有效显示提交 | E4-C：RenderConfiguration/Environment筛选；Player重复设置报错与失败清屏 |
| Ncma.Editor.App/Program.cs:431 | World身份/revision变化 | E4-C：设置筛选；Editor警告并保留上次有效设置，不能统一成Player策略 |
| Ncma.Editor.App/EditorPresenter.cs:149 | revision/选择/目录/文档变化 | E4-D：清理失效行ID；不是稳定每帧都全扫描 |
| Ncma.Editor.Services/EditorAssetWorkflow.cs:187 | 导入影响检查事件 | E4-D：静态/蒙皮/ClipPlayback引用并集、去重和顺序；保留导入ticket验证 |
| Ncma.Scene/SceneDocument.cs:30 | 观察外部World revision变化 | E4-D：失效UUID绑定清理，区分成员变化与值变化；保留文档revision与历史失效 |
| Ncma.Runtime/ComponentAuthority.cs:48 | 权威申请 | 保留完整冻结成员覆盖校验，不因消除扫描而接受子集；不是逐帧角色扫描 |
| Ncma.Managed/SceneWorld.cs:23 | 用户显式GetObjects调用 | 保留高级GameObject副本列表合同；同一World存储，不是废弃Runtime.GameObject |
| Ncma.Runtime/World.cs:84 | API定义 | 保留显式复制枚举API和E0对照；不是待删除兼容入口 |

### 10.3 快照与间接消费者：保留和迁移的边界

- `CharacterPlayRuntime.Compose`、`SceneAnimatorRuntime.Compose`的组合验证和资源准备、`MovementCoordinator`构造/Start的原启动字节对照、`RuntimeSessionOwner.Preflight`及PlaySession启动/重载是冷路径，保留完整验证。固定步已经使用受控绑定/演员列表，不应重写成每步动态重新收集角色。
- `RenderSceneExtractor.Extract`目前没有完整文档序列化，且具备World身份/revision/资产/相机/尺寸/蒙皮/显示变换引用缓存；只在缓存未命中时复制实体列表。E4-C减少该复制/筛选，不将其描述为已存在的“每帧JSON路径”。`SceneRenderEnvironment`、`SceneRenderPostProcess`、`SceneRenderShadows`和两宿主间接调用设置读取，须一起验证，不只改静态Read方法。
- `EditorWorkspace.Snapshot`按文档generation/revision缓存；`EditorSceneHud.RefreshEditSceneHud`在编辑revision变化或显式启动时读取快照，Play时无参路径直接返回。`EditorAnimationGraphPreview`使用隔离预览场景，其资源Open准备与每帧Draw不同。E4-D可优化面板读取，但不把这些已有缓存/隔离全部移除。
- `EditSession`的Documents/CommandParticipants/CommandImpacts/Interactions/InteractionGate/CaptureBytes，以及`SceneDocumentFiles.Save`、Prefab制作/验证、场景克隆、资源重建/显卡恢复、Player最终摘要属于事务、完整验证、保存或恢复合同，保留完整文档路径；不重写Undo存储、场景格式或File.Replace策略。
- `Capabilities.Inspect(ncma.scene.inspect)`和`ObjectDetails`目前为一次请求捕获完整文档再分页；E4-D评估只复制请求对象/页的受检读取。必须保持创建顺序、组件/绑定/Export原有排序、offset非负、limit分别为1..128/1..64的分页限制、256KiB对象详情输出限制、请求授权/身份/revision与复制隔离。`ncma.scene.validate`仍需完整组合验证，不替换成局部Query。
- UI自身的UiDocument/UiRuntime/UiCanvas数据和骨骼内部层次不迁移成场景父子树。2D/UI路径不因“统一ECS”引入3D/物理资源，也不添加Python游戏逻辑、推理服务或Agent直接写权限。

### 10.4 查询适配的具体约束

1. Query计划由消费者拥有并复用；World实例替换或同一实例Restore导致Identity变化时重新建立，不能只观察对象引用。成员列表看MembershipVersion，值/设置缓存仍看Revision；成员版本未变不代表Transform/设置未变。不持有跨结构提交的枚举器。
2. `World.Has<T>`对不存在的类型池返回false，而`Query<T>`要求类型已注册。迁移需覆盖默认/精简注册表，不能让原先“无此可选功能”变成初始化异常。只能在需要该领域且类型注册成立时建立相应计划，不能给纯2D额外注册/初始化所有3D模块。
3. Query默认创建顺序；角色/动作/Animator原有UUID排序继续保持。多查询并集去重后按原消费者顺序输出，不能直接拼接导致双处理或顺序变化。
4. 渲染查询以几何组件为筛选条件，后续仍检查Transform；不能通过Transform交集把错误对象过滤。Static+Skinned冲突、多主灯、重复Off设置仍必须被看到。顺序变化会影响FrameIndex/拾取/诊断，必须逐项对照。
5. 对未知UUID，现有`FindEntity`抛异常；FollowView等原先FirstOrDefault的可空行为不能直接替换为无保护FindEntity。E4-B优先评估一个只读、owner-thread、返回完整Entity的TryFindEntity，不以catch所有异常吞掉身份/线程问题；这是待实现项，不是已存在API。
6. 资源与数值系统依旧使用持久UUID+受检World/session/tick映射到原生不透明句柄。Native批量ABI保持不变；不把Entity序列化，不新增World副本，不扩大组件写权威。

E4-B回归等待期间的只读核对补充（非E4-C实现）：`EntityQuery`已有创建顺序比较及Scene.Rendering友元访问，因此几何并集可以在当前World的受检查询结果中合并、去重并按创建顺序缓存，无须新增公开序号或用UUID/槽位排序替代。缓存须同时记录World身份与MembershipVersion；组件值缓存仍观察Revision。`SceneEnvironmentState.VerifyBoundary`及原生资源会话持有的expectedWorld必须先验证，不能借“自动重建查询”让过期原生会话接受Restore后的World；纯数据Extractor重建查询与原生会话拒绝陈旧身份是不同合同。后续设置读取还须覆盖SceneRenderEnvironment/PostProcess/Shadows的间接消费者，不能只替换三个静态Read函数后每次显示重新分配查询计划。

### 10.5 测试映射与本片结果

E4-B使用Runtime身份/Query/权威用例、Gameplay提交/Movement用例，并扩展真实角色、根运动、Animator、Headless宿主覆盖；fake Movement不能代替Jolt实测。E4-C使用SceneRendering提取/诊断用例及Rendering的SceneCandidateSafety、SceneExchangeSafety、SceneConstructionSafety、SceneCostJoint、GraphicsRecoveryJoint等现役产品测试。E4-D使用SceneDocument、EditorCore、EditorService、EditorTransport、ApplicationService以及UI路由/联合宿主测试。每片需新增对应失败/恢复/排序边界的断言，原测试不能削弱；E4-E回到完整Debug/Release产品组合，不以子集代替。

本片为审计文档变更，不新增运行时代码或测试用例，**未重新构建或运行完整89项回归，也不冒称新性能测试**。验证包括：

- `out/verification/ecs-e4a/verify-audit-first.ps1`：实际清单逐项匹配，以及内存中模拟漏项、重复、错误行号、错误源码、错误分类5类负例均拒绝；生产Query使用基线匹配，合计 **7/7**。原文件不被负例改写。
- E3-D同一939文件源码冻结再次通过；SHA仍为 `47905BDD40B61E63ED8B89D5DF5D8EABC6E329D735C3C364DCB8442FCE288BFC`。因此本片引用上轮双配置89/89功能结果，不重跑未改变的产品。
- 重新执行只读交付核验：10个候选包、正式144清单/148文件及148备份、E0原始18程序集、E3-A原始8输入/9日志全部保持完整。正式E1-B EXE SHA仍为 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。
- 原939修复与937退化性能证据、File.Replace首轮失败以及4项无关IDE修改均保留。源代码只读盘点不是整帧成本证据；ECS-PERF-01/02不关闭。

本机静态校验日志 `out/ecs-e4a-audit-first.log` SHA256 `BF0113D24B3B0FE74970D929FA1A0DC12BC71A82931FD5F7EF51C1A7708344AA`；只读完整性日志 `out/ecs-e4a-integrity-first.log` SHA256 `9F9876CFD48DA6112323B776A7132EF38DAAF6533013EDFBC123B179C0004F55`。清单文件SHA256 `DC7F75C8D7D871067836A6EDDD723AA783C26AF12DB04219601D8E8E1FB2B4FC`为本机原始字节哈希（Git换行规范化可能改变工作副本字节，字段/源码逐项匹配才是清单判定）。辅助脚本与日志只保留在ignored out，不作为新增产品入口。

E4-A审计片完成，下一片是E4-B角色/动画查询与身份访问适配。E4-B/C/D/E尚未实现，E4整体与E5仍未验收，不进入M8.3。

### 10.6 E4-B 角色/动画查询与身份访问：自动候选验证完成

2026-10-19，基于已交付的E4-A `0ef48c38ab21b8c843e0b23500c173ef8c5124e0`继续实施。本节覆盖上节“E4-B尚未实现”的旧状态；**候选代码、双配置完整自动回归及交付核验已完成，待本片提交推送；不部署、不关闭ECS整体及性能门禁**。下文按发生顺序保留定向失败、首次完整失败和复测过程，末尾最终结果覆盖运行中记录。

- Runtime新增只读 `World.TryFindEntity(Guid, out Entity)`：只读取已提交UUID索引，缺失/空UUID返回false/default，存在时返回完整WorldIdentity/Index/Generation；仍先检查owner线程。它不授予写权限、公开候选对象或持久化Entity。`ComponentRegistry.IsRegistered<T>()`只报告CLR类型注册，不表示成员存在或可写。
- 内部 `SceneStartupEntities`只用于离帧组合准备：单类型、双类型交集、All/None及按UUID排序的双类型并集；返回拥有的Entity副本。可选类型未注册时沿用原Has的缺省语义，并集去重且最多World.MaxObjects。没有每帧重建Query、第二份组件存储、跨恢复Entity缓存或新增原生ABI。
- 已迁移CharacterPlayRuntime的角色/碰撞体、FollowCamera启动集合；RootMotionSet/GraphRootMotionSet分流；ActionController的Health/动作并集；SceneAnimatorRuntime的Animator收集。既有UUID顺序/创建顺序分别保留；缺蒙皮、缺资源、预算、冻结成员、共享动画时钟与物理fail-stop检查保留。
- FollowView改用TryFindEntity，保留缺相机返回null及缺FollowCamera/Camera行为；Player启动相机也改为精确查找。Player的Headless动画存在性改为RootMotion/Animator Query；该宿主在创建World时明确注册这两个类型，未扩大纯2D/无Python依赖。角色固定步仍遍历受控绑定，动画仍使用原有候选/提交生命周期。
- 按E4-A相同生产源码过滤规则复核：原22行/19文件的GetEntities匹配现为13行/13文件（含1定义、12调用）；本片6个消费者文件中的9处旧遍历已替换。其余为E4-C/D待迁移及ComponentAuthority、高层GetObjects等现役合同；没有为了清零搜索结果删除受检复制API，也未改写原E4-A基线JSON。词法残留核对不是性能证明。
- 新增Runtime查找8组：缺失/空UUID、同UUID删除复用、恢复/跨World、未提交创建、候选槽位替换/撤销、只读/线程、注册≠成员、预热查找无列表分配。新增领域查询8组：可选注册、UUID/创建顺序、并集去重、恢复/复制隔离、300轮固定种子结构变化对照、4096容量、线程、错误组合不被过滤、真实Play跟随相机删除/重建（其中组合项合计8组）。
- 定向第三轮两配置Runtime新增8/8、原Runtime13/storage14/identity15/Query17/候选8/Paged10/增量25/Schema6通过；SceneRendering **121/121**（新增8组）通过；Gameplay53/结构26/增量3/提交12/联合15、Movement38 fake、UI18、E0fixtures11通过。会话65326已结束exit0，不轮询。日志 `out/ecs-e4b-{Ncma.Runtime.Tests,Ncma.Scene.Rendering.Tests,Ncma.Gameplay.Tests}-{Debug,Release}-targeted-third.log`，无编译警告。这不是实际Jolt/DX11产品联合验收或E5性能证明。
- 早期定向失败保留：第一轮新测试把旧Entity拒绝误认为只能抛InvalidOperationException，实际原契约为ArgumentException；第二轮测试在同一候选步删除后重复创建同UUID，被既有StageCreate规则正确拒绝。修正测试为原异常合同及不同UUID的槽位替换，并保留独立安全边界同UUID复用用例；未放宽产品规则。首轮PowerShell stderr提前中止的日志同样保留，后续显式捕获进程退出码。
- 新源码冻结 `out/verification/ecs-e4b/source-freeze-first.json` 共 **942文件**，SHA256 `216E02F6DA690BF15194765D9AB777346FB3FE7180603D6A3B312BCF3DB75931`。顺序完整门禁会话 **51807** 正在执行：标准Build.bat -SkipTests构建两配置，各全89项CTest及smoke/Python45/inspect；脚本 `out/verification/ecs-e4b/run-gates-first.ps1`，总日志 `out/ecs-e4b-gates-first.log`。构建Skip只表示测试由外层单独执行且禁止部署，不表示跳过本片完整验证。期间不修改冻结源码或启动重复构建。

待完整双配置结束后执行 `out/verification/ecs-e4b/verify-delivery-first.ps1`核对新冻结、完整结果、候选包、正式安装与备份，再更新交付状态及提交推送。E4-A JSON保留为6f4d666基线，不将其旧调用行号冒充当前源码；迁移后残留由本片重新扫描。ECS-PERF-01/02、人工/目标机/自包含/整帧/1h、历史File.Replace调查保持开放。

首轮完整门禁终态（覆盖上文运行中记录）：Debug标准构建通过，候选 `0015eda4cd854d43a39e5eaaa570a154`，未发现编译警告；会话51807已结束exit1。Debug **88/89，1203.78秒**，唯一失败是 `NcmaRenderingTests` 超过原240秒上限（240.06秒），Release及补充门禁未启动。此前进度消息将CTest继续执行后续用例误认为主渲染通过，现明确更正；不能据此宣称完整门禁通过。完整失败报告保留于 `out/ecs-e4b-ctest-debug-first.log` 和 `out/ecs-e4b-ctest-debug-details-first.log`，输出停在M6.6-B真实NCA用例后，未出现最终生命周期汇总；根因尚未证明。

只读进程核对未发现残留dotnet/Editor/Player/Rendering测试进程，未终止进程或删除文件。独立原CTest及其fixture复测会话63307已结束exit0，2/2通过，渲染耗时168.06秒（合计168.52秒），最终32次生命周期及validation0/0通过；保持240秒上限和所有断言，不能以独立通过代替完整双配置回归，也不能据此证明首次超时根因。

第二轮完整门禁会话36824正在执行 `run-gates-second.ps1`（总日志 `out/ecs-e4b-gates-second.log`）：同一942冻结，重新标准Debug/Release构建、完整89项及补充验证。build/CTest/details/补充入口使用second日志；尚未执行过的smoke/Python/inspect使用first日志。终态后使用 `verify-delivery-second.ps1`核验，不覆盖首轮失败。未提交、推送、部署或开始E4-C。

第二轮Debug已完整通过：**89/89，1099.17秒**，主渲染170.05秒；托管/原生smoke、Python45、inspect与942源码冻结通过。候选 `cc6067a8935347408d99eb50ddd32bc4`，无编译警告；Release标准构建在同一36824会话继续。此记录只确认Debug，不提前确认Release或最终包/备份核验；首次超时根因仍未证明。

另运行只读 `out/verification/ecs-e4b/verify-deferred-performance-first.ps1`，E3-D修复后性能归档 `ff40e2cf71514201943d7304632ed924` 的18输入、6日志、原939冻结和配对分位数证明全部匹配；日志 `out/ecs-e4b-deferred-performance-first.log` SHA256 `B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A`。只证明历史证据保留，不是本轮新性能采样或性能通过。

#### E4-B 最终自动验证结果

- 同一942文件冻结，标准Build.bat两配置第二轮构建均成功、无编译警告。SkipTests只把测试移到外层独立完整执行，禁止部署；未宣称新no-Skip/旧三轮历史性能门禁。
- Debug **89/89，1099.17秒**；Release **89/89，996.57秒**。主渲染分别170.05/167.79秒，保持原240秒上限和全部断言。两配置smoke、Python45、inspect及冻结前后核验通过；新Lookup8、SceneRendering121与原Runtime/Gameplay/Movement/UI等完整用例通过。
- 十个候选包清单逐文件SHA/大小与实际闭包通过：每配置Editor144、Player-Null56、DX11113、Null-Physics67、DX11-Physics116项；实际文件分别146/57/114/68/117，额外文件仅为清单及Editor既有允许日志。候选Debug `cc6067a8935347408d99eb50ddd32bc4`、Release `5411fe4c6b824d118b54cd7fdc620ec5`。
- 正式E1-B generation `7f3d3822dd39431b9c203e17194afb8c`仍为144清单/148实际，原148备份逐文件一致；EXE SHA `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。E0原始18程序集、E3-A原始8输入/9日志完整。
- 最终核验 `out/verification/ecs-e4b/verify-delivery-second.ps1` exit0，日志 `out/ecs-e4b-delivery-verification-second.log` SHA256 **037B7E4D6CEC3FD0DB9FB3B00E579E7091BBF3877697854A2DF7C82624B1557E**。会话36824已结束exit0；51807和63307也均已终态，不再轮询或重启。
- 首轮Debug88/89渲染超时和早期测试夹具错误完整保留，首次超时根因仍未证明；未修改产品、超时或断言来换取通过，没有终止残留进程或删除失败文件。双配置自动候选通过不等于无偶发故障保证或性能验收。

本片自动候选完成，提交推送后再推进E4-C渲染提取/设置查询。E4-C/D/E、E5、M8.3尚未完成/开始；ECS-PERF-01/02以及人工、目标机、自包含、完整整帧性能、1h和历史File.Replace调查保持开放。正式EXE未更新。

### 10.7 E4-C 渲染提取与设置查询：自动候选验证完成

E4-B已提交推送 `1ef60366bdf3ad4a7107266ea887784d3573095f`，本地与远端已核对。E4-C已完成下列实现及同冻结自动候选验证，提交交付后再进入E4-D；不表示整个E4或性能门禁完成。

- `SceneComponentReader<T>`由消费者持有，读取已提交World；可选类型未注册时返回空，查询容量随World规模从16增长至4096，完整成员计数不截断，只有唯一成员才返回值副本。先校验expectedWorld/owner/提交边界，再重建Restore后的计划；不缓存组件值，不扩大权限。
- 内部几何并集缓存按World身份与MembershipVersion刷新，合并静态/蒙皮、去重后保持创建顺序，不过滤缺Transform、双几何等错误。Extractor保留Revision/资产/相机/视口/插值等完整缓存键，显式相机改为TryFindEntity，灯光改为可复用类型查询；独立阴影集合不变。
- 环境/后处理/阴影设置读者及SceneRenderSession间接消费者复用计划；Editor和Player各自持有配置读者，前者重复配置保留last-valid并告警，后者仍抛原错误。冷启动静态读取仍可创建短命读者，不是每帧全局缓存或第二World。
- E4-C范围7处GetEntities调用已替换。按原过滤规则生产残留6行（1定义、5调用），分别是E4-D待迁移与权威/用户显式副本合同；不删除现役复制API。
- 新增12组基础用例：可选类型、重复Off、值更新与成员版本分离、陈旧/外World、线程/提交边界、4096容量增长、暖态分配、混合成员/错误诊断及创建顺序/槽位复用。第二轮Debug/Release **133/133**，真实Rendering.Tests消费者均编译成功、0警告；会话10903结束exit0。日志 `out/ecs-e4c-scene-{Debug,Release}-targeted-second.log`、`out/ecs-e4c-rendering-{Debug,Release}-build-second.log`。
- 首轮65463结束exit1：暖态测试只预热读取，未预热生成的TransformData相等比较器，分配断言失败。隔离测量两配置均显示读取0B、首次相等比较88B、完整表达式预热后0B；修正测试预热范围并同时保留读取/完整表达式严格0B断言，未改变产品或放宽阈值。原日志保留，局部0B不代表整帧性能通过。

以下按发生顺序保留候选补测、冻结与完整回归记录；历史“进行中”不代表当前仍有对应测试会话。最终状态以本节末尾的交付核验为准。

后续进展（覆盖上述待补测试）：新增4096混合/重复几何容量用例，基础测试第三轮两配置 **134/134**。现役PipelineHostProbe增加真实故障注入：Player先成功呈现一帧并预热查询，注入重复配置/环境后验证准确原错误、exit5、无后续呈现及正常释放；Editor检查重复设置告警、last-valid、重复期间值改变不选错对象、移除重复后恢复、新World身份及精确原视口恢复，同一读者实例可重建计划。源宿主两配置各 **177 checks**，构建均0警告；会话33825结束exit0，日志 `out/ecs-e4c-{scene,host}-{Debug,Release}-targeted-third.log` 和 `out/ecs-e4c-rendering-{Debug,Release}-build-third.log`。测试仅使用既有可信探针，没有新增产品写入/授权后门；尚不能以源宿主测试代替搬移候选包测试。

最终候选冻结 **944文件**：`out/verification/ecs-e4c/source-freeze-first.json` SHA256 **F09131174EDEB4B0AFF3D79A3DDC092ACA0A2B4DCC5A1855C09A4F5D1F423821**。各轮均计划顺序执行标准Debug/Release构建（SkipTests只把全89测试移至外层并禁止部署）、完整CTest、smoke/Python45/inspect及前后冻结核验。首轮脚本 `out/verification/ecs-e4c/run-gates-first.ps1`、总日志 `out/ecs-e4c-gates-first.log`的失败终态见下文；正式EXE仍为E1-B，不部署。

首轮标准Debug构建成功，候选 `79040c18ea6940629fcac5414aa2f684`，未发现编译警告；该轮未获得完整双配置结论。只读历史性能证据核验通过：E3-D修复归档18输入/6日志、原939冻结与分位数证明未变；日志 `out/ecs-e4c-deferred-performance-first.log` SHA256 `B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A`。这仅证明证据保留，不是新性能测量或验收。

#### E4-C 首轮完整回归失败与原条件复测

会话15355已结束exit1，不能继续轮询。首轮完整Debug **88/89，1128.43秒**；唯一失败为`NcmaSceneDocumentTests`完整保存用例，未修改的`SceneDocumentFiles.Save:60`在`File.Replace`抛出IOException“无法删除要被替换的文件”（测试第209行）。其余88项通过，包括主渲染177.21秒、真实源与搬移候选管线宿主 **657 checks**、SceneRendering **134/134**。Release和补充门禁尚未执行，首轮不能记作完整通过。

失败目录`out/tests/scene-document-f60e91b9a2cf48528c818622f308bcac`及同次另外6个测试目录保持原样。只读核对目标`Scene.ncmascene`仍为964字节、可解析的Scene v1，Logic的Health为100，SHA256 `4B02C729B66528013BC313041445C30A2212C1AB460DA0632DE664DDF7EBA28F`；缺少失败前独立哈希，故不宣称已证明逐字节回滚。目录/文件清单证据`out/ecs-e4c-scene-failure-inspection-first.log` SHA256 `6840EEFFFE9281EADE8BA9382D7ABC432B71604386FBFE160D352BCD8422B8D6`。

原CTest单独复测 **1/1，1.23秒**，Scene原36/36及运行时文档11/11通过；测试自身创建全新目录，没有再次替换失败目标。日志`out/ecs-e4c-scene-recheck-debug-first.log`及`scene-recheck-debug-details-first.log`保留。未改产品、断言、原子写策略或944文件冻结；没有删除、强制覆盖、关闭锁持有者或忽略异常。根因仍未证明，历史File.Replace调查不关闭。

第二轮会话 **6955** 使用`out/verification/ecs-e4c/run-gates-second.ps1`，总日志`out/ecs-e4c-gates-second.log`，保持同一944冻结。该轮在构建阶段失败，未执行交付核验；正式EXE和E3-D性能未达标状态不变。

第二轮随后结束exit1：标准Debug构建在可选gRPC项目恢复时触发`NU1900`，无法获取官方NuGet漏洞数据源；没有进入第二轮CTest、补充或Release。默认沙箱中的只读curl也遇到Schannel凭据错误。经联网提权执行相同锁定依赖恢复`dotnet restore managed/Ncma.Modules.Grpc.Tests/Ncma.Modules.Grpc.Tests.csproj --locked-mode --force --nologo`成功，日志`out/ecs-e4c-nuget-restore-first.log`；未改版本/锁文件，未关闭漏洞审计、忽略失败源或降低警告级别。

第三轮会话 **45145** 使用`run-gates-third.ps1`，总日志`out/ecs-e4c-gates-third.log`，仍使用原944冻结。该轮在完整Debug测试阶段失败，未执行补充测试或Release；首轮保存失败及第二轮网络恢复失败全部保留，不能继续轮询或重启已结束会话。

第三轮标准Debug构建通过，候选`b6a43637a3dc4611874ba408327a34f8`，未发现编译警告。日志含一处PowerShell 5对用户Git ignore读取权限提示的NativeCommandError包装；交付辅助核验仅按完全匹配的六行记录将其分类为非编译诊断，原日志/哈希保留并单独报告计数，不忽略其他PowerShell错误、编译警告或测试失败。该辅助分类器的真实记录、变更路径、插入编译警告、插入无关错误4项检查通过；产品源码与测试断言未改动。

第三轮完整Debug最终为 **87/89，1202.61秒**（会话45145终态exit1）：`NcmaModuleTests.Grpc`在错误凭据负例中收到Schannel“安全包中没有可用的凭证”，尚未进入预期Unauthenticated应用鉴权路径；`RelocatedClient`报告startup_failed后生命周期等待超时。原场景保存测试本轮36/36通过，Runtime文档11/11、渲染基础134/134、源/搬移宿主657检查及主渲染189.93秒通过，但不能拼成完整通过。

两个原始模块CTest在批准的正常Windows/TLS环境下原条件复测 **2/2，39.44秒**（34.24/5.18秒），会话22040终态exit0；日志`out/ecs-e4c-module-recheck-debug-first.log`及对应details-first。未改鉴权状态码、证书校验、超时、源码或依赖/审计配置。该结果支持执行环境差异的判断，不证明首轮File.Replace根因。

第四轮会话 **52983** 经批准在上述环境运行`run-gates-fourth.ps1`，已结束exit0：同一944冻结，重新标准Debug/Release、完整89项及补充验证；总日志`out/ecs-e4c-gates-fourth.log`，build/CTest/details/补充外层为fourth，此前未执行过的smoke/Python/inspect为first。随后`verify-delivery-fourth.ps1`核验通过，包括首轮7个原测试目录的完整文件SHA/大小清单未变。原始失败与独立复测均保留，没有把第三轮87/89与独立2/2相加冒充一次完整门禁。

#### 最终双配置自动交付核验

| 同一944源码冻结 | Debug FOURTH | Release FOURTH |
| --- | --- | --- |
| 标准Build.bat（SkipTests、不部署） | 成功，0编译警告 | 成功，0编译警告 |
| 完整CTest | 89/89，1106.53秒 | 89/89，1000.40秒 |
| 主渲染 / 实际源与搬移宿主 | 175.40秒 / 657检查 | 169.57秒 / 657检查 |
| 渲染基础 / 场景 / 运行时文档 | 134 / 36 / 11通过 | 134 / 36 / 11通过 |
| 真实TLS / 搬移客户端 | 4583 / 1174检查 | 4583 / 1174检查 |
| managed/native smoke、Python、inspect | 通过 / 45通过 / 通过 | 通过 / 45通过 / 通过 |
| 候选代次 | `2076bbfa42084230bb052e3c826d50a6` | `0496eb36a93e409f99c1c39f3e85372b` |

原Runtime13/storage14/identity15/Query17/lookup8/candidate8/paged10、增量验证25/schema6、Gameplay53/batch26/真实Play3/commit12/joint15、Movement38（fake）、UI18/E0fixtures11均通过。暖读严格0B仅为局部断言，不代表整帧性能。双配置构建没有需要分类的Git提示；旧分类器证据仍保留。

最终只读核验`out/ecs-e4c-delivery-verification-fourth.log` SHA256 **B6D8BB7A0DDE2E9BB4045032520529F642D968A31AD18B7E20DC3B7B831ED4DF**通过：同一944冻结；十个当前包与本轮构建代次匹配，每配置manifest为144/56/113/67/116文件；正式安装144清单/148实际、原备份148文件SHA与大小；E0原始18程序集、E3-A原8输入/9日志；前三轮失败日志和首轮7目录完整闭包。原File.Replace根因仍未证明，不将文件保留冒充事前/事后逐字节回滚证明。

E3-D延期性能归档第二次只读核验`out/ecs-e4c-deferred-performance-second.log`通过，SHA仍为 **B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A**；未重采样或改变ECS-PERF-01/02。正式E1-B安装代次`7f3d3822dd39431b9c203e17194afb8c`及EXE SHA256 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**不变。不是新的no-Skip/旧三轮profile/部署验收；人工、目标机、自包含、整帧性能、1h与历史调查门禁仍开放。

E4-C自动候选完成，下一步限定21个任务文件提交推送并核对远端，排除4项IDE设置和out；E4-D/E未实施，E5与M8.3未开始。全部测试会话已结束，不重启旧会话或覆盖其日志。

### 10.8 E4-D 读取边界准备核对（待E4-C交付后实施）

以下是等待E4-C回归时对现役源码的只读核对，不是E4-D实现或验证结论。

1. `EditorPresenter`的行ID清理可对已记录UUID使用受检`TryFindEntity`，结合确切World身份/成员版本避免选择或目录变化时重新复制全对象列表。`SceneDocument.ObserveWorld`仍须观察每次World Revision并使编辑历史失效，但失效绑定清理仅需在成员/身份变化时扫描已有绑定UUID；Restore、运行时提交、删除/复用和元数据根安装都须同步观察戳。不要改变未被观察期间同UUID重建的既有行为，也不要仅凭成员版本跳过值修订。
2. `AffectedRenderObjects`是导入事件，不是每帧热点。保留prepared ticket/会话/项目检查，用StaticMesh、SkinnedMesh、ClipPlayback的可选类型查询求并集，再按创建顺序去重输出UUID；空类型注册、双几何、同对象多引用及绑定被删除等情况仍可诊断。不将SceneStartupEntities的UUID排序误用于此接口，不新增导入批准能力。
3. `ncma.scene.inspect`和`object.inspect`目前每次完整CaptureSnapshot后分页；新读取应面向请求页/对象，仅返回独立DTO，不暴露Entity、内部数组或第二World。**不能直接把全局4MiB/元数据验证变成只验证当前页**：World外部Set只保证单组件验证，未知JsonConverter/计算属性也可能在Revision不变时改变编码。实现前需明确完整验证戳的来源：只有经过现役完整作者/运行时验证且编码稳定性可证明的精确文档/World代次才可走局部读取；外部World变更、未知编码器或无法证明的状态继续走原有有界完整检查，不以缓存掩盖失败。保存、撤销/重做、场景验证、资源恢复及草稿预览继续使用其原完整文档合同。

读取测试须与旧完整codec结果对照：创建顺序与非UUID顺序、删除/复用、换场景与Restore、值更新、元数据排序/异常UTF-16规范化、offset边界/整数溢出、128/64条分页和256KiB输出、返回副本隔离、陈旧session/revision、外线程/未提交边界、编码回调试图写World；特别加入“页外异常编码/全局超预算仍拒绝”的负例。未知编码的冷路径是正确性回退，不是新兼容格式。使用现有捕获计数证明稳定路径没有整文档复制，另保留完整回归；该计数不能代替E5性能测量。

读取与验证还须区分：原`CaptureSnapshot`执行codec/元数据/字节深度等读取检查，并不等于`PrepareRestore`的完整组件schema/作者组合验证。World直接Set的自定义值验证和载荷检查也不是完整schema验证。新检查戳/冷路径必须精确保持原读取前提，不能用`ValidateAuthoring`替换inspect从而隐藏原本可供诊断的数据；增加“可读但scene.validate拒绝”的对照用例，同时保持所有写入/安装的原校验不变。

### 10.9 E4-D 场景、编辑与既有AI读取：自动候选验证完成

E4-C已提交推送`14cf0dc3b832b36d29301c9cfe93e0fc437f0dda`并核对远端。E4-D现已完成以下实现及同冻结自动候选门禁，限定提交推送后进入E4-E；不表示整个E4或性能门禁完成：

- `SceneDocument.ReadPage`（最多128对象）与`ReadObject`是既有Editor.Core能力的内部读取路径，不新增外部能力或写权限。第一次/修订变化执行原完整CaptureSnapshot与codec，检查全局预算和页外元数据；仅在所有在用组件的编码稳定性具有现役证明时，记录精确World身份、World Revision和文档Revision的成功读取戳。未保留完整文档或组件值副本作为第二存储。
- 同一有效戳的后续读取只复制目标页/对象，复用创建顺序Query；返回前仍使用原codec规范化名字、JSON与Export。未知转换器/计算属性每次走原完整冷路径；缺失对象也不能跳过全局检查。读取不增加PrepareRestore/schema/作者组合要求，保留原本可诊断但不合法安装的数据。全程只读边界及冷编码重入拒绝，不允许编码回调修改World。
- 既有`ncma.scene.inspect`和`ncma.scene.object.inspect`保留会话/revision检查、详情64条、输出256KiB、分页缩减、缺失对象错误优先级及原授权/Undo路径；没有新的MCP批准入口。文档修订变化仍会使读戳失效并完整冷读，不宣称消除每个修订的全部序列化或整帧性能问题。
- `SceneDocument.ObserveWorld`仍观察每次World修订；仅在成员版本/World身份变化时用UUID查找清除失效绑定。Restore与运行时元数据提交同步观察戳，未被观察期间同UUID删除再创建的既有行为不变。`EditorPresenter`按身份/成员变化清理已有行ID，保留选择、草稿、分页和视图generation语义。
- 导入影响检查用StaticMesh/SkinnedMesh/ClipPlayback可选类型查询的并集，去重后保持创建顺序；保留双几何、缺Transform和不可见对象的引用报告。该入口是离帧导入事件，原prepared ticket/项目/会话检查不变。用户显式GetObjects副本与ComponentAuthority冻结成员合同保留。

新增13组场景读取测试覆盖完整codec对照、4096容量、offset溢出、值与成员版本、删除/复用/Restore、副本隔离、UTF-16/Export顺序、页外元数据错误、全局4MiB、未知编码冷读、可读但schema不合法、线程/步边界、回调写入与重入、UUID绑定观察。新增6组能力测试覆盖精确旧输出、128/64分页、session/revision/权限/Undo/Redo/Play冻结、诊断与validate区分、256KiB场景拒绝及详情缩减续页。新增3组消费者测试覆盖可选注册、4096混合导入引用、实际Presenter行ID/选择/删除/撤销恢复；原M3.6真实导入工作流仍执行。

初步Debug：Scene原36/Runtime文档11/新增13、Editor.Core原36/新增6、Editor.Services157通过。首轮服务编译因误用`CreateQuery<T>`而CS0308失败，修正为已存在的`Query<T>`，日志`out/ecs-e4d-services-debug-targeted-first.log`保留，未放宽断言。最终源码定向双配置脚本为`out/verification/ecs-e4d/run-targeted-first.ps1`，总日志`out/ecs-e4d-targeted-first.log`；各轮状态按下文终态为准，不轮询已结束会话。ECS-PERF-01/02仍由E5统一修复与验收。

最终定向会话83599已结束exit0：Debug/Release均通过原Scene36/Runtime文档11/新增读取13、原Editor.Core36/新增能力6、Editor.Services157（含3新增消费者），六次显式构建均0警告。日志`out/ecs-e4d-{scene,editor-core,editor-services}-{debug,release}-{build,tests}-targeted-first.log`，总日志同上。

最终源码冻结**949文件**：`out/verification/ecs-e4d/source-freeze-first.json` SHA256 **AC7549B1C1EE09E208425DFD0D50DD8053DA3EE363711CCA54BB021E11F0261B**。在批准的正常Windows/TLS环境顺序执行标准Debug/Release构建（SkipTests、不部署）、外层完整89项、smoke/Python45/inspect及前后冻结核验；会话**55989已结束exit0**，所有本片测试会话均已结束，不重启或轮询旧会话。

| 最终同源码门禁 | Debug FIRST | Release FIRST |
| --- | --- | --- |
| 标准构建 | 成功，0编译警告 | 成功，0编译警告 |
| 完整CTest | 89/89，1122.43秒 | 89/89，1014.55秒 |
| 原Scene / Runtime文档 / 新读取 | 36 / 11 / 13组通过 | 36 / 11 / 13组通过 |
| 原Editor.Core / 新能力读取 / Editor.Services | 36 / 6 / 157组通过 | 36 / 6 / 157组通过 |
| 真实源与搬移管线宿主 / TLS / 搬移客户端 | 657 / 4583 / 1174检查通过 | 657 / 4583 / 1174检查通过 |
| 托管/原生smoke、Python45、inspect | 通过 | 通过 |

日志为`out/ecs-e4d-build-{debug,release}-first.log`、`ctest-{debug,release}-first.log`、`ctest-{debug,release}-details-first.log`、`supplemental-{debug,release}-first.log`（后三类同样使用`out/ecs-e4d-`前缀）；总日志`out/ecs-e4d-gates-first.log`。原Runtime/身份/Query/结构批次/增量验证/Gameplay/Movement/UI/E0等回归标记均经最终交付脚本核对，不将新增组数混入原计数。

最终交付核验`out/ecs-e4d-delivery-verification-first.log` SHA256 **987398AF120B4F276E1E453372E701EFB796139CE6C87507714F93DECFC1B26F**通过：

- 同949源码冻结、双配置完整日志、十个候选包及其构建代次一致；Debug候选`7106b50f6f6449c6a9a1c802ffbe037c`，Release候选`b0da58e3418c4908ad90920752d318dd`。
- 每配置Editor/Player Null/DX11/NullPhysics/DX11Physics清单为144/56/113/67/116文件；实际146/57/114/68/117（含manifest，Editor另含受检日志），SHA/大小/闭包通过。
- 正式E1-B代次`7f3d3822dd39431b9c203e17194afb8c`、144清单/148实际/148备份未变；EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。E0原18程序集、E3-A原8输入/9日志不变。
- E4-C首轮7个失败目录、三轮失败日志及最终交付证明保留；File.Replace根因/失败前独立哈希仍未证明。E4-D首轮CS0308日志SHA256 `B169788127DEE697C0C8BAA531EDA7AFDA6E24292E884A65299504B62779A681`保留，未削弱产品或断言。
- 原19消费者当前源码哈希及残留清单核对通过；本轮无原生ABI、MCP权限或部署策略变更。

门禁结束后再次实际只读核对E3-D旧18输入/6日志/冻结/原证明：`out/ecs-e4d-deferred-performance-second.log` SHA256 **B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A**，与first一致；没有重采样或关闭性能债务。辅助核验按源码managed/部署out各自边界检查路径，未放宽部署约束。

本片限定20个源码/测试/文档文件提交推送并核对远端，排除4项IDE设置和out。**E4-D自动候选门禁完成；E4-E尚未实施；E3-D仍“功能验证通过、性能未达标”。** 未执行新的完整no-Skip/旧三profile/正式部署；E5及M8.3未开始，人工/目标机/自包含/整帧性能/1h与历史调查全部保留。

### 10.10 E4-E 联合门禁准备核对（只读，待E4-D交付）

等待E4-D完整回归期间，按E4-A原过滤规则重新检查：22处GetEntities匹配降为3处（1定义、2调用）；B/C/D分别消除原9/7/3处表达式，原审计JSON未改。剩余三处与原retain分类的源码原文一致：`World.GetEntities`现役显式副本API、`SceneWorld.GetObjects`高层用户副本、`ComponentAuthority`完整冻结成员覆盖检查。证据`out/ecs-e4d-consumer-residual-first.log` SHA256 **12CC5AD2EDA4DC011B26660B1FDB7B8341C047D211ABC241F555B811792414AC**同时记录原19消费者文件的当前哈希；这是词法与源码核对，不是完整调用图或性能测量，尚不构成E4-E验收。

后续联合验证应保留三条现役路径：真实Editor/Player/Null及搬移包的管线宿主；真实Play固定步→Query/提取→完整codec冷对照；私有管道/MCP→当前EditSession的分页读、批准写、撤销/重做与撤权负例。沿用现役运行入口，不创建第二个模拟World或以fake替代原生物理/GPU门禁；新增交叉断言后须建立新冻结并完整双配置回归。E4-D已通过的独立领域测试与完整门禁不自动关闭E4-E。

必须保留的完整冷路径还包括保存、恢复、Undo与作者验证、资源准备及隔离预览。`EditorWorkspace`的完整视图快照已有文档代次/revision缓存，选择/草稿预览与组件类别读取仍依赖这一合同；本片没有把它宣称为每帧新建或删除其缓存。AI读取戳在每次修订变化后仍需要完整冷读，所有这些成本须如实纳入E5，不将局部分页计数当作整帧性能验收。

E5测量还应区分首次与暖态请求：当前页读取按需创建默认容量4096的EntityQuery结果缓存，即使小场景也存在首次缓存分配；页/对象返回仍拥有独立数组与JSON数据。当前13/6组测试验证的是语义、限制与完整捕获次数，不是分页读取0B或5%时间门禁。是否调整缓存容量须依据场景规模与实际测量，不在冻结回归期间修改源码或以这些计数代替性能数据。

### 10.11 E4-E 联合验证实施中

E4-D已提交推送`7636609f10e764daa60122178790c17c990015c5`，开始本片前重新只读核对远端main一致。本片首先扩展现役测试，不更改生产代码、ABI、场景格式、授权或调度边界：

- `EcsJointCases`：原100/1000/4096 × 无写/稀疏/全写/成员变动/删除创建15组保留；在原受检固定步窗口外，用同一Play文档的冻结只读EditSession分页检查Query、渲染视图UUID、当前tick/World身份、组件JSON与完整codec冷对照相同。暖页/对象不增加完整捕获，唯一历史保持空；冷对照不是整帧性能测量。
- `PipelineHostProbe`：真实Editor在渲染修改、Undo/Redo、隔离Play/reload/Stop后检查原活动EditSession的场景和组件读取；真实Player/Null在原现役入口安装只读提交观察者，检查tick/Query/UUID和写入拒绝，调用者边界作完整codec对照。源及严格搬移产品都执行，原图像、资源、API验证、无图形Null和故障断言仍保留；不为Player增加Editor依赖，不在固定步进行IPC/IO。
- `EcsReadTransportCases`：真实当前用户私有管道、显式配对、131对象跨页与创建顺序、单次冷证明/暖页、默认拒写、批准写、UI Undo/远端Redo、恢复后Entity失效、组件副本、revision/generation过期、页预算和连接撤权。原MCP真实stdio子进程另加暖分页与129上限拒绝；第三方手动MCP验收仍未关闭。

Debug定向已通过：Gameplay原53/Movement38/UI18/E0fixture11、原联合15及新跨域15；私有管道871检查、原G4/G5/生命周期和真实stdio；实际源与搬移宿主管线1607检查（原657与新增交叉检查共同执行）。三个构建均0警告。日志`out/ecs-e4e-{gameplay,transport,hosts}-debug-{build,tests}-first.log`；会话16945/90047/37224均已结束exit0。Release定向脚本`out/verification/ecs-e4e/run-release-targeted-first.ps1`运行中，尚未形成最终同冻结完整双配置结论，不能据此关闭E4或提交推送。

之后必须新冻结、标准双配置构建与完整89项回归、补充smoke/Python/inspect、十包/正式安装/备份和历史证据核验，并重新复核E4-A残留分类。源代码迁移清单只证明消费者落点，不代替跨域回归或性能证明。正式E1-B EXE保留；E5仍须解决ECS-PERF-01/02并完成原始等价/配对性能门禁，未通过不得进入M8.3。

Release定向会话16202随后结束exit0：Gameplay/Transport/真实源与搬移宿主均通过，新增跨域15、私有管道871、宿主1607与Debug一致；三个构建均0警告。本片四个测试文件变化，不修改生产源码。最终冻结950文件，`out/verification/ecs-e4e/source-freeze-first.json` SHA256 **5CA74DA6BC19FB00589C08D09E61789C68B9554E17C497B821E04788C38CE917**。完整门禁会话**8761**在批准的正常Windows/TLS环境执行`run-gates-first.ps1`，当前Debug标准构建；仅此会话运行，不重复启动，不修改冻结源码。冻结命令末尾rg无警告匹配返回1，冻结自身明确成功并获标准构建前复核，不记成构建/测试失败。

本轮残留清单`out/ecs-e4e-consumer-residual-first.log` SHA256 **1A8BDC814FC4B96C1AA5FD90744E1CE09C4C43F3F5E1917DC36610EBDA641E18**确认原22行现3行、定义1/保留调用2、B/C/D替代9/7/3及原19消费者源码哈希；原审计JSON不变。E3-D归档再次只读核验`out/ecs-e4e-deferred-performance-first.log` SHA256 **B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A**，未重采样。最终交付脚本`verify-delivery-first.ps1`已准备并通过语法解析，但须在完整双配置及补充门禁结束后才运行，不能用该脚本的存在或定向结果提前宣布通过。

同一会话8761的Debug标准构建成功，0编译警告，候选`648cf38e27d74c49bb58acfb92a78d38`；**Debug FIRST完整89/89，1141.20秒**，smoke/Python45/inspect及测试后950冻结核验通过。最终详细日志独立核对跨域15/管道871/真实宿主1607/stdio、TLS4583/搬移1174、D读取13/能力6/服务157标记完整。同一8761现进入Release标准构建，尚无最终双配置结论，不与先前D门禁或本片定向结果拼接。

Release标准构建随后成功，0编译警告，候选`644c396f375a4806b8e6bce69bab7c32`；同一8761的Release完整CTest已通过15/89，主渲染进行中。最终双配置及交付核验仍未完成。

**Release FIRST随后失败，不能记作通过**：第16项主渲染73.85秒遇到未修改的`SceneDocumentFiles.Save:60`原子`File.Replace` IOException“无法删除要被替换的文件”，调用方`EnvironmentProductTests:95`，发生在第一组环境身份负例保存。会话8761仍在执行剩余CTest，尚未终态；不启动重复门禁、不修改冻结源码。

只读定位目录`out/verification/m2/render-Release/environment-products/b660bf8f0c604ef38821bd54c1449622`，目标`runtime-static/start.ncmascene`为2993字节，SHA256 **3F7F2EBC0DB12A84E68CDBD444840D915175BE085A22B140D5CA3D7E32E73CC8**，属性Archive、环境generation=1；身份负例报告为0，前一坏NCP的tick0/session空报告存在。目录687文件的路径/大小/哈希保留证明`out/ecs-e4e-release-failure-inspection-first.log` SHA256 **D41B15311DAE76C0D5EA7142307B2D2C6CCEDEE3197654721CB588F2E26CCC50**；该目录并非正式out/bin，检查没有改动它。当前目标/父目录ACL为继承允许项、未见显式拒绝；这不证明故障时无瞬时占用，更不证明根因。没有独立保存前哈希，不将现有文件可读冒充逐字节回滚证明。

等待8761终态后检查完整结果，使用原测试在新GUID目录独立复测，再按同源码完整门禁处理；保留FIRST全部日志/目录，不直接重试覆盖或清理失败目标，不改原子策略/断言/超时。原FIRST最终核验脚本会正确拒绝本次失败，后续成功轮次必须使用新日志和核验，并检查本次687文件失败证据仍完整。

E4-E首轮后续结果：会话8761已结束exit1，Release **88/89，932.48秒**，唯一失败为上述主渲染原子保存；全部新增跨域15/管道871/stdio/真实宿主1607及TLS4583/搬移1174均在该轮详细日志通过，但整轮仍失败且没有Release补充阶段。首轮CTest日志SHA256 `845D6AB34CE649785947302F881715A1F83AC0704830EF9BFAB6F0C56014EF22`，details `1FDC230C5ABA67E74B6F88FE47F9AE352E7E1B33C026120BE2B52A353205ED05`，总日志 `DEB2AF0F5EAFD135FDEE318400F429977DBAE56A814534242D6A69425324CA8A`。原渲染测试及必需夹具现以会话30404独立复测，保持950冻结和全部原断言，在新GUID目录运行；当前尚无复测结论。若通过，执行独立SECOND完整双配置，最终核验必须同时保留FIRST日志和687文件原失败闭包。

E4-E独立复测随后通过：会话30404结束exit0，原生参考夹具和原主渲染 **2/2，169.39秒**（主测试168.70秒）；新目录`environment-products/8d0a3447d69447b2873fa7805dda959d`原产品630检查完成。复测日志SHA256 `91AD2DBFCD278094B26D05BADC1719705123EC2F4B9A61E8D6B06DD4C64BC326`，details `43663028316F4A9D5F897BB727BBA2958F69B1108D56BFE184B16CD3110497CB`，前后950冻结一致。没有产品修复或根因结论。会话30568现执行`run-gates-second.ps1`，同一冻结重新完整Debug/Release标准构建、各89及补充测试，使用SECOND独立日志；首轮和隔离结果不拼接为验收。最终`verify-delivery-second.ps1`额外校验首轮三份失败日志及原687文件闭包；当前仍未完成门禁/提交/部署。

同一会话30568的 **Debug SECOND完整89/89，1135.05秒**，补充smoke/Python45/inspect与前后950冻结均通过；标准构建0编译警告，候选`75d133d684544171bc5dbd9386c807fb`。第二轮details中的跨域15/管道871/stdio/宿主1607/TLS4583/搬移1174/D读取13/能力6/服务157再次独立核对完整。目前30568已进入Release SECOND标准构建及完整回归，尚无最终双配置或交付结论；首轮失败、隔离结果及性能债务保持原状态。

### 10.12 E5 测量入口准备核对（只读，待E4-E交付）

等待E4-E门禁时只读取现有合同与源码，未启动E5实现或任何性能采样：

- `Measure-EcsPaired.ps1`仍锁定交付的E2-B参考包，复制六个真实产品程序集和三个同驱动文件；各变体九文件闭包前后验证、独立进程、交替运行顺序、TieredCompilation=0、完整持久语义摘要均须保留。当前输出目录/事件名称仍为E3-D；E5如引入新证据命名须独立建档，不能覆盖或改称旧测量。
- 原`EcsBaseline`的`read_transform*`测的是GetEntities副本枚举，不能替换为typed Query后继续称相同基线。E5应保留该共同工作量，再单独记录typed Query、E4页/对象冷暖读取与实际领域路径；CPU合成收益不代替真实GPU/上传/驻留证据。
- 原World全写只有整体耗时/分配/GC次数，Play才提供分层时间。源码中的BeginStep成员字典复制、EncodeObject派生字典/DTO数组、组件schema/深度/精确编码预算及派生缓存存活期只是待剖析位置，**不是已证实的尾延迟根因**。需关联分项耗时、分配与实际GC暂停，再选择修复；不删验证、不跳过可信回调、不扩大预算、不增加裸ref或第二存储权威。
- 当前PATH未发现dotnet-trace/dotnet-counters命令，只能说明该命令查找没有命中；不等于机器无诊断工具。本次未安装工具、改变GC/JIT配置或与完整回归并行测量。后续先确认可用诊断入口，诊断采样与最终无干扰配对分开保留。

放行条件仍以第5/6节及ECS-PERF-01/02为准：至少三轮同驱动独立Release进程，保留全部原始样本与语义/闭包；热点改善与小场景5%待测目标不得用重抽样挑选或放宽阈值关闭。最终还须完整无Skip双配置、原格式/ABI/权限/无Python、受检包/部署恢复和历史证据核验；人工/目标机/自包含/整帧性能/1h继续分别记录。E4-E通过不自动构成E5或整个ECS完成。

### 10.13 E4-E 最终自动验证结果

第二轮顺序会话30568已结束exit0。以下结果来自同一950文件冻结，覆盖前文运行中状态，不与首轮或定向结果拼接：

| 门禁 | Debug SECOND | Release SECOND |
| --- | --- | --- |
| 标准构建 | 成功，0编译警告 | 成功，0编译警告 |
| 完整CTest | 89/89，1135.05秒 | 89/89，1009.45秒 |
| 新Play跨域 / 私有管道 / 源与搬移宿主 | 15 / 871 / 1607检查通过 | 15 / 871 / 1607检查通过 |
| 实际MCP stdio / 原真实TLS / 搬移客户端 | 通过 / 4583 / 1174检查通过 | 通过 / 4583 / 1174检查通过 |
| 原领域回归、smoke、Python45、inspect、冻结检查 | 全部通过 | 全部通过 |
| 候选代次 | `75d133d684544171bc5dbd9386c807fb` | `444ac639667440fba4568309e10725f9` |

最终证明`out/ecs-e4e-delivery-verification-second.log` SHA256 **102FC955EC1C9CAFCA3845311D9AA17D0D76DE8FC9909DA72BBC102D9CD16C65**核对通过：十个候选包与本轮构建代次、清单、大小及哈希一致；正式E1-B仍为144清单/148实际/148备份，EXE仍为`C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`；原E0/E3-A/E4-C/E4-D证据完整。E4-E首轮失败三份日志、原失败目录687文件及隔离复测日志逐项校验保留，File.Replace根因仍未证明。

测试后重新实际检查E3-D延期性能归档，`out/ecs-e4e-deferred-performance-second.log` SHA256 **B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A**不变；没有重测或关闭性能债务。原GetEntities审计22行现3行（1定义、2保留合同调用），19处B/C/D迁移与19个原消费者源码哈希再次核对。

**E4消费者迁移的自动功能门禁完成，ECS整体与性能验收未完成。** 本片四个测试文件及五个文档文件进入限定交付，排除4项IDE设置和out；未执行新的无Skip部署，正式EXE不变。用户最新要求先整理E5详细方案，因此当前仅制定下节计划，不自动开始E5生产代码修改或性能采样。

## 11. E5 详细实施方案

状态：2026-10-19用户已确认“按方案开始E5阶段”，E5-A自动功能门禁与基线证据已完成，详见11.11；E5-B实施中，详见11.12。E4-E自动门禁及本地/远端提交e51ac6a89e3546a490a676410197787679768c98已核对；按E5-A至E5-F顺序执行。E5不是新一轮架构改写，而是修复性能债务、证明端到端等价、精确清理残留并完成ECS交付；通过约定门禁后再恢复M8.3及后续，最后进入M9 Windows发布。既定性能目标采用本节方案；PERF01/02尚未关闭，未部署新EXE。

### 11.1 目标和不变范围

- 关闭ECS-PERF-01：直接World的4096对象全写P95三轮仍比E2-B高12.895%、10.202%、15.891%。必须诊断并修复，约30%分配下降不是替代条件。
- 关闭ECS-PERF-02：100对象复制读取尚未证明P95退化不超过5%。这是待证明项，不能把噪声当作已证实退化，也不能把typed Query成绩替换原复制工作量。
- 证明E4真实角色/动画、渲染、Editor读取及AI工具路径在同一World上等价；分别记录冷启动、稳定帧、结构变化和恢复成本。
- C#唯一World、Entity代次身份、同存储GameObject门面、扁平组织、唯一Undo、owner-thread、提交后只读和原生物理fail-stop均保持。无第二ECS、裸ref写、并行World写、Python游戏脚本或新场景格式。
- 不扩展Vulkan/OpenGL、网络复制、AI推理服务或M8.3功能。AI仍通过原配对/批准能力读取副本、提交提案；不能为了性能绕过权限或让IPC/推理阻塞tick。

### 11.2 小阶段与依赖

| 小阶段 | 核心交付 | 放行到下一片的条件 |
| --- | --- | --- |
| E5-A 测量合同与当前基线 | 固定对照、工作量、阈值、环境记录和原始证据格式 | 驱动负例/语义对照通过；同源当前基线可复现；统计不确定项明确登记 |
| E5-B 密集写入诊断与修复 | 解决PERF-01，减少已证实的准备/验证/分配成本 | 定向与完整双配置通过；同驱动配对证明全写尾部修复，其他工作量不被牺牲 |
| E5-C 查询与小场景成本 | 解决PERF-02，验证E4冷暖读取、查询重建和复制成本 | 原复制基线及独立typed Query分别通过预登记标准；无无界缓存或额外写权 |
| E5-D 真实领域与产品成本 | Editor/Player/Null、DX11、动画/物理/UI及工具联合报告 | 原功能/图像/API/权限通过；实际CPU/GPU/上传/驻留证据满足冻结预算 |
| E5-E 精确残留清理 | 旧实现/入口/构建/包依赖清单及精确删除 | 受影响消费者全部迁移、完整回归通过；现役副本和历史对照不误删 |
| E5-F 最终验收与交付 | 同一最终源码完整无Skip双配置、最终性能证明、受检部署和交付记录 | 功能、性能、包/恢复全部通过且提交推送核对；待验人工项不冒充通过 |

每片先定向测试，再同源码Debug/Release完整回归和补充核验；需要性能放行的片再做无并发干扰的Release配对。失败保留现场，修复产生新冻结并重跑受影响的完整门禁；通过后限定提交/推送/核对远端，再进入下一片。E5-A允许交付可重复的失败基线以进入B修复，但不能把该基线标为性能通过；若B无法达到预登记标准，则停在B，不直接跳到F宣布完成。

### 11.3 E5-A 测量合同与当前基线

1. 冻结E4-E交付源码和候选包；保存提交、驱动/脚本、实际加载程序集、资产、包清单、配置和环境哈希。保留E0原18文件、E2-B对照包、E3-D退化与修复归档和全部失败证据。
2. 沿用`EcsBaseline.cs`、`EcsPairedEntry.cs`及`Measure-EcsPaired.ps1`的共同工作量和输入闭包。为E5建立独立证据目录/事件版本，不能继续把新运行记成E3-D或覆盖旧归档；驱动新增字段需要相应严格验证器和缺失/重复/篡改负例。
3. 主配对继续使用同一个驱动分别加载E2-B和最终候选的六个产品DLL及三个驱动文件，前后验证各九文件闭包。另以E4-E为增量对照，归因B/C收益；不得把E4对照替换E2-B后掩盖旧债务。E0用于原始语义与总迁移核对；如要给出相对E0的性能比例，必须先建立跨版本共同工作量，不能直接拿不同JIT/驱动的历史数字相除。
4. 保留原33项、100/1000/4096对象、无写/约1%写/全写、Behaviour全写和1%替换，以及原GetEntities复制读取。新增typed Query、混合组件/结构变化、Editor/AI页读取的冷暖组使用独立名称，不修改旧组定义。
5. 正式Release至少三轮独立进程，AB/BA/AB交替；保留原12次预热、100样本和每样本操作数的可比系列。若增加采样，旧系列保留，新方案在运行前固定并对两边相同执行；不挑最好三轮。TieredCompilation=0只属于这一历史可比系列，产品默认JIT/GC配置另作端到端组，不把基准配置偷偷变成产品默认。
6. 记录CPU、运行时、OS、频率/电源状态、并发负载和时间。正式测量与构建、CTest、诊断追踪分开；不自动杀未知进程或改变系统安全设置。异常轮保留并说明原因，不能因结果不佳删样本。

产物：测量合同、工作量矩阵、环境/输入清单、原始时间与线程分配、每轮P50/P95/P99/最大值、GC次数和内存口径、语义摘要、可执行证据校验器。分位数沿用nearest-rank以便对照；进程累计峰值不称作某个工作量的增量驻留，GC次数不称作暂停时间。

### 11.4 性能判定规则

以下落实第6节目标，实施前在E5-A冻结适用集合，不允许测完后移动标准：

- 高频热点改善目标仍为至少20%，小场景P95退化上限目标仍为5%；预登记每个热点的规模、冷暖态和边界。所有非热点共同工作量也要完整报告，不能以平均收益掩盖单项严重回退。
- 对PERF-01，本方案建议以4096全写P95恢复到不高于E2-B为修复目标，并同时检查100/1000和P99；若仅达到某个较宽容差，必须明确结果并请用户确认，不能自动宣布债务关闭。
- 对PERF-02，保留原复制读取作为5%判断对象；typed Query另测并要求暖态枚举/Get不出现逐实体装箱或分配。复制数组和分页JSON存在合理输出成本，不能把整个API都标成0B。
- 至少三轮是最低复现要求，不是噪声情况下的充分证明。报告每轮比值、环境波动及预登记的过程级统计方法；区间跨越阈值或重复不稳定时记为“未证明”，按预定策略增加成对样本/轮次，不放宽阈值或只挑有利结果。
- 真实领域按E5-A/D冻结的CPU、GPU、上传和资源预算分别判断；合成CPU、kernel-only图片或fake物理不能代替实际产品。已有图像/API零错误要求和生命周期断言保持。

### 11.5 E5-B 密集全写尾延迟

先定位，再选数据结构改动；以下均为待验证候选，不是已确定根因：

1. 对World直接步拆分Begin、用户写入、schema/编码、候选准备、发布，以及提交后清理；对Play沿用现有分层时间。诊断版本单独归档，确认默认关闭开销后才能进入正式样本，追踪结果不能替代无追踪配对。
2. 关联真实GC暂停、分配来源和存活期。检查`World.BeginStep`成员/UUID字典复制、`World.Validation`派生对象/组件字典与DTO数组、序列化/深度扫描及稳定编码缓存；不得只看到GC次数下降便推断暂停改善。
3. 若证据支持，依次尝试：非结构步惰性复制身份索引；仅对脏对象更新派生编码结构；复用已验证的稳定组件编码与精确预算；缩短候选缓存引用存活期。每次只引入可归因的改动，验证不成立就不保留。值页大小不盲改，通用对象池/全局共享缓存不作为默认答案。
4. 保留未知getter/转换器每次验证的有界冷路径、可信回调、完整schema、64KiB组件/4MiB世界及现役结构预算、UTF-16规范化和Export顺序。所有可能失败的准备在发布前完成；不得为减少分配在部分发布后执行扩展回调。

预计修改边界：`World.cs`、`World.Validation.cs`、`ComponentStorage.cs`、`PagedValues.cs`及对应Runtime/Scene/Gameplay测试，以实测定位为准。测试覆盖abort后已提交值/缓存不变、多批覆盖、结构毒化、槽位烧号/复用、跨World/Restore、权限、信号与回执、观察者故障和原生fail-stop；最终同驱动全33项重新配对。

### 11.6 E5-C 查询和小场景读取

- 将查询构建、首次求交、暖态迭代、MembershipVersion变化、值变化、Restore重建分开测；对All/None、1/2/3类型、空集合和4096容量保留完整身份及创建顺序。
- 检查默认4096结果缓冲对100对象场景的冷分配、临时集合和排序。只有测量支持才调整有界初始容量/复用策略；最大4096、caller-owned结果、不截断、不返回可写底层数组保持。
- 对`SceneDocument.Read`、`ncma.scene.inspect`及对象详情分别测首次完整证明、暖页、revision失效、翻页和恢复。保留128/64分页上限、256KiB输出、独立JSON副本及页外错误检查；未知编码仍不能借暖缓存逃过验证。
- 检验选择/草稿、行ID、导入引用和资产变化是否增加热路径成本。Editor完整冷快照/Undo允许存在，不为“所有路径零分配”牺牲正确性。

预计边界：`EntityQuery.cs`、`World.Read.cs`、Scene读取及Editor.Core相关文件；先测试再决定是否修改。需要同时通过原复制基准、typed Query独立组、13组场景读取/6组能力读取和E4-E管道/stdio/实际宿主联合回归。

### 11.7 E5-D 实际产品和领域成本

固定场景与视角，覆盖Editor编辑→Play→暂停/单步→Reload→Stop、独立Player DX11、Null及严格搬移包。以当前真实FBX角色、动画/根运动、Jolt/Box2D与UI夹具为起点，分别记录无角色、小角色组和现役最大预算；不强行把4096个图形实体当成允许4096个角色。

测量运行时组件查询、动画/物理准备、渲染提取、GUI展示、CPU提交、实际GPU、上传字节及原生资源生灭。图像对照、DX11验证、现役角色/骨骼预算、材质/阴影与恢复断言同时保留。纯2D/Null路径不得因ECS变化引入无用3D资源；2D与3D仍使用各自产品管线，不能以开启更多3D功能掩盖开销。

AI工具增加真实私有管道/stdio的有界读与批准事务成本记录：冷暖分页、频繁revision、Undo/Redo、连接撤权和有界队列。请求在安全边界处理，测量“工具响应”与“模拟帧”分别报告，不把模型推理或外部服务延迟算作已实现能力。必要优化仍通过同一个EditSession，不开第二条命令历史或直接World写口。

产物为领域成本矩阵、原始CPU/GPU/上传/驻留证据与图像/权限等价结果。真实用户素材、目标机、自包含、人工UI/MCP和1h长稳仍分别验收；本机自动夹具不替代它们。

### 11.8 E5-E 残留审计与精确清理

先生成“路径、调用者、执行时机、替代物、处理、验证”清单，覆盖C#引用、原生导出、构建目标、SDK示例、包内容和文档。确实废弃的类型/入口直接删除，不创建转发别名或兼容格式。

必须区分三类：可删除的旧生产机制；应保留的保存/恢复/Undo/完整验证冷路径；仅在测试/历史归档中的参考算法。当前`World.GetEntities`、高层`SceneWorld.GetObjects`和`ComponentAuthority`覆盖检查属于现役合同，不能因搜索仍命中而删除。原基线DLL、失败现场和数值kernels/SDK/用户资产不属于清理对象。

清理后重新核对Runtime→Scene/Gameplay/Editor的单向依赖、Player无Editor/MCP/Gui依赖、可选Python不进入默认产品、UUID持久化/运行时Entity拒绝、旧格式/旧入口拒绝和批量C ABI。每一项删除必须有替代消费者和回归证据；清理改变最终程序集就重新测量，不能复用删除前的性能证明。

### 11.9 E5-F 最终同源码验收与发布

1. 冻结完成修复/清理的最终源码与测试驱动；候选构建、完整双配置、原负例、领域测量和至少三轮配对均以此为准。任何产品/测试/构建修复使旧冻结失效，保留旧证据并重新形成最终链。
2. 为部署增加E5验收凭据校验，接到现有`Build.ps1`的`Invoke-EditorDeployment`之前。凭据绑定源码、当前配置的功能结果、Release正式测量输入及产品字节；缺失、过期、配置错误或包哈希不一致一律不部署。当前入口尚无这项E5校验，这是待实施项，不能把已有M2审计当成E5性能通过。
3. 先生成不部署候选、完成最终功能/性能证明，再运行原完整无Skip `Build.bat -Configuration Debug`和`Build.bat -Configuration Release`。保留原13原生/76托管产品组合及新增测试、Python45/inspect、真实TLS/搬移/无Python、旧三轮profile和前后审计；测试总数如有新增按清单核对，不把“仍89项”当硬编码上限。
4. Debug的部署凭据绑定同源码及本配置功能门禁，不宣称Debug性能通过；Release需匹配实际用于最终性能验证的产品字节。重新构建若改变相关二进制，旧性能凭据失效，必须在部署前补测而非先发布后解释。
5. 继续受检staging、原子替换、备份和journal恢复，最终Release安装到`out/bin/NcmaEngine.exe`。校验完整安装、备份、实际EXE验证、独立Player/Null包及模块包边界。失败保留目标/日志，不直接删除journal、不强制覆盖、不降低原子策略；未经证明的File.Replace根因保持开放。
6. 交付报告列明每片提交、最终冻结/输入/结果/包哈希、两项性能债务的判定、剩余人工项目和M8.3前置条件。限定提交推送并核对远端精确SHA后，才按用户指示进入后续阶段。

E5完成的必要条件是功能等价、既定性能目标和交付恢复均有匹配最终源码的证据；“代码已改”“全部单测通过”“平均FPS提高”或“分配减少”都不足以单独放行。无法证明的性能项继续标记未通过/未证明；人工待验项独立保留，涉及后续阶段前置时仍阻止放行，不自行豁免。

### 11.10 E5-A 测量合同实施记录

用户确认执行后，首先核对E4-E本地/远端精确提交e51ac6a89e3546a490a676410197787679768c98。当前改动仅测试、测量工具和文档；没有修改生产Runtime、场景、权限、原生插件或部署策略，ECS-PERF-01/02仍开放。

- `scripts/ecs-e5-contract.json`为采样前登记的v1合同：原33项共同工作量、100/1000/4096、12预热/100样本、原每样本操作数、Release与TieredCompilation=0不变。三对独立进程AB/BA/AB；每轮P95比值和均值log比值的双侧95% Student-t区间（df=2）一起报告。逐轮及区间上界均不高于阈值才判该项met；跨阈值为unproven，不挑最好轮次。三对不保证统计充分，区间假设及机器噪声限制显式保留。
- 主对照固定E2-B发布包47f4cceb6906453e9ba274442320fde8及清单SHA C8A6B120D70F3A5DEF4F0088088E5239D1DC4EBD831A7AD1E295682C8C80F8F8；增量对照固定E4-E Release SECOND包444ac639667440fba4568309e10725f9及清单SHA 73F53A65BBD11B8EF5B1CEE868D8C59FFE33AD6E923CDA09638644A5A1658D0E。E4-E只用于归因，不能代替E2-B关闭旧债务。
- 预登记PERF01为4096直接World全写P95比值≤1.0；PERF02为100对象两种原复制读取≤1.05；20%热点目标明确为1000/4096的World/Play无写及1%写四类，其他共同工作量/P99完整报告，不用平均值掩盖回退。新typed Query暖态分配目标单列为0B，不替代原复制数组成本。真实领域预算仍待E5-D冻结，不声称这些CPU标准能放行整个E5。
- 新读取组27项，仅E4-E/current可比：9种×3规模，Query冷建、暖态、混合组件、成员变化重建、Restore后重建；Editor冷页、暖首页、暖尾页、对象详情。失效操作/请求ID构建在计时和线程分配区间外；Query冷建本身及返回值/UUID/顺序检查在区间内。GC和进程内存包括区间外准备，生命周期峰值不称作单项驻留；读取组不包含管道/stdio传输或图形整帧成本。
- `Measure-EcsE5.ps1`使用独立ecs-e5a/GUID目录与ecs_e5事件；存档合同、冻结源码清单、脚本/验证器、包清单、两份相同三文件驱动与各六产品DLL，进程前后检查真实加载位置与字节。记录CPU/电源方案/一秒进程CPU增量，发现并发构建/测试负载拒绝正式启动，不自动关闭用户进程或改变系统设置。受限环境读不到CPU信息时明确记unavailable，不能称机器空闲证明。
- `ncma_tools.ecs_e5_evidence`离线复核归档闭包、合同/输入/日志哈希、完整工作量矩阵、JSON重复字段、原始样本/nearest-rank摘要、配对顺序/语义与运行时配置；基线验证成功也固定fullPerformanceAccepted=false/deployed=false。Python新增8项测试含归档链21种负例，以及缺失/重复/采样参数/统计篡改和不确定区间；实际程序集输入拒绝测试扩为E5的14项，原E3入口保持可用。
- 初步Release驱动构建0警告；准备性共同冒烟归档fad0cc3a346e4e8fbbbd214d950af173、读取冒烟485cd0f8bd8c41c198b91f4d884f0efc及输入负例negative-881a8410c9db4802a35f2b2f50f7e674通过。这些不是最终冻结正式测量或完整门禁证据。早期脚本PowerShell5 JSON数组包装/源码误用部署路径校验失败保留于`out/verification/ecs-e5a/preparation-failures.txt`及原冻结记录；只修工具检查，未修改产品。
- 最终本轮候选冻结955文件：`out/verification/ecs-e5a/source-freeze-first.json`，SHA 186D9DE142553876CB70D9603F2357A3B935F2A4C62FCBCE2BE5C68CA13085D1。完整双配置结束后才开始正式采样，没有与本任务构建/CTest/诊断追踪并发；最终结果见下节。

### 11.11 E5-A 最终结果：功能与基线证据通过，性能债务仍开放

同一955源码通过标准`Build.bat -Configuration Debug/Release -SkipTests`（两配置均0编译警告、不部署），之后分别完整CTest **89/89**，耗时 **1116.60/995.04秒**；managed/native smoke、Python **53/53**、inspect及前后冻结检查全部通过。两配置新读取夹具9组，原Runtime/Query/结构批次/增量提交、E4-E联合15组/私有管道871/真实stdio/源目录及搬移宿主1607、真实TLS4583/搬移模块1174等原断言均保留。候选Debug **f49627d47f574a31a34c5faaa5e3dfdf**、Release **6e9d44be5bf54119b494ad9bec858e79**。完整门禁会话77856和配对会话87517均TERMINAL exit0，不存在待轮询测试。

采样使用最终Release候选、同一个三文件驱动、各六个真实产品DLL。最终共同冒烟d7d22727024e459f88be339ec5adef05、读取冒烟3fdb40240abe4b47919966c732183866和14项真实输入拒绝negative-86a008fc95264b42b9ae682fe4d74c25通过。三组正式证据均为独立进程AB/BA/AB三对，全部逐行语义相等、原始样本/分位数/输入及日志哈希校验通过：

| 对照组 | 工作量/每进程 | 归档（out/verification/ecs-e5a） |
| --- | --- | --- |
| E2-B → 当前，共同基准 | 33项×100样本 | 413bac4208444fb8a0c4e346c2307f9c |
| E4-E → 当前，共同增量 | 33项×100样本 | 7a0fdbff6a794d72ac40c0a1967c06df |
| E4-E → 当前，独立读取 | 27项×100样本 | 45ae14f3a29c4b168b4e320cc6a2c91d |

合计18个正式进程、55,800个原始时间样本及55,800个线程分配样本；另有4个最终冒烟进程。五个档案共90份九文件闭包输入副本，后验重新读取全部实际档案，并确认跨组同一驱动/同一最终候选。机器为Ryzen5 5600X（6核12线程）、.NET8.0.28、Windows10.0.19045、workstation GC、平衡电源方案；WMI报告频率3701MHz只是快照，不是连续turbo记录。正式各进程启动前负载快照3%–47%，不能称无人干扰环境；没有结束用户进程或改电源/安全设置，不能把波动归因于已证明的根因。

| 验收项 | 三轮P95候选/对照 | 本轮判定 |
| --- | --- | --- |
| PERF01：4096直接World全写 | 1.213900 / 1.081095 / 1.262726 | 三轮观察均超过1.0；95%过程区间[0.968347,1.446126]跨门槛，达标未证明，债务不关闭 |
| PERF02：100对象Transform复制读 | 0.938199 / 1.012806 / 0.826902 | 区间[0.715498,1.190078]上界超过1.05，未证明 |
| PERF02：100对象混合复制读 | 1.197063 / 0.683491 / 1.374315 | 区间[0.414854,2.606525]及逐轮波动均不支持5%门槛，未证明 |
| 预登记20%热点 | 1000/4096的World/Play无写与1%写，共8组 | 本基准各轮和区间均达标；这是既有增量路径的相对收益，不是E5-A新增优化或整帧性能结论 |
| 独立暖态Query分配 | 单类型/混合类型×100/1000/4096，6组×3轮 | 所有计时区间样本0B；不包含准备、输出副本、整个帧或IPC |

全写4096的参考P95为65.1670/65.9882/66.0982ms，当前为79.1062/71.3395/83.4639ms；当前P99为85.6554/84.7712/105.5108ms。线程分配P95仍约20.16MB→14.09MB，不能替代尾延迟修复。同样工作量相对E4-E增量对照的P95比值为0.979777/0.970951/1.032548；当前没有生产运行时源码改动，不以这些波动宣称新增优化。包字节仍按实测哈希登记：例如Runtime信息版本从E4-E包的7636609基底变为当前e51ac6a基底，不能因源码未改就假定DLL相同。

完整93行对照、每轮P50/P95/P99/max、分配/GC及目标判定保存于 [E5-A证据摘要](ECS_E5_A_EVIDENCE.json)，原始数组和环境/输入/日志保存在对应本地档案。冷态定义为计划/数据校验失效后重建，代码已按合同预热；Restore/Rename及请求构造排除在读取计时外，不能称完整恢复或进程冷启动成本。进程峰值和GC次数的统计口径不变，未测GC暂停。

最终功能/包/安装/备份/历史失败复核：`out/ecs-e5a-delivery-verification-post-pairs-first.log` SHA **D631996C9D83604E5321A775E7A6A8C2D955680F23851CBE40A7C847241E5962**，与采样前功能证明完全一致。实际五档案后验：`out/ecs-e5a-final-evidence-first.log` SHA **AF3E506B63D6562F4AA639A9500D6F58FBC2A5FA73AAE92ABF55E604017D32C2**。再次实际核对E3-D历史18输入/6日志所得`out/ecs-e5a-deferred-performance-first.log` SHA **B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A**未变。E0原18文件、E3-A输入/日志、E4-C失败现场、E4-E首轮失败687文件/精确日志/隔离复测等均保留；历史File.Replace根因仍未证明。

本片满足E5-A“有效基线可带未达标结果进入B”的条件，不满足整个E5性能验收。正式E1-B安装7f3d3822保持144清单/148实际/148备份，EXE SHA **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**不变；无noSkip/旧三轮profile/部署/人工/目标机/自包含/整帧/1h验收声明。下一片E5-B先做全写分段时间、分配来源与真实GC暂停诊断，再实施可归因修复并重复同源门禁；不跳到E5-F或M8.3。

### 11.12 E5-B 密集写候选与分段诊断（实施中）

E5-A本地/远端提交`1974a214cde3d4c3ef470d99e03b7471bf943419`已核对。本片尚未提交/部署；以下诊断/功能进度为实施记录，最新正式性能结果及未解决项见11.13，不以旧进度替代最终状态。

新增测试专用`--ecs-e5-diagnose[-smoke]`和`scripts/Measure-EcsDenseDiagnostics.ps1`，按12次预热/100样本、100/1000/4096对象拆分Begin、写入（含值编码）、Prepare（含schema/预算）、发布（含原清理）和finally清理。反射只在计时前绑定委托；生产World没有诊断探针/开关，原33项与读取27项基准不变。每个诊断场景在计时外与公开WorldRunner逐字节比较真实codec结果。

记录运行时`GC.GetTotalPauseDuration()`差值而不是以GC次数推断暂停；阶段记账可能滞后，不冒充ETW分配栈或存活期证明。独立重复Transform微探针包括编码、schema/深度、组件预算序列化和raw UTF-8计数；嵌套探针不可相加或代替整步。

| 顺序诊断候选 | 档案（`out/verification/ecs-e5b/`） | 4096全写P95毫秒（三轮） | 每步中位分配字节 | 100样本累计GC暂停毫秒（三轮） |
| --- | --- | --- | --- | --- |
| 原运行时 | `diagnostic-8e4cf9f1d3024c6cb68f3abdd85e2940` | 72.48 / 78.38 / 75.93 | 14,080,656 | 844.20 / 852.74 / 820.50 |
| 延迟快照DTO | `diagnostic-8a3e2b76042846e98b3117315cf8fd7d` | 68.94 / 66.68 / 65.63 | 11,836,048 | 621.90 / 592.90 / 611.20 |
| 再改派生数组 | `diagnostic-783937accaab46178be1f5f4ce3a9234` | 66.09 / 66.09 / 60.19 | 11,066,024 | 494.17 / 447.38 / 407.20 |

三版诊断源码相同，但测试DLL哈希不同；`out/ecs-e5b-diagnostic-audit-second.json`逐档核对原始分布、九文件闭包/包manifest/冻结/执行/环境及最终语义，明确`sameDriverAcrossArchives=false`、`formalPerformanceAccepted=false`。首个审计因跨版驱动字节不同拒绝，失败输出和脚本保留；第二个只证明各档案自身完整性，不能授予同驱动配对因果结论。正式结果必须由最终同一驱动重测。

候选改动限于派生验证数据：普通Prepare不生成完整ObjectSnapshot和排序组件数组，显式快照观察者仍获取独立副本；每对象最多64个派生组件元数据改为尝试独占只读数组，相同顺序按索引读取、结构变化按类型身份回退；最后将内部`TypesAt`改为值类型遍历，去掉每对象闭包/LINQ/装箱迭代器。第三项晚于上表，不复用上表为其测量结果。唯一组件存储、schema/深度/精确预算、未知编码冷路径、权限、提交顺序和外部ABI不变。

新增快照排序/副本隔离/预算/abort、64组件移位与增删、稀疏遍历/分支隔离/4096次零迭代器分配测试。最终Debug/Release的Runtime/Scene/Gameplay定向均通过；增量校验28组、诊断默认冒烟2组，原断言全部保留。最终957源码冻结`out/verification/ecs-e5b/source-freeze-first.json` SHA **CD7821A6B24BE035159B77D5E3D159C8D53A9AC0CFD1E8F40ADBC32334D57810**；完整双配置89项及smoke/Python53/inspect正在串行执行，尚未宣布通过。日志`out/ecs-e5b-gates-first.log`及`ecs-e5b-{build,ctest,supplemental}-*-first.log`。之后核对十包/正式安装/备份/历史现场，再做原33项三对AB/BA/AB及增量对照；未证明的性能项继续阻止阶段放行。

准备失败均保留：直接构建与旧候选字节不同被诊断脚本拒绝；首次标准Release因受限网络NuGet NU1900失败，正常环境同命令第二次成功，未关闭审计；新测试初版误写ObjectSnapshot.Id导致编译失败，改为现役PersistentId后通过。未更新正式E1-B EXE，四项IDE修改、历史失败及人工/目标机/自包含/整帧/1h门禁不清除。

Debug完整结果：同一957冻结在前后复核均未变；候选`4510c0cc01084691a3b06872ead3a180`，全部89/89 CTest在1105.45秒通过，managed/native smoke、Python53/53及inspect通过。详细日志同时确认增量校验28/诊断2、原实际宿主1607、真实TLS4583/搬移客户端1174、私有管道871等原断言。当前继续同源Release标准候选构建和完整测试，未形成最终双配置门禁或性能结论；禁止将单独Debug通过当成E5-B交付。另已逐项核对上述三版诊断归档的14个Gameplay.Tests源码相同，仍保留其二进制差异限制。

2026-10-20完整双配置功能门禁通过：Release候选`bd7a0321c86a4e0185a33aa1015a8e70`，全部89/89在1009.32秒通过，smoke/Python53/inspect及957冻结前后复核通过；两配置均无编译警告，详细日志保留上述新增和原全部断言。交付复核`out/ecs-e5b-delivery-verification-first.log` SHA **F2842F016ED4B03B1BE45CD5BED84622FF211ADADF545752F37FC828FBE3589C**确认十包、正式144清单/148实际/148备份、E0原始输入、E3/E4历史失败687文件及原日志；EXE仍为E1-B，不部署。随后完成最终同驱动三组配对，日志`out/ecs-e5b-pairs-first.log`，沿用原E5合同和阈值、原样保留所有结果；共享工具把新GUID档案写入`out/verification/ecs-e5a/`，并非复用旧结果。结果如下。

### 11.13 E5-B 首轮正式结果与读取回退复核（未放行）

同一957源码、最终Release候选和同一三文件驱动完成18个正式进程：原33项主对照/E4-E增量对照和27项读取，每组独立三对AB/BA/AB、12预热/100样本。功能会话11527、正式配对6091及后验核对83461均TERMINAL exit0，不存在这些会话的待轮询工作。

| 对照 | 首轮正式档案（`out/verification/ecs-e5a/`） | 结论 |
| --- | --- | --- |
| E2-B，共同33项 | `6daa20a7a9664ec9bf00dd7b2f842cc4` | PERF01与8项HOTSPOT达标；PERF02未证明 |
| E4-E，共同33项 | `a25fd2733e9d4c8e950afb94b975e841` | 增量诊断，不能代替E2-B债务对照 |
| E4-E，读取27项 | `4f9d4dcd755e4705a9a96a9c83d49fba` | 6项暖Query分配均0B；1000对象冷Query尾部回退待复核 |

PERF01（4096直接World全写）的三轮P95比值为 **0.791742 / 0.808180 / 0.876811**，几何均值0.824769，按原合同的95%过程区间 **[0.721273, 0.943116]**，各轮及上界均≤1.0，判定本候选**met**。参考P95为70.1764/66.8862/65.4619ms，候选55.5616/54.0561/57.3977ms；候选P99为58.9060/57.8238/60.0207ms。候选每步中位线程分配9,951,976B，参考约20,152,480–20,152,600B；不是完整帧或GPU指标，正式样本中的GC次数不是暂停证明。此前E3-D/E5-A未达标结果完整保留；最终E5-F还必须绑定清理后真实产品字节重新验收。

PERF02的100对象Transform复制读取比值0.896812/1.025656/0.697917、区间[0.530906,1.401693]；混合复制读取比值0.808946/1.134670/1.024723、区间[0.636594,1.507999]。两者均未证明1.05门槛，仍属于E5-C必须解决的债务，不用暖Query的0B结果替代。

对全部93行进行诊断复核，`query_cold/1000`三轮P95比值 **1.089655 / 1.111435 / 1.100460**，参考0.2465/0.2405/0.2608ms，候选0.2686/0.2673/0.2870ms；中位线程分配均131,480B。事后按相同公式计算区间[1.073760,1.127867]，这是未作多重比较校正的调查信号，**不是追加或放宽原验收阈值**。本片未改EntityQuery源码，且该冷读工作量不执行固定步；但此事实不能证明无间接影响或认定只是噪声。原查询仍为默认4096容量分配结果数组，其成本问题保留给E5-C，尚未修改实现。

按原合同continuation条款，在额外采样前冻结独立复核计划`out/verification/ecs-e5b/reads-confirmation-preregistration-first.json`（SHA **9AC5899921C24C79698D21909F730E5EC602858B7A88A7E707F73966D3A36D42**）：仅追加一组完整27项/三对/原采样参数；同候选、同对照、同驱动，独立报告所有结果，不择优替换、拼接或抹去首轮异常。不运行并发构建/测试/追踪，不杀用户进程或调整系统设置。

追加复核现已结束，会话69159 TERMINAL exit0；档案`0e1e53322161421387052026154d9919`仍位于`out/verification/ecs-e5a/`。全部27行语义与原始证据校验通过，六项暖Query分配继续满足0B。`query_cold/1000`三轮比值为 **1.029571 / 0.716509 / 0.978034**，过程区间 **[0.550961,1.460049]**；候选P95为0.2855/0.2118/0.2627ms，参考0.2773/0.2956/0.2686ms，中位分配仍131,480B。原回退未稳定复现，但区间上界仍超过1.05，**既不证明无退化，也不证明原异常只是噪声**。所有27行独立报告，不合并六轮或挑有利三轮。实际重读两档案及跨档18输入/当前驱动与产品完全相同，`out/ecs-e5b-confirmation-verification-first.json` SHA **13DA9485D4B6E263CC5EADB95B818C7E47B07E7DBBA63898D799BA2BA1FC389E**。

全部首轮93行和追加27行的比值、P50/P95/P99/max及中位分配汇总见 [E5-B证据](ECS_E5_B_EVIDENCE.json)，完整分配分位数/GC/原始数组/环境仍在逐档绑定哈希的verification与日志中。合计24个正式进程、72,000时间样本和同量分配样本，另4个冒烟进程。启动前CPU负载快照2%–40%，不能称机器无噪声。下一步是定位冷查询的分配/代码生成及重复性风险；不继续无计划抽样直到偶然通过，不改当前门槛，不提前放行B。若诊断需要新的产品/测量驱动代码，另立冻结并重跑所需完整门禁。

最终共同/读取冒烟档案分别`def395e74b394790a90de7a1c3e3ccb0`、`271a407b9a7040a398572595c0c1d1e4`；输入拒绝14/14（`negative-46e2d2801f0c46a7a19c9f16dc84d319`）。首轮采样后实际重读全部五档案、原始数组/日志/输入及跨组同一驱动/产品，证明`out/ecs-e5b-final-evidence-first.log` SHA **2D8A934BF5E3300AAAE932A12A3B06E07E325F16808559DCDEF8609A530DE248**；包/正式安装/备份/历史现场的`out/ecs-e5b-delivery-verification-post-pairs-first.log` SHA仍为 **F2842F016ED4B03B1BE45CD5BED84622FF211ADADF545752F37FC828FBE3589C**。E3-D原18输入/6日志再次实际复核SHA **B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A**不变。新增事后诊断脚本首版误用报告name/count字段失败；原脚本/输出保留，第二版使用workload/objects，不修改正式样本或验证器。

追加采样后再次复核957源码、十包、正式安装/备份及历史失败，`out/ecs-e5b-delivery-verification-post-confirmation-first.log` SHA仍为 **F2842F016ED4B03B1BE45CD5BED84622FF211ADADF545752F37FC828FBE3589C**，与首轮前后审计一致。四项IDE文件的实际大小/SHA与E5-A留存相同；证据JSON已从全部实际档案重新生成并逐值比对，120行/3882标量及证明哈希一致，证明`out/ecs-e5b-summary-verification-first.json` SHA **B604E666B24CC1958296C2312FB5B0802305C57EF10B31F1898DC7A8D16ADEA3**，`git diff --check`通过。首次用序列化文本作比对因JSON数值1.0/1的表示不同拒绝，后改为精确键/数组顺序/字符串/布尔/数值对比，不使用浮点容差、不改正式样本或判定。

2026-10-20收到继续指令后，追加独立A/A诊断计划：`out/verification/ecs-e5b/aa-reads-preregistration-first.json` SHA **53EAA7312A1D457A726E8D9CE96228852BFE66A1C9A832DF4347AF4EF47BAD84**。两份九文件闭包均来自同一最终候选，实际variant仍为candidate，不冒充E4-E参考；完整27项、12预热/100样本、三对ab/ba/ab，独立检查所有原始值/顺序/语义/环境。仅判断相同产品跨进程波动，不能据此证明原回退根因、代替正式对照或授予阶段验收。诊断开始前记录合同，所有脚本/输出在out中，不更改957冻结源码；待全部结束再做后验核验。

A/A诊断已结束，会话28593 TERMINAL exit0；档案`out/verification/ecs-e5b/aa-reads-4a2c79adbe4e45dd8fb86181832cd0c4`保存六进程的27项×100样本（另16,200时间/分配样本，仅诊断，不混入正式对照）。两份九文件完全相同，六次语义完全相等，原严格采样/分位数/闭包校验全部通过。`query_cold/1000`的b/a比值为 **1.020604 / 1.033406 / 1.012245**，区间 **[0.995940,1.048841]**；A侧P95为0.2912/0.2754/0.2695ms，B侧0.2972/0.2846/0.2728ms，每次中位分配仍131,480B。该行满足诊断用1.05规则，但这不是E4-E→候选的性能证明。全部27行中4项符合诊断规则、23项未证明、无连续稳定超过5%的项；不能据此将原回退认定为噪声或放行B。

全部A/A结果见 [相同候选诊断](ECS_E5_B_AA_DIAGNOSTIC.json)。首个归档证明SHA **1F696458A305E84CD392142CD0F14BF5CBBE2F001C6B651F7CBA737668ECE543**；再次实际重读相同输入/日志的`out/ecs-e5b-aa-reverification-first.json` SHA **6F0C2C27DAD37D73E06374BA41C1BCC4ED14C3757C19F46081C0752BBD2AE75B**，27行数值与首个证明完全相同（记录的archive路径一份绝对、一份相对，故文件哈希不同）。后续`out/ecs-e5b-delivery-verification-post-aa-first.log`仍为 **F2842F016ED4B03B1BE45CD5BED84622FF211ADADF545752F37FC828FBE3589C**，确认957源码、十包、正式安装/备份及历史现场未变。当前无运行中的测试/采样，不再无计划重复采样。

只读代码核查另确认两个后续候选点：`EntityQuery`每次新计划按默认4096分配结果数组，100/1000对象也承担同样容量；`World.GetEntities`每次按CreationOrdinal排序并复制。它们在旧对照中已经存在，不是本片回退根因证明。若后续优化查询初始容量，必须保留4096硬上限、显式小容量超限拒绝、不截断、结构版本失效/Restore失效及caller-owned结果；若缓存创建顺序，必须仍给调用者独立副本，不暴露World内部数组。这里只登记候选与正确性约束，尚未修改这些路径或进入E5-C。

本片仍在调查读取回退，未提交/推送/部署，未开始E5-C实现或放行M8.3。正式E1-B EXE、历史失败、四项IDE修改、人工/目标机/自包含/真实领域/1h门禁均保持。

### 11.14 E5-B 冷查询有界缓冲修正（新候选）

2026-10-20继续推进：基于反复观测到的每次冷计划约131KB固定分配，以及源码中默认4096容量分配，实施最小范围的缓冲修正。该成本并未证明是先前跨版本回退的根因，不以“噪声”抹去原始结果。这项实现原属E5-C候选，现明确提前纳入E5-B的读取风险修复；仅限查询缓冲，不同时改GetEntities、场景分页、读权限或计时合同，C的其余范围与PERF02验收仍保留。

- `EntityQuery`将调用者指定的结果上限与实际缓冲大小分开。初次缓冲为min(上限,已提交World.Count)，空World不分配4096结果；结构版本变化后、重新求交前有界增长，最大仍为原指定容量/4096。不缩减已有缓冲、不对外暴露数组，不增加组件副本World。
- 查询可见性仍为已提交成员。未提交创建/删除和abort不改变缓存；成功结构提交使旧迭代器在访问捕获数组前拒绝。Restore仍先检查World身份再处理缓冲。显式小容量超限仍抛错，不截断、不向CopyTo目标发布部分结果；全类型/None过滤和创建顺序不变。
- 三组新增测试覆盖0/100/1000冷分配、从空计划经abort/commit增长、显式容量2超限及恢复、逐步增长到4096/顺序/输出副本隔离/Restore失效。原17组查询断言完整保留，现20/20。
- 双配置Runtime/Scene/Gameplay六个定向进程已通过，Runtime增量28及其余原断言保留。Debug定向冷查询分配为0对象328B、100对象3616B、1000对象32416B；这是完整冷API的局部分配检查，不是正式P95、整帧性能或跨版本因果证明。

新冻结仍为957文件：`out/verification/ecs-e5b/source-freeze-second.json` SHA **8892989D8EB982778C58D5CB5121E0001DAA37C6B15C2FFB09CFFB41AB483BB5**。定向日志`out/ecs-e5b-targeted-*-*-query-final.log`，完整门禁`out/ecs-e5b-gates-second.log`。仅会话79002串行执行Debug/Release标准SkipTests构建、完整CTest、smoke/Python53/inspect及前后冻结检查，当前尚未形成完整门禁通过结论。旧第一冻结/全部正式配对/追加读取/A/A证据均保留，但不能作为这次改变Runtime字节后的最终性能验收。完整功能通过后必须重新做同驱动原共同33项及读取27项配对，比较原E2-B/E4-E，不复用旧样本；正式E1-B EXE和备份不部署。

SECOND双配置功能门禁已完成，会话79002以exit0结束：标准Debug候选`eb452d1e5f43409a9d3423be2e09423e`、Release候选`06c0756a16bc4d649202136939751e62`，完整CTest各 **89/89**（1137.08/1035.97秒），两配置managed/native smoke、Python **53/53**、inspect及957冻结前后复核通过；无编译警告。详细日志再次核对Query20/增量28/诊断2、源目录及搬移宿主1607、真实TLS4583/模块搬移1174、私有管道871等标记。

实际交付核验`out/ecs-e5b-delivery-verification-second.log` SHA **056D03981E23D1FFF1960F0FCCD76BA6680DD0A58CD4C8D777B022A55BCF77ED**通过：十个候选包、正式E1-B的144清单/148实际/148备份、E0原18二进制及历史失败现场保持。此记录只证明功能与证据完整性，不证明性能或允许部署。随后会话80687执行`run-pairs-second.ps1`：新common/reads冒烟、14输入负例、原E2-B共同33项及E4-E共同33项/读取27项各三对AB/BA/AB，日志`out/ecs-e5b-pairs-second.log`。测量期间保持源码/驱动不变，不并发构建测试；尚无新候选性能结论，B未放行/提交/推送。

### 11.15 E5-B SECOND 最终限定结果：全写目标通过，复制读取债务进入C

会话80687和后续核验6506均已exit0结束，无运行中的构建/测试/测量。新候选同957冻结及双配置完整门禁见11.14；最终采样后交付核验仍为 **056D03981E23D1FFF1960F0FCCD76BA6680DD0A58CD4C8D777B022A55BCF77ED**，实际源码、十包、正式安装/备份及历史现场未变。

本轮三个正式归档分别为E2-B主对照`271b069c28014897ad98ab51392edfd4`、E4-E共同工作量`a3487446ce8445c392f0b4e37006c3e4`、E4-E读取`56d5765ff7374204ace18b5911e84cd7`（均在`out/verification/ecs-e5a`，共享测量工具的真实输出路径）。各三对AB/BA/AB，共18个进程、55,800时间样本及同量分配样本，另四个smoke进程、14输入负例；没有抽样择优或复用第一版样本。实际重读五个归档、全部原始样本和输入后，`out/ecs-e5b-final-evidence-second.log` SHA **9CF2DF9954E030491D7CF53B33EC0E5E585546050E58306A082CE73828C9836F**确认相同3个驱动文件/6个候选产品，语义相等。全部93行及诊断区间保存在 [SECOND证据](ECS_E5_B_SECOND_EVIDENCE.json)，已逐值回读核对，SHA **4F282E5D8532FB556CD790CE7D41BE4C841F59914B5B16791051C7830944BEE0**。

- **PERF-01限定目标通过**：4096全写P95参考67.3851/62.5718/62.3888ms，候选54.5368/54.1863/54.4110ms，比值0.809330/0.865986/0.872128；预登记95%过程区间[0.766002,0.940260]，各轮及上界都≤1.0。候选P99为57.5213/57.3973/58.0824ms，线程分配中位数9,951,976B；不把分配变化当作GC暂停或整帧性能证明。8个既定HOTSPOT-20目标也通过，包含前序增量机制的收益，不全部归因于本片。
- **冷查询修正证据通过**：1000对象冷查询P95比值0.922957/0.923755/0.933501，事后诊断区间[0.912281,0.941398]；中位分配131,480→32,416B。100对象为131,480→3,616B。4096仍需要全容量，中位分配131,488B，相比旧版增加8B，P95比值0.984114/0.750823/1.048160，不宣称所有规模都提速。六组既定暖查询分配检查均为0B通过。第一版回退/追加读取/A-A原证据不撤销，旧波动根因仍未证明。
- **PERF-02仍未证明**：100对象Transform复制读取比值1.137632/0.712558/1.441176，区间[0.432312,2.565834]；混合复制读取0.860603/1.000000/0.804036，区间[0.670333,1.167066]。原1.05门槛不放宽，进入既定E5-C修复。
- **保留4096复制读取调查信号**：相对E2-B三轮P95高17.71%/13.25%/7.92%，诊断区间[1.013288,1.257700]；相对E4-E三轮比值0.951000/0.926432/0.915693，区间[0.887181,0.976825]。后者不支持“本片相对当前产品牺牲此路径”的判断，但不能证明根因或抹去主对照偏慢，E5-C同时检查100/1000/4096复制路径。全部93行中没有诊断区间下界>1.05的项，但多项波动大且区间跨线；**不据此声称所有API满足5%上限**，也不将事后诊断变成新增验收标准。

据预先划分的阶段范围，本片达到E5-B功能、PERF-01和冷查询风险修正的限定自动候选门禁，可做本片代码交付；这不是E5整体性能通过或生产发布。C承担原复制读取债务、上述大场景信号和查询/场景/AI有界读取成本；D真实领域、E审计、F最终同源凭据/部署以及人工/目标机/自包含/整帧/1h门禁仍待完成。不得跳到M8.3。

保留证据复核：第一B的六个smoke/正式/追加读取归档与A/A逐输入/原始日志再验证通过，`out/ecs-e5b-original-archives-preserved-second.log` SHA **5CFDE53A7A4DEDC3BD7A21ABCE46CBF1620A368BCBCEB3F25E512ADA2455CA39**；第一B摘要仍为9715A701…不改。原E3-D18输入/6日志证明仍为B4BE5E4C…。全部93行诊断`out/ecs-e5b-regression-inspection-query-final.json` SHA **76C2C0A2BADDF8A647D3F78589872137C46B5D6EDD78354A3040B37B77AE625E**。正式E1-B EXE仍为C50742AD…，未部署；四项IDE修改与所有失败现场保留。下一步限定提交推送并核对远端SHA，之后实施E5-C。

### 11.16 E5-C 创建顺序副本候选（实施中）

E5-B已提交并推送`1acf15ba5d141bf493bf681429b59673b9d70e4a`，本地与GitHub main精确核对一致；19项阶段文件交付，四项IDE设置保留。以下为新的未提交C改动，不复用B的源码冻结或性能证明。

首先为`GetEntities`增加9组回归：独立数组/调用者篡改隔离、稀疏槽位复用的创建顺序、直接结构与值修改、冷暖读取在abort期间只见已提交成员、prepare与install边界、验证失败、成功/失败Restore、只读和异线程拒绝、纯值步以及100/1000/4096暖复制分配。旧实现先运行得到预期8/9（100对象每次5,680B，超过仅输出预算），失败日志`out/ecs-e5c-runtime-red-debug-first.log` SHA **B9C5AC01CBA4C5395EC6CE729E9B9E65AC2C1CCBED58CD51779710BA53AF5CB7**原样保留。

候选仅改World内现役读取路径：私有缓存最多4096个Entity身份的创建顺序，绑定World Identity和已提交MembershipVersion，首次/成员变化后完整构建，再发布缓存引用。缓存不含组件值、无新World/写权/历史。未提交候选与abort不改变可见缓存；值修改/空步不重复排序；组件增删也按原membership戳保守失效，不额外引入一套结构版本。Restore通过身份和版本拒绝沿用旧缓存。每次非空调用仍Clone返回独立数组，空数组没有可写元素，不向用户暴露内部数组；保留原公开返回类型、4096上限和owner-thread验证。

改动后Debug/Release Runtime全部通过，新9/9；暖复制100/1000/4096每次分配 **3,224 / 32,024 / 131,096B**。两份Runtime输出SHA同为 **9806AB88AD998B37E4637F761D366626FA8C60F079964B0AE2C04C4F737A2FAA**（相同断言输出，不表示不同配置二进制相同）。另六个Scene/Gameplay/Editor.Core双配置定向运行均通过，包含场景读取13/能力读取6、原Gameplay53及原基线夹具；会话89849已exit0结束。日志`out/ecs-e5c-targeted-*-*-first.log`。

**尚无C延迟结论、完整同源门禁、提交或部署。** 原33共同项和27读取项计时合同不改。继续核对场景/AI分页与未知编码/权限/预算，通过新冻结、标准双配置完整回归和原三组正式配对验证PERF-02及4096复制读取信号，同时重查PERF-01不回退。不得将上述分配断言当作P95或整帧验收；正式E1-B EXE、历史归档和人工/目标机/自包含/领域/1h门禁保持。

### 11.17 E5-C 生命周期诊断与完整门禁（进行中）

新增独立`read-diagnostics`合同与入口，不改原common/reads/dense计时工作量。10类案例覆盖首次/暖复制、值更新、组件成员失效、删除重建、Restore重建以及值更新后的查询、三类型交集、None排除、空池。冒烟0/8对象共20案例；测量0/100/1000/4096对象共40案例，每项12预热/100样本。失效操作及夹具准备在计时外；冷读每样本从新World开始。UUID顺序、值和缓存不别名校验独立于被测结果。记录原始时间/线程分配、实际GC暂停差值及分位数；缓存反射计数只在计时外记录身份数组payload，**不冒充堆驻留测量**，GC/内存总量也明确包括计时外夹具。

开发闭包20正例及17输入负例通过（原14项加禁止E2B、错误入口、其他诊断cohort），日志`out/ecs-e5c-read-entry-second.log` SHA **7824F4779D9CCD906D34A0EB68E41DFB4CE8BC1CFDD36214A29246E4CDD260E2**。该开发闭包不是标准构建候选，不用作性能或发布验收。首次辅助准备误将仅适用于out目录的路径验证器用于managed/bin而拒绝，日志和空准备目录保留；第二辅助器精确验证源文件及父路径后生成新闭包，未修改产品路径防护。标准Release候选之后仍须重做诊断冒烟/负例及实际测量。

完整门禁前两次定向序列失败原样保留：第一次Editor.Core原测试35/36，第二次Scene原测试35/36，均在未改动的`SceneDocumentFiles.Save`之`File.Replace`失败。第一次按测试时间窗口只读留存4目录/6文件，证明`out/ecs-e5c-editor-failure-inspection-first.json` SHA **CFB6DB27DFAF82D769E396B59593C0CF51858B9C394C14434AB8BBBB29F8423D**；第二次7目录，证明`out/ecs-e5c-scene-failure-inspection-second.json` SHA **296467FF42CC80294C217826BA9318E244D18E3494A1CC19B1DD509045310EFD**。未取得保存前哈希，精确失败文件名未由测试输出，不扩大证据结论。原测试新目录独立复测通过，但**根因仍未证明，也不能推定为沙箱问题**；没有删除失败文件、强制覆盖、弱化原子保存或断言。前两序列均在源码冻结和标准构建前终止。

第三序列以正常权限运行，Runtime/Scene/Gameplay/Editor.Core双配置8项定向均通过。新冻结960文件：`out/verification/ecs-e5c/source-freeze-first.json` SHA **25F9CBADE86E302D583034EC88ECD332A33F1B1EA75B8E8AD2F620AC05C776D9**。仅会话15871串行运行标准Debug/Release SkipTests构建、完整89CTest、smoke/Python53/inspect及冻结复核；当前Debug标准构建成功，完整测试进行中，尚无双配置完整通过结论。`out/ecs-e5c-gates-third.log`保留总进度。测量不得并发于构建/测试，不修改冻结源码，不将B的旧性能样本用于C验收；正式E1-B EXE不部署。

本轮后续进度：Debug标准SkipTests构建零编译警告，候选`d98a5a0f356f4292b1f4965546d55992`；完整CTest **89/89，1153.40秒**，managed/native smoke、Python **53/53**、inspect与前后960冻结复核通过。详细记录再次检查复制9/生命周期20/Query20/增量28、Scene36/Core36/读取13+6、TLS4583/搬移1174/私有管道871。仅同一会话15871继续：Release标准构建已成功，候选`4d75e95a95ad4215a99fc6d1cbd6df10`，完整CTest进行中；尚无Release完整门禁、最终包核验或性能结论。

后续证据脚本已准备并作语法检查，尚未执行：`out/verification/ecs-e5c/run-evidence-first.ps1`在双配置完成后依序核验十包/正式安装/历史证据，执行标准候选生命周期冒烟/17负例/三次40项诊断与独立原始样本审核，再做原33/33/27正式配对、14负例和采样后核验。独立诊断解析器18项自检通过（`out/ecs-e5c-read-verifier-selftests-first.log`），只证明解析器正负例处理，不能替代产品回归或测量。原始样本与分位数、九文件闭包、同源960清单、实际GC暂停及有界身份payload都需在真实归档生成后重读核对；所有性能阈值、失败保留和未部署状态不变。

完整双配置功能门禁现已通过，会话15871以exit0结束，不再轮询或重启。Release完整CTest **89/89，1030.19秒**，同样零编译警告、managed/native smoke、Python **53/53**、inspect及冻结复核通过。实际十包、正式144清单/148文件/148备份、原E0/E3A及E4失败现场、C两次失败11目录和总日志哈希均复核通过：`out/ecs-e5c-delivery-verification-first.log` SHA **0BFD3D712BC6A0E5644E8F31B88CAEC1E62DDB0E6838C0286F3C55BB894C55BB**。原消费者清单中的World.cs合法改动分别绑定B原冻结与C新冻结，不以“排除此文件”代替验证；其余原消费者保持原哈希。功能通过不关闭任何性能门槛。

仅会话4538继续`run-evidence-first.ps1`，日志`out/ecs-e5c-evidence-first.log`。标准候选生命周期冒烟20项、17输入负例及独立归档审核已通过，归档`out/verification/ecs-e5c/read-diagnostic-3b69976899d04d4b8c740747fb66845c`；随后三次40项生命周期诊断已启动，尚无完整性能结果。原33/33/27配对将在同一串行流程继续，不并发构建/测试或重采样。另19项**合成**测量模式解析器自检通过（`out/ecs-e5c-read-measured-parser-selftests-first.log`），与18项冒烟解析自检一起只验证审核器，不混入真实样本或性能结论。C仍未提交、推送、部署或放行D/M8.3。

### 11.18 E5-C 生命周期实测完成，正式对照进行中

三次独立Release生命周期诊断现已完成，归档`out/verification/ecs-e5c/read-diagnostic-45df9e97a297414caca4b3db74085d57`。每次40项、每项100样本，共12,000时间/线程分配/GC暂停样本；输入九文件、960源文件、原始值重新计算分位数、完整文档语义跨轮一致均通过独立审核。审核`out/ecs-e5c-read-diagnostic-measured-verification-first.json` SHA **F14A4850029E2F288609A73B649BBD4C082D5D99201BECF146184F2CBAC406F9**。全部120行已发布至 [生命周期证据](ECS_E5_C_LIFECYCLE_EVIDENCE.json)，逐值回读与原审核相等，SHA **76DEAFE9A12445D8DA7EB77B792A1472A9F4EF2FF3FC445C78E9E1DA4D464153**。17个输入负例的独立原日志/输入复核为`out/ecs-e5c-read-negative-verification-first.log`，SHA **67D69C6EB40F7625BEE3A5574F0D92909073AFF2EA84523D11332AF68CB5384F**。

此组是候选自身的生命周期诊断，不是旧版对照或正式5%门槛：

| 对象数 | 暖复制分配中位数/次 | 首次冷读分配中位数/次 | 缓存身份数组内容大小 | 暖复制P95，三轮ms | 首次冷读P95，三轮ms |
| --- | --- | --- | --- | --- | --- |
| 0 | 0B | 224B | 0B | 0.0001 / 0.0001 / 0.0001 | 0.0007 / 0.0006 / 0.0009 |
| 100 | 3,224B | 8,928B | 3,200B | 0.0003 / 0.0004 / 0.0004 | 0.0079 / 0.0094 / 0.0079 |
| 1000 | 32,024B | 84,528B | 32,000B | 0.0023 / 0.0023 / 0.0024 | 0.0853 / 0.0824 / 0.1417 |
| 4096 | 131,096B | 344,592B | 131,072B | 0.0388 / 0.0483 / 0.0409 | 0.3562 / 0.3836 / 0.3360 |

缓存内容大小只计Entity内容，不含数组对象头、旧/输出数组或进程堆；**不据此声称堆驻留已测量**。冷读取包括重建缓存再复制的额外成本，不能只报告暖态。4096组件成员变化后的复制P95为0.3273/1.0628/0.2471ms，Restore后为0.3775/0.8469/0.4602ms，第二轮较大尾延迟原样保留，不用暖复制结果掩盖。值变化后仍保持暖缓存路径。查询的值变化/三类型/None/空池诊断没有线程分配，但计时包含身份/顺序/值校验，不直接与只做数组复制的计时相除。GC统计包含计时外准备，暂停归属可能延后，不做因果归因或整帧保证。

同一会话4538随后完成原common/reads冒烟及原14输入拒绝检查，冒烟归档分别为`out/verification/ecs-e5a/fe5e4928a41b46f080afaf336681ccaa`与`out/verification/ecs-e5a/f355d2f2d6f94995a83f591d73b7b461`。当前正在原E2-B共同33项主对照采样，之后为E4-E共同33项和读取27项；日志`out/ecs-e5c-pairs-first.log`。尚不能判定PERF-02、4096复制读取风险或PERF-01新候选维持情况，C不提前提交/部署，D-F/M8.3继续保留门禁。

### 11.19 E5-C 主对照证据拒绝：GC内存计数异常

会话4538已以exit1结束，全部构建/测试/采样会话均终止，不能按旧“运行中”交接轮询或启动重复采样。原E2-B主对照归档`out/verification/ecs-e5a/f75c884ceb9c48c683c6e80781b1bac6`六个进程均exit0、各输出33项，但原严格审核器拒绝`Invalid memory metric`。`verification.json`为空，**这不是已验证的性能结果**；后续E4-E共同/读取对照并未启动。

三轮候选同一项`world_step_stride_0 / 100`的`managedBeforeBytes`分别为 **-956384 / -911432 / -936072**。现役EcsBaseline直接记录`GC.GetTotalMemory(false)`，不是前后相减，不对原始数据归零、截断或改为通过。所有18个输入/六份完整日志及execution记录原样保存并重新核对哈希，`out/ecs-e5c-memory-failure-inspection-first.json` SHA **3371E27EE074952933EEE6A35F933D6429FB458117B598670F338AB0E7A2E468**。该清单只确认现场和异常，未替代原始样本/语义/全部指标审核；不据此宣称PERF-01或PERF-02通过。

诊断均在独立out目录执行，未更改正式驱动、合同、审核器或960冻结源码：

- 无引擎引用的.NET8.0.28、TieredCompilation=0有界128阶段数组分配探针没有复现负值，最小计数540040B，日志`out/ecs-e5c-gc-probe-first.log` SHA **9F5FA778FD92589F858B91FE6759A300412CE9E941966B2314D5ACAC13AF42EC**。
- 隔离复制原前三项工作量的诊断驱动，加入连续计数与最近GC信息，也未复现；无写步之前两次计数均3021576B，最近GC堆973920B、碎片8360B。六个产品DLL逐个核对与原候选相同，但驱动字节及启动/诊断开销不同，不能当作原测量通过。日志`out/ecs-e5c-gc-engine-probe-first.log` SHA **B37A27CE71FC4BC5F838AB12B76949F2C6292BB284072329253EBF9CB0D009F0**。首次辅助编译因内部分析API不可见而失败的5个错误保留；第二次仅在隔离目录使用已有测试友元程序集标识编译，零警告通过，未改变产品权限或替换归档驱动。

两次未复现不足以定位根因或排除缺陷。[Microsoft的API定义](https://learn.microsoft.com/dotnet/api/system.gc.gettotalmemory)是扣除碎片后的堆字节数，并没有把负数定义为有效内存值。本地异常来源仍需进一步区分精确启动/分配条件和运行时计数行为，**不根据网页或探针猜测宣布运行时bug**。不通过强制GC、改变运行时/机器设置、跳过指标或反复完整采样寻求“变绿”。

失败后十包、正式安装/备份、历史现场和960冻结再次通过只读核验：`out/ecs-e5c-delivery-verification-post-memory-failure-first.log` SHA **70CE807945E4059894D41B94C960889FFFFEFEB3CF82B75C38D6D746D1468EA8**，解析后的证明与原0BFD3D71…完全相同；字节SHA不同来自此日志UTF-8与前日志UTF-16编码，已核对文件头。功能89/89双配置和生命周期诊断保留其原范围，不升级为完整性能验收。下一步针对原启动条件做有界诊断，证据不足前不更改v1合同；若修正驱动/实现/合同，须新冻结和相应完整回归，不复用旧证据冒充新候选。C未提交、推送或部署，E5/M8.3仍不放行。

### 11.20 E5-C 保留启动流程的有界探针复现异常

新隔离目录`out/verification/ecs-e5c/gc-startup-probe-first`恢复原入口的九文件清单/哈希校验、六产品程序集加载与启动JSON输出。六产品DLL与失败归档逐字节哈希相同；诊断驱动是新编译字节，不冒充正式驱动。仅运行100对象的两项读取和无写World步，第三项语义输出后终止，不运行整个基准。探针在原内存读取后增加立即重复读取与`GetGCMemoryInfo()`采集，未修改原计时循环；附加诊断仍可能影响GC/布局，因此任何时间值均不用于性能验收。启动时执行原加载路径校验，提前退出不冒充正式完整运行的末尾复核。

固定三进程诊断计划全部exit0，构建零警告/错误。结果如下，单位为字节；其他两项读取均未出现负计数：

| 轮次 | 无写步前计数 / 立即重复 | 步后计数 | 最近GC序号 | 最近GC堆大小 | 最近GC碎片 |
| --- | --- | --- | --- | --- | --- |
| 1 | -909960 / -909960 | 252248 | 1 | 5182928 | 4147304 |
| 2 | 3227776 / 3227776 | 4391344 | 1 | 1020320 | 9080 |
| 3 | 3254136 / 3254136 | 4417704 | 1 | 1044880 | 9104 |

独立审核九文件闭包、原六产品哈希、三次正常结束和全部九个语义记录与原失败归档前三项一致；证明`out/ecs-e5c-gc-startup-probe-verification-first.json` SHA **6CAFA7824000CD65507049A8D64E35574BBE89D4C311BD8990AFA0FF93620C11**。原始日志为`out/ecs-e5c-gc-startup-probe-{1,2,3}-first.log`，构建日志`out/ecs-e5c-gc-startup-probe-build-first.log`。证明明确标记`diagnosticOnly=true`、`performanceAccepted=false`、`rootCauseProven=false`。随后960冻结复核通过；没有产品代码、原驱动、合同或审核器改动。

这次复现排除了“只存在于原日志解析”的解释，也说明前两种不同启动探针未复现不能否定异常。负值与高碎片在本探针中同时出现，**相关性不是因果证明**：GC信息属于最近一次回收，并非同一时刻的当前堆快照，不将堆大小减碎片强行替代原指标。[.NET v8.0.28原生入口](https://raw.githubusercontent.com/dotnet/runtime/v8.0.28/src/coreclr/vm/comutilnative.cpp)把`GetTotalBytesInUse()`转为INT64；[该版本GC实现](https://raw.githubusercontent.com/dotnet/runtime/v8.0.28/src/coreclr/gc/gc.cpp)使用各代空间减碎片的近似计算。这支持继续检查近似计数/碎片条件，但尚未取得本机内部操作数，不能宣称已证明下溢或具体运行时缺陷。

下一步隔离回收/碎片形成条件，评估是否需要独立版本的测量指标修正及其验收定义；原v1失败永久保留，未获充分证据前不改定义。没有强制GC、替换运行时、裁剪负值或重新采样整组寻求通过。所有会话终止，C仍未提交/推送/部署，不进入D-F/M8.3；既有功能通过与未验收门槛不变。

### 11.21 E5-C 分代诊断与测量定义待确认

进一步的`gc-generation-probe-first`仍使用失败归档中相同六产品，保留原入口启动路径，三个独立进程仅运行原100对象前三案例。只在第三案例记录最近GC的分代信息，不再为前两案例增加诊断输出。所有进程正常结束、构建零警告错误；第一轮再次复现计数/立即重复 **-912528/-912528B**，第二、三轮为3218944/3218480B。异常轮为第一次Gen0回收，非并发且执行压缩，固定对象数2；Gen0回收后大小4074040B、碎片4046928B，全堆5045912B、碎片4055192B。正常两轮固定对象数1、Gen0碎片480B。分代总和与全堆信息一致，但依旧是最近GC信息，不冒充当前堆的同步快照。

随后用**不引用引擎**的.NET8.0.28程序做固定有界反例调查：先分别在1/4/8MiB分配位置固定一个32B数组；根据观察到的Gen0碎片范围，再预先固定3072/3584/3840KiB三个位置。每进程192阶段、每阶段256个1000B数组，期间记录原始计数与GC信息，结束释放GCHandle；没有强制回收、后台进程或性能验收用途。全部六进程1152阶段均未出现负数，其中Gen0碎片最大4130024B。因此**固定对象数或约4MB碎片本身不是已证明的充分条件**，不能据此修改引擎内存管理或宣称特定运行时bug。

独立审核器重验诊断九文件闭包/原六产品、全部九个语义结果与原归档一致、分代字节总和、六组192连续阶段及负值计数；证明`out/ecs-e5c-gc-generation-probe-verification-first.json` SHA **9B820777219A07E7A75BDB2F287702140368E6C0ACBE65F464ADD1228A426042**。原始日志为`out/ecs-e5c-gc-generation-probe-{1,2,3}-first.log`、`out/ecs-e5c-gc-pinning-probe-{1,4,8}-first.log`和`out/ecs-e5c-gc-pinning-offset-probe-{3072,3584,3840}-first.log`。960源码冻结再次通过；正式代码、v1驱动、合同和审核器均未变。所有会话终止，所有失败仍保留。

现行`ecs-e5-a-v1`明确将GC/内存列为**必填诊断**，CPU比率和线程分配另有阈值；现有审核器还要求所有内存数值非负，因此当前归档必须继续判失败。已向用户请求以下**v2定义变更**，尚未获确认，未实现：

1. 原始`GetTotalMemory(false)`值不裁剪、不替换；负数明确标记`invalid-runtime-reading`，不得作为有效内存字节、增益或内存验收证据。缺失、类型错误、状态与数值矛盾仍拒绝。
2. 另记录最近GC的序号、堆大小、碎片及其“最近回收而非当前堆”语义，不将其与原指标直接互换。内存驻留/长稳验收保持待定，不因CPU通过关闭。
3. CPU、线程分配、语义、样本矩阵、输入闭包和统计阈值不变；v1审核器及原失败归档不改判，不对旧日志套用v2获取通过。
4. 若获批准，独立版本合同、驱动、审核正负例与记录分离；新冻结后重新完成相应完整回归，再按原顺序生成新的完整配对数据。没有许可不修改验收定义。

这是首次提出该定义选择；本轮已产生新的排除证据，不按重复外部阻塞处理。C尚未提交/推送/部署，E5/M8.3和所有人工/目标机/完整性能门槛仍开放。

### 11.22 E5-C v2 批准实施与新冻结回归

用户明确选择“采用v2”，解除11.21的定义审批阻塞。已实现独立`ecs-e5-c-v2`合同与审核器、`--ecs-e5-v2-smoke/measure`入口、`Measure-EcsE5V2.ps1`和17项输入负例工具。原`EcsBaseline`、`EcsReadBaseline`、v1合同、审核器与归档工具保持原样；`f75c884ceb9c48c683c6e80781b1bac6`原失败不转换、不改判。产品World代码没有因v2而改变。

v2合同/归档schema2、输入清单/启动环境schema3、样本schema2，与v1显式分离。计数原值保留有符号64位范围；非负标记`valid-estimate`，负值标记`invalid-runtime-reading`。字段缺失、布尔/浮点冒充整数、溢出、状态与原值矛盾或最近GC字段越界均拒绝。每个前后采样另记录最近完成GC的序号、代数、堆大小、碎片、提交字节和压缩/并发标记，明确不是当前堆或驻留。报告逐项列出全部无效计数，**无论是否出现负值，memoryAcceptance恒为pending**；完整性能/部署仍为false。不用最近GC数值覆盖原始估值，也不强制GC或改变运行时设置。

v2工作量复制只在测试项目内存在。源文本等价测试在规范化类名/schema及新增计时外遥测行后，要求两个文件与原v1完整相同；同时要求合同移除版本/内存定义差异后与v1完全相同。因此原场景准备、预热、计时循环、语义断言、33/27矩阵、AB/BA/AB顺序和CPU/线程分配阈值均保留。新增GC信息采集会改变计时外分配条件，不能跨版本合并旧样本，所以仍须新冻结、新完整回归及新配对证据。

定向证据：Release测试项目构建零警告/错误，日志`out/ecs-e5c-v2-targeted-build-release-first.log`；Gameplay原测试、v2计数边界6项、共同11项与读取9项夹具均通过，日志`out/ecs-e5c-v2-gameplay-release-first.log`。Python新增13测试方法通过，覆盖两组完整矩阵、原21归档负例、18内存状态/类型/范围负例、signed64边界、v1继续拒绝负数、跨版本拒绝，以及六日志三对**合成**归档中异常值完整保留/内存待定；日志`out/ecs-e5c-v2-python-targeted-first.log`。合成测试不是实际性能结果。初次新审核器语法检查发现重复逗号，修正后再通过编译与测试，未放宽断言；发生在冻结之前。

真实产品开发冒烟归档为`out/verification/ecs-e5c-v2/486ed2ada2af458387b7a95069365867`（E2-B共同）和`9c3c43d3a3e6482fa3f2f62e82f9c763`（E4-E读取）；17/17输入负例为`negative-c8550ad8117645ebb73a187132b8274d`，含缺损/额外输入、错版本、错入口、错误合同ID及禁止E2-B读取cohort。均使用同驱动的原/候选实际产品并通过闭包/语义审核，但驱动来自定向构建，**不是最终标准候选或正式性能证据**。最终完整门禁后仍重做标准驱动冒烟和负例。

新冻结970文件：`out/verification/ecs-e5c/source-freeze-v2-first.json` SHA **0F5374D310D906EE065F0A8D454E5C1B99153F0E288AD5B0A52A114402D400FF**。会话82478串行执行`run-gates-v2-first.ps1`，总日志`out/ecs-e5c-gates-v2-first.log`：标准Debug/Release SkipTests构建、各完整89CTest、smoke/Python66/inspect及前后冻结复核；当前Debug构建进行中，尚不能声称双配置完整通过。期间不改冻结源、不并发采样、不部署。结束后须检查最终十包/正式安装备份/历史失败，使用新标准驱动完成三组正式配对及独立证据复核，才能判断C的CPU目标；不自动关闭内存/整帧/人工/长稳门槛。C仍未提交/推送/部署，D-F/M8.3未放行。

后续状态覆盖上段“82478运行中”：82478以exit1终止，Debug标准构建在未修改的可选Grpc项目NuGet漏洞数据获取处报NU1900，尚未执行完整CTest。`out/ecs-e5c-build-debug-v2-first.log`和总日志保留；没有关闭漏洞审计、降级警告、删日志或更改源码。通过权限机制获准后，原流程在会话80839以新`run-gates-v2-second.ps1`重试，写独立`*-v2-second.log`，继续使用同970冻结。Debug标准构建现已通过，候选`f302fefd77054fb3a6a05341e553fcc4`；完整CTest前19/89通过，主渲染含32生命周期循环用时181.01秒通过，第20项PostHosts运行中，随后继续Debug补充和Release全套门禁。完整Debug/Release尚未通过，不并发采样。门禁后交付核验及新配对辅助器已准备并语法检查，但尚未执行；仍须逐项审核真实测试和归档后才可判断通过。

运行期间完成原v1失败的独立只读复核：重新检查18输入/6完整日志与空verification，结构化结果与原3371E27…证明完全一致；原v1审核器仍以`Invalid memory metric`拒绝该真实归档。新证明`out/ecs-e5c-v1-preservation-v2-first.json` SHA **2FA9AF6C8A796C6130FAFF168A4A9ACA8B6087C9470EC55550C69181C1297CFA**，新拒绝日志SHA **1189ADAC7AA008ABCD1607EB5FCD41256A4FE23D29E958C30B22C4AEC1DD7004**。不是以v2重解释旧数据，也没有覆盖原日志。最终五个新归档的独立复核脚本`verify-evidence-v2-first.ps1`已准备并通过语法检查，尚无归档可执行；将严格核对全部原始数据、跨组同驱动/候选、17负例及内存待定标记，不能仅凭输出“通过”字样放行。

最新完整门禁进度：Debug现已全部通过，同970冻结前后校验一致，标准候选`f302fefd77054fb3a6a05341e553fcc4`、零编译警告；完整CTest **89/89，1150.05秒**，managed/native smoke、Python **66/66（6.070秒）**、inspect全部通过。原始详细日志再次确认v2计数6项、复制读取9项、Query20、增量校验28、Gameplay53、真实TLS4583、搬移客户端1174、私有管道871。完整结果见`out/ecs-e5c-ctest-debug-v2-second.log`、`out/ecs-e5c-ctest-debug-details-v2-second.log`和`out/ecs-e5c-supplemental-debug-v2-second.log`。同一会话80839仍运行，已开始Release标准构建，之后执行完整89项和补充回归；尚无Release完整通过或最终交付核验结果，不提前测量、提交或部署。新93行汇总辅助器也只完成语法检查，明确将早期960源码生命周期证据列为独立历史驱动结果，不冒充v2测量。

后续完整门禁已结束：80839 **exit0**，Release候选`0643f9f7f2d64ddeb9a5c78289379562`零编译警告，完整CTest **89/89，1040.03秒**；Python66/66、managed/native smoke、inspect及970冻结前后核验均通过。新实际十包/正式E1-B 144清单、148实际、148备份/历史失败现场核验通过，`out/ecs-e5c-delivery-verification-v2-first.log` SHA **8D7EB20484DCCA442C4687E1BE68A8BBA4FC944A58EAC255EDC15C48D3393D6D**。该凭据明确`measurementContract=ecs-e5-c-v2`、`memoryAcceptance=pending`、`deployed=false`，不能代替性能结论。

在上述门禁之后，新会话50940开始`run-pairs-v2-first.ps1`，外层记录`out/ecs-e5c-pairs-v2-first.log`。流程使用最终Release候选，依次新common/reads冒烟与17输入拒绝检查，然后E2-B主共同33项、E4-E增量共同33项及读取27项，每组原3轮AB/BA/AB，归档独立写入`out/verification/ecs-e5c-v2`。尚无完整测量结果，不复用v1、旧960生命周期或B阶段结果验收本候选；等待终态后再次核对实际归档与全部93行，不并行构建/测试/重采样。E5-C不提前提交或部署，D-F/M8.3及内存/人工/目标机/自包含/整帧/1h门禁仍保留。

### 11.23 E5-C v2 最终限定结果：复制读取目标通过，内存验收保留

80839（完整门禁）、50940（正式配对）、31887（事后核验）均已 **exit0**，没有运行中的构建、测试或测量，不重启旧会话。结果均绑定同一970文件冻结 **0F5374D310D906EE065F0A8D454E5C1B99153F0E288AD5B0A52A114402D400FF**，Debug/Release完整CTest各89/89（1150.05/1040.03秒）、Python各66/66、smoke/inspect通过，两个标准SkipTests构建零编译警告。没有执行部署。

新的三组正式归档位于`out/verification/ecs-e5c-v2`：主共同`1a3599f190a54687be46fd12bce2d37d`、增量共同`8fd6a581022d463d971984597b3b69f0`、增量读取`26303f475bcd41ed98ebe862939e2e8d`。各3对独立Release进程，原AB/BA/AB、12预热、100样本不变；共18进程、93比较行、55,800时间及同量线程分配原始样本。两个新冒烟归档`a9869c33fe27437aaadc4cbdb41c06b3`、`96097af2860c47598596106fc558ac37`，17输入拒绝归档`negative-6bc6c575fa4c4b58b424d731080c6db2`。实际五归档逐输入、日志、语义、同三驱动/六候选产品再验证通过，证明`out/ecs-e5c-final-evidence-v2-first.log` SHA **8CDF778BE28EB5DD0AB20DE8F5E0E667CB40EC9399D1CE5757B8B9C14F645179**。

| 预登记目标 | 三轮候选/参考P95比值 | 过程级95%区间 | 本候选结果 |
| --- | --- | --- | --- |
| PERF-01，4096对象World全写，≤1.0 | 0.928135 / 0.906601 / 0.877023 | [0.841994, 0.969877] | 达标 |
| PERF-02，100对象Transform复制，≤1.05 | 0.336079 / 0.452044 / 0.504396 | [0.252068, 0.715721] | 达标 |
| PERF-02，100对象混合复制，≤1.05 | 0.685585 / 0.707944 / 0.679487 | [0.654843, 0.728937] | 达标 |
| HOTSPOT-20，原无写/稀疏写8组 | 全部逐轮与区间上界≤0.8 | 全部保留于证据JSON | 达标 |
| typed Query暖态/混合暖态，3种规模6组 | 三轮计时API线程分配均0B | 不代表整帧或所有读取0B | 达标 |

4096复制路径的原风险已有本候选新证据：Transform相对E2-B为0.529216/0.478516/0.416599，区间[0.350598,0.636829]；相对E4-E为0.459552/0.433791/0.426018，区间[0.398401,0.484963]。混合4096对应区间[0.344313,0.561091]和[0.397226,0.639341]。不把旧B或失败v1的时间并入这些统计。原复制基准每操作包含枚举/断言开销，100/1000/4096线程分配中位数从5,712/52,512/213,504B降为3,256/32,056/131,128B；这与单独GetEntities输出数组的3,224/32,024/131,096B不是同一计时边界。

全部93行（含P99/max、GC和内存诊断）保存于[完整v2证据](ECS_E5_C_V2_EVIDENCE.json)，SHA **D17923F00F21E4ADCEEF3D0B28F2243D7BDAD6D2C6D97D43C16014ABC1E1E158**。事后5%诊断没有任何行三轮全部>1.05，也没有区间下界>1.05；但例如4096冷查询区间[0.733264,1.662566]、4096混合暖查询[0.714349,2.640990]较宽，**不能声称所有API不退化或满足5%上限**。World全写第二轮P99在1000和4096分别17.82ms对15.48ms、67.74ms对64.14ms，保留尾部调查，不用P95目标通过掩盖。4096全写线程分配仍9,869,576B/步；真实帧、领域和驻留成本须在D处理。新归档进程前CPU读数5%–35%、平衡电源、WMI报告3701MHz，不是持续空闲/turbo或根因证明。

本次三组v2实测未出现负估值，但这不证明原负计数根因消失或已修复。原始有符号计数/有效性/最近GC信息完整保存，`memoryAcceptance=pending`、`fullPerformanceAccepted=false`、`deployed=false`保持；最近GC不是当前堆。v1原失败与零字节verification继续原样拒绝，保留证明2FA9AF6C…；早期960源码生命周期120行保持独立历史诊断，不混作v2性能证据。发布JSON保留测量审核器的`stageAccepted=false`（它不做阶段放行），本节另作**限定CPU读取候选门禁通过**的评审结论，不表示整体E5通过。

摘要首次复制因PowerShell将UTC字符串自动转换为本地时区而被额外精确比较拒绝；失败副本`out/ecs-e5c-published-summary-timezone-failure-v2-first.json` SHA **F3E89DF061D161DC8A23CCE25DB1C677718B2E08D99D9D2089E63BDC29C20EA7**保留。改用保留JSON原字符串与数字的词法序列化，没有修改原始报告。第二次比较全部93行、41,126个标量与实际归档报告完全相同，无数值容差；证明`out/ecs-e5c-published-summary-verification-v2-second.json` SHA **296093F11A8D4E38A39658ECF16424E2F7ABBE74324E0132F7966C0002CA4291**。此为交付格式修正，不重测、不改变冻结源或合同。

测量后实际十包/正式144清单148实际148备份/所有历史失败核验仍为 **8D7EB20484DCCA442C4687E1BE68A8BBA4FC944A58EAC255EDC15C48D3393D6D**；原E3-D实际归档核验仍为B4BE5E4C…，正式EXE仍为C50742AD…。按本片范围可进行限定源码/测试/文档提交推送，排除四项IDE修改和out；下一片为E5-D真实产品、CPU/GPU/上传、内存及AI工具成本。E5-E/F、人工/目标机/自包含/整帧/1h与历史File.Replace根因保持未完成，不能直接进入M8.3。

### 11.24 E5-D 领域测量启动：真实管道读取开发探针

E5-C代码/证据已交付`5731ad2c0e4f11f744c3268d526941c59df1fb00`，交接记录`163745ce733fd3b06842d181484de543a164d378`已推送并核对。上一轮属于实际交付进展；本轮从这个工作树开始D，没有重跑C或复用C门禁验收D。

先检查现役边界：`PresentationCostReport`只计展示调用，不包含模拟；`RelocatedPresentationCosts`的恢复后窗口刻意保持暂停；旧M4联合样本中的GPU last-valid观测也不等于独立的当前帧样本。因此不能把它们重命名为完整E5-D证据。D保持以下完整范围：

1. 真实Editor的Edit→Play→暂停/单步→Reload→Stop，以及独立DX11/Null/严格搬移产品；复用真实FBX和原Jolt/Box2D、动画/根运动/UI夹具，0/小组/既有预算分别记录，32角色安全上限不是帧率承诺。
2. 分开记录查询、动画/物理准备、提交步、渲染提取、GUI、CPU提交、GPU来源代次/帧、上传与原生资源生灭；不把嵌套计时相加，不用重复/旧GPU样本补齐独立样本数。纯2D/Null保持零无用3D资源。
3. 真实私有管道与MCP stdio的冷暖分页、revision变更、批准事务、Undo/Redo、撤权和有界队列；用户批准不计入工具计算延迟，不新增AI授权能力或第二历史。
4. 托管/原生内存驻留、最终同源产品闭包、三进程完整队列、图像/API0/0与生命周期门禁；本机数据、用户素材/目标机、自包含、人工UI/MCP和1h分别验收。

新增预登记`scripts/ecs-e5d-domain-contract.json`，SHA **648DFA1FCA1EEE20477664B1F3327C8724370864949B9CDE7A2C440DFC9AD1C0**。列出上述十个必需领域、原容量/权限约束，以及首个管道读取矩阵：100/1000/4096对象×冷revision页/暖首页/暖尾页/暖对象详情，12预热/100样本，最终3进程。128对象页、64详情、256KiB输出、4连接/64总队列/16每连接/4每Pump保持；10秒是超时拒绝边界，不是性能通过预算。已异步询问目标硬件/分辨率/帧率，尚无回答；合同明确等待确认，**不自行创造目标机或FPS验收结论**，这不阻止仪器与本机诊断继续开发。

首个实现仅在`Ncma.Editor.Transport.Tests`：独立`--ecs-e5-pipe-cost-smoke/measure`入口使用新建隔离的真实当前用户私有管道及唯一EditSession。测试侧明确配对，不接管现有编辑器或批准真实用户请求；所有远端调用只读，冷失效由测试拥有者在计时外通过现役本地事务改名。记录每次encode/write/Pump/wait/read/decode端到端墙钟与owner线程分配，以及嵌套Pump时间/owner分配、调用次数、响应字节和完整codec捕获次数。`Thread.Sleep(1)`是明确的轮询贡献，异步工作线程分配不包括在owner计数中，不能标作总进程分配或模拟CPU。

场景页对照独立启动codec的UUID创建顺序、当前名称、组件类型/版本；对象详情对照完整规范化组件JSON。冷页每次1次完整证明，暖页/详情0次；每个夹具在末尾拒绝撤权后的读取、无远端写grant、历史仍有界、结束后释放自身发现文件。原传输默认测试也执行4项短冒烟，保留原真实stdio/队列/生命周期与E4-E 871项。生产World、权限、原生ABI和安装目录未改。

Debug/Release定向构建零警告，原传输回归均通过：`out/ecs-e5d-transport-{debug,release}-targeted-first.log`，四项新短夹具分别位于`pipe-smoke-38e11cee23f74003bceb48ebdf18f684`与`pipe-smoke-35e1cd2a850847f39d30d8d51fc09531`。新增Python开发探针审核器不授予正式闭包/阶段验收，5组测试覆盖完整矩阵、17种矩阵/权限/类型反例、11种原始成本反例、合同边界、6种实际文件链反例。全Python **71/71（5.974秒）**，最终日志SHA **8E61F1D9602F6C40AA3C2937DA0D07853B7269E7EBC2CB7ABBE51341EA1B0C56**。3个真实CLI负例（未知模式、缺参数、Debug测量）在执行工作量前拒绝，联合开发核验`out/ecs-e5d-pipe-development-verification-first.log` SHA **0240400CACF72C8C303D6FA46EDC38AB1CD7732D35E78B8E49CD4C01BA70918D**。

在完整矩阵运行前保存`out/verification/ecs-e5d/development-pipe-first-plan.json`：仅一次独立Release开发进程，**不是最终三进程队列**，失败不自动重复。会话2199现已exit0，12项/1,200请求样本全部完成，实际归档`out/verification/ecs-e5d/pipe-development-2360cb5bdff04357ab1be3fc591237a7/pipe-costs.json` SHA **CE65CAA4B95B50DCCBD73EAFDB4AF3D3B044C6309C5B29C3706EDD4CE5301B3B**。六个实际已加载程序集与合同字节核对通过，最终独立审核`out/ecs-e5d-pipe-matrix-verified-second.json` SHA **2B761CA45414C953F069FBDAC59501F6D4894AEA5DE64555A4EE543A695F02D2**。没有并发构建/测试或重采样。开发观察：4096冷页owner分配中位数33,323,456B、Pump P95约55.06ms，暖128项页约1.405MB/次；多项端到端P95约32ms但包含轮询/排程，**不能全部归因于ECS、管道或证明定时器根因**。这是后续分层剖析信号，不是性能预算达标。

两个开发期校验错误保留：首次对象详情oracle误用Wire序列化选项，日志SHA **AF95199D9459552868E9C6C760A3DB1721944FDE41EDAB2BA832B86817E549DC**；补强页检查时误认为摘要带组件payload，第三次日志SHA **D368D2DD669584DC02764155D9903A91892892C3C079E893B9535E5DFA0F1F9A**。修正为现役完整codec oracle及明确的“页描述符/详情payload”边界，未修改产品接口或削弱原测试。第一次4项成功探针使用较早测试驱动，只保留为历史开发证据；最新双配置和12矩阵结果才绑定当前驱动。

当前全部会话78012/2199已结束；D尚未形成新全源冻结/标准双配置完整89门禁/正式产品闭包/三进程领域队列，**未提交、未部署、未验收E5-D**。下一步补齐批准事务/Undo/Redo与实际MCP stdio测量、图形产品和驻留采集，再按完整合同冻结和验证；不能用本片四种读取工作量替代其余必需领域，E/F/M8.3仍未开始。

### 11.25 E5-D 审计IO范围纠正与真实批准事务历史探针

代码核对发现`EditorEndpoint.Authorization.Record`在每个实际Invoke后尝试追加审计文件，包括只读请求。因此11.24合同中的“计时窗口无文件IO”不成立，不能据此把旧探针解释为无IO的ECS/IPC成本。原合同 **648DFA1F…**、报告 **CE65CAA…**、原审核结果及全部失败不覆盖；在重构测试驱动前，将原六个实际程序集与五个驱动/审核器源文件保存到`out/verification/ecs-e5d/pre-audit-boundary-first`，六DLL与原报告哈希再核对相同。旧数据只保留为有范围缺陷的开发观察，不升级为正式验收。

新的D开发合同为schema2、`ecs-e5-d-domain-v2-audit-inclusive`，SHA **81348C1688885F2F87585251A0B37CA4055D4716C18FC264A2FE1F87F25B8F18**。明确包括产品的审计IO、事务/历史序列化，只排除探针自身文件IO、独立语义检查以及配对/批准动作；保持原读矩阵、12预热/100样本、正式3进程要求、容量与权限，新增100/1000/4096×批准事务/Undo/Redo矩阵。此为D开发测量定义，不是修改已交付的C/v2合同、阈值或证据，更没有关闭产品审计来降低耗时。

新增测试侧`EcsPipeAudit`在计时结束后逐行检查实际审计的实例/会话/连接及请求顺序，并保存实际文件哈希。读取每组核验prime+warmup+samples全部审计，不能以成功返回替代写盘检查。新增`EcsPipeHistoryCosts`使用自己创建的隔离端点与文档：每轮先证明默认/旧grant不能批准新请求，再由测试拥有者显式批准**精确proposal**，测量真正管道事务、Undo、Redo。重复原请求必须复用执行结果且不再次提交；完整独立codec oracle逐次验证所有对象/组件/行为数据，检查唯一历史、Undo/Redo后的旧句柄失效与有界历史。末尾撤销grant必须拒绝缓存写与历史，撤销连接后不能继续读取。没有修改生产权限、历史服务、World或原生ABI，不接管真实用户编辑器。

Debug/Release定向构建均零警告/错误，默认Transport原回归（含真实MCP stdio、队列/生命周期/E4-E 871项）及新4读+3历史冒烟通过，会话21732已exit0。日志`out/ecs-e5d-history-{build,transport}-{debug,release}-second.log`。四份实际读/历史冒烟的程序集/审计复核为`out/ecs-e5d-audit-{debug,release}-{cost,history}-verified-first.json`。Python全套 **74/74，5.909秒**，日志`out/ecs-e5d-audit-python-full-first.log` SHA **702782E6CEBD000E81A2A78E0383927E027C52BF57B8E2ABA50D091CEA6C0A5D**；新增历史审核测试包括完整合成矩阵、18个范围/权限/原始成本反例及10个审计事件反例。合成数据不是性能证据。

四个实际CLI负例（未知模式、缺参数、Debug测量、旧D合同）均在工作量/输出目录创建前拒绝，证据位于`history-negative-4c0d4861cc6742acb605fc33e548bb07`与`out/ecs-e5d-history-negatives-second.log`。首次负例辅助脚本的PowerShell字符串连接优先级错误，将多个参数合并，dotnet未能启动驱动；严格错误原因检查将其拒绝。原脚本/日志保留，新second辅助器显式分离路径与参数并重新执行负例，没有修改产品或重新采样性能矩阵。

在采样前保存`out/verification/ecs-e5d/audit-inclusive-development-first-plan.json`，预先限定一次Release读取进程、一次Release历史进程，全部矩阵，不自动重采样。会话96130现已exit0：

- 读取12项、1,200样本：`audit-cost-development-5c351973e333424d999481227eb4dcca/pipe-costs.json` SHA **3C9581FA658CD032524135156A22896DEF97D29E2D180DA4A3F48C22FD177CEB**；实际审核`out/ecs-e5d-audit-cost-matrix-verified-first.json` SHA **D5DCAC47CADF5642E8F66053DB7CB656CBDF71E7215A921AD395C4FE908FDCC2**。
- 历史9项、900样本：`audit-history-development-d05e5fff7e51469a94f9449dc451070b/pipe-history-costs.json` SHA **E267D094A8BAE1306B95C95A34A1F0CE0F23DB46B3D6083700A09863C19DE5C7**；实际审核`out/ecs-e5d-audit-history-matrix-verified-first.json` SHA **B53AADDE6AF21D2144EEBF36C351A06109BE86145CC9C5B1D118A1DED1150817**。

两组加载的六程序集完整声明相同，实际哈希逐项核对，所有审计文件及读/写/拒绝/重试/历史事件再复核。当前六DLL和八个驱动/审核源文件另保存到`audit-inclusive-products-first`，供后续源变更时保留原证据。这不是标准发布包闭包或完整源冻结，不能冒充正式三进程领域队列。

本次4096对象开发观察（nearest-rank P95；线程分配为第50个有序样本）如下，所有其他规模与原始样本仍在上述报告：

| 操作 | 端到端P95 ms | 嵌套Pump P95 ms | owner线程分配中位数 B |
| --- | ---: | ---: | ---: |
| 批准事务 | 265.2860 | 244.7568 | 157,339,656 |
| Undo | 157.0711 | 127.2224 | 80,103,552 |
| Redo | 142.0263 | 122.6299 | 80,103,488 |

这些数值含现役文档处理/审计成本，端到端还含管道轮询与调度；不是完整进程分配、驻留、渲染帧或已证明的单一根因。它们说明D必须继续分层调查真实工具操作成本，不能仅凭ECS查询/步进微基准推断整个编辑器高频操作已经高效。目标机/分辨率/FPS尚未确认，不创造通过预算，也不移除完整验证/审计或新增直写World通道。

当前没有运行中的构建/采样。D仍未提交/部署/验收，正式EXE重新核对仍为 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**。下一步真实MCP stdio读/写成本、图形产品/驻留与完整领域容量/生命周期证据，然后新冻结、标准双配置完整门禁和正式队列；保留11.24所有十领域及人工/目标机/自包含/整帧/1h。E/F/M8.3仍未放行，不把目标预算问题当作阻止所有仪器开发的理由。

### 11.26 E5-D 真实MCP stdio读取与批准事务开发矩阵

本轮从11.25实际代码与证据继续，新增测试侧`EcsMcpCosts`及`--ecs-e5-mcp-cost-smoke/measure`入口。每个对象规模创建独立测试端点、唯一EditSession与真正独立的`Ncma.Editor.Mcp`进程；通过产品JSON-RPC初始化、工具发现、会话刷新、私有管道和同一个编辑器Pump执行。不是测试内直接调用业务接口冒充MCP，不涉及模型推理。测试明确证明tools/list在配对前保持等待，再由测试拥有者显式配对自己的端点；批准事务同样先默认拒绝，再显式批准精确proposal，不接管已有编辑器或放宽生产权限。

独立开发合同`scripts/ecs-e5d-mcp-contract.json` SHA **7CB39DA3BD64C85A8B315A012BCBED598E4E05DC6A49ABD3AB12192CD366111B**引用原样保留的D审计范围合同 **81348C16…**。100/1000/4096对象各覆盖冷revision页、暖首页、暖尾页、暖详情、批准事务、Undo、Redo；12预热/100样本，最终仍要求3个父进程队列。计时含MCP编码/stdio、真实会话刷新、管道、审计IO和产品文档处理，不含启动/配对/批准/独立oracle与探针文件IO。父owner线程分配**不包含子进程和异步工作线程**，不能说成完整工具分配或驻留。256KiB结构化结果和1MiB协议帧分别检查，不用扩展协议预算容纳测试结果。

每次输出检查关联ID、会话、现役工具输出schema以及text/structuredContent一致；读取核对独立启动codec的创建顺序、当前名称、组件描述符/完整详情，冷证明1次、暖0次。写入、Undo、Redo检查完整文档等价、一次提交、同一历史、旧运行句柄失效、重复请求去重。撤销grant后原缓存请求和历史都拒绝；撤销连接后发送**完整有效的读取请求**，验证会话刷新拒绝，而非靠非法输入制造失败。实际审计逐行核对启动catalog/prime、所有预热和样本、默认拒绝、批准写、重试及历史操作。正常关闭stdin后要求真实子进程exit0、stderr为空，并记录PID/UTC创建时间；失败时仅收尾本夹具拥有的子进程。

新离线审核器核验全部原始样本、审计实际字节与身份/事件、六个已加载父程序集，以及四个MCP启动文件（helper DLL、deps、runtimeconfig、Protocol DLL）；协议DLL需与父进程一致，helper声明依赖只有MCP和Protocol。此为开发输入核验，不是SDK环境、自包含或正式产品闭包。文件链反例不是执行性能证据，也不会以一个声明`semanticChecksPassed`替代原始审计复核。

会话46635已exit0：Debug/Release定向构建零警告/错误，原Transport真实stdio/871项/并发/生命周期、4读与3历史冒烟以及新7项MCP冒烟全部通过，日志`out/ecs-e5d-mcp-{build,transport}-{debug,release}-second.log`。两个最新MCP冒烟分别为`mcp-smoke-8d018e44ccd04eef8c9089299bd02500`和`mcp-smoke-f1265e7f608440c3a8e26364cb80c3f4`。六份读/历史/MCP实际冒烟文件审核汇总`out/ecs-e5d-mcp-smoke-verification-first.log` SHA **BF6AECE47F81F4249478E86236E02C1764F0F2F52FFC43D94B9A632DCCFD3DE7**。最早单独冒烟`7b97ca0…`在补充子进程创建时间前，保持历史用途，不作当前驱动证据。

Python全套 **77/77，6.281秒**，`out/ecs-e5d-mcp-python-full-first.log` SHA **E106D929A2FC2934A9AEE1435AFB6BE29C851B42F984CA6808B37F4C23EFFBAE**。新增3个测试方法覆盖完整合成矩阵、21个范围/权限/子进程/成本反例，以及7个实际文件链/审计语义反例。首次单文件运行显示6项，其中3项来自导入的历史TestCase被发现器重复收集；改为导入模块后，全套77项只计各方法一次，不声称增加6项。四个真实CLI负例（未知模式、缺参数、Debug测量、错误合同）在工作量/输出目录创建前拒绝，位于`mcp-negative-38bdda20a2414b9cb62e827e9c455458`及`out/ecs-e5d-mcp-negatives-first.log`。

采样前登记`out/verification/ecs-e5d/mcp-development-first-plan.json`：一次Release父进程、三个依次退出的实际MCP子进程、21项/2,100原始样本，不选最好一轮、不自动重采样。会话40344已exit0，归档`out/verification/ecs-e5d/mcp-development-4025ab14b6ab4e9098cf435185663475/mcp-costs.json` SHA **8DC35A8AEE47D59EEAB92D8D65B95794C7511AAE0BE0082C1659B1D2CD88233E**。独立实际审核`out/ecs-e5d-mcp-matrix-verified-first.json` SHA **487BC318060918750C06FC1A4C610F08A673EEC5A989CA89DC24B90F21197FF5**，全部通过；再次按记录PID/创建时间核对三个子进程均不再运行。当前九个不同输入文件及五个本片驱动/合同/审核源文件保存到`mcp-products-first`；重构前的旧Debug六DLL另保存到`pre-mcp-debug-products-first`，先前Release与失败归档均不变。

4096对象开发观察如下。完整21项样本保留在实际报告，以下不是配对比较、目标预算或整帧数据：

| 工作量 | stdio端到端P95 ms | 嵌套Pump P95 ms | 父owner分配中位数 B |
| --- | ---: | ---: | ---: |
| 冷revision页 | 89.7893 | 56.1522 | 33,232,896 |
| 暖首页 | 47.1677 | 2.9637 | 1,315,376 |
| 暖尾页 | 47.4229 | 3.0775 | 1,319,216 |
| 暖详情 | 47.9442 | 0.9123 | 39,928 |
| 批准事务 | 289.1094 | 257.6598 | 157,335,744 |
| Undo | 173.4875 | 132.6459 | 80,100,360 |
| Redo | 157.4194 | 121.9632 | 80,101,128 |

工具写入仍存在高分配/处理成本，不能因为typed Query或World步进改善就认定整套编辑器工具高效。Pump与stdio墙钟嵌套、不相加；未独立归因到序列化、验证、审计或调度，不进行“差值就是MCP开销”的跨驱动推断。此处没有关闭审计或绕过统一命令历史。

当前所有工作会话终止，D仍未提交、未形成完整新冻结/标准双配置89项/正式3父进程领域队列、未部署。正式EXE再次确认 **C50742AD…**不变。下一片必须进入11.7的真实Editor/Player DX11/Null/严格搬移产品，覆盖模拟+动画/物理+渲染、0/小组/既有32角色预算、GPU独立来源/上传/生灭/驻留，以及有界队列成本；原功能并发测试不是队列性能结论。只读检查已确认`RelocatedPresentationCosts`窗口仍排除模拟，`Ncma.Characters.ScenePlayRuntime.Compose`尚未转发角色profile开关，本轮未修改这些生产路径。不能拿暂停展示窗口替代完整领域验收，目标机/人工/自包含/1h以及E/F/M8.3继续保留。

### 11.27 E5-D 用户确认目标机预算

2026-10-20，用户明确确认以下最终性能目标，独立记录于`scripts/ecs-e5d-target-budget.json`（`ecs-e5-d-human-target-v1`）：

| 项目 | 已确认目标 |
| --- | --- |
| 显卡级别 | GTX 1060 6GB / RX 580 8GB级；其他等效硬件须提供等效依据 |
| 输出 | 1920×1080 |
| 内部渲染 | 可使用原生分辨率；允许0.75–0.85比例，正式队列前固定选择并记录 |
| 画质、限帧 | Low，30 FPS上限 |
| 平均帧率 | ≥30 FPS |
| 1% Low | ≥27 FPS |
| 单次Hitch | 代表性游玩路径中不得超过80ms |

**这是目标确认，不是性能通过。** 代表性内容、相机/输入路线、测量时长、预热边界以及平均FPS/1% Low/Hitch的准确计算定义必须在正式采样前冻结，不能根据结果事后改口径。记录实际CPU、内存、驱动、显示/同步配置及显卡身份；这些附加硬件条件尚未由用户指定，不能擅自宣称任意CPU均可达标。

展示帧间隔（含限帧等待）与CPU工作、GPU时间分开报告。保留路线内加载、切换与慢帧，不删除失败样本；限帧30与平均≥30的严格边界照录，不能自行通过取整或容差放行。CPU微基准、暂停的渲染窗口或更强本机GPU结果不能替代目标机整帧验证。

既有D领域v2合同`81348C16…`及MCP合同`7CB39DA3…`和其开发证据保持原字节；其中当时“目标待确认”的记录由本节补充，不回写历史。下一份正式冻结须绑定新目标文件，并完成新候选完整回归及所需正式领域队列。当前`performanceAccepted=false`、`targetHardwareVerified=false`，内存、人工、自包含、1h与E/F/M8.3门禁继续开放。当前模拟采集实现及其局部测试不等于这些门禁通过。

### 11.28 E5-D 真实模拟采集与产品定向验证

新增可信主机显式选择的`--simulation-costs`，与现有`--presentation-costs`分开，默认关闭，不增加MCP权限或第二个World/runner。

- `PlaySession`值类型采集覆盖实际`AdvanceFrame`（包括所有追赶固定步）、headless固定步和暂停`Step`。记录会话/World/推进序号、前后tick、CPU墙钟和owner线程分配；失败或只完成部分固定步不能标作完整成功，固定步失败前未进入Update时明确为false。
- `ScenePlayRuntime`向现役角色组合转发profile选择，关联同会话/World/tick的最后一个已提交`CharacterStepProfile`。其分段时间嵌套、原生计数累计，**不能把它当作多个固定步之和**。零步没有新角色样本；Stop/Reload清除旧推进记录。返回值是副本，不保留新的原生所有权。
- Editor在真实Play推进后读取报告，也能观察由暂停单步直接产生的样本；暂停展示重复持有同一个推进序号，不冒充新测量。Player在展示前复制模拟报告，正常关闭后写入独立可选JSON字段。未选择时原字段缺省，Null不会为了采集加载GPU或无用角色/物理模块。
- `SimulationCostCases`22项、真实0/1/8/32角色组合100项：开关前后完整场景字节相同、多个固定步归属正确、owner-thread约束、部分失败、重载新身份、暂停单步和Stop释放。空推进暖态观测附加分配为0只是局部测试，不是整机零分配承诺。

标准`Build.bat -Configuration … -SkipTests`分别生成Release候选`ffba1310d3214f3384f774cff3b3777e`、Debug候选`e5dab6a3160a4cbcb0d95bc96e5f0954`，编译零警告，无部署。最终定向CTest如下：

| 配置 | 范围 | 结果 | 日志 |
| --- | --- | --- | --- |
| Release | Gameplay、Player、PresentationCosts、GraphicsRecoveryJoint | 4/4，162.62秒 | `out/ecs-e5d-simulation-ctest-release-second.log`，SHA E26818917D4352FEA4D7D3E5EEF738D132353226FD78A3DC746BBEE3DC1FE509 |
| Debug | 同上 | 4/4，159.34秒 | `out/ecs-e5d-simulation-ctest-debug-first.log`，SHA 4621FD3F0D8CE989525E6FA3FA2ADC17B0BDB8CB15C024F64799A5ACEFAAF47C |

每配置Player58项、展示45检查、联合产品1278检查及原Gameplay组均通过。联合产品继续覆盖ASCII/binary真实FBX、合成已知根运动伴随角色、Jolt、UI、图像和API0/0、三类管线与图形恢复；严格搬移Editor/DX11 Player/物理Null/纯Null禁止回退仓库程序集，增加实际apphost开关和默认关闭验证。这里的High阴影功能夹具不是用户指定Low/1080p目标机性能场景，也不是新的整帧正式测量。

独立复核`out/verification/ecs-e5d/verify-simulation-development-first.ps1`检查十个实际候选包全部清单文件、384份实际加载程序集副本、48条选定Editor模拟记录（按身份/序号拒绝重复）及12份实际Player CLI报告。输出`out/ecs-e5d-simulation-development-verification-first.json` SHA **8BB2CB450197D4D4F87057DEF89582684F46B8CEBD75DAC7B696AA8B04BA01B9**，明确`fullRegressionPassed=false`、`targetPerformanceAccepted=false`、`memoryAcceptance=pending`。这些是功能性观测，不是正式3进程成本矩阵。

保留两次失败：首次直接联合测试的新驱动配到旧发布包，出现`SceneSimulationCostReport` TypeLoadException，日志`simulation-joint-release-first` SHA24A8422F…；用标准流程生成新候选解决，不修改严格加载器。第一次定向回归中新增断言跨越了原测试已有的真实`Step`，错误要求报告完全不变，日志`simulation-ctest-release-first` SHA264DC947…；修正为首次恢复保持原样、显式Step恰好增加一次序号、后续纯图形操作保持新样本。所有失败目录、日志与构建前两份包映射保留，未降低产品/图像/权限断言或挑选性能样本。

当前全部工作会话已结束，正式EXE仍为 **C50742AD…**，旧D/MCP合同原字节与历史C/v1失败、C/v2证据保留。D未提交、未形成最终源码冻结、完整89项双配置或正式领域队列。下一步继续完整产品模拟+GUI/提取/提交成本、唯一GPU来源/上传、生灭与托管/原生驻留、实际有界队列；冻结目标路线/时长/统计定义后开展正式测量。内存、用户素材/目标机、人工、自包含、1h、E/F和M8.3仍未完成。

### 11.29 E5-D 运行窗口、原生计数序列化修复与GPU覆盖缺口

新增`EcsDomainCosts`，在严格搬移的真实FBX/根运动/Jolt/HUD产品上执行运行窗口：Editor使用可信帧API，Player在实际runner内部使用可信SDK推进。每窗口12帧预热、100帧记录，三个管线模式×ASCII/binary×两个主机×Debug/Release，共24窗口、2,400帧。每帧提交一个实际固定步，采集主机回调CPU/owner分配、嵌套模拟和展示报告、选定上传计数与GPU来源；窗口前后记录选定存活资源、进程工作集/私有字节、带有效性标记的原始托管堆和上次GC信息。

**这是固定1/60输入的开发观测，不是产品调度循环、限帧等待或目标机帧率验收。** 原暂停展示窗口保留。没有截图、强制GC、GPU等待或逐帧JSON；CPU分段嵌套不相加，GPU不同领域不相加，进程私有字节不减去托管值来伪造原生驻留。小型Python校验单测与开发门禁有重叠可能；这批数据不是隔离环境下的正式接受测量。

第一版预登记`scripts/ecs-e5d-running-development.json`对应两配置2/2功能测试通过（Release85.89秒，Debug90.44秒），但独立检查原始JSON发现`CharacterStepProfile.Native`为`{}`：原结构是interop公开字段，默认序列化器只保存属性。第一版驱动、源文件和包映射已保存在`out/verification/ecs-e5d/running-first-driver`，全部原始目录保留，不补写丢失计数或宣称其物理计数完整。

修复采用`CharacterNativeCostCounters`：十个明确的只读托管属性，复制现役原生统计，不修改C ABI、不暴露interop布局、不全局启用字段序列化。0/1/8/32角色测试新增实际默认JSON与内存值比对，共103检查。单独的开发格式第二版`scripts/ecs-e5d-running-development-v2.json` SHA **F24E314FB743DC89800A148B4A2871CDB19D829784C4AE7222A2EE36A03AC753**；它不是E5-C计时合同修改，也没有改变目标机阈值。新驱动在校验前保存完整原始窗口，采集回调/读取异常时保存已完成前缀，使用CreateNew且IO位于计时外。

新同源标准SkipTests候选为Release`c2c8dd184d364c609c02c04508a295d0`、Debug`324357d0a4e9492fa95f56c43a3661a1`，零编译警告、未部署。定向Gameplay/Player/PresentationCosts/GraphicsRecoveryJoint各4/4通过，分别150.60和158.08秒；包括原Gameplay组、模拟22、角色103、Player58、展示45、GPU账本21和联合产品1278检查。Python全套 **79/79，7.196秒**，日志SHA **1DC84CFDABFE5D9104D8D653C9ADF757EEE5B983D5E60BB5E793D2F030EC16CF**；新增两个方法涵盖23个篡改负例，拒绝空物理计数、错序号、NaN/布尔数值、跨代次/未来GPU来源和伪造汇总，负原始堆值只能明确无效且内存验收待定。

独立`ecs_e5d_domain_evidence.py`从原始帧重新构建GPU去重结果，并绑定逐窗口原件、实际加载的搬移程序集与当前标准候选。总凭据`out/ecs-e5d-running-verification-v2-first.json` SHA **3D203F9BCAC9B3563805D88BE35EE70D71210C73E989FFE98236E907A589AA7F**，核验8份报告、24窗口/2,400帧、10候选包/992文件，全部窗口结果保留。实际驱动、场景资产完整输入冻结及正式队列仍需后续完成，不能把这份开发绑定当成最终同源性能接受凭据。

Release六个Editor窗口的主机回调P95范围1.0539–1.8809ms，owner线程分配中位数112,368–113,504B；六个Player窗口为0.6571–1.2573ms、70,816B。范围仅描述不同夹具开发数据，不跨配置/版本做性能归因，不换算为满足30FPS的结论。

GPU去重保留所有原始观察，以设备代次/领域/序号为键，同一来源的源帧/耗时/资源/内容代次发生冲突即拒绝。未就绪不当成0ms样本，窗口外有效来源不改标当前，重复值不是独立样本。24窗口中主提交21个窗口无窗口内有效来源，总共只有28个唯一来源；Skin为5个空窗口、245个唯一来源；UI全部无窗口内新生产来源，暖态复用的旧UI来源单独保留。**现有GPU覆盖不足，不能形成整帧GPU结论。** 只读源码检查发现`RendererPlugin.cpp::BeginTiming`与`GpuWorkTimingKernel::Begin`先Resolve再用同一槽开始Recording，完成结果可能在主机读取前被覆盖；尚不能据此断言某次观测的唯一根因或硬件缺陷。

全部会话终止；正式EXE仍C50742AD，历史合同与失败不变，D未提交/未部署。下一步调查并补齐有界已完成GPU结果保留，维持当前状态/来源身份、不加GPU等待或伪造有效值；再完成实际产品循环/帧间隔、GUI与提取、原生/托管驻留、有界工具队列、目标路线/统计定义及完整输入闭包、新冻结、完整双配置89项与正式队列。`targetPerformanceAccepted=false`、内存待定，E/F、人工/目标机/自包含/1h和M8.3仍未完成。

### 11.30 E5-D 有界GPU完成结果快照与定向验证

新增可选原生`query21/v1`及C# `RendererSession.ReadGpuTimingSnapshot`。单次返回当前槽和最近一次已解析结果，覆盖主提交、蒙皮调度和UI离屏生产三个域；query19/20的现役“当前槽”语义保留不变。

- 每域仅保留一个已解析的POD副本，三个域副本负载合计248字节（不含内核引用、托管委托等其他开销，不是驻留测量）；没有新GPU查询、历史队列、资源所有权或GPU等待。一次读取最多三次DONOTFLUSH轮询，不惰性创建缺失的蒙皮/UI资源。
- `LastResolved`只接纳Valid/Disjoint/Invalid；新的无效完成结果会替换旧有效结果，不挑选“最近有效值”。Failed/Abandoned始终可从Current看见，历史结果不掩盖当前故障。
- 下一次Begin不会擦除已完成副本。资源退役保留其历史token和内容代次，但不授予访问权；新设备代次重置两个槽，旧托管副本独立有效。
- 来源由设备代次/域/序号及源帧确定。两个槽的ObservedFrame都是本次读取视野，不是完成时刻，允许推进。单槽保留仍可能漏掉读取间隔内多个UI生产结果，必须报告序号缺口，不能宣称完整GPU覆盖或累加为整帧。

原生新增54检查，原工作计时扩展至104检查：用实际GPU工作和**测试专用**完成等待，复现三个域“完成后不读取，直接开始下一次工作”，验证结果未丢失；另覆盖未就绪、无效/不连续/失败、退役/重建、错误线程/重入/参数、复制输出原子性、API0/0。C#主计时24检查、工作计时21检查通过，含128次主域及256次工作域暖态快照读取owner线程零分配；只是该读取API的局部边界，不是完整引擎零分配承诺。

本轮保留全部失败：

1. 首轮Release新增测试错误地比较了整个历史样本，未考虑ObservedFrame从41推进到57。原生和其他四组通过，主计时断言失败，继发活动帧清理异常也保留。修正为仅更新预期观察视野，源帧/序号/耗时等仍严格相等。日志SHA **19795C90AC2F27B7D6230F02389FF38A307E7BEB2661FA213F32A5B53E41132E**；失败驱动/源码/包映射在`gpu-snapshot-failed-assertion-first`。首次PS5包装因CTest stderr提前抛出，终止后另行复制完整测试日志，没有吞掉失败。
2. Debug首次链接DLL发生LNK1163，`SkinKernel.cpp.obj`的std::logic_error虚表COMDAT selection6被拒绝；保留目标文件/PDB/库/配置/日志于`gpu-snapshot-debug-link-failure-first`。原流程重试已成功链接DLL，但另一个原生测试EXE首次链接发生同类错误，另外保存其对象/PDB/日志于`gpu-snapshot-native-test-link-failure-first`。随后原流程重试该剩余目标成功。两个对象哈希始终分别为`3536D111…`和`0D898768…`，没有源码、编译器、链接选项更改或手工删除产物。
3. 单独的非增量链接诊断将同一DLL对象链接到`out/verification/ecs-e5d/gpu-link-isolation-first`成功；未替换产品。它不证明工具链缺陷或唯一根因，最终产品仍来自原始标准构建。两次链接失败日志SHA分别为`D60BE4B4…`和`7696EA3E…`，隔离诊断日志SHA`975E5252…`。

最终同源标准SkipTests候选：Release`0cee7abddeb14b248d104a8118341cb1`、Debug`388eb6e70a2c477c9fcdc260867bd37a`，零编译警告，未部署。定向原生渲染、GpuTiming、GpuWorkTiming、PresentationCosts、GraphicsRecoveryJoint均通过：

| 配置 | 结果 | 日志 |
| --- | --- | --- |
| Release | 5/5，79.12秒 | `out/ecs-e5d-gpu-snapshot-tests-release-second.log`，SHA A5D0608447100530418CE965A809B25D093777A53142F4502E2CD5348789A2DC |
| Debug | 5/5，82.40秒 | `out/ecs-e5d-gpu-snapshot-tests-debug-fourth.log`，SHA CB0217A7A06392A0F24D9B3E14E80AA9ACAF39D31760DF917C57484DCE1F75F2 |

Python全套79/79，6.724秒，日志SHA **15BCC40AA2EE8E47DFC8A50AA0E7B3C9CA0685C310287E95DFA942D2E7BD3724**。独立复核`out/ecs-e5d-gpu-snapshot-verification-first.json` SHA **889B1505EA2EBB797BBCE4A52B57B8F9969EE55F04DCAC3F9977E7CFA25F412A**绑定实际10候选包/992文件、8份严格搬移产品报告及其24窗口/2,400帧、当前驱动/原生二进制和相关源文件，正式EXE仍为C50742AD，上一轮证据原字节保留。旧版运行驱动和包映射保存在`pre-gpu-snapshot-driver-first`。这是开发功能证据，不是完整源码冻结、双配置89项或正式同驱动性能队列。

**展示报告及EcsDomainCosts尚未消费新快照**，本轮没有GPU覆盖改善或性能验收结论。下一步将Current与LastResolved同时接入报告，以单独版本的开发合同/校验器记录、去重及统计缺口，保留旧v2合同、原件及其失败/不足，不回填历史数据。再继续11.7全部领域、实际产品循环/帧间隔、GUI/提取、驻留、工具队列、目标路线/统计定义、新冻结/完整回归/正式队列。用户GTX1060/RX580级、1080p、Low、30fps预算不变；目标机、内存、人工、自包含、1h、E/F和M8.3均未放行。全部工作会话已结束，D仍未提交或部署。

### 11.31 E5-D GPU双槽报告接入与v3运行证据

`PresentationCostReport.Version`升级为2。Main/Skin/Ui继续表达当前槽，新增MainResolved/SkinResolved/UiResolved表达最近一次解析完成结果；采集器每域读取一次query21快照，分别归属源帧。不以旧有效值替换失败或未就绪状态。Abort不读原生并清空六个字段；纯2D/空域不会为观察初始化3D工作。实际Player JSON、Editor/Player恢复及搬移窗口均消费同一现役报告，无旧格式兼容转发。

运行证据采用独立`scripts/ecs-e5d-running-development-v3.json`，SHA **2204086A14DB1464BD1937450F12564B0DB179A5B3D1C4D9A353A68B16123AB8**。原v2合同SHA F24E314F…、校验器SHA D20E7F11…和全部历史原件保持不变；改动前驱动/包映射/相关源文件保存于`out/verification/ecs-e5d/pre-snapshot-report-v3-first`。没有将旧v2数据补写为v3。

新`EcsGpuSnapshotLedger`保存预热末帧基线，只对LastResolved的Valid/Disjoint/Invalid来源去重，另列当前状态分布、窗口外来源、重复结果、序号跳跃与尾部。跨代次/未来来源、当前与完成槽矛盾、同一来源字段冲突、序号回退均拒绝；解析后的无效结果不能被过滤成“最近有效值”。序号缺口是**未观察到解析结果的序号槽**，可能包含放弃的工作，不能直接称为丢失的有效耗时。没有末尾GPU排空，也不把不同域耗时相加。旧测试专用EcsGpuLedger实现已替换。

原始窗口仍在校验前CreateNew写入；新增预热报告绑定。后循环的内存/资源读取异常也会保留已完成帧前缀，持久化自身失败会与原异常一起报告，不隐藏首个异常。这个异常处理边界经过代码检查；本轮未通过故障注入宣称所有磁盘/进程终止路径都能保存。

独立Python v3校验器重建全部GPU序列与统计，并精确绑定原始窗口/基线及实际加载程序集；JSON布尔值不能冒充数值0/1。未修改v2校验器，仅在内存中投影当前槽以复用其既有CPU/物理/内存/资源约束，不输出或接受一份伪造的v2记录。新C#账本34检查，展示46检查；Python新增4个方法，涵盖24个记录/汇总篡改负例和2个实际文件绑定负例，另含当前有效、当前Pending但历史已完成、无效完成结果与序号缺口正例。原GPU54/104原生检查、24/21托管检查、Player58和联合产品1278检查均保留。

标准SkipTests候选为Release`02a3f323e4b9462bb2141a3529cd0a3c`、Debug`40d9e027647541f2b015f9d3bef08cbe`，零编译警告、无部署：

| 配置 | 定向回归范围 | 结果 |
| --- | --- | --- |
| Release | 原生Renderer、GpuTiming、GpuWorkTiming、PresentationCosts、GraphicsRecoveryJoint、Player | 6/6，153.02秒，日志SHA 56F5B6E8F716DDB112297A7278D5038B0042ADCB1017FD52325DF5E08B41B4A3 |
| Debug | 同上 | 6/6，155.13秒，日志SHA B97596750EC5A7C779481F1E5AFA3442E677DB4766EFE21567B34380E6DC686C |

Python全套 **83/83，8.647秒**，日志SHA **FDD0094718E4B4F8419E8D30457CA733099C127F126DD028AB5AD0171D8908BE**。两配置的首次产品调用均通过，无性能挑选或重采样。独立总凭据`out/ecs-e5d-snapshot-v3-verification-first.json` SHA **5CD9FB250FD479087240E1E4C00B5F25D1A1F1DA2DBF59FBCAF63149C699712D**核验10候选包/992文件、8份严格搬移报告、24窗口/2,400帧、实际程序集与基线/逐帧原件。

以下为**同一批原始帧、各窗口内去重后计数之和**，不是旧v2与新v3跨版本计时比较：

| 域 | 当前槽有效测量来源 | 完成槽有效测量来源 | 仅完成槽保留下来的来源 | 未观察到解析结果的序号槽 | 窗口尾部序号槽 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 主提交 | 16 | 2,360 | 2,344 | 1 | 24 |
| 蒙皮 | 235 | 2,209 | 1,974 | 170 | 23 |
| UI离屏生产 | 0 | 0 | 0 | 0 | 0 |

主提交与蒙皮的24个窗口均获得窗口内有效完成来源；当前槽分别有2,384和2,165条Pending观察，仍保留其原状态。主提交另有24个窗口外有效来源、蒙皮20个、UI12个；UI暖态没有新的窗口内生产，不伪造零耗时。有效当前槽来源没有被完成槽遗漏。

单副本不是完整历史：例如Release Editor模式0、HostFrame92，上一帧蒙皮Current为序号39/Pending、Resolved38，下一帧Current及Resolved均为40/Valid，因此39的解析结果没有出现在报告中。其余所有缺口同样原样保留；这是观测限制，不证明GPU执行丢失或硬件缺陷。主/蒙皮尾部也未排空，**仍不能以此报告完整GPU均值/P99或目标机性能通过**。

全部43366/74848会话已终止exit0，无活跃构建/采样。正式EXE仍C50742AD，前轮凭据889B1505…原字节不变，D未提交、未部署、未最终冻结或完成完整双配置回归。下一步按11.7推进实际产品运行循环与展示帧间隔（现役PlayerRunner用墙钟delta，当前SDK窗口固定1/60不能替代）、GUI/提取分段、托管/原生驻留和有界工具队列，再冻结代表性目标路线与统计定义。GPU缺口继续在正式领域证据中显式保留，必要更改需新合同/冻结/相关完整回归，不为取绿重复测量。30fps用户预算、目标机、内存、人工、自包含、1h、E/F和M8.3门禁保持未完成。

### 11.32 E5-D 实际产品循环与Present返回间隔开发验证

本轮落实真实循环观测，不再使用固定1/60的SDK窗口代表产品循环。新增独立开发合同 `scripts/ecs-e5d-loop-development-v1.json`，SHA256 **56DAE3C2E0FBCFB5DE87FA3548B93891954629B7914CC74CC59E52772F0080E0**；此前运行v2/v3合同、原始失败、GPU缺口及C-v1拒绝状态不改写。修改前的Debug/Release三个测试驱动及依赖和包映射保存在 `out/verification/ecs-e5d/pre-loop-costs-first`。

- C# Application层新增有界 `LoopCostCapture`，CLI通过默认关闭的 `--loop-costs` 显式启用。Editor使用现役 `ApplicationLifetime.Run`，Player使用现役 `PlayerRunner`，没有第二循环、固定替代时钟、强制GC、GPU等待或新的Agent权限。
- Stopwatch原始时间戳与频率、循环序号、帧号、结束状态和owner线程分配独立保存；阶段覆盖控制/平台/编辑意图/Agent/展示准备/输入/模拟/展示/恢复。阶段值是**墙钟时间，包含调度、同步等待与观测开销，不是纯CPU时间**，也不代表所有线程分配。报告复制/序列化和启动/关闭不在循环范围内。
- 原生Present成功返回后才打时间戳；Null、CPU-only和未实际Present的循环没有虚假样本，已经成功Present而随后宿主失败时仍保留该事实。间隔跨越跳过、故障和恢复，不将恢复后重新建立模拟时钟基线误当成无停顿。Editor恢复发生在Run之外，不计入单次循环阶段，但进入下一次Present返回间隔；Player恢复在Recovery阶段。
- 最多保留前512条，不覆盖旧样本；后续循环持续累计遗漏数、Present数量/间隔数量和最大间隔。默认无观测器；暖态捕获器本身256次检查为0 owner分配，**不代表宿主整循环零分配或无观测开销**。无完整原始覆盖时不推导全程分位数，不输出“目标性能通过”。
- Player在现有CreateNew、1MiB报告预算下保存独立可选字段，默认字段缺省；Editor在成功或异常退出后的finally输出JSON。尚不是一小时全程持久化/崩溃恢复采集器，不把控制台输出等同已有完整长稳归档。

定向验证结果（不是完整89项）：

| 门禁 | Release | Debug |
| --- | --- | --- |
| 标准Build.bat -SkipTests，无部署 | 0编译警告，候选7d042a343faa4995b8f1d30d388be84d | 0编译警告，候选948c82d1829a44b1abbe7ed281b7790f |
| CTest原生渲染/Application/Player/PresentationCosts/GraphicsRecoveryJoint | 5/5，152.09秒 | 5/5，157.34秒 |
| 新循环基础检查 / Player用例 / 展示成本检查 | 42 / 59 / 52通过 | 42 / 59 / 52通过 |
| 真实候选apphost CLI | Editor DX11/CPU-only、Player DX11/Null各1次，全部exit0 | 同左，全部exit0 |

42项基础检查含线程亲和、重入/无活动记录拒绝、容量溢出、返回副本、阶段和等于循环区间、合成模拟时钟不作为采样时钟、平台/输入/模拟/Present及Present后故障。实际Player另覆盖观测前后相同场景哈希、514步/512前缀/2遗漏、取消、游戏故障和持久报告；新图形检查覆盖真实Editor产品循环和Player返回点。原生图形恢复联合用例继续通过，但不能宣称此次为每一种原生故障单独采集了loop报告。

Python完整 **86/86，8.627秒**，日志SHA256 **2E27998D0E583891D8BD4007596B6C5AE162879D39048B4D3D9D68A0F1AD7B61**。新独立校验器3个方法含18个坏数据变体，严格复算阶段区间和实际返回间隔，拒绝错误计数/时序/布尔冒充整数/遗漏覆盖声明。实际校验8个源宿主报告和8个候选CLI报告，共16份；未计算FPS或选择较快结果。图形Player达到3个固定步可能只需2次墙钟循环，原样保存，不人为对齐为3帧。

证据与核验过程：

1. `out/ecs-e5d-loop-verification-second.json` SHA256 **B58DB62F7F3F934779F107D264B063167A12777E56B17B51901F469D24F1F43E**，绑定16份观测、原始stdout/stderr/JSON、实际候选文件、场景夹具和当前源文件。8次CLI执行没有测试截图回调或合成时钟；它们仍是开发夹具，不是代表性目标机游玩路线，也不是新增严格搬移CLI测量。
2. 首次核验在读取CTest详细日志的中文时区头时发生UTF-8解码失败，尚未启动任何CLI。保留原脚本、空归档及 `out/ecs-e5d-loop-verification-first.log` SHA256 **B769DE6A8066510D8ACC655C6B171B023C4EBA72D8F53B5637DCE9B6EDC89576**；第二脚本逐行严格UTF-8/CP936解码并记录行号，无替换字符或忽略字节，不重跑已完成的构建/测试。
3. 首次文件闭包检查将运行日志也要求字节不变，拒绝Editor日志追加；原脚本与失败输出保留。独立列举所有1084个固定条目后，**只有**两个候选Editor的 `out/user/logs/editor-candidate.jsonl` 改变，Release422→1259字节、Debug421→1259字节，旧前缀SHA一致。第二闭包脚本只识别这两个精确路径的追加，并确认它们不是产品清单项；保存旧前缀和完整追加后文件，不豁免任何程序集/资产/其他文件。
4. 最终 `out/ecs-e5d-loop-closure-second.json` SHA256 **6EA80E5CD6477BDFD8535877D3B25CC973EDFE3F01A6D64B454755FD7F040B25**：1084条绑定、10个实际候选包全部清单大小/哈希、各包Application程序集与对应配置构建产物一致；未再次采样。正式EXE仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**，前轮5CD9FB25…证明原字节不变。

63489/75448均已终止exit0，8次CLI与最终核验已结束，没有活跃构建/采样。本轮是实现和功能证据进展，不是无进展阻塞。D仍未最终冻结、未全89双配置回归、未正式领域配对、未提交推送/部署。下一步优先完善全程采集/产品限帧和目标配置边界，再GUI/提取细分、托管/原生驻留、有界工具队列、目标路线/统计定义及全部正式门禁。Present返回时间不证明物理显示；GPU单副本缺口、内存、实际1060/RX580级目标机、人工、自包含、1h、E/F/M8.3仍未完成。

### 11.33 E5-D 可选限帧与等待精度风险

本轮已实施可选限帧，但**当前等待精度不足的风险未解决，不视为性能交付**。功能与性能分别判定，不能因为功能测试通过便进入E/F或部署。

#### 实现边界

- Editor/图形Player支持 `--max-fps 1..240`，默认不创建限帧器；Headless在参数解析与SDK运行预检均拒绝该选项，保留不等待墙钟的固定步推进。候选示例：`NcmaEngine.exe --editor --max-fps 30 --loop-costs`。正式out/bin仍是旧已交付版本，本轮没有部署这些选项。
- C# `FramePacer`使用独立Stopwatch时间戳，限制相邻循环放行的最小间隔为 `ceil(Frequency/fps)`；首次立即放行，慢帧以后从实际放行时间重新计间隔，不突发补帧。不会改写现役模拟时钟、固定步长、Play累积器或图形恢复权限。
- 当前实现请求最长8ms的Thread.Sleep片段，剩余不超过1ms时使用短SpinWait并重新读时钟；**请求时长不保证实际唤醒精度**。Player等待中轮询既有取消回调，放行后重新检查运行时限，避免超过时限后再推进游戏。低帧率模式的等待与既有工作可能使返回晚于时限；它不是硬实时调度器。
- 循环报告升级为v2，新增 `Pacing` 阶段、配置帧率与限帧器实际放行时间戳。新的Python校验器严格复算间隔下界；未限帧的记录不得伪造Pacing或放行戳。旧v1校验器/合同保留，复用其公共检查仅发生于内存内投影，不产出假v1数据或改写旧证据。
- Player采样结束使用已返回的PlayStatus，不再为观测额外调用一次状态getter。原始循环序号仍是采样身份；帧号是最近返回的PlayStatus帧号，提前取消/异常可为上一帧或0。
- 新开发合同 `scripts/ecs-e5d-loop-development-v2.json` SHA256 **94186FF6F2BBB9D12D173D35B7BCC30D3227A5B2267EC77A01422F8E5CA8C6E4**。修改前的双配置驱动、包映射、循环源和旧合同/校验器保存在 `out/verification/ecs-e5d/pre-pacing-first`。

#### 功能与原始数据验证

| 项目 | Release | Debug |
| --- | --- | --- |
| 标准无部署Build.bat -SkipTests | 成功，0编译警告；ed29429c0bae4795aaca81eefa93e825 | 成功，0编译警告；0718a5ad1c254ed491791c13dffcfb7a |
| 原生渲染/Application/Player/PresentationCosts/GraphicsRecoveryJoint | 5/5，153.76秒 | 5/5，157.64秒 |
| 新限帧基础 / 原循环 / Player / 展示检查 | 28 / 42 / 60 / 57通过 | 28 / 42 / 60 / 57通过 |
| 实际候选CLI | 项目Editor DX11/CPU-only各12帧，Player cap30，Player时限停止，3个非法参数用例 | 同左 |

28项限帧检查覆盖整数边界/长整数频率、线程亲和、取消不消耗放行、捕获后重入也拒绝、时钟倒退、慢帧后不突发补帧、限帧与观测配置不符时预拒绝、模拟delta不变、真实单调时钟放行间隔及暖态限帧器本身256次0 owner分配。这个0B结论不代表整个引擎或采集过程零分配。

实际Player用例另覆盖非法/重复参数、Headless拒绝、原始场景哈希及固定步长不变、等待中取消不再输入/模拟；既有图形恢复用例保持通过。14个CLI过程全部达到预期结果：6个正常退出0、2个运行时限退出8、6个参数拒绝退出2，**没有为取绿重跑任何CLI**。完整Python **89/89，8.652秒**，日志SHA256 **DCE2121E309C2DED78A0C49FDA63E18D6ECDE959C6D026DDD2873C96D16D0E7B**；新增3个方法含16个负例。这里的89是Python用例数，**不是完整89项CTest门禁**。

首个综合核验在14次CLI完成后的文件检查失败：Editor指定项目时按现役逻辑写入项目 `out/user/logs/editor-candidate.jsonl`，核验器误把夹具内运行日志当成不变输入。保留 `out/ecs-e5d-pacing-verification-first.log` SHA256 **2641F14DA8ACA6285D6BCF1A1E0F5BC9A8C59F31CDC281A1E5CE70B1F0498423**、原脚本与全部运行数据。原核验内存中的测前完整夹具哈希清单没有落盘，不能事后宣称已经证明整个夹具测前/测后闭包或该项目日志旧前缀不变。

离线核验不启动任何新过程，读取原有20份报告（源宿主12、CLI8）、14条退出回执、当前10包清单/大小/哈希和Application程序集，重新检查配置、Pacing原始时间戳、阶段/Present关系及实际场景字节哈希与Player回执相等；单独保存当前项目日志。产物 `out/ecs-e5d-pacing-offline-verification-first.json` SHA256 **3F0AFCC5F22D93D6686D43B2A36F0415F5F4D79CF26AA56FDDC4006A9666E755**，绑定1101项；明确 `prePostCompleteFixtureClosureProven=false`、`originalFirstAuditAccepted=false`、`additionalSampling=false`。这是**事后文件绑定的开发功能证据**，不补写缺失的测前证据，也不代替正式性能冻结。

#### 同一原始数据中的节奏问题

以下是短开发窗口的循环放行间隔，不是物理显示FPS、正式目标路线或稳态性能：

| 配置/入口 | 平均放行间隔ms | 最大放行间隔ms |
| --- | --- | --- |
| Release Editor DX11 | 52.938 | 128.112 |
| Release Editor CPU-only | 48.516 | 64.554 |
| Release Player | 46.901 | 48.667 |
| Debug Editor DX11 | 54.893 | 142.209 |
| Debug Editor CPU-only | 48.114 | 72.998 |
| Debug Player | 47.987 | 51.169 |

设置30时名义周期为33.33…ms，这些结果只证明没有过快放行，**不证明达到30 FPS**。同组除首条外Pacing阶段均值37.78–41.40ms，其他循环墙钟均值1.33–3.01ms；不同均值包含的区间不同，不能相减求因果。所有冷帧/长间隔保留，不删除样本。检查文件 `out/ecs-e5d-pacing-cadence-inspection-first.json` SHA256 **F584FAC639C90F1B15940EFF70DD8AEBD6EBE4579A37CDBF59B65B1C49E4F61F**，只重算原始数据，没有补采样。

进一步预先限定一次独立进程诊断：1/2/4/8ms各24次Thread.Sleep，共96次；同一实际产品Application程序集的FramePacer(30)调用24次；无重试、丢弃预热或强制GC，无渲染调用。构建0警告，.NET8.0.28、频率10,000,000；实际加载程序集与当前Release DLL SHA **EF33BE10559CB266372F53076887BCB18C3D775B7A40E2321A08EBD58714500C**一致。请求1/2/4/8ms的均值分别15.282/15.698/15.815/15.711ms；限帧器23个放行间隔均值46.971ms。原始文件 **A4C3B6B2D68C8932CE07A33BFCF2B626DB55209857D80588C5E0E81BEC4D5764**，独立重算与输入/DLL绑定证明 `out/ecs-e5d-pacing-wait-probe-verification-first.json` SHA256 **D3B7DE277B1F0432B8331A9344F6C22A69B15FD5F9E8F60204885D90690E074A**。

这支持优先修复当前等待精度，不是.NET运行时缺陷结论，也不证明每个Editor长帧都由Sleep造成。下一步先核对平台等待能力，为C#调度提供合适的高精度等待路径，保持默认路径与模拟权威不变；新实现必须新归档/相关双配置回归并预先持久化正确的输入清单，再采集新样本。不要重跑本轮矩阵挑更快结果，也不能通过扩大统计容差、换算假FPS或切掉慢帧满足门禁。

所有97656/37310会话已结束exit0；63855结束exit1（核验器，不是CLI过程失败），离线校验与独立探针均已结束。正式EXE仍C50742AD；上一轮B58DB62F…/6EA80E5C…文件原字节不变。E5-D限帧精度、全程采集、1920×1080/Low/内部比例与路线冻结、实际目标机、GPU缺口、GUI/驻留/工具队列、完整CTest、新正式测量、内存/人工/自包含/1h/E-F/M8.3仍未完成，未提交推送或部署。此轮有实现与新诊断证据进展，不是阻塞审计。

### 11.34 E5-D 平台高精度等待与实际入口接线

本轮前一回合只是重新核对用户目标，没有新实现或新验收证据，属于无进展但并非阻塞。随后继续修复11.33中有界探针复现的短等待偏慢问题，不调整用户的FPS/Hitch阈值、不重跑旧矩阵挑更好结果。

实现前将491个选定源文件及双配置测试/平台二进制复制到 `out/verification/ecs-e5d/pre-precision-first`，带实际复制哈希清单；这是开发输入留档，不是完整正式源冻结。旧v2合同和原始慢帧/失败证据保留。

- 原生Platform新增显式请求的1.1表（144字节）；现役未限帧入口仍请求1.0（120字节），没有暗中分配定时器。每个模块最多拥有一个独立于窗口/渲染器的等待资源。
- `CreateWaitableTimerExW` 只请求高精度同步定时器，`SetWaitableTimerEx` 使用相对100ns单位、一次性、零合并容差；不调用全局 `timeBeginPeriod`，不注册回调、不泵事件、不等待GPU。不支持或创建失败明确返回错误，不静默回退到粗粒度Sleep。
- C# `FramePacer` 仍拥有单调时钟、放行策略、取消检查及最后≤1ms的短自旋；通过可信本地委托调用1..8000µs平台短等待。独立通用构造器仍是可移植粗粒度路径，不代表产品CLI的高精度接线。
- 托管 `PlatformPrecisionWait` 持有模块租约，构造失败释放租约；线程错误、无效参数、过期/已释放资源拒绝调用。原生Status统计该资源，未释放时Shutdown返回Busy；关闭失败保留句柄和租约，禁止强制卸载。
- Editor（含CPU-only）与图形Player仅在显式 `--max-fps` 时请求1.1并创建服务；它由现有Presentation生命周期持有，渲染器恢复不重建计时器或重置限帧器，最终销毁发生在Platform卸载前。Headless仍拒绝限帧，模拟delta/固定步规则未变。
- `scripts/ecs-e5d-loop-development-v3.json` 单独说明新等待实现。循环报告仍为v2，因为采集字段/时间含义没有变化；未把旧报告改名成新实现证据。

微软文档说明高精度标志从Windows 10 1803起支持，但计时器准确度仍取决于硬件/调度，不能把“高精度”当作精确唤醒或目标机通过保证：[CreateWaitableTimerExW](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw)、[SetWaitableTimerEx](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-setwaitabletimerex)。1秒看门狗仅用于等待异常返回，8ms是请求上界而非硬实时取消延迟保证。

新增原生检查覆盖1.0/1.1协商、短表、未知版本、空输出、非法/过期句柄、参数上下界、错误线程、重复创建、Shutdown Busy及32轮释放；托管检查覆盖租约、窗口独立性、错误线程、重复Dispose、32轮释放和1.0不隐式分配。Application新增等待委托失败不消耗放行、失败后取消仍可用和空委托拒绝，限帧基础检查28→33。真实OS创建/设时/关闭故障注入尚未覆盖，不能宣称所有系统错误路径均已实测。

#### 本轮验证与剩余问题

双配置标准 `Build.bat -SkipTests` 均成功、0编译警告、不部署；Release候选 `0567fbb1e43f497eaab7d0a711f692dc`，Debug `81c0eef13fa4487aaf9d858f8e281fb6`。随后各8/8定向CTest通过：原生渲染、原生平台/GUI、托管展示、Interop、Application、Player、PresentationCosts、GraphicsRecoveryJoint，分别153.01/157.67秒。日志SHA256分别 **EA11C07E85B4D2A062C22591EA32C018E8C088ED4E8A6B32F0C3CF85C4058B23** / **2FA3A3A369C7B3749A65CC4077EA85384DF921CABC25EEE2D32B14144989C507**。Python89/89，8.630秒，日志 **032B7B822EA95D30EE57CD711667AD8BC4F58CB3D9C37DA602A5DAAC6711D8C2**。这些仍不是全套CTest或最终性能门禁。

新合同v3 SHA **8474FD6EC901B06F0907E39B83E66ED3FE38BDEE4F3E3551F83661A20267A9F5**。实际CLI启动前，先将14次预定命令、1065项不变输入、两个项目夹具文件清单以及项目/应用运行日志旧字节持久化到 `precision-cli-first/preregistration.json`，SHA **153386E866FA36AD05963A68DF8E01BD42EF33389A3304E8B34C6629B2621543**。这一次明确把运行日志与不变输入分开，运行后核对清单集合、所有输入哈希和日志追加前缀，没有补写上一轮缺失的测前证据。

14次CLI全部首次得到预期退出：6个正常0、2个运行时限8、6个参数拒绝2；双配置各有Editor DX11/CPU各12帧、实际图形Player cap30、1FPS时限停止及非法参数/Headless拒绝。10个候选包实际文件与清单、Application/Platform/Interop程序集、原生Platform DLL和当前构建产物一致；Player报告确认平台1.1。源入口12份报告与CLI8份报告全部按原v2字段规则核验。

综合证明 `out/ecs-e5d-precision-cli-verification-first.json` SHA **C6F391CEAAFA12D61E32C1DDAD85CBB2F7E7151894BAE7E60EA400FA2CC63AA9**。另一个只读脚本从实际归档重新核验1065输入、57输出、14回执、预登记内容和运行日志，再重算全部CLI时间戳/逐帧阶段；输出 `out/ecs-e5d-precision-offline-audit-first.json` SHA **EF347A9F2E400118FF9CE9A799BDD736A17EBFBFA5E20C35BABB12BD6FF281BE**。没有新增采样、删冷帧或重跑挑选。

| 配置/入口 | 全部放行间隔均值ms | 最大ms |
| --- | --- | --- |
| Release Editor DX11 | 42.3705 | 132.7304 |
| Release Editor CPU-only | 35.8500 | 60.9987 |
| Release Player | 34.6548 | 46.5386 |
| Debug Editor DX11 | 43.2029 | 141.8867 |
| Debug Editor CPU-only | 36.6939 | 70.2692 |
| Debug Player | 35.4584 | 54.5728 |

这些短开发窗口仍包含第一次展示的昂贵工作，不是正式配对收益或目标机FPS。逐行证据中，各组第一个间隔最长；Editor DX11首帧BeginPresentation为32.7948/32.7262ms、Presentation为89.7714/96.2036ms，首帧Pacing仅0.4236/0.7972ms。其余已记录间隔约33.3334–33.3557ms；这个描述不删除首帧、也不把余下样本另立为“通过队列”。阶段计时只定位到调用阶段，**尚未证明JIT、上传、GUI或某一原生调用是具体根因**；放行间隔也不是物理显示Hitch。

下一步优先细化首次GUI/渲染展示及提取/上传成本，保留冷启动和运行期路径；继续§11.7中的全程有界采集、目标分辨率/Low/内部比例与代表性路线冻结、实际GPU/驻留/工具队列成本。严格平均≥30与现行向上取整的最小放行间隔之间仍需审视实际显示调度/统计定义，不可暗加四舍五入、容差或宣称30设置已经达标。新窗口不证明整帧、实际目标硬件、内存或1h通过。

会话28456（双配置门禁）及35313（Python/CLI）均已结束exit0，无运行中的构建或采样。正式EXE仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**；既有失败、v1拒绝/v2历史及四项IDE改动保留。E5-D仍未完成、未提交推送/部署，E-F/ECS/M8.3及人工/自包含/目标机/1h门禁均未放行。

### 11.35 E5-D 首次展示阶段诊断

上一轮完成高精度等待的接线和定向验证，属于真实进展。本轮针对仍然昂贵的第一次展示补充默认关闭的 `--presentation-stages`，不宣称已经定位JIT、上传或GUI的根因，也不把原始冷帧排除在验收之外。

修改前选定415个源文件、两配置测试二进制和包映射留档至 `out/verification/ecs-e5d/pre-stages-first`，清单SHA **B48BDF1CF6EC83DFFEA4019BB24B47272C460713993E4A65E221100CFCAC421C**；不是完整最终源码冻结。新开发合同 `scripts/ecs-e5d-presentation-stages-development-v1.json` SHA **F1EB005D316423BB0FE3F714D892270A43CA3639A1C524373BB63F3A5DC0A175**，既有循环v1/v2报告及限帧v3合同不改写。

实现边界：

- C# Application层的 `PresentationStageCapture` 不依赖Renderer、Scene或World；没有GPU读取、回调、文件IO或资源所有权。分配固定容量数组，只保留最前64次调用，累计总调用/遗漏数量；导出时复制，外部修改报告不能改写内部历史。
- Editor记录 `BeginPresentation` 与 `Present`，Player记录 `Present`。就绪检查仍在原来的异常处理边界外，不因为打开/关闭诊断改变图形失败状态机；调用中途异常保留 `Completed=false` 的记录。
- 13个顺序阶段：Control、Observer、Surface、GuiBegin、GuiBuild、ViewPreparation、GuiDraw、SceneSubmission、GuiGpu、DiagnosticCapture、NativePresent、Cleanup、Diagnostics。记录进入次数、墙钟ticks和当前线程分配增量；一个阶段可以多次进入，合计必须与调用总量相等。
- 阶段是**宿主CPU侧调用范围**，包括嵌套工作/桥接/观察开销。GuiGpu不是GPU执行时长，SceneSubmission也不是独立的提取或上传计数。进入次数不是原生函数调用数，无操作分支也可能进入阶段。未覆盖调用入口JIT、就绪检查、启动/关闭；用原 `--loop-costs` 的外层范围对照未覆盖边界时间，不将差值直接归因于JIT。
- Sequence唯一；FrameHint可能因挂起/失败/恢复重复，不充当GPU源帧身份。对象由宿主持有，图形恢复不清空历史。Null显式启用时只输出零调用报告，不加载图形模块；未启用不创建收集器，也不输出新字段。
- Player沿用已有有界CreateNew报告，Editor在finally中输出独立JSON行；这仍不是崩溃可靠全程流式存储。前缀满后缺失明确报告，不能据其计算全程百分位或宣布性能通过。

新增26项Application检查涵盖参数/线程/边界、阶段与分配守恒、失败行、重复FrameHint、外部报告修改隔离、首前缀/遗漏及暖态收集器本身256次0 owner分配（不是整个引擎0分配）。展示检查57→66，包含实际CPU-only没有NativePresent阶段、故障行与显式恢复连续性、Editor Play/Reload/Stop后的序列以及既有恢复图像对照；源Editor限帧检查同时改为调用现役平台高精度等待，而非测试自己的通用粗等待实例。Player检查60→61，增加默认字段缺席、显式Null零图形及模拟字节等价。

新Python校验器包含4个测试方法/22个变异负例，检查字段类型、枚举、前缀/遗漏、顺序不重叠、所有13阶段顺序与唯一性、未进入阶段零数据、逐调用时间/分配严格守恒以及拒绝虚假性能标记。它验证记录结构和边界，不证明数值根因或目标机性能。

#### 本轮实际验证

两配置标准 `Build.bat -SkipTests` 均0编译警告，不部署。Release候选 `1e60fa8291d4454e9d68d2983ab27c34`，Debug候选 `282f6ae341054c8c865ae38693f10504`。各8/8定向CTest通过，耗时153.54/159.16秒，覆盖Application、Interop、Presentation、原生Presentation/Renderer、Player、PresentationCosts、GraphicsRecoveryJoint；日志SHA分别 **552D037C7318ADA83A44EB799F5BEFA00AFACAF98C84E3AD3A8AC48EF8E4BED9** / **D22B30BDAFA659802360637AC9CDD9EA4CEB7F0C2740FF078170CE8B35FFFCEB**。详细输出确认26/66/61项新计数。Python完整93/93、8.828秒，日志SHA **07ED1494ED496872A8FA96274504C5A825726A3846B96B703ED1147875B1B292**。这不是全套CTest或最终性能验收。

首次CLI核验辅助脚本在任何子进程或预登记写入前失败：误把发布包Editor DLL与无RID输出比较。现有打包流程使用 `dotnet publish --runtime win-x64`，实际包与对应RID DLL一致。保留原脚本、仅含包映射的first目录及失败日志 **BACF091EAE21A67D3ABD242147D97D9AB1FA5F2DE522B4C6C55410D601197603**；另建second脚本匹配实际RID宿主与Application库，不修改产品、不重构建，也没有先失败后重采样。

实际进程启动前持久化12条命令、1088项输入、10包清单、两个夹具清单及四份运行日志原字节；预登记SHA **B06BD0EFECF81B305B3B41196D78BA06088CB4813DD77B8C2D815F56180FE3B5**。首次12个CLI均为预期退出：两配置各Editor DX11/CPU-only、图形Player、Null正常退出0，以及Editor/Player重复参数拒绝2，共8个正常、4个拒绝。所有输入/夹具和日志旧前缀复核通过，22份阶段报告（源测试14、CLI8）有效，CLI共118条调用。

综合证明 `out/ecs-e5d-stages-cli-verification-second.json` SHA **CAFB81A0B363C02CEA09AE8424AD828B81BBEE2E971DD7A6090291FA962ABD75**。独立离线检查重新哈希1088项输入与51项输出，检查12个回执，并按实际时间戳将118条调用关联到唯一外层循环记录；不使用可能重复的FrameHint作唯一键。输出 `out/ecs-e5d-stages-offline-audit-first.json` SHA **B84946399EC169B54DED686C6860B8EBE2F772627CD28976C28A417FCA89F676**。没有新增采样、删除冷帧或挑选结果。

#### 首次调用定位与下一步

下表仅展示完整保留数据中的首个Present，单位ms，为CPU侧范围，不是GPU时长或物理显示Hitch：

| 入口 | Present范围 | GuiBuild | SceneSubmission | NativePresent | Observer | 外层未覆盖边界 |
| --- | --- | --- | --- | --- | --- | --- |
| Release Editor DX11 | 89.0545 | 43.7356 | 26.4262 | 2.7782 | 11.3751 | 0.4753 |
| Debug Editor DX11 | 87.7412 | 48.3726 | 27.4950 | 2.7117 | 2.3516 | 0.5424 |
| Release Editor CPU-only | 55.0081 | 44.1151 | 0.0003 | 0 | 8.7935 | 0.4657 |
| Debug Editor CPU-only | 54.4047 | 48.2527 | 0.0029 | 0 | 3.5598 | 0.6497 |
| Release Player | 42.3972 | 0 | 36.6726 | 2.4821 | 0.9976 | 0.9353 |
| Debug Player | 52.4424 | 0 | 45.4683 | 2.8438 | 1.3418 | 1.0254 |

Observer包含进入嵌套PresentCore直到下一阶段的范围，可能含嵌套入口JIT；此次CLI未启用旧 `--presentation-costs`，不能把该范围称为GPU计数器开销。CPU-only的SceneSubmission极小记录是分支/阶段记账，不是实际原生场景提交。外层差值包含入口、检查及观测边界，不是已证明的JIT成本。首条数据用于定位，未从队列中剔除或改列为验收豁免。

只读检查现有源码确认：GuiPlugin的AttachRenderer调用ImGui DX11初始化但没有提前CreateDeviceObjects；首次NewFrame在字体采样器不存在时创建设备对象，其中包含D3DCompile着色器编译与字体纹理创建。DPI变化也会使资源失效。**代码路径存在不等于本次全部耗时已归因于它**；目前没有单独记录其中每个实际原生操作的时间。托管GuiBuild包含Workspace/Catalog捕获、界面重建和嵌套首次调用，仍须细分。

下一步设计并验证GUI设备资源的受检准备边界，覆盖启动、恢复、DPI变更、失败返回与资源生命周期，同时保留准备期成本；不能仅将工作移到计时窗口外便宣称优化。随后细分托管GUI构建和场景提取/上传，并补齐全程采集、目标1920×1080/Low/内部比例、代表性路线/统计口径、GPU缺口、驻留/工具队列及完整门禁。

会话82568与56367均已结束exit0，辅助校验也全部结束，没有运行中的构建或采样。正式EXE仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**，上一轮高精度证明/审计原字节不变。四项IDE改动和全部历史失败保留。本轮未提交、推送或部署；E5-D/E-F/ECS/M8.3、内存、人工、自包含、目标机与1h门禁仍未完成。

### 11.36 E5-D GUI 设备对象受检准备与失败回滚

本轮先处理11.35暴露的初始化可靠性边界，不提前移动GUI创建工作、不更改限帧或性能阈值。修改前52个选定源文件、原生二进制和包映射复制到 `out/verification/ecs-e5d/pre-gui-prepare-first`，清单SHA **62A64455FC75D54A771085AA87614E3D47743D6849D536A47BE5F747A228B9D2**；是选定输入留档，不是完整源冻结。

实现内容：

- 给现有vendored ImGui DX11后端加小范围本地补丁：检查两个着色器编译、VS/PS、输入布局、常量缓冲、混合/光栅/深度状态、采样器、字体纹理与视图共12个创建步骤的HRESULT。失败回滚所有部分设备对象；字体纹理创建失败不再断言后继续解引用空指针。另补初始化空设备/上下文和DXGI取得失败的清理。
- GuiPlugin在Begin中显式调用受检准备，再进入ImGui NewFrame；失败返回错误、不开启帧、不写入捕获结果。首次与DPI变化均保持在Begin范围，成功后正常帧不重新创建；原生接口在DPI失败后显式再次调用Begin时，同一比例仍会重试，不以已更新的字体比例冒充设备就绪。托管宿主保留原有图形故障/显式恢复策略，没有新增自动循环重试。
- 使用原有模块诊断读取入口返回 `guiPreparation` v1，没有新增ABI操作或权限。记录GUI句柄、attempts/failures、totalTicks/lastTicks、QPC frequency、ready和明确的scope/performanceAccepted=false。这是单个GUI生命周期内CreateDeviceObjects的CPU墙钟聚合（含失败回滚，不含CPU字体图集），不是全程流、GPU耗时、内存驻留或设备健康保证。仅创建时增加两次QPC读取，诊断文本仅显式读取时构造；不声称默认路径绝对零开销。
- 故障注入是测试翻译单元编译期的表达式替换，生产构建直接执行真实HRESULT表达式，没有运行时开关或导出故障接口。测试使用实际产品Renderer创建的DX11设备，依次失败12个步骤，检查全部资源字段与字体TexID清空、捕获输出不变、帧未开启、同DPI重试成功、暖帧不创建、DPI失效、3次新GUI生命周期与失败后销毁释放借用。它不是操作系统真实OOM/设备移除故障注入的完整覆盖。
- 实际托管Editor测试从未注入故障的产品GUI DLL读取诊断，核对启动没有隐藏创建、DX11只准备一次/CPU-only零次、图形恢复更换GUI身份并重新准备一次，保留既有恢复前后图像比较。该比较不是旧版本与新版本的跨版本图像证明。

首次构建因新测试比较有符号/无符号告警失败；第二次因故障表达式分支暴露编译器对旧未初始化shader blob变量的告警失败。均已修复，原始日志保留，未降低/WX。第三次原生与相关托管构建通过，但NuGet漏洞数据源连接失败导致NU1900中断；保留日志，不关闭漏洞检查。第四次使用同一构建流程及网络权限重试。最终验证结果在本节后续追加；未在运行中预判通过。

#### 最终定向验证与边界

第四次标准构建两配置成功、0编译警告；Release候选 `26726db362044a799ecfdb7f726ece6b`，Debug候选 `700b5a1d15c94b8c8c14258dea17af5a`。各9/9定向CTest通过，156.49/164.67秒；在上一轮8项基础上增加实际DLL卸载重载检查。测试日志SHA分别 **81DDBE986C899F68CAAC908FF5A3929A3BB2693BC5964F8F4930BF93DCF44E63** / **5F71AB0ACA6F0E988856B011851735F23604E63BC1DFC0BEE40AD548EF81C18A**。实际展示检查66→72；两个配置均保留12步故障/重试的全部24行时间与严格聚合校验。Python93/93，8.786秒，日志SHA **253194598FF2E26A0ABD8206290A2DCA8F40A2B58A92028058D08987BF4EB618**。

只读产物核验检查10个候选包全部清单文件及其配置/路径/大小/哈希，Editor包中GUI DLL与本轮原生构建产物一致；绑定CTest命令、实际测试程序和依赖、原始阶段与准备报告、改动源码、52份修改前副本和三个失败日志。共1282个文件绑定、6份生命周期报告；逐调用阶段仍通过原严格校验，准备总耗时位于已记录GuiBegin范围内，CPU-only没有虚构DX11准备。证明 `out/ecs-e5d-gui-prepare-verification-first.json` SHA **7F09CD42CBC457C56D137E4FA80669528B8756C522C260CE5D81BD376AEEFB99**。这是功能测试后的产物核验，不是完整正式冻结、完整CTest、全程测量或新增CLI性能队列。

源宿主功能测试中，每个新GUI对象的首次设备准备CPU墙钟如下；窗口保持全部数据，没有剔除初始化：

| 配置 | Editor短窗首次准备ms | 独立恢复测试的初次准备ms | 恢复后的准备ms | CPU-only尝试数 |
| --- | --- | --- | --- | --- |
| Release | 30.3132 | 27.0868 | 10.6203 | 0 |
| Debug | 32.2626 | 26.0429 | 10.7343 | 0 |

这些是在功能测试进程内顺序运行的不同GUI实例，**不是三个配对收益样本、独立冷进程队列或实际目标路线**。它们证明受检设备对象准备是一个可观的实际CPU侧范围，尚不能区分其中着色器编译、驱动创建及字体纹理上传的占比；较小的恢复时间不能被解释为本轮优化收益。本轮优先交付错误检查/回滚和可核查边界，没有宣称降低首帧或达到FPS目标。

下一步细分该受检路径的着色器编译/设备对象/字体纹理创建成本，再选择有证据支持的优化；同时继续托管GUI Build及场景提取/上传细分。不得只提前创建、删冷帧、重采样挑选或改变阈值来满足验收。仍需完整全程采集、目标配置与路线/统计口径、GPU缺口、驻留/队列、完整新冻结/回归/正式性能队列及人工/目标机/自包含/1h门禁。

会话53138结束exit1（NuGet）；90058与39038均结束exit0，无运行中的构建或测试。正式EXE仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**；旧分段证明 **CAFB81A0…** / 离线审计 **B8494639…**原字节不变，但不拿旧源码结果冒充新候选性能。四项IDE改动未处理，全部失败证据保留。未提交推送/部署；E5-D/E-F/ECS/M8.3与内存验收仍未完成。

### 11.37 E5-D 单色字体图集 R8 候选

上一轮有真实修复和验证进展。本轮先使用单独的有界原生诊断探针拆分已检查的12个HRESULT步骤：3个进程，每个进程各运行中文/默认拉丁字体的首次准备、150% DPI重建和GUI重建，固定18条记录/216个步骤，不进行场景绘制/Present、不丢预热、不自动重试。使用当前实际Platform/Renderer DLL、编辑器字体 `C:/Windows/Fonts/msyh.ttc`、同一GuiPlugin与ImGui源码；探针是单独编译的诊断程序，不冒充产品CLI或正式同驱动基准。

运行前持久化29项选定输入、执行顺序和边界。完整核验 `out/verification/ecs-e5d/gui-steps-probe-first/verification.json` SHA **51EF702C1EBDFAB3E27EDD47F38D35015BA6105C7D4B521E9D39820EB7701F5A**。三个进程中文首次准备约29.62–31.84ms，字体纹理/视图约24.69–26.87ms，VS/PS编译约4.35–5.17ms；CPU字体创建另有约31.08–33.02ms，DPI的CPU重建也没有隐藏。这把优化优先级从只考虑编译调整为优先检查字体上传，不能由此宣称所有长帧根因已穷尽。

先做一次单独的不可变纹理用法试验：预登记3对AB/BA/AB、6进程/36条记录，唯一源码开关是字体纹理的DEFAULT→IMMUTABLE，产品不修改。三个中文首次准备均未观察到下降，**不合入该方案**。全部36行/432步骤、原始退出回执及旧源码精确副本保留；证明 `out/verification/ecs-e5d/gui-usage-pairs-first/verification.json` SHA **BCA642405CE7AE421523790E9ED6CAC675F10A129F53B4C13074C898842D90B1**。后续候选改变的3个原路径按预先复制的精确字节核验，不重写旧输入哈希。这是诊断淘汰依据，不是统计验收或重新采样找绿。

据此实现R8候选：

- 单色字体直接使用Alpha8覆盖率数据，DX11纹理/SRV均为R8_UNORM；输入行距由4字节/像素改为1字节/像素。同尺寸的上传像素载荷减少75%，不等同于驱动分配/驻留或总帧内存减少75%。
- 独立字体像素着色器重建白色RGB与覆盖率Alpha；普通图片继续原RGBA着色器。绘制按字体纹理身份切换，避免同类连续批次重复绑定；处理现有内部回调与ResetRenderState，保留结束时的原PS状态恢复，不新增脚本/AI任意GPU回调入口。
- `TexPixelsUseColors` 为真的图集保留RGBA与原着色器，不能丢弃彩色字体/自定义图集的RGB。GuiPlugin常规TTF与DPI路径不再额外生成RGBA32 CPU副本。
- 新字体着色器编译/创建同样检查并回滚，故障步骤从12扩展为14；诊断补充atlasWidth/atlasHeight/atlasUploadBytes，仍是明确范围的开发数据，不是内存验收。
- 新实际GPU测试以RGBA参考路径进行逐字节图像比较，覆盖默认拉丁/中文字体×100/150/200%共6组、文字与RGBA图片交替、透明度、Reset及原着色器恢复；额外用真实有色图集数据检查RGBA回退。同步/读回仅在测试中执行，产品不新增GPU等待。

首次R8构建成功，但9项回归中4项失败：纹理格式改为R8后，SRV仍错误声明RGBA8，受检创建因此拒绝。已改为从纹理描述继承格式，保留 `*-atlasfirst.log`、失败候选 `b5173bad8b5f4895bab93a070e1a801c` 及全部测试输出；未放宽校验。修正后的第二轮双配置构建/回归及固定新候选诊断结果另行追加，未预判通过。

第二轮修正SRV后，两配置各9/9通过（155.56/158.77秒），但复查发现原彩色测试同时保留Alpha8，未覆盖合法的RGBA-only图集；先调用GetAlpha8会触发ImGui重建并可能丢弃自定义RGB。补丁现在仅在两个像素缓冲都缺失时构建，已有RGBA-only数据直接保持RGBA，绝不通过请求Alpha8重建。测试明确释放Alpha8、保留有色RGBA后验证实际GPU颜色。第二轮会话71084已结束后才修改源码；最终候选必须重新跑第三轮双配置门禁，不把第二轮结果作为最终源码证明。期间尚未启动R8成本比较，没有测完之后修改候选再复用性能结果。

#### 最终候选验证

第三轮两配置标准构建0编译警告：Release候选 `286ee4602d8048aa804e3ed4f7063d9b`，Debug候选 `6b8bff1e4c044cb482d43ccc38dbf0e5`。各9/9定向CTest，155.54/160.51秒；日志SHA **18D388A05E901768E5901CBF99931B90EF20B82F45117471DE36DECE217DCAA6** / **A6BF8F363677D82971626E4F280D828A7F1DA392F50B82F1934867FF5E6BA0B7**。每个配置都包含14步故障/清理/显式重试、6组逐字节GPU比较、RGBA-only有色回退以及实际产品图形恢复。展示检查72→74，核对实际Editor单色图集的1字节/像素载荷与CPU-only零上传。Python93/93，8.719秒，日志SHA **199AD4BC9B3F5218567EBCBB0927A04BEC28F3CA4EC121E6FE0F4A944D639C8F**。

实际产物核验绑定1288项文件/记录、10个候选包、6份产品生命周期报告及原始阶段报告，确认当前GUI DLL与包一致、初始化仍在GuiBegin范围、旧失败保留、正式EXE未改变。证明 `out/ecs-e5d-gui-r8-verification-first.json` SHA **C9FF17C75A324D09BAB8B01CF0D8227CAD2E07CB894475472C9785DF492A9744**。这是定向功能/产物验证，不是全套CTest、正式性能冻结或内存验收。

两配置全部终止后，才启动R8固定诊断对比：旧RGBA基线使用已保存的原可执行文件，新候选使用当前受检源码，Platform/Renderer/GLFW字节相同。预先落盘40项输入与3对AB/BA/AB顺序，6进程×6个GUI案例=36条记录，共468个实际创建步骤；候选额外包含字体着色器的13/14步骤，不能将这部分成本隐藏。记录全部保留，没有挑选/剔除/追加采样。验证 `out/verification/ecs-e5d/gui-r8-pairs-first/verification.json` SHA **E033C3616738F07EBEDF9808C29F59F79DBBDAAC22728500834D0954F14C9A89**。

| 案例 | 3对RGBA准备ms | 对应R8准备ms | 解释 |
| --- | --- | --- | --- |
| 中文首次 | 33.5546 / 35.2934 / 33.4781 | 13.8416 / 14.1393 / 13.0330 | 本窗口3对均降低；原8MiB载荷→2MiB |
| 中文DPI | 6.7101 / 21.0628 / 7.3758 | 19.5413 / 16.8171 / 20.7277 | 不一致，不能宣称重建改善 |
| 中文重建 | 17.5041 / 4.1739 / 18.9596 | 12.6236 / 12.5404 / 14.6557 | 不一致，不能宣称重建改善 |
| 拉丁首次 | 5.0798 / 4.7417 / 5.0127 | 6.8396 / 6.7950 / 7.3265 | 小图集准备更慢，风险保留 |

精确数值以完整JSON的18行对比和36行原始记录为准，未列的拉丁DPI/重建行同样保留。新增字体着色器编译/创建约1.25–2.00ms，对小图集收益不利；CPU字体创建也未证明加快。以上是无实际绘制/Present及GPU完成等待的诊断流程，后续行可能包含前序上传尚未完成的影响，**不能把差异归因为已证明的驱动问题/噪声，也不能冒充真实帧间DPI恢复测量**。

当前判断：R8候选的功能/颜色等价和同尺寸单色图集上传载荷缩减已验证，中文首次准备有局部改善证据；**不验收通用性能，不清除小图集与重建风险**。下一步优先消除新增运行时字体着色器编译成本（评估受检构建期字节码），再验证实际呈现后的DPI/恢复与暖态图片/文字交替批次吞吐；这也覆盖新增PS切换可能带来的热路径成本。原候选原始数据不改判，新源码需重新相关构建/回归与新预登记证据。

会话24965终止exit1（首轮SRV错误），71084/76915终止exit0（第二/最终门禁），45052终止exit0（Python/产物核验）；所有诊断进程终止，没有运行中的任务。正式EXE仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**。未提交/推送/部署；E5-D及全程采集、目标机/路线/统计口径、完整回归/正式队列、GPU缺口、驻留/队列、内存/人工/自包含/1h、E-F/ECS/M8.3仍未完成。

### 11.38 E5-D GUI 固定着色器构建期编译

接续11.37中小图集和重建风险，本片将GUI的三个固定着色器（顶点、普通RGBA图片、R8字体）从设备准备函数移到构建期编译。原始R8结果不改判、不重采样；`pre-gui-bytecode-first` 保留改动前后端与两个原生测试源文件副本，原R8产物证明保持不变。

- 新增仅构建主机运行的 `NcmaGuiShaderCompiler`，使用原 `main` 入口、`vs_4_0` / `ps_4_0`、编译flags=0，不更换优化级别或允许源码include。三个有界HLSL输入必须全部编译成功且无诊断后，才写生成头文件；编译/读写失败返回非零，构建失败，不回退旧产物。
- CMake显式依赖编译工具及三个HLSL文件；生成头文件嵌入NcmaGui和白盒测试，不部署HLSL、缓存文件或构建工具。产品不新增运行时读取路径或AI权限。
- 运行时编译步骤1/5/13真正移除，保留原始设备步骤编号2/3/4/6/7/8/9/10/11/12/14。11个实际HRESULT仍逐个检查，失败清理不变；资源创建仍在GuiBegin内计时，未将运行成本藏到预热窗口。
- 新 `NcmaGuiShaderBuildTests` 检查同输入生成字节一致、三个输入分别语法错误时不覆盖先前有效输出、空文件不生成产物，以及无效输出位置拒绝。测试每次保留独立夹具和执行日志，不清除失败证据。
- 原生GPU测试额外保留独立的旧运行时编译表达式作为测试专用参考：中文/默认拉丁×100/150/200%×R8/RGBA共12组逐字节比较，包含普通图片、字体、透明度、Reset及原PS状态恢复。原6组R8↔RGBA比较和彩色RGBA-only回退继续保留。运行时参考编译只存在于测试中。

本片先进行双配置标准候选构建、每配置10项定向CTest，再做架构检查、Python测试与实际候选包/import表核验。以下追加实际结果；不把代码中删除编译调用直接换算成已证明的启动、DPI重建或帧率收益。真实Present分隔的重建及暖态混合批次测量仍待开展。

后续正式新冻结必须显式覆盖新增 `tools/gui-shaders`、三个HLSL、修改后的 `engine/sdk/imgui/backends/imgui_impl_dx11.cpp`、生成头文件/编译器产物及实际驱动闭包；旧E5-C冻结脚本的路径白名单不包含这些新范围，不能直接沿用并宣称完整。本片实际产物审计会绑定上述输入/生成结果，但不冒充正式完整冻结；旧冻结和失败归档不改写。

#### 实际验证结果

标准 `Build.bat -Configuration <Release|Debug> -SkipTests` 两配置成功、0编译警告；Release候选 `5beb901e59614ddda83f61e2e8f6a39a`，Debug候选 `ed398304ccaf4d58befd828d392d2045`。每配置10/10定向CTest通过，分别156.79/163.21秒，包含生成器正负例、原生Renderer/Gui、图形重载、展示成本、联合恢复、Interop、Application和Player。两配置各额外1/1架构检查通过。新原生故障路径为11次失败+11次显式重试，累计计时与22次实际准备回执一致；被删除的编译步骤不是伪造成功的占位项。新增12组旧运行时编译参考比较与原6组R8/RGBA比较均通过，实际产品展示检查仍74项。

- Release定向日志SHA：**8A41896518CCD6E8D9D2A8C53D95B67DF3A54FE6896D61DCDBDD2D3A4541F232**。
- Debug定向日志SHA：**2E931B657DA71CAE2C12CA04A809CE82BBE4FB36FCDC6925BFF68CF8AC40E9E0**。
- Python最终93/93，8.778秒，日志 `out/ecs-e5d-gui-bytecode-python-second.log` SHA **C618C60C301A6A37CE3872CA2DC5B135E9A7830ABBDADE5C81689C76988AC36D**。
- 两配置生成头文件字节一致，SHA **4E20BC1DC47AA2FC9ADFFB50610360844DB0D9E5438EC4E3EAEA7BAF368D9D60**。实际DLL导入表确认NcmaGui无D3DCompile/d3dcompiler直接依赖；Renderer本身的编译功能未移除，不能宣称整个引擎不再依赖编译器DLL。

首个补充包装器使用旧 `powershell.exe`，在Python第一条正常unittest stderr进度处因 `NativeCommandError` 提前终止，非断言失败，未完成的首份日志为0字节。初次受限CIM查询被拒绝；经批准只读复核没有遗留Python进程，未终止任何进程。保留首个脚本、空日志和 `gui-bytecode-wrapper-failure-first.txt`，用当前pwsh仅重新运行Python补充套件，再执行产物审计；未重跑已通过的整套候选构建/图形门禁来掩盖错误。原生创建/构建/性能阈值均未放宽。

最终 `out/ecs-e5d-gui-bytecode-verification-first.json` SHA **D9FBA4514057DE760484A9DDB5A5580F3D0333CC6EDB37999691B87485102D97** 绑定1323项文件、10个实际候选包、6份产品生命周期报告、新工具/输入/生成头/导入表/测试夹具及旧证明。确认包内没有部署该构建工具/HLSL/生成头、GUI DLL与实际构建一致，准备仍计入GuiBegin，原R8证明未改、正式EXE仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**。审核明确 `performanceAccepted=false`、`fullRegressionPassed=false`；不是正式完整冻结或全套CTest。

91870（双配置门禁）和44774（Python/最终审计）均已终止exit0，没有运行中的构建/采样。**本片未新增性能队列，也没有将旧R8的小图集/DPI风险改判为通过。** 下一片先冻结新的有界开发测量定义，在真实提交/Present分隔条件下观察首帧、DPI和GUI重建，并覆盖暖态字体/图片交替批次的额外PS绑定开销；保留冷帧、全部原始结果，区分CPU、排队和GPU完成，不把Present返回当成GPU完成或目标机30fps证明。其后仍需全程/目标路线和统计定义、完整回归/正式冻结队列、GPU缺口、驻留/队列、内存/人工/自包含/1h等门禁。E5-D、E-F/ECS/M8.3未完成；未提交/推送/部署。

### 11.39 E5-D 有实际提交/Present 的 GUI 开发对照与单 PS 原型

上一轮完成产品构建期编译及定向回归。本轮先复核同一产品源码/产物证明，再建立**独立、非产品CLI、非目标机验收**的原生开发探针；本轮未修改产品源码或重新部署。两套不同候选实验分别预登记顺序和输入，在所有构建结束后采样，既没有重跑旧无Present队列找绿，也没有将新原型结果回填为旧候选收益。

#### 固定测量范围

- 使用实际Platform/Renderer DLL及同一GuiPlugin/ImGui代码；640×480、vsync=0、无30fps限帧。短暂显示不抢焦点的测试窗口，并检查未最小化、窗口和帧缓冲尺寸不变。**不控制桌面遮挡/扫描输出，不把Present成功返回或计数当成物理显示帧率。**
- 每进程中文/默认拉丁两个字体组；每组首次准备、1.5倍字体栅格重建、GUI销毁重建三个阶段。1.5倍是向现有接口输入的可控栅格比例，**不是跨显示器/真实OS DPI事件验收**。
- 每个生命周期阶段保留首次Create/Begin/设备准备CPU时间及首次实际图像，然后32帧真实submit/RenderGpu/Present，再调用有界GPU排空。首次图像读回也会同步GPU，因此这不是未经干预的完整Editor帧流；后续生命周期不存在“前序上传未排空却归因于重建”的混淆。
- 暖态三个模式：32段文字；32段文字与32张普通RGBA小图交替；相同不重叠内容按文字/图片分组。模式间先留一个图像验证帧并排空，预创建查询，再连续记录128帧；全部128帧保留，不挑选或丢弃慢帧。批末有界排空后读取timestamp/disjoint，GUI CPU、含Present的探针帧CPU、Present CPU及GUI-only GPU分别记录。
- GUI-only GPU区间只包围RenderGpu，不含场景、完整帧或物理显示；查询、槽状态哨兵（第二套）都是诊断开销。分组模式只用于不重叠夹具的对照，**不授权产品任意改变2D绘制顺序**。
- 每套先固定3进程冒烟，再固定3轮×3变体，位置循环轮换；第2轮拉丁字体先执行，其余中文先执行。每完整进程972次submit/Present、6个生命周期记录、768条暖态记录、12张RGBA读回。没有重试/追加队列/择优选样。

#### 第一套：确认编译移出与双 PS 混合成本

变体为已归档R8运行时编译后端、当前构建期编译R8后端，以及**当前构建期后端仅强制RGBA图集的实验对照**（不是历史产品）。三个变体使用同一个驱动和依赖。原始运行时后端精确SHA仍为D91CEF…。

冒烟36个暖态样本/36张图像通过；完整队列9进程、6,912个暖态CPU/GPU样本、108张图像全部完成。所有对应图像逐字节相同，GPU样本全部有效，Renderer验证错误/警告均0，提交/Present计数与实际执行帧一致。

- 完整证明：`out/verification/ecs-e5d/gui-present-probe-first/full-verification.json`，SHA **44B3C3244AB595845044A80C090C98D1DA2208B6534C12EEC98637610D80B774**。
- 冒烟证明：**D2A3F8EF2B5C575F829EA5DB8CC16D2576CE547CD8FF89E8CC38D0137E8F7074**。
- 当前构建期R8中文首次设备准备在3轮为约7.73 / 5.99 / 7.54ms（精确值以JSON为准），运行时编译为约13.84 / 10.74 / 12.21ms；默认拉丁设备准备约0.20–0.64ms，而运行时编译约4.47–6.87ms。Create+Begin也单独保留，不能把CPU中文字体栅格创建约数十毫秒从总启动成本中删掉。本文CPU指标均为CPU侧墙钟区间，可能包含阻塞，不是线程CPU占用时间。
- 双PS R8的交替批次GUI GPU中位数相对RGBA对照：中文3轮比率约1.29 / 1.25 / 1.28，拉丁约1.13 / 1.13 / 1.14。**这确认了该负载中的局部GPU回退，不能以冷准备更快掩盖。** CPU结果有波动，全部比较行保留，未宣称通用无回退。

首次探针构建因缺少GLFW声明失败，保留 `Probe-build-failed-first.cpp` / `build-runtime-first.log` 后补充正确头文件，三变体第二次构建0警告成功。运行包装器首次解析缺少闭合括号，在任何注册/采样之前失败，保留 `run-parse-failed-first.ps1`，修正并解析检查后才启动冒烟。没有失败测量被删除或伪装成成功数据。

#### 第二套：单一 PS + 不可变实例标记原型

针对上述回退，在 `out/verification/ecs-e5d/gui-unified-probe-first` 单独构建原型，**尚未合入产品**：

- 保持R8图集、顶点/索引内容与绘制顺序不变，以一个不可变8字节顶点输入缓冲 `[0,1]` 存放RGBA/R8模式。
- 每批次使用一个实例的DrawIndexedInstanced，StartInstanceLocation选择模式；顶点着色器通过额外的per-instance输入读取标记，并以nointerpolation字段传给统一PS。没有每批Map常量缓冲、没有修改或复制共享顶点来塞标记。
- 不把SV_InstanceID误当可随StartInstanceLocation任意设置的标记：规范中该系统计数从0开始。这里依赖的是**per-instance缓冲取址偏移**，不是实例ID值。设计依据：[微软DrawIndexedInstanced文档](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-drawindexedinstanced)、[Direct3D 11.3规范8.18](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm)。
- 统一PS按标记重建R8的白色RGB/覆盖率或保留RGBA。删除原型中的独立字体PS对象，新模式缓冲的HRESULT仍受检、失败回滚释放。
- 新增使用的IA顶点缓冲槽1与槽0一并保存/恢复；新驱动对**所有变体、每一帧**绑定外部槽1哨兵并检查绘制后指针/stride/offset原样恢复。该测试开销不藏到单个变体外部。

新队列使用同一新驱动，对照当前双PS构建期R8与RGBA实验路径；不是将第一套不同驱动数据与新数据混池。冒烟36个暖态样本/36张图像通过后，另行预登记完整9进程、6,912条暖态记录/108张图像。所有图像既与本轮对照相同，也与第一套原始读回完全相同；槽1每帧恢复、GPU查询全有效、验证错误/警告0。

| 交替批次GUI-only GPU中位数 | 双PS R8（3轮，微秒） | 单PS原型（3轮，微秒） | 原型/双PS比率 |
| --- | --- | --- | --- |
| 中文 | 5.792 / 5.760 / 5.792 | 4.480 / 4.480 / 4.512 | 0.7735 / 0.7778 / 0.7790 |
| 拉丁 | 5.792 / 5.856 / 5.792 | 5.088 / 5.120 / 5.120 | 0.8785 / 0.8743 / 0.8840 |

单PS对RGBA交替对照GPU比率中文0.9929/0.9929/1.0000、拉丁0.9876/0.9816/1.0000。该夹具中观察到混合GPU回退消失，但绝对节省只有约0.67–1.31微秒，**不是整帧/FPS提升证明**。原型同时改变输入布局、实例化绘制和着色器，不能将所有变化归因为单独一个API调用。

CPU未证明普遍无回退：例如第1轮拉丁交替CPU P95比率1.4618，第1轮拉丁纯文字中位数1.3765；其余轮/模式存在下降或不一致。全部54组进程统计、72行比较都保存，不能只摘GPU获益行，不能武断声称这些CPU差异已证明为噪声。首帧/重建也不作无回退声明；中文Create+Begin仍有数十毫秒成本。

- 完整证明：`out/verification/ecs-e5d/gui-unified-probe-first/full-verification.json`，SHA **6B9A284EFB7AFEDD4A7BE13B2D9DADC0B17D676CC531B321D83BD23023C62B9A**。
- 冒烟证明：**C8A72F0EDA4C9930178035186838DA71ECEE30C94EA83B8F1BE24C07F56316AF**。

#### 持久化与下一步

两套合计18个完整进程、13,824条暖态记录、216张完整队列图像；额外6个冒烟进程/72条记录/72张图像独立保留，不混入完整统计。硬件记录为DXGI vendor4318/device11524、featureLevel45312、专用显存8275361792字节；**未由此认定为用户指定目标机**。本机小窗口、合成负载及边界排空均不足以验收1920×1080/Low/30fps或实际游戏路线。

修改任何当前产品来源之前，已重新逐字节哈希328项唯一输入/输出，并把两套队列的71项选定输入复制到独立归档：`out/verification/ecs-e5d/gui-present-inputs-first/manifest.json` SHA **3418E85613D968B96CB67D80A535625CECDAF7234C8E272C768CBD5E9D4FEA65**。后续原路径改变时，只能通过这些已验证精确副本核验历史输入，不能重写旧哈希或忽略不一致。

仓库内 [ECS_E5_D_GUI_DEVELOPMENT_EVIDENCE.json](ECS_E5_D_GUI_DEVELOPMENT_EVIDENCE.json) 保存两套所有进程统计及144行比较，SHA **1E2AC0EE818083E88B91E0F8EDA1DD876CFE884598DBB48DEB13805DB820372E**。生成汇总时一次工具输出截断导致解析失败，未写入不完整文档；改为分块读取后生成，并以独立Python校验与原始两份证明逐值相等、71归档副本哈希一致。`out/ecs-e5d-gui-present-summary-verification-first.json` 保留该核验结果。

下一步将单PS作为**候选实现**接入受检构建路径，补齐新缓冲创建失败/回滚、双槽恢复、RGBA-only彩色回退、Reset/回调、顶点/索引偏移及独立旧着色器参考，重新双配置相关功能门禁。再按新产品字节与新冻结定义进行必要的CPU/整帧验证，不直接拿本原型的GPU获益验收产品。CPU差异、中文字体创建、GUI高层构建/场景提交及全程采集仍需关注，不能持续只优化微秒级GPU差异而忽略整帧预算。

23158（第一套修复后编译）/61182（原型编译）均已终止exit0；两套冒烟、两套完整队列和归档/汇总校验都已结束，没有运行中的工作。正式EXE仍C50742AD…，当前产品源码仍是11.38候选；未提交/推送/部署，E5-D/E-F/ECS/M8.3及内存/人工/自包含/1h/完整冻结回归/目标机等门禁未关闭。

### 11.40 E5-D 单 PS 产品候选与边界回归

本片把11.39的单PS原型接入实际GUI候选，但**不把原型性能数据改记为产品验收**。修改前归档21项源码/测试/两配置工具和产物：`out/verification/ecs-e5d/pre-unified-product-first/manifest.json`，SHA **39D9869A8BC085A0FBA787A0107D8EFF57BEFACC0C32E06C6F874E967C55FED5**。前片71项测量输入副本、所有原始队列/失败和历史汇总保持原样。

实现边界：

- 产品保留一个构建期VS和一个统一PS，删除独立 `GuiFont.hlsl`、字体PS成员及其创建/释放路径；旧文件可从上述精确归档恢复，未保留产品兼容入口。
- 不可变8字节模式缓冲作为IA槽1的per-instance输入，每次一个实例，StartInstanceLocation选择RGBA/R8。顶点/索引内容、绘制顺序和用户图像颜色语义不变。
- 模式缓冲HRESULT为步骤15，独立字体PS步骤14退役；实际受检顺序为2/3/4/15/6/7/8/9/10/11/12。仍在GuiBegin内创建，失败释放部分资源，不转移计时成本。
- 保存并恢复IA槽0/1的缓冲引用、stride和offset；Reset回调重新绑定模式流。没有每批动态Map模式数据。
- 着色器生成器只输出两个blob，负例覆盖两份源码语法失败不覆盖旧输出、空文件不产出、错误输出路径失败；运行时没有编译器回退。

独立测试参考保留旧VS、旧image/font PS表达式和旧DrawIndexed调用，仅通过测试编译期接缝使用，不进入产品DLL。测试覆盖拉丁/中文×100/150/200%栅格比例、R8/RGBA和彩色RGBA-only内容、混合图文、故意破坏PS/模式流后Reset、两个顶点缓冲槽（含槽1为空）及精确stride/offset恢复、超过64K顶点的非零VtxOffset/IdxOffset和多个绘制列表。共18组新旧路径实际GPU逐像素比较，另保留6组R8/RGBA比较。资源失败测试覆盖11处失败与11次显式重试、相同DPI重试、暖帧不创建、GUI重建及失败时销毁。

验证结果在本节下方按实际终态记录；以上实现范围不代表完整E5-D、内存或目标机性能通过。

本片最终验证：

| 配置/检查 | 实际结果 |
| --- | --- |
| Release标准候选构建 | 0警告；候选 `18670f480fc2493e83e26335500f0f70` |
| Debug标准候选构建 | 0警告；候选 `2d05507fa0be49aa87bb32d8c48ee5df` |
| Release定向CTest | 11/11，157.97秒 |
| Debug定向CTest | 11/11，161.43秒 |
| Python | 93/93，8.834秒 |
| 生成字节码 | 两配置逐字节相同，SHA **E136B32C9406FF6942638C0D36FD2D3B752BD545B3E313C8541383877ECDE489** |
| 原生回归 | 每配置18组独立旧路径GPU比较、6组R8/RGBA比较、11处创建失败/重试及精确计时和验证通过 |

11项包括着色器生成、原生渲染/GUI、GraphicsReload、架构、PresentationCosts、GraphicsRecoveryJoint、Presentation、Interop、Application和Player。两份日志的PresentationCosts均74检查，原模拟0/1/8/32角色组合103检查等仍通过。这是**相关定向回归，不是完整CTest/最终同源门禁**。

最终独立审计 `out/ecs-e5d-gui-unified-product-verification-first.json`，SHA **60C5DB6D8021E0F46CD103BF06AB595247E4CB45965705D1080306780DCE592E**，记录1695项文件哈希、10个实际候选包、6份GUI生命周期/阶段报告。验证实际候选GUI DLL与测试产物相同、清单逐文件一致、构建工具/HLSL不入包、GUI DLL不导入运行时D3DCompile/d3dcompiler、准备成本仍在GuiBegin内。按修改前精确副本解析历史输入，重新哈希两套原型测量的328项唯一输入/输出，没有重写旧哈希或将产品字节冒充旧输入。21项产品前态和71项测量输入副本、原汇总证明均未变。

本片构建、相关功能测试和最终审计均一次通过。准备审计脚本时，一次纯语法检查因Python默认GBK读取UTF-8失败，显式指定UTF-8后解析通过；这不是产品/测量失败，未改变任何测试断言。一次文档补丁因上下文不匹配未应用，核对原文后重新补丁；测试产物未因此改变。

10604（双配置门禁）/54713（Python与审计）均已终止exit0，没有运行中的构建或采样。正式 `out/bin/NcmaEngine.exe` 仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**。未提交/推送/部署；四项用户IDE改动未触碰。

下一步优先实现实际Editor/Player整帧**全程、有界、显式报告丢失/失败**的观测：现有LoopCostCapture只保留512次前缀，不能用于长路线的平均FPS/1% Low/Hitch验收。观察自身开销不能隐藏；保留启动/加载/恢复慢帧，Present返回不等于显示完成。同时继续CPU字体创建、托管GUI构建/场景提取与上传等较大成本，CPU无回退仍未证明。本片没有新增产品性能队列，11.39原型数据仍是原型数据；随后还需路线/统计口径、包含tools/HLSL/SDK的完整新冻结、双配置完整回归/正式领域队列，以及目标机、驻留/内存、人工、自包含、1h等门禁。不进入E/F或M8.3。

### 11.41 E5-D 全程循环记录候选

本片新增默认关闭的 `--loop-stream <new-file.ncloop>`，同时启用原循环前缀诊断。Editor和Player使用相同C#服务；Python仅负责独立离线校验，没有新增MCP文件写入、Agent授权、游戏Python脚本或性能策略。父目录必须已存在、扩展名必须正确，拒绝已存在文件/目录与检测到的reparse父路径，以CreateNew避免覆盖证据。它是可信本地宿主输出，不声称提供对抗并发目录替换的安全沙箱。

记录与失败边界：

- 固定4096项值类型队列，最多1,000,000次循环记录、1GiB文件预算。发布线程只进行有界队列操作；短队列锁不包含磁盘IO，序列化/文件写入在专用后台线程。
- 不用环形覆盖伪装全程：队列满丢弃新样本并累计QueueDrops；记录上限及写入失败分别累计BudgetDrops/FailureDrops，已入队但未成功写出的项保留Unwritten。序号不压缩，故障/取消/恢复原有Outcome和跨帧Present时间间隔不清洗。
- 文件依次包含版本化header、逐次sample和sealed尾记录。sealed只是逻辑队列排空，不表示Flush/Close成功；必须同时检查宿主最终报告。写入/关闭失败或5秒关闭等待超时均不得标记完整覆盖；超时标记不可因稍后写完而改判，禁止在挂起IO期间强制释放流。
- Written是后台成功提交给缓冲写入器的记录数；在未成功关闭或失败状态下不代表磁盘持久化。WriterWallTicks覆盖显式测量的序列化/写入/Flush/底层Close区段，不含队列空闲等待，不是线程CPU占用或全部观测开销证明；是否影响游戏性能仍须另作有/无观测对照，不能从这些计数直接宣称零开销。
- 原前512项报告的OmittedIterations/CompleteRawCoverage语义不变；新增Stream报告单独描述全程结果。正常宿主若全程记录不完整，返回9；游戏/图形/取消已有错误码优先，不能被诊断成功掩盖。
- 发布耗时/发布线程分配、写入线程活动墙钟/分配和字节数显式报告。发布发生在样本End之后，会影响后续Present间隔；后台开销与主线程重叠，不能直接相加成CPU耗时。JSON序列化/IO/GC会扰动运行；默认关闭时不创建线程或文件。
- 时间范围仍为owner-loop，**不包含进程启动/最终关闭**，不是完整进程生命周期、GPU执行或显示扫描输出。没有自动FPS/1% Low验收，不能拿此接线功能测试代替代表性游戏路线或目标机。

独立Python校验器逐行有界读取（单行≤8192字节），拒绝截断、重复字段、未知结构、乱序/缺失、错误阶段分区、Present/限帧矛盾、虚假完整覆盖以及前缀/全程/尾记录/宿主不一致。它检查512条以后的实际后缀，不通过扩充旧前缀的容量来冒充新证据。

改动前240项选定旧证明可变输入/源码已精确归档，`out/verification/ecs-e5d/pre-loop-stream-first/manifest.json` SHA **5153FD5537DA92C7F2D40E876D063A9DDAA79F15ADBABD898623ECC8CEC05347**，不是整仓冻结。首轮Player新增测试在运行循环前因未规范化路径与规范化路径字符串比较而失败，保留 `out/ecs-e5d-loop-stream-player-first.log`；改用同一FullPath规则后定向Player62项通过，未放宽产品IO/采集断言。核心1349项检查包含800项完整记录、压力/预算、写/关闭故障、sticky timeout、线程/绑定限制和拒绝覆盖；完整双配置与实际入口结果在后续验证记录中列出。

最终功能验证：

| 配置/验证 | 结果 |
| --- | --- |
| Release标准候选 | 0警告；`80e177973d2f45819014bbf10091c415` |
| Debug标准候选 | 0警告；`15e0e7cbd8754dd2ae010bab0b9fe3e5` |
| Release定向CTest | 11/11，158.32秒 |
| Debug定向CTest | 11/11，163.51秒 |
| 新核心/宿主断言 | 每配置full stream1349检查、Player62项、PresentationCosts76检查；原GUI18组独立GPU比较等保持通过 |
| Python最终套件 | 96/96，9.808秒；含大小写冲突字段、32组畸形/丢失负例及两种宿主序列化形状 |

首轮96/96通过后，代码复核发现大小写归一化可能覆盖冲突字段。先归档旧校验器/测试，再新增严格报告字段集和递归冲突拒绝，重新运行整套96项；没有改动已通过的C#/原生产品，因此未以额外构建或重采样替代校验器测试。首份全套日志9.938秒保留。

固定实际入口验证不是性能队列，先落盘输入清单/作业定义，不选择最好一轮。Editor使用600次空参考循环，Player使用源测试静态场景的800固定步；图形/Editor模式选择120上限仅为功能检查，**不是用户指定1920×1080/Low/30fps验收负载**。

| 实际入口 | Release全程条数 / Present返回数 | Debug全程条数 / Present返回数 |
| --- | --- | --- |
| Editor CPU-only | 600 / 0 | 600 / 0 |
| Editor DX11 | 600 / 600 | 600 / 600 |
| Player Null | 800 / 0 | 800 / 0 |
| Player DX11 | 1595 / 1595 | 1595 / 1595 |

8个成功进程共**7190条全程循环样本**，每份均超过512项前缀，序号/原始阶段/Present间隔/限帧信息、前缀对应值、文件字节数、sealed边界及最终关闭结果全部离线通过；未删除慢帧。每配置另一次Editor已有文件请求按预期退出2，原文件哈希不变。源宿主测试的另外4份4帧Editor记录单独校验，不混入7190总数。记录数相同不表示跨配置时序/性能相同。

首份CLI包装器错误地递归收集了候选目录内的运行时日志，将其当作不可变输入。第一个Release Editor CPU-only进程已完成600条并校验成功，之后的预检查发现 `editor/out/user/logs/editor-candidate.jsonl` 由422字节追加至838字节，**在第二个进程启动前停止**。源码明确该日志是输出；只读核对唯一变化且前422字节SHA与预登记完全一致，保留首计划/日志/完整结果和精确前缀；`interruption-verification.json` SHA **57768D8C9EC03FDA63839B5D6DC7C7A9C8EDFBC8E19DD1F8FA4FA47BD61E55AD**。一次临时PowerShell检查不能调用AsSpan扩展方法，没有生成成功证明；随后显式字节数组检查才形成上述证明。

另建second计划按部署manifest枚举不可变产品文件，把可变运行日志作为输出，仅执行剩余9个用例，**没有重跑首个已通过的600条用例**，没有改写首计划。两份测前清单SHA分别为 **BA966B2487559B270F7EAFB0755A8DAA17483DD5F9F9940B863D0EC1289C0434** / **2A2DCE3B025B912DD1B5CF7948E12F0A7E765694724EFB0C27B57F9AC7082984**。所有产品输入、两套原结果与预期退出被最终统一校验；原始运行日志的首态以精确前缀副本保存，不将已变日志伪装成旧文件。

首份最终审计又发现240项前态归档未涵盖两配置会被构建覆盖的 `out/verification/m2-7/*/packages.json` 索引。旧的10个候选包/清单仍完整保留；保留审计失败后，根据旧证明中的候选ID，对当前索引中恰好5处候选ID进行逆替换，保持原始BOM/空格/换行，**只有长度和SHA逐字节匹配原证明才接受**。两份恢复索引均满足原哈希；`recovered-pre-loop-index-first/manifest.json` SHA **3CA9CAC588D4E0421755C6E8A184332D99EB9F64ED6716EF0E81DAE0102D5184**。这是可验证的旧字节恢复，不声称此前已复制，也不以语义相同放宽检查。

最终 `out/ecs-e5d-loop-stream-verification-second.json` SHA **051AD6124EDA6B18D2CC0A01EA37FF82A34EE5D2BB6718B2984B3D569005F572**，绑定4463项文件、10个实际候选包、8份全程CLI记录、2次拒绝及4份源宿主记录，并通过240份前态副本/恢复索引重新验证上一片1695项证明。原失败、首版校验器、首计划和旧GUI测量均保留，没有重写旧哈希。不是整仓源码冻结或完整CTest，`performanceAccepted=false`。

94501（Player复测）、59698（双配置门禁）终止exit0；4511（首CLI包装器）终止exit1；63868（剩余9例）及12502（只读文件查找）终止exit0。没有活跃测试/采样进程。正式EXE仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**，未提交/推送/部署，IDE改动未触碰。下一步继续较大的CPU字体/托管GUI/场景提交成本和代表性路线/统计定义，不将本次合成入口接线当作CPU无回退、目标机或内存通过；全程观测自身的有/无对照、GPU缺口/驻留/队列、完整新冻结/回归/正式队列、人工/自包含/1h等仍待完成，E5-D/E-F/ECS/M8.3未关闭。

### 11.42 E5-D 托管 GUI 文本缓冲候选

已有阶段记录提示首次GuiBuild约47ms、暖态约0.35–0.45ms/34KB分配；这不是新测量，也不能据此证明JIT是根因。本片处理源码中明确存在的每控件UTF-8临时数组，不将局部分配结果当作解决首次卡顿。

`EditorPresenter.Add` 改为先GetByteCount校验，再扩展复用List并直接向Span编码。保持原Encoding.UTF8替换回退、控件数据、偏移、布局和4096字节标签/1023字节值/8192控件/2MiB总预算。拒绝超限发生在缓冲增长之前；没有改动ABI、业务、权限或本地化顺序。

新增4组测试覆盖120控件/128,076字节与旧数组逻辑逐字节相等，包括中文、组合字符、emoji、NUL及孤立代理项；边界按字节而非字符判断；拒绝时原数据不变；完整总预算和控件上限；清空复用不泄露旧尾部。预分配列表、无动作/本地化的512次追加，原路径分配40,960B，候选为0B。**不是整个GUI零分配、CPU计时收益或目标机性能通过。**

| 验证 | 结果与范围 |
| --- | --- |
| 原实现Release负例 | 前3组通过，第4组检出40,960B后未捕获断言；不是正常终态 |
| 候选Debug直接构建 | no-restore，0警告/0错误；4组通过 |
| Debug定向CTest | 3/3，82.70秒；编辑器服务161/161、UI包准备11组、UI选择6组 |
| 候选隔离Release构建 | 独立OutDir，0警告/0错误；4组通过 |
| 同字节Release完整服务 | 编辑器服务161/161、UI包准备11组、UI选择6组 |

首个隔离Release完整服务因测试从父目录推断配置名而将配置认作`ecs-e5d`，找不到Gameplay.Sample夹具。保留失败后，将实际102项同字节构建文件复制到独立`Release/net8.0`布局再运行剩余服务；逐文件哈希一致，没有改产品、断言或重跑已通过的四组定向用例。复制脚本先因误预期72项而在复制前拒绝，核对102项实际依赖/产物后修正；不是产品失败或放宽测试。

原负例留下PID3360及子进程22936，完整命令行、创建时间和父子关系已核对。常规Stop-Process失败，已请求用户批准仅处理这两个辅助进程，当前尚未收到答复；没有尝试更强的终止方式。旧日志仍被占用，保留共享只读字节快照而不冒充进程终态。后续定向测试入口已捕获异常并返回1。原执行句柄88386仍待收尾；其余本片验证均已结束。

证据与保留：

- 修改前18项可变输入/源码/包索引/IDE文件归档：`pre-presenter-utf8-first/manifest.json`，SHA **D85983C8046A5353CCE09148E0F9C060E44E782D5A8B78375A2C8CBEB65AAFB5**。前置脚本两次语法准备失败及隐藏`.suo`读取失败均发生在归档前；原脚本保留，随后显式Force读取并成功归档。
- 原负例72项产物闭包：`presenter-utf8-red-first/manifest.json`，SHA **630C4F5EB3124DAA676C7E05849A0D1F9C707708E82223DF4BDF083469922A66**。
- 首次最终核验因原日志锁定停止；新只读快照SHA **D9D01E733FE83DB9E52A3C293DE915204EEAA3E08B6ADC9F55482B9F86AF9C7F**，只证明读取时的内容，不是最终退出记录。
- 最终 `out/ecs-e5d-presenter-utf8-verification-second.json`，SHA **0754489752A6E1B6CC274BC0FFF8510CDAFED42B1FB5C6C241FB9FDAA0813C2D**，核验5057项绑定，包括全部上一片4463项、归档副本、同字节搬移、源测试和日志。四项IDE用户变更和正式EXE C50742AD…均未变。

本片仅直接/隔离构建和定向回归，**尚未标准Build.bat双配置候选重建、完整回归或性能验收**。未提交/推送/部署，不放行E/F/M8.3。先在用户批准后重新确认残留身份并清理，再补标准候选门禁；随后优先继续首次GUI构建/JIT归因、CPU字体/场景提交、代表性路线/统计与观察扰动，而非把文本局部收益扩展成整帧结论。完整新冻结/正式队列、内存/目标机/人工/自包含/1h门禁保持待完成。

### 11.43 E5-D 授权清理与文本候选标准门禁

用户明确批准仅终止已核对的PID3360、22936。再次核对时3360已经退出；22936命令行、18:54:11创建时间、父PID3360仍与原测试一致，仅对该进程调用终止，返回0。CIM短暂保留空命令行条目，随后通过Process.GetProcessById确认两个PID均已不存在。没有终止其他进程、删除日志或覆盖原负例产物。

原执行句柄88386结束：包装器exit0表示它观察到了预期失败，**实际测试退出码为-532462766，不是测试通过**。原日志解锁后SHA仍为D9D01E73…，与保留快照完全一致。前节授权阻塞已解除；原失败证据永久保留。

标准构建前重新核验上一片5057项绑定，并归档77项可能被构建覆盖的输入（含两配置packages.json索引），`out/verification/ecs-e5d/pre-presenter-gates-first/manifest.json` SHA **1B2A5522D2DBCCDD89B96D402F3FADDD2F64BB2E8AB4F019DB3C832E39D900F7**。这不是完整源码冻结。

首轮Release标准构建在可选gRPC模块的NuGet漏洞数据查询处因受限网络报NU1900，尚未启动CTest。保留首日志后，以网络允许的同一Build.bat流程重试，没有关闭漏洞审计、降低警告策略或修改产品代码。

| 验证 | 结果 |
| --- | --- |
| Release标准候选 | 零警告；`dce2011bac124b7399622b881678d0fc` |
| Debug标准候选 | 零警告；`c0a321f8ebe6407bb0a28f6710fc2259` |
| Release相关CTest | 14/14，236.46秒 |
| Debug相关CTest | 14/14，244.72秒 |
| Python | 96/96，9.720秒 |

14项是原11项相关门禁加编辑器服务、UI包准备及UI选择。每配置确认编辑器161项、UI包准备11组、UI选择6组，120控件/128,076字节精确对照及512控件预分配追加0B；原GUI18组独立GPU图像比较、PresentationCosts76、LoopStream1349和Player62等仍通过。**不是完整CTest或目标机性能验收**。

最终核验 `out/ecs-e5d-presenter-gates-verification-first.json` SHA **E83A2D390760B29D2A866775CB2281DEB4C680C8F82747941DFA80D52DF00814**，绑定6177项文件和10个新候选包。逐文件验证包清单、包内Editor程序集与当前RID编译物相等，独立验证4份新源宿主CPU/DX11循环记录（各4条），通过77项精确副本重新核验上一片5057项；保留NU1900首失败及全部历史证据。四项用户IDE文件和正式EXE C50742AD…保持原哈希。

5703（首构建）终止exit1，7482（网络允许的双配置门禁）终止exit0；本片无待轮询的构建/测试/采样会话。未提交、推送或部署。本片未追加性能采样，局部分配收益仍不证明首次GUI卡顿、整帧CPU/GPU预算、内存或目标机性能通过。下一步继续有界冷GUI/JIT归因和CPU字体/场景提交成本，后续代表性路线/统计定义、观察扰动、完整新冻结/回归/正式队列及内存/目标机/人工/自包含/1h等门禁不变；E5-D/E-F/ECS/M8.3未完成。

### 11.44 E5-D 托管 GUI 冷构建固定诊断

本片不修改产品实现，使用11.43 Release包中相同30个托管DLL，隔离驱动仅放在 `out/verification/ecs-e5d/presenter-cold-probe-first`。目的为区分首次Build编译、工作区Capture与包构建成本，不是完整Editor、字体、原生插件、GPU或Present测试。

固定四种条件：原样、仅PrepareMethod(Build)、仅Capture、两者组合；空场景和32对象（首对象选中）两个确定性夹具。每个条件使用独立进程，轮换条件顺序和夹具顺序，提前落盘输入/执行计划，不按结果补采。测量辅助包装器在所有条件下同样准备。保留Setup、Prepare、Capture、FirstBuild及每一条暖态记录；**组合成本包含Prepare+Capture+FirstBuild**，不把预处理移出窗口当作优化。另保留子进程总墙钟；进程启动、驱动入口JIT与结果序列化不属于上述阶段计时。

- 冒烟：8进程，各8条暖态记录，共64条。
- 完整诊断：32进程，各128条，共4096条；与冒烟分别报告、不合并估计。
- 40个进程均正常exit0，无超时、无残留清理；标准输出/错误为空。
- 运行时均8.0.28，TieredCompilation/TieredPGO环境覆盖均为空。这不是完整CLR/OS闭包或目标机/空闲状态证明。

下表列完整诊断每条件4次观察的范围，单位ms；不是置信区间或验收阈值：

| 夹具 | 条件 | FirstBuild | Prepare+Capture+FirstBuild |
| --- | --- | --- | --- |
| 空场景 | 原样 | 25.281–32.478 | 25.281–32.478 |
| 空场景 | 仅预编译 | 14.691–18.529 | 26.241–32.580 |
| 空场景 | 仅预读 | 26.254–31.451 | 28.404–33.768 |
| 空场景 | 两者 | 14.651–20.782 | 28.114–36.308 |
| 32对象选中 | 原样 | 29.775–34.373 | 29.775–34.373 |
| 32对象选中 | 仅预编译 | 18.639–19.605 | 30.315–34.503 |
| 32对象选中 | 仅预读 | 28.567–33.329 | 30.348–35.059 |
| 32对象选中 | 两者 | 15.837–19.781 | 31.371–33.723 |

原样FirstBuild期间进程JIT计数器分别记录约23.949–30.838ms、27.534–32.312ms；单独Prepare约11–16ms，Capture约1.5–2.6ms。**JitInfo是进程范围累计编译时间，不能与主线程墙钟相减来给出排他归因**。证据表明冷构建伴随显著JIT活动，提前编译主要移动成本，未显示一致的组合收益；不采用纯预热作为已证优化。也不能证明此前真实宿主约47ms全部由此导致。

所有条件的首次及末次包完全一致：

- 空场景：45项/622字节，SHA **80143B0956D59142D7CCA4E4468779B3E8045C85F6105DA2DB47EC6139829415**。
- 32对象选中：105项/1280字节，SHA **24D411BB7B53F03B019F9C258621621B8CF2F92BB03BF1E3D005D9F46A5C8690**。

工作区快照读取由0到1，暖态仍为1。每条暖态分配分别为1992B/10000B，原样每进程暖态中位数约0.0091–0.0094ms/0.0362–0.0642ms。该夹具缺少真实宿主资产/服务配置，与前述0.35–0.45ms/34KB不是同一工作负载，**不能相除宣称产品加速**。

证据保留：

- 冒烟计划SHA **E4F5033B9A7C3D1865A1E1712934262ED35C609D6D85B9F00294A418B9277D6D**，验证SHA **98255047DF136C6A9CAEB1800F34345ADF3DDB9F4199835C47BB84716BE6C17A**。
- 完整诊断计划SHA **1FE30F68B7E9DC8CC98B568CF30166FA4BAB055704EAC4BB374C177FA4F1A999**，验证SHA **B20CF69FAA4CCFFD7D7C7E25353DE3E9F68B128DB3EB3B0F76105ECDC9E91C30**。
- 独立最终复核 `out/ecs-e5d-presenter-cold-verification-first.json` SHA **CC63129245A64456A44043676C5F18E758AABB41AC5B40499817CDCAFFDB7B72**；重新读取所有原始结果、输入和加载程序集，重算两份证明逐字段相等，同时重验此前6177条绑定。去重后共4391项文件，保留每进程观察和全部暖态行。条目去重不代表删去旧证据。
- 驱动首次编译因漏引入TransformData命名空间报CS0103；原源码保存在`Program.compile-failed-first.txt`及失败日志，随后仅补Ncma.Runtime using，第二次编译0警告/0错误。一次只读PowerShell展示命令管道语法失败，未运行新样本或修改证据，修正展示语法后读取成功。

源码检查显示Build包含较多可选面板分支，部分大辅助方法进入后才检查是否打开。下一片候选为拆分主路径与冷面板实现，**尚未实施或证明收益**；必须保留短路/权限/状态变化顺序、GUI字节、动作映射及所有其他调用方，并测首次开启面板，不能隐藏功能或转移卡顿。实施前归档受影响的现有证明输入，修改后重新双配置相关门禁与固定新旧对照。CPU字体/场景成本、代表性路线/观察扰动和完整新冻结/回归/正式队列等后续项目仍待完成。

无活跃构建/测试/采样会话，无提交/推送/部署，产品及正式EXE/四项IDE文件未改变；本片不重复执行未受影响的标准构建。E5-D、内存、目标硬件、人工、自包含、整帧与1h验收仍未完成，不进入E/F/M8.3。

### 11.45 E5-D 可选面板冷路径候选与 Debug 门禁失败

本片先实施最小可独立核验边界，不把整个Build一次重构：角色调试、动画图检查、动画运行观察、AI工作流四个方法各增加轻量开关入口，原实现移至禁止内联的Visible方法。开关关闭时不调用大方法；原实现内的开关/服务检查、审批、失效处理和控件/动作顺序全部保留。场景、动画图编辑器、UI编辑器仍调用相同入口。没有新增预热、自动打开面板、权限或业务变化。主Build内部其他大分支尚未拆分。

修改前重新核验11.44的4391项文件，归档11项可能被本轮编辑/构建覆盖的源码、RID程序集和两配置包索引。`out/verification/ecs-e5d/pre-presenter-panels-first/manifest.json` SHA **90C1B08316B72F9B75A16092E0FF63C62DC1C2C2AF202372D7B9C35E4D272A97**。最终复核移除新增包装器后，四份面板源码与归档原文完全一致，避免把“只调整入口”当作未经验证的描述。

新增PresenterPanelTests五组：关闭时不访问已释放Owner；开启但服务缺失时不输出或消耗标签ID；四面板首次打开/暖态/关闭/再次打开的完整控件字节和动作映射一致；图检查及工作流使用真实带帧戳关闭事件；场景文档、Undo、Play、Endpoint及快照读取保持不变。其余两个面板用测试反射设置开关，**不是完整菜单/人工交互验收**。服务为无运行中角色、无已打开图资源状态，不能泛化为全部活跃业务路径。

| 验证 | 结果 |
| --- | --- |
| Release定向构建/新增用例 | 0警告/0错误，5/5组 |
| Release标准候选 | `121fc71d87c0476abe2010b168ed714c`，0警告 |
| Release相关CTest | 14/14，239.20秒；服务166/166 |
| Debug标准候选 | `2043190e6e5b443e8602ea2bda5dbdbe`，0警告 |
| Debug相关CTest | **13通过、1失败**，245.02秒 |
| 独立Debug文本/面板 | 4/4组（0B）及5/5组 |
| 独立Python | 96/96，9.595秒 |

完整Debug在既有`PresenterTextTests`最后一组失败：预分配、无动作/本地化的512次追加记录 **6,808B**，断言仍要求0B。该测试源码及产品Add文本实现均未改动，Release同组为0B；完整Debug服务进程在此返回1，因此未运行后面新增五组。独立Debug一次定位中原四组为0B通过，另外五组通过，**不说明完整进程异常已修复或必然是噪声/JIT，不能替代失败门禁**。未重复跑完整队列取绿、关闭审计或降低阈值。曾在进度消息中根据日志尾部误将“执行到13/14”说成“通过13/14”；收到终态后已明确更正，以本节最终结果为准。

准备了相同驱动/仅Editor DLL不同的固定AB/BA/AB方案，覆盖空场景/32对象和关闭/四种首次打开、重开，并记录Cold+FirstOpen。但双配置门禁未通过，**驱动未构建，冒烟/正式诊断进程均未启动，未产生新性能数据或收益结论**。`presenter-panel-probe-first`仅源码与项目，`run-panel-pairs-first.ps1`/`verify-panel-pairs-first.py`/`verify-panel-delivery-first.py`仅准备/语法检查；其中交付校验器仍针对门禁成功情形，不可误执行为当前状态证明。

本片其他失败同样保留：新增测试首次CS0019（uint与枚举比较）修正为显式枚举转换；第二次字典枚举错误由LINQ Cast误把泛型项当DictionaryEntry，改用IDictionary枚举器。两次均保留原源码/日志，异常被捕获正常返回1，没有残留测试进程。状态审计首次遇CTest本地化时间戳夹杂非UTF8字节而解码失败，保留原校验脚本后采用surrogateescape并要求逐字节编码回环相等；原日志不修改，断言仍匹配精确文本。

最终 **部分状态证明** `out/ecs-e5d-panels-status-first.json` SHA **6848DB7B95DCDBA15CFD0606E6A7CB583A029C7FD6F2480261649FE08E388CD0**，绑定5585项文件、10个实际候选包、两配置测试程序集闭包、4份新源宿主4帧记录、旧4391项证据及所有新增源码/失败。`functionalGatePassed=false`、`rootCauseProven=false`、`performanceAccepted=false`、`performanceCohortsStarted=false`。正式EXE及四项IDE文件原哈希不变。

标准门禁会话20515已终止exit1；独立检查84328已终止exit0，其余定向构建均终态，无活跃会话可继续轮询。当前候选保留未提交，不部署、不推送、不进入下一阶段。下一步应针对**完整服务测试进程**采集有界分配定位（区分列表容量、测试动态委托/运行时与追加调用本身），保留原断言和失败；不能仅凭单独复测通过就归因或放行。定位后才补齐相关门禁和固定新旧首次开启对照。完整冻结/回归/正式队列、内存/目标机/人工/自包含/整帧/1h等仍未完成。

### 11.46 E5-D 完整服务分配与计数器有界诊断

本片仅在`out/verification/ecs-e5d`创建隔离诊断，不改产品源码、正式测试、候选包或运行配置。原11.45 Debug门禁的6,808B失败永久保留；下述通过结果不替代该失败，也不证明异常为噪声、JIT或运行时缺陷。

首先重新核验5585项既有绑定，保存29份测试源码和72份Debug测试输入文件。隔离完整服务驱动保持原测试顺序、原512次预分配追加以及原`allocated == 0`断言；只在测试副本增加可选逐调用计数及前后列表容量记录。计数数组/反射/报告准备不在原追加循环内，新增计数辅助方法预先准备，不增加Add暖身次数。产品DLL逐字节等于原Debug输入。

固定三个完整进程，无结果驱动的追加重跑：

- 每进程166/166，正常exit0，原累计分配、追加区间内外分配均0B。
- 共1536个追加区间均0B，三个进程内列表容量均未改变，前后线程一致。
- GC/JIT辅助快照涵盖附加诊断工作，不能当作原测量区间的排他归因。插桩改变IL和调度；“这些进程未扩容”不能回推原失败进程未扩容。
- 输入清单SHA **F8DBB810332E0E14AE045F329664FE536A445EBE942549E6561648105C9C889B**；固定计划SHA **B82A2B86CFAB0C313384F4AD41123A5583A95D4537F0CD8C7E0088715BDA8F80**。
- 独立证明`out/ecs-e5d-allocation-probe-verification-first.json` SHA **69A3AE2F07DAD2A30B2F2964525D3207174231A45F4490268C02597AD3C4F9E3**，绑定5833项文件、原5585项与实际输入/原始结果，检查测试差异仅为声明的插桩。

随后检查官方资料。[API说明](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-8.0)定义的是线程累计托管分配，不是当前驻留量；[8.0.28原生实现](https://raw.githubusercontent.com/dotnet/runtime/v8.0.28/src/coreclr/vm/comutilnative.cpp)使用分配上下文累计量及剩余空间计算。[上游问题134724](https://github.com/dotnet/runtime/issues/134724)报告另一运行时版本上的空区间计数异常，[草案PR134855](https://github.com/dotnet/runtime/pull/134855)提出后台GC记账修正。本次读取时PR仍为草案；**这只是待验证假设，不是本机8.0.28或原6,808B的根因证明，也不是已部署修复**。

为验证该假设，另建无引擎依赖的BCL程序，固定六进程AB/BA/AB，比较默认GC与仅子进程`DOTNET_gcConcurrent=0`。每进程32个空区间控制、32个已知80B分配控制、256个有独立有界GC工作线程的空区间。工作线程使用有限存活图与轮换缓冲、最多160次非阻塞Gen2请求，主线程结束即停止；强制GC仅为合成诊断刺激，不用于正式测试、产品或性能验收。驱动设置15秒取消、工作线程有界等待，外部30秒限时，不自动强杀或重启。

| 对照 | 进程数 | 空区间观察 | 后台GC最后索引 |
| --- | --- | --- | --- |
| 默认 | 3 | 每进程256次均0B | 每进程5（开始为0） |
| 仅诊断子进程禁用后台GC | 3 | 每进程256次均0B | 每进程0 |

192个空控制均0B，192个正控制均80B，1536个刺激空区间均0B；全部exit0且工作线程结束。相同驱动IL、实际.NET8.0.28 X64 CoreLib/coreclr/clrjit哈希一致。默认条件实际发生过后台GC，但并不证明每个测量区间与其重叠；合成工作负载也未复刻原测试进程历史。**未复现，不能据此排除所有计数器问题，更不能采用禁用GC作为引擎修复。**

固定计划SHA **C4B39BAAB1505BAB7E64108A67FE37F9190B56E5DB222FA26F13B8AD8916E028**。最终独立证明`out/ecs-e5d-allocation-counter-verification-first.json` SHA **F88FC8C4C211602A6C6E1130E7ED7D8542C8E9559BA49728244E540C62DF62D2**，重新验证前5833项及六个实际进程输入/日志/结果，共5869项文件。两个诊断构建均0警告/0错误；正式EXE C50742AD…、四项IDE文件、所有原失败和产品输入哈希不变。

本片全部进程终态，无残留或待轮询会话。原Debug完整门禁依然失败；`functionalGatePassed=false`、`rootCauseProven=false`、`performanceAccepted=false`。未构建或启动面板成本对照，未提交/推送/部署。下一步应对原版完整服务测试进行有界分配事件追踪，明确事件采样粒度、可能遗漏和观察扰动，保留失败而不是继续重复相同诊断取绿；如需改变验收定义或正式运行环境，须另行明确决策，不能从本次继续请求推定授权。E5-D及其完整冻结/回归/正式队列、内存/目标机/人工/自包含/整帧/1h仍未完成。

### 11.47 E5-D 原版完整服务事件追踪

上一片为实际诊断进展，本片改为读取原版、未经插桩的完整服务测试运行时事件；不修改产品或正式测试。固定版本微软`dotnet-trace 8.0.547301`仅安装到`out/verification/ecs-e5d/eventpipe-tools-first`。首次受限网络安装无法加载NuGet索引；允许网络的同版本安装成功，未修改全局工具、CLR版本或引擎运行配置。解析器使用该工具携带的TraceEvent程序集，独立构建零警告/零错误，无产品引用。

采集配置为`Microsoft-Windows-DotNETRuntime:0x40008019:5`（GC、Loader、JIT、Exception、Stack），64MiB环形缓冲、180秒观察上限，不启用强制GC关键字或CPU采样。按照[官方EventPipe说明](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventpipe)，该机制采集托管及运行时事件；[诊断工具说明](https://github.com/dotnet/diagnostics/blob/main/documentation/dotnet-trace-instructions.md)区分分配采样与全分配跟踪。**缓冲未报告丢事件不等于记录每次对象分配**，亦不证明6,808B这样的小额变化不存在。

首次固定一次启动式采集，计划SHA **32F1B16F87A53D7D3D2442CD5C70B037E837BC771DD6963D586BD2DD014020CD**。原版测试在较早的M3.8 MCP标准输入输出用例失败，JSON首字符为`T`，尚未进入UTF-8用例。可能与启动器诊断端口/暂停环境继承有关；测试未保存子进程原始行，因此不能仅由`T`断言确切原因。该追踪、失败日志和退出1全部保留，不记为原6,808B复现。没有修改MCP协议、子进程测试或超时阈值。

为避开启动器环境影响，另行预登记**仅一次**正常启动后立即按PID附加：计划SHA **2F18137AD4C57FB7A2797BD9D17FBE6D722AE6FB46CBC53C1F31F0FDD20F6EA2**。测试进程继承正常环境，采集器独立附加，不向测试注入诊断端口参数。明确缺少附加前的启动前缀；这不是与第一次同配置重复取绿。目标PID17288和采集器20664均exit0，完整166/166，原512次追加0B。此结果仍仅为诊断，不能覆盖原标准Debug门禁失败。

| 原始追踪 | 大小 | 事件数 | 分配采样 | 有栈分配采样 | 报告事件丢失 |
| --- | --- | --- | --- | --- | --- |
| 启动式，未到目标用例 | 6,637,198B | 15,718 | 230 | 230 | 0 |
| PID附加，完整服务 | 45,450,286B | 148,142 | 23,782 | 23,781 | 0 |

两份原始nettrace SHA分别为 **46A404ECEEA9EC04B2458501D1B331920568566AE8F75765EF86DC508E70BC7E** 和 **05C3D1A8C6F36ABDF6EE75B30527B3E7A70162DCBFE238BDCB0BE52F42712BCE**。保存ETLX及结构化解析结果，不以转换文件替代原始追踪。

完整追踪中78个分配采样栈匹配PresenterTextTests或EditorPresenter.Add，匹配包含其他调用方和冷准备，**不是78次暖态循环分配**。其中复用用例可见EditorSessionOwner准备、LambdaCompiler编译委托及GuiItem[]扩容；源码的预热循环位于正式计数之前，两轮使用同一委托，未添加精确循环起止事件，不能仅从方法名把扩容归入暖态。邻近后台GC288的开始/结束为相对时间84354.7093/84362.03ms，复用用例JIT开始为84357.8571ms；这表明邻近活动，不证明精确测量区间重叠，更不回溯解释原失败进程。

独立核验 **`out/ecs-e5d-original-service-traces-verification-first.json`** SHA **1088DA49B8EC19FE982F82F677FC24B97D9B9D30661BFBACA31A96A6CBFE3594**，绑定6017项文件：此前5869项、原测试闭包、实际原生插件、工具、解析器、计划、原始追踪和解析结果。校验事件总数、采样/堆栈统计、进程身份、退出状态和原日志，不把成功采集当作性能通过。正式EXE C50742AD…和四项IDE文件哈希不变。

准备脚本首次读取归档隐藏`.suo`未加`-Force`，在创建采集目录/启动进程前停止；保留原脚本为`.hidden-check-failed.ps1.txt`，仅修正文件可见性后执行。一次CIM进程枚举被拒绝，未尝试提升权限终止进程；记录的退出状态及`Process.GetProcessById`确认11180、17288、20664均已不存在。所有会话终态，没有新残留清理请求。

当前仍`functionalGatePassed=false`、`rootCauseProven=false`、`performanceAccepted=false`。原门禁失败未关闭，面板性能对照仍未启动，未提交/推送/部署。下一片优先分析分配上下文退还及实际GC时序条件，或增加明确测量边界的隔离诊断；应说明观察扰动，不重复同样的采样队列期待失败消失。正式GC配置、0B断言和完整阶段门禁不变，E5-D/E-F/ECS/M8.3及目标机等验收仍未完成。

### 11.48 E5-D 分配上下文退还对照与测试 v2 待确认

本片取得新的可复现实验结果，不是重述状态或重跑相同队列。原版追踪提示GC时序值得检查，进一步核对[准确8.0.28版gc.cpp](https://raw.githubusercontent.com/dotnet/runtime/v8.0.28/src/coreclr/gc/gc.cpp)：`fix_allocation_context`的GC分支先扣除未用空间再清空指针，而`void_allocation`直接清空指针/上限；后台标记末尾调用后者。结合线程计数器公式，该差异提供了具体记账机制假设。[上游草案134855](https://github.com/dotnet/runtime/pull/134855/files)针对同类路径扣回未用空间。本片不替换运行时，不把草案当作已发布修复。

新增独立BCL程序放在`out/verification/ecs-e5d/allocation-retirement-probe-first`，不加载引擎。与此前随机重叠探针的区别：工作线程保持2097152个小对象的有界存活图，并在每次非阻塞Gen2请求返回后通知主线程；主线程**此时才分配已知大小的锚对象**，随即测量仅Sleep或SpinWait的空区间。即使如此仍可能错过后台完成，逐行记录请求前、区间前及区间后的已完成后台GC索引，不把“发起过GC”当作区间覆盖。

固定两进程：默认配置、仅对照子进程`DOTNET_gcConcurrent=0`。相同驱动各64阶段，四种锚对象大小与Sleep/Spin条件按固定顺序轮换；16个无工作线程空区间控制/进程，记录全部结果，不按非零结果追加样本。5秒单次交接、6秒线程结束、180秒外部观察上限；无自动强杀/重启。所有强制GC只属于该合成刺激，正式引擎和测试配置不变。

| 默认模式阶段 | 锚对象实际分配 | 空区间类型 | 空区间计数增量 |
| --- | --- | --- | --- |
| 1 | 80B | Sleep | 7832B |
| 2 | 256B | Sleep | 7656B |
| 17 | 80B | Sleep | 7832B |
| 27 | 1024B | Sleep | 6888B |
| 46 | 256B | Spin | 1472B |
| 47 | 1024B | Spin | 160B |
| 50 | 256B | Sleep | 2224B |
| 59 | 1024B | Sleep | 6888B |

默认模式8/64区间非零，恰好对应记录到后台GC完成索引变化的8个区间；其余56项为0B。禁用后台GC的独立进程64/64均0B，后台索引均0。两进程合计32个空控制全0B，128个锚对象分配均精确等于80/256/1024/2048B。空区间方法的IL只调用线程计数器及Sleep/Spin并返回值类型；未在区间中创建托管对象或序列化。实际CoreLib/coreclr/clrjit均为与原测试相同的8.0.28字节。

**本机空区间计数异常已经复现；原6,808B的直接因果仍未证明。** 此结果支持后台GC记账机制的解释，但不能伪造原失败进程缺失的调用区间证据，更不能把原失败改成通过。没有重新运行完整引擎性能队列或改变既有统计结果。

构建0警告/0错误，两进程正常退出、工作线程结束。预登记SHA **E1B36470BCADF36FD11A2475022BC44A2E09FB0922B263FD3F67E83177928B1F**。独立核验`out/ecs-e5d-allocation-retirement-verification-first.json` SHA **A9790D81E7351D2E3B3EECE1B52BA2E61E30AC7B3893C44B80A880FC9C96F5B4**，重新验证先前6017项并绑定新输入/全部128行/控制/实际运行时，共6037项文件。`emptyCounterAnomalyReproduced=true`，但`originalFailureCauseProven=false`、`functionalGatePassed=false`、`performanceAccepted=false`。正式EXE、四项IDE文件及所有失败证据未变。

#### 待确认的单项测试 v2（尚未实现）

已向用户提出以下明确选择，**尚未收到答复，自动继续不视为批准**：

1. 采用独立v2边界：仅UTF-8预分配追加微测试的原512次调用在受检NoGCRegion内执行，仍严格要求0B；其余完整服务、引擎及正式性能测试保持正常GC。开始失败不跳过，退出失败或中途GC/预算失效不忽略，均令测试失败；正常退出必须在finally得到保证。不将进入/退出成本隐藏后宣称性能收益，该测试仅验证指定追加路径是否产生托管分配。
2. 保持v1：不修改正式测试，继续追查原失败的精确条件。

若批准v2，实施前归档受影响的6037项绑定输入，保留原失败、复现程序与所有旧证明；添加零分配、已知非零分配及NoGCRegion进入/退出失败的针对性覆盖，确认不是把真实分配吞掉。之后标准双配置相关门禁必须重新执行，原结果不复用为新候选通过；面板成本对照仍需在这些门禁通过后启动。该决定仅作用于本项功能分配微测试，不改E5-C内存口径、E5-D正式性能合同或目标机条件。

本片有实质诊断进展，且本决定是首次请求，不构成三轮阻塞。所有进程已终态，无待轮询工作。未修改产品或正式测试，未提交/推送/部署，不放行E/F/M8.3；完整E5-D和其余验收仍未完成。

### 11.49 E5-D 已批准分配测试 v2 与双配置门禁

用户明确选择“采用受检NoGCRegion的测试v2”，据此执行，不把自动继续当作批准。变更前重验11.48的6037项绑定，归档211项可能被测试编辑/标准构建覆盖的源码、程序集、原生插件及两配置包索引；`pre-text-v2-first/manifest.json` SHA **DC5C45985A2755B629543E0A4288D38D4BAC06C4864603FFD495A0708A809C3B**。原v1失败、诊断原始数据和旧证明继续保留。

仅修改三份测试文件：

- 新增`NoGcAllocationTestScope.cs`：测试专用4MiB受检作用域。资源对象在进入前创建；TryStart失败或抛异常不跳过测试，成功后由using/finally退出，EndNoGCRegion的预算/收集失效异常直接传播；拒绝非Owner线程清理，成功或失败退出均不重复调用结束API。
- `PresenterTextTests.cs`：只包装原有512次暖态追加测量，保留相同委托、预热次数、字符串、顺序、0B断言及完整字节/容量检查，输出和用例名称明确标v2。进入/退出在分配计数区间外，本用例不宣称启动耗时或整帧性能收益。
- `Program.cs`：注册新增五组边界检查；定向文本测试由4组变为9组，完整服务由166项变为171项。

**NoGCRegion是暂时的进程级GC状态，不是仅主线程的GC开关。** 它只包围该顺序执行微测试的短测量区间及独立边界控制；其他完整服务测试不在作用域内，产品配置和正式性能测量均未改变。没有改全局gcConcurrent、CLR版本、v1失败结果或性能合同。

新增五组覆盖：进入返回false/抛出原异常、正文抛异常仍清理且重复Dispose只结束一次、退出失效原异常传播且不重试、错误线程拒绝后Owner仍能清理、实际运行时空区间0B/已知对象80B正控制及退出NoGCRegion。确定性进入/退出失败路径用注入回调覆盖，**不声称实际耗尽GC预算的压力测试**；真实正控制确认不是把非零分配吞掉。

| 验证 | Release | Debug |
| --- | --- | --- |
| 定向构建/文本与边界用例 | 0警告/0错误；9/9 | 0警告/0错误；9/9 |
| 标准候选 | `cfec3afc4c4e4df2baf0a18530177add`，0警告 | `670d9c3a31264869824eb75fc083d7fa`，0警告 |
| 相关CTest | 14/14，236.08秒 | 14/14，244.69秒 |
| 完整编辑器服务 | 171/171 | 171/171 |
| 暖态512追加 / 空控制 / 正控制 | 0B / 0B / 80B | 0B / 0B / 80B |

Python96/96，9.650秒。两配置原120控件/128076字节对照、UI包11组、UI选择6组、GUI18组GPU对照、LoopStream1349、PresentationCosts76、Player62等保留通过。14项是相关门禁，不是完整E5-D回归或目标机性能验收。

首次标准构建会话89267因可选Grpc模块NuGet漏洞数据TLS错误NU1900停止，尚未进入CTest；日志`out/ecs-e5d-text-v2-build-release-first.log`完整保留。联网同源重试94157成功，审计和警告级别未关闭；第一/第二轮10项选定源码记录逐字段相等。该记录只约束本片选定源码，不能称作完整源冻结。

最终证明 **`out/ecs-e5d-text-v2-gates-verification-first.json`** SHA **0F077F85B210D5A07FA2DC63FF244B8FF3960E23C5C4C4348098CA2D1CF5F805**：

- 绑定7225项文件，使用211项归档重验先前6037项，保留原6,808B失败与全部诊断证据。
- 逐文件核验10个实际新候选包；其中314份DLL/EXE与修订前相应候选包逐字节一致，测试边界修订未改变产品二进制。
- 4份新CPU/DX11源宿主循环记录各4行，功能与记录完整性核验通过，不用短记录宣称性能验收。
- 核验v2新增边界日志、原512调用/严格0B及批准范围外测试正文未改变。
- 正式EXE C50742AD…和四项IDE文件保持原哈希。

本片`functionalGatePassed=true`仅表示明确批准的v2相关功能门禁；`originalV1GateRejected=true`，原6,808B直接原因仍未证明，`fullRegressionPassed=false`、`performanceAccepted=false`。会话2406、89267、94157均终态，无活跃测试可继续轮询。未提交/推送/部署，E5-D整体未完成。

下一步恢复面板同驱动成本对照。旧`run-panel-pairs-first.ps1`仍绑定原失败Debug日志，**不得把旧日志改成通过或直接移除检查**；应另建绑定本节v2证明和新候选的执行入口，保留旧运行器与失败事实。对照驱动、冒烟和完整采样目前均未启动。后续CPU/场景/代表性路线/观察扰动、完整新冻结与回归、正式队列及内存/目标机/人工/自包含/整帧/1h验收保持待完成。

### 11.50 E5-D 面板 v2 固定成本对照

在11.49明确批准的相关功能门禁通过后恢复诊断，未修改产品或正式测试源码。先重验7225项既有绑定；将原来仅准备、未构建的面板驱动逐字节复制到独立`out/verification/ecs-e5d/presenter-panel-probe-v2`。另建`run-panel-pairs-v2.ps1`及对应核验器，明确绑定11.49证明的精确哈希、v2功能通过/原v1拒绝状态和两份新14/14日志；原运行器、原Debug失败和全部历史记录保持原样。

基线为Release包`dce2011bac124b7399622b881678d0fc`，候选为Release包`cfec3afc4c4e4df2baf0a18530177add`。每份30个产品DLL中29个逐字节相同，只有包含四面板冷路径候选的`NcmaEngine.dll`不同。同一诊断驱动三份运行文件用于两变体；驱动只引用归档产品，不重新构建产品。隔离驱动构建0警告/0错误。

#### 固定设计与边界

两种固定场景为空场景和32对象、首个带Transform对象被选中；五种模式为关闭全部面板以及Characters、Graph、Animation、Workflows分别打开。服务均已挂接但空闲，没有活动资产、角色或审批。每进程依次记录冷关闭构建、关闭暖态、首次打开、打开暖态、关闭及重开。窗口尺寸1280×720；检查GUI包字节/动作在进程内及跨匹配场景/面板/变体的一致性，World、Undo、Play、Endpoint状态不变，SnapshotReads恰为1。

只对相同测量包装方法进行PrepareMethod，不预编译产品Build以隐藏其成本。测量使用正常GC，**不扩展NoGCRegion到本对照**；原始分配数值照存，11.48所见计数异常风险仍适用。JIT计数属于进程范围，不作为主线程排他归因。本对照只测托管Build，不含原生GUI、字体、GPU或Present，更不是整编辑器启动/整帧/目标机性能。

运行器拒绝继承的GC、TieredPGO、TieredCompilation、EventPipe和诊断端口覆盖；固定已安装.NET8.0.28的CoreLib/coreclr/clrjit及dotnet宿主文件。驱动报告运行时8.0.28，但未枚举实际加载的原生运行时路径，不称作完整运行时/OS闭包。每进程45秒有界等待，不自动终止或重启。

| 队列 | 固定顺序 | 进程数 | 暖态记录 | 结果 |
| --- | --- | ---: | ---: | --- |
| 独立冒烟 | 一轮AB | 20 | 320 | 全部exit0 |
| 完整诊断 | 三轮AB/BA/AB，交替场景顺序 | 60 | 15,360 | 全部exit0 |

完整队列必须在冒烟完整核验通过后运行，并重验原7225项绑定；冒烟与完整结果不合并统计。冒烟计划SHA **ABAA97CE7C996FBE8589637503640F8D09A4385150BED978269F2FB67ADA4775**，证明SHA **7526867BCDD44BBC332556AE65D5D165877ED51E620306DF72FEA6BEE9F3AA98**；完整计划SHA **3FE1E8C123F395DDCB4C5B234937413DE5EB0E40FA2D5D4919091116828AFE68**，证明SHA **27A007C07699E5F04165DB2F906EC0BCFCC091D365E7AB9B98C1FB3AF477BF2A**。全部原始行保留，没有截断、钳制、按结果增采或选取最佳进程。

#### 观察结果：冷成本向首次开启转移

以下为各组三个进程的中位数，单位ms，A为基线、B为候选。各列独立取中位数，前两项中位数相加不一定等于合计中位数。closed模式的“首次打开”仅为相应阶段的继续关闭构建。

| 场景 | 面板 | 冷A | 冷B | 首开A | 首开B | 冷+首开A | 冷+首开B |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| empty | closed | 28.93 | 23.01 | 0.03 | 0.03 | 28.95 | 23.04 |
| empty | characters | 30.64 | 24.11 | 0.47 | 1.48 | 31.23 | 25.59 |
| empty | graph | 29.41 | 23.98 | 0.58 | 1.35 | 29.99 | 25.08 |
| empty | animation | 26.55 | 27.30 | 0.56 | 1.58 | 27.04 | 28.83 |
| empty | workflows | 26.29 | 23.08 | 0.55 | 2.10 | 26.87 | 25.67 |
| selected32 | closed | 29.74 | 29.10 | 0.07 | 0.07 | 29.81 | 29.17 |
| selected32 | characters | 32.84 | 25.62 | 0.51 | 2.13 | 33.33 | 27.75 |
| selected32 | graph | 27.62 | 25.89 | 0.46 | 1.16 | 28.10 | 27.10 |
| selected32 | animation | 31.03 | 29.68 | 0.77 | 1.93 | 31.57 | 31.57 |
| selected32 | workflows | 30.68 | 26.04 | 0.45 | 1.86 | 31.13 | 27.84 |

30组完整配对中，冷构建23组下降、7组上升；冷构建加首次打开19组下降、11组上升。**24组实际面板首次打开全部增加，增量0.4636–2.0362ms。** 该结果显示成本转移和部分冷路径收益，不支持“所有启动都更快”或“无性能回退”。每组仅三组配对且没有预登记正式统计验收阈值，不补造置信度/显著性结论，也不据此直接接受或回滚候选。不重复相同队列取绿。

`out/ecs-e5d-panels-v2-summary-first.json`保存全部30配对/10组、逐阶段A/B值、配对差值/比值、原始分配与暖态汇总，SHA **F5F7F3848D651C15CFF8946562438A9DA94DE75151B2C5C63F012B72DF5F6AE5**。独立`verify-panel-delivery-v2.py`重新计算两队列证明并精确比较JSON，重验实际输入副本/加载产品哈希/原始输出/跨队列GUI等价及原7225项。

最终证明 **`out/ecs-e5d-panels-v2-verification-first.json`** SHA **5D3AF5460AE7C4998F3DB196D3AC514CFC60FCFAF3B7D40FB9D906CFC03BA684**，绑定7613项文件。正式EXE C50742AD…、四项IDE文件、原v1失败与全部历史证据未变。一次只读PowerShell表格展示因foreach管道语法失败，改用先收集数组再显示；未改变文件或样本。会话22143已正常终止exit0，无活跃工作可轮询或重启。

本片`diagnosticOnly=true`、`performanceAccepted=false`、`productChanged=false`、`fullRegressionPassed=false`；功能通过仅引用先前v2相关双配置门禁，原v1仍拒绝。未提交/推送/部署，不放行E/F/M8.3。下一步评估主Build内大块冷分支拆分，保留所有调用入口、GUI/动作语义及首次打开/重开成本；**本片尚未实施主Build拆分**。任何后续编辑或构建前，先归档受影响的7613项绑定输入。CPU/场景/代表性路线、观察扰动、含SDK/HLSL/工具的完整冻结、全回归/正式队列、内存/目标机/人工/自包含/整帧/1h仍待完成。

### 11.51 E5-D 主 Build 分支拆分与相关门禁

11.50完成了固定对照和独立核验，本片推进产品实现，不重复原采样。编辑前重验7613项绑定，把其中156项可能被源码编辑/构建覆盖的文件及两配置包索引保存到`out/verification/ecs-e5d/pre-build-split-first`；清单SHA **0B1533E87F1AFAE67D2E95FF1A6FEAED23B8870662A90E1B088B272F9D58F88E**。旧输入、失败和全部性能观察仍可按原哈希复核。

新增`EditorPresenter.BuildSections.cs`，从主Build提取五个独立NoInlining方法：

| 方法 | 原调用条件/职责 |
| --- | --- |
| BuildSceneFileControls | 非统一布局或场景文件浮层打开；场景路径/保存/历史/浏览按钮 |
| BuildSelectedInspector | 存在选中对象；组件/JSON页/脚本绑定/属性编辑 |
| BuildLocalPreferences | 偏好面板打开且存储与待编辑值存在；本地设置历史 |
| BuildFbxPreview | 预览服务存在、预览未禁用且对应布局/菜单条件成立；FBX隔离预览 |
| BuildActionPreview | 动画预览服务允许显示，面板打开且Current存在；数值/骨骼画布 |

**显示条件继续在原调用处判断**，未预热或加载关闭的模块，未增加缓存/组件镜像/权限入口，保留控件顺序、编号、共享labelId引用和副作用顺序。五段业务体原样搬移，必要的原局部值通过参数传递；FBX和设置服务以非空参数传入。独立核验器把五个方法体放回调用点后，重建源码与归档原文件逐字相等，包括正文空白；此证明约束机械提取，不代替运行行为和性能测试。

新增`PresenterBuildSectionTests`四组并注册到完整服务测试（预计171→175）：

- plain、toolbar、workspace三种入口分别检查重复Build、首次打开/关闭/重开、GUI项/UTF8字节/动作映射一致性，保持文档、Undo、Play、Endpoint和SnapshotReads不变。
- 动画面板标志打开但Current为空时不进入动作预览体。
- ShowPreviews=false时即使预览服务已Dispose，普通和统一布局都不触碰它们；守卫错误会直接抛出而不能被静默吞掉。

上述前三种布局合计三组，最后关闭守卫一组。原服务集仍包含真实FBX导入/复制数据/画布、动画命令及过期回执、偏好历史、组件JSON分页、UI/Graph和GPU路径；本片未以新轻量检查替代这些原测试。

两配置定向构建均0警告/0错误；每配置新增4组、原面板5组及文本/分配v2的9组均通过，原512次严格0B/正控制80B保持。会话88142正常终态。源码-only核验已重验原7613项及五段逐字重建；尚不宣称完整门禁通过。

准备提取脚本首次PowerShell语法解析失败，未改产品源码；原文件保存在`prepare-build-split-first.syntax-failed.ps1.txt`。仅修正生成补丁的括号后通过apply_patch应用机械提取。首次标准构建79950在可选Grpc测试的NuGet漏洞数据查询处NU1900失败，尚未进入CTest，保存`out/ecs-e5d-build-split-build-release-first.log`，不关闭审计或降低警告级别。

**会话20545已正常终止exit0**。同源联网`run-build-split-gates-second.ps1`完成两配置：

| 验证 | Release | Debug |
| --- | --- | --- |
| 标准候选 | `bef53151937f49058154e1df8234b2b7` | `58362a52a5d5463b809fd7b925218736` |
| 构建警告 | 0 | 0 |
| 相关CTest | 14/14，238.25秒 | 14/14，248.34秒 |
| 完整编辑器服务 | 175/175 | 175/175 |
| 定向Build分支/原面板/分配v2 | 4/5/9组通过 | 4/5/9组通过 |

Python96/96，9.714秒。UI包11组/选择6组、GUI18组实际GPU对照、循环记录1349、PresentationCosts76和Player62等在两配置中均通过。原v1失败保留，NoGCRegion仍只在批准的测试边界内，没有扩大到产品或性能诊断。

`verify-build-split-first.py`完整模式已执行：证明`out/ecs-e5d-build-split-verification-first.json` SHA **287728CA40B609DDF00FF810B7398380ABD1D342A68E45E8F6A53C0FAACFFFBC**，绑定8799项文件。独立重验归档7613项、五段逐字重建、第一/第二轮9项选定源码相同（非完整冻结）、10个实际包清单和两配置4份源宿主4帧记录。与拆分前候选相比，产品DLL/EXE中仅每配置editor包的`NcmaEngine.dll`改变；Player等包二进制不变。正式EXE C50742AD…和四项IDE文件保持原哈希。

这些是相关功能门禁，不是完整E5-D回归或性能验收。旧面板队列只适用于11.50旧候选；本候选的新对照见11.52。不提交/推送/部署，不宣称E5-D完成。完整冻结（含SDK/HLSL/工具）、全回归/正式队列、观察扰动、代表性路线、内存/目标机/人工/自包含/整帧/1h等仍待完成。

### 11.52 E5-D 主 Build 固定新旧对照

在11.51所有构建/测试终态且完整证明通过后才构建并运行隔离驱动，没有与标准回归同时采样。基线为拆分前Release `cfec3afc4c4e4df2baf0a18530177add`，候选为新Release `bef53151937f49058154e1df8234b2b7`；同一驱动三份运行文件，30产品DLL中29份相同，仅`NcmaEngine.dll`不同。所有输入实际复制并比对，运行器绑定11.51证明精确SHA，每次队列前重验8799项文件。

驱动在独立`presenter-build-probe-first`，不修改产品/正式测试。首次编译漏掉System.Runtime命名空间，4条CS0103；原日志和源码`.compile-failed.cs.txt`保留。仅补using后构建0警告/0错误，之前未启动任何采样进程。

#### 固定工作负载与限制

七种场景：空场景关闭状态、32对象且选中首个Transform对象、场景文件浮层、从未选中到选中的32对象检查器、本地偏好设置、已导入FBX预览、已打开数值模块的动作预览。每进程记录准备、冷关闭Build、关闭暖态、首次打开、打开暖态、关闭和重开。1280×720，测量区间只包含托管Build调用；切换标志/选择在区间外、双方一致。

FBX和动作预览使用同一受检Release `out/build/windows-ninja-release/NcmaNative.dll`。FBX正常Import会生成随机AssetId及派生网格ID，无法逐字比较跨进程GUI；因此**仅诊断准备阶段**通过公开ImportedCharacterResource创建固定UUID资源，再用反射按Import相同的资源所有权/State/Commit流程建立预览，检查AssetId和Revision。没有修改产品Import或测量正文，也没有对输出做UUID替换。完整原始报告和GUI字节仍精确比较。资源导入/模块打开属于单独setup，不伪装为启动或导入成本优化。

运行后读取实际CoreLib及coreclr/clrjit/Ncma原生与托管模块路径和SHA，与输入清单匹配；不称作完整OS闭包。正常GC运行，原始分配计数不校正、不夹零，已知计数器异常限制仍适用；JIT指标是进程范围。没有原生GUI字体栅格化、GPU绘制、Present或代表性游玩路线，因此不是整帧/目标机验收。

| 队列 | 顺序 | 进程 | 暖态记录 | 结果 |
| --- | --- | ---: | ---: | --- |
| 冒烟 | 一轮AB，七场景 | 14 | 224 | 全部exit0、语义核验通过 |
| 完整诊断 | AB/BA/AB，每轮场景顺序旋转两位 | 42 | 10,752 | 全部exit0、语义核验通过 |

完整队列只在整个冒烟证明通过后启动。每进程45秒有界等待，无自动强杀/重启。所有World/Undo/Play/Endpoint/偏好版本/预览状态保持不变，SnapshotReads为1；每种场景的关闭、打开、关闭返回和重开GUI项/UTF8字节/动作映射在匹配进程间完全一致。

冒烟计划SHA **11C0202D390EEA2D7D8001C4C9101F245D79D61B8B9885751794497A5BFAA11A**，证明SHA **5972A71833800B97AAD093729720D6512DE719D3B11BD9D410FC24A64A953765**；完整计划SHA **947348E3BA95BD80CACF2AA78BEE1A3F1419C0224954A3E859C7017317CAAB8B**，证明SHA **D82B327030F2186013B0D2FBA5BC1480985F3BB28FAC9E3CF64669389E64A842**。冒烟和完整不合并统计，不按结果补样或选最好进程。

#### 全部场景的观察

各组三进程中位数，单位ms，A=基线、B=候选。中位数分别计算；closed_empty与selected32的“首开”是继续相同状态的Build。

| 场景 | 冷A | 冷B | 首开A | 首开B | 冷+首开A | 冷+首开B |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| closed_empty | 23.9911 | 16.2287 | 0.0247 | 0.0278 | 24.0158 | 16.2518 |
| selected32 | 30.3543 | 22.6343 | 0.0723 | 0.0822 | 30.4266 | 22.7165 |
| scene_files | 28.1931 | 19.2910 | 0.2927 | 0.6667 | 28.5050 | 20.0687 |
| inspector32 | 25.0447 | 17.7861 | 3.4508 | 7.3432 | 28.5414 | 25.1293 |
| preferences | 24.6237 | 17.3833 | 0.0851 | 0.3955 | 24.7088 | 17.7181 |
| fbx_active | 24.9207 | 15.8923 | 15.0208 | 15.9012 | 40.0593 | 31.7935 |
| action_active | 24.7646 | 17.3172 | 5.3024 | 6.0671 | 30.0670 | 23.3843 |

21组配对冷构建及冷+首开全部下降，合计比值0.590174–0.916606。**首次打开总计17组上升、4组下降；排除两个不切换的场景后，真实打开的15组中14组上升**，检查器增加2.4759–4.2067ms。不能只用冷构建收益掩盖交互第一次使用的延迟。

暖态打开Build的21组中16组下降、5组上升。selected32的组中位数从0.04035ms到0.05225ms，其中一组配对约1.52倍；FBX也有一组约1.05倍、增加约0.06ms。绝对值小不等于已经证明无回退，保留全部配对和暖态原始样本，后续实际路线需覆盖首次选择和持续选中状态。每场景仅三组配对，没有预登记正式统计验收阈值，不补造置信度/显著性或通用改善结论。

暖态分配集合逐场景新旧一致：空1992B、选中/检查器10000B、场景文件2664B、偏好2704B、FBX727352B、动作102832B。它们是本诊断正常GC原始计数，不是全帧零分配或驻留内存结论。部分冷样本相差49,200B，保留原值，**未证明来自产品分配、JIT或已知GC记账问题**；不据此宣称确定的内存改善。

独立`verify-build-pairs-delivery-first.py`重新计算冒烟和完整证明并精确比较，检查跨队列GUI等价，重验8799项既有绑定和实际加载模块。最终 **`out/ecs-e5d-build-pairs-verification-first.json`** SHA **EF980501EF4CC32AF39C62593B55D7EC60A68400A249C5E02218FFD01875B77C**，绑定9055项文件，内含全部七组/21配对/逐阶段差值与比值及暖态中位数，不丢弃不利结果。

20545（标准门禁）、2483（完整对照）均已exit0，全部进程终态，无待轮询或需终止工作。正式EXE、四项IDE文件、原v1失败与历史证据不变，未提交/推送/部署。当前相关功能通过、诊断显示局部收益，`performanceAccepted=false`、`fullRegressionPassed=false`。下一步转向实际宿主CPU字体/场景成本、代表性路线和观测扰动，纳入首选对象及暖态风险，不重复本微对照取绿。任何新编辑/构建前先归档受影响的9055项绑定输入；完整冻结/全回归/正式队列、内存/目标机/人工/自包含/整帧/1h验收仍待完成，不放行E/F/M8.3。

### 11.53 E5-D 真实宿主与观测开销固定对照

本片不改产品/正式测试源码。先检查11.51实际宿主4帧记录：首个DX11 GuiBuild段仍约40ms，而不是11.52隔离空场景约16ms；二者场景、调用链、启动历史和观测范围不同，不能直接归因或把微测试收益外推。源码同时确认CPU字体图集在GuiPlugin.Create中`GetTexDataAsAlpha8`构建，属于GuiSession创建/宿主启动，**不在现有逐帧分段内**；GuiBegin的时间不是完整CPU字体创建时间。

#### 实际入口与固定计划

`run-host-observer-first.ps1`先重验9055项历史绑定，再按实际部署清单分别复制两个Release编辑器的144个文件与清单：

- A：拆分前`cfec3afc4c4e4df2baf0a18530177add`。
- B：拆分后`bef53151937f49058154e1df8234b2b7`。

每个进程各用一份相同9文件的独立静态四边形场景夹具，来自11.51的Release源宿主测试；只允许新夹具生成自己的`out/user/logs/editor-candidate.jsonl`。原候选包、原夹具及历史日志均不写入。复制后校验所有清单哈希/大小/相对路径，并核对最终目录集合没有额外产品输出；场景输入逐文件不变。此处未采集截图/宿主内存World快照，**不是新的逐像素或内部状态等价证明**；已有功能测试与GUI字节对照仍是各自范围的证据。

执行的是复制后的`NcmaEngine.exe`正式入口，不替换宿主循环。每进程`--project ... --smoke-test --frames 120 --max-fps 30`，CPU模式另加`--cpu-only`。窗口由smoke路径隐藏，1280×720；没有交互游玩或目标机1920×1080/Low路线。

| 维度 | 固定值 |
| --- | --- |
| 运行模式 | DX11、CPU-only |
| 观测模式 | off；`--loop-stream`；`--loop-stream --presentation-stages` |
| 新旧次序 | 三轮AB/BA/AB，每轮六种模式顺序旋转两位 |
| 进程 / 帧数 | 36进程，各120帧，合计4320帧 |
| 限时 | 每进程60秒，超时保留身份，不自动终止/重启 |
| 计量 | 父进程启动调用前到退出观察的wall、子进程累计全部线程CPU；有观测时保留全部loop原始行 |

预登记SHA **45F396E47CE5576C1832F43318351A031714419B4829526E8E4EAF9F8E47C19E**。正常GC，不改变帧率阈值、环境或产品行为，不按结果增加进程。系统字体msyh.ttc、配置运行时文件和全部输入固定；CLI没有返回实际加载的CLR模块路径，因此本片不宣称完整运行时/OS加载闭包。

#### 覆盖与遗漏

36进程全部exit0，均报告120帧及资源释放。off的12进程无测量JSON或循环文件；24个有观测进程合计**2880条全程循环记录**，各120行、无队列/预算/失败丢失，保留首轮和慢行。DX11每进程120次Present返回，CPU为0，不伪造显示帧。

12个stages进程每个240次调用，仅保留原合同的前64次（32帧），共768条；总共**2112条调用遗漏**明确报告。分段前缀不能代表后88帧，也不能替代全程循环数据。原始分段相加等于调用wall/分配量，失败调用为0；没有更改容量来隐藏遗漏。

#### 新旧与观测结果

下表为每组三进程中位数，单位ms；A/B各自取中位数，不是配对差值的中位数。进程wall包括启动/关闭/父进程启动与退出观察开销，并受30fps上限影响；CPU是所有线程累计，不能称为主线程wall或独占CPU耗时。

| 模式 | 观测 | 进程wall A | 进程wall B | 进程CPU A | 进程CPU B | 首轮 A | 首轮 B |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DX11 | off | 5262.82 | 5246.01 | 2515.63 | 2171.88 | 无记录 | 无记录 |
| DX11 | stream | 5249.37 | 5278.27 | 2328.13 | 2484.38 | 110.16 | 109.66 |
| DX11 | stages | 5305.52 | 5317.62 | 2562.50 | 2546.88 | 112.54 | 113.19 |
| CPU | off | 4561.99 | 4562.42 | 1312.50 | 1250.00 | 无记录 | 无记录 |
| CPU | stream | 4630.39 | 4590.56 | 1437.50 | 1437.50 | 69.89 | 57.45 |
| CPU | stages | 4611.39 | 4611.37 | 1484.38 | 1453.13 | 65.44 | 59.59 |

全部有观测进程的范围：

- DX11首轮107.7221–126.4366ms，父启动调用前到首轮开始1111.6285–1173.0623ms；后119轮Presentation段的逐进程中位数0.7401–0.8078ms。
- CPU首轮51.5874–73.7053ms，启动调用前到首轮507.7887–585.3398ms；后119轮Presentation段中位数0.3785–0.4164ms。
- DX11成功Present返回之间的最大间隔约33.61–34.22ms，**不包含第一次Present之前的启动/首轮延迟**，不能拿它声称没有超过80ms的单次慢帧，更不代表物理显示FPS。
- 新旧配对结果有升有降：没有复现11.52“全部冷+首开下降”的一致性，也没有由三个进程/组证明宿主性能退化的统计结论。保存全部18组产品配对，不挑选off较好或CPU较好的一组做最终结论。

24组同变体、同轮次的on/off比较中，累计进程CPU有23组上升、1组下降，原始差值-203.125至437.5ms。测量有调度/计时粒度影响，且包括额外JIT、线程、序列化、启动/关闭，**不能把差值除120后当作确定的每帧观测成本**。原始全部进程CPU值可见约15.625ms级离散粒度，不把其小数位当成相应精度。

循环发布总wall约0.49–0.66ms、报告分配0B；写线程总wall约31.50–49.42ms、分配约0.935–0.960MB。这些作用域重叠且并非整个观测开销，不能相加作为总CPU成本，也不证明观测无扰动。

首轮stages中，DX11 GuiBuild约34.03–43.32ms、SceneSubmission约23.13–30.02ms、NativePresent约11.52–18.98ms。名为Observer的阶段约8–11ms，但PresentCore等被调方法的首次JIT发生在下一阶段标记之前，可能计入该段；**阶段名称不是纯观测开销或根因证明**。启动约1.1秒同样包含清单校验、CLR/JIT、窗口、字体、渲染器和场景准备，当前尚未分开。

#### 证据和下一步

独立`verify-host-observer-first.py`核对36次启动/退出身份、固定次序、原生入口参数、实际包复制/目录集合、原夹具与9055项历史文件、全部循环/分段合同、off无观测输出及仅允许的新运行日志。最终 **`out/ecs-e5d-host-observer-verification-first.json`** SHA **3830EA79092C2A4EF362D133CDAD5314D0D3E6E30D6A7A87F9528091ACDB65D9**，绑定9819项文件，包含全部36进程观察、18组产品配对和24组观测配对。

25488（宿主队列）、81126（核验）均正常终止exit0，没有活跃会话可轮询或重启。正式EXE C50742AD…和四项IDE文件原哈希不变，没有产品代码修改、提交/推送/部署。

当前是新的实机宿主诊断证据，不是目标机/代表性路径/完整性能通过。下一步进行有界启动与首帧归因，先区分清单校验、CLR/JIT、窗口、CPU字体图集、渲染器和场景准备，再选择修复，不直接假定字体是约1.1秒的主要原因。首次选择对象/实际路线、观测扰动、完整冻结/全回归/正式队列、内存/目标机/人工/自包含/整帧/1h仍待完成；编辑或构建前归档受影响的9819项绑定，不放行E/F/M8.3。

### 11.54 E5-D 默认关闭的启动分段观测

用户确认的受检 NoGCRegion v2 已按11.49落实；本片没有再次改写该测试、放宽512次调用/严格0B断言或改变产品GC。原6,808B失败保持拒绝状态。

#### 实现边界

新增 `StartupCostCapture` 和 Editor 的显式 `--startup-costs` 开关，默认不创建采集器、不输出启动报告。仍存在参数检查和空引用分支，不声称默认路径绝对零开销。只记录顺序的 `MainPreparation`、`PresentationStart` 两次调用，各固定24个阶段；同一采集器不允许覆盖或重试失败调用，跨线程访问拒绝，返回数据为独立副本。

入口分为包校验、参数、项目与宿主构造；服务分为场景/着色器预检、日志/插件、节拍/物理、窗口、偏好、GUI创建、渲染器、图标、编辑器所有者、文档、创作服务、渲染资产、环境、HUD、游戏程序集、Presenter、场景同步与诊断。原业务顺序不变。失败时在finally中结束未完成调用，常规ApplicationLifetime仍独立负责回滚和资源释放。

报告v1包含原始Stopwatch时间戳和有符号线程分配差值，各阶段与调用总量精确分区。**这些是嵌套工作/JIT/IO/观测在内的wall段，不是纯CPU或GPU时间。** 不包含进程/CLR入口前、入口方法JIT、采集器构造、报告复制/序列化、调用间空档、循环、回滚与关闭。`GuiCreate`包含托管与原生工作，不是独占字体图集耗时；阶段进入次数也不等于内部实际工作次数（如CPU模式下Renderer条件段）。

新增30项单元检查；实际CPU/DX11服务启动及失败启动追加10项检查，参数位置识别追加3项检查，PresentationCosts共89项，包括正常阶段、精确分区、关闭后不变、部分失败和常规清理。实际复制候选EXE检查覆盖CPU/DX11各开/关、重复参数、未知参数、缺失项目、缺失插件及作为参数值的开关文本；这是功能检查，不是固定性能队列或旧/新速度对照，原夹具失败如下保留。

#### 当前验证状态

编辑前保留受影响157项可变文件与包索引，清单 `out/verification/ecs-e5d/pre-startup-costs-first/manifest.json` SHA **6DAB6504254A26EB1A0A03C7456F8398D6DDCCD93969D6FF92C4DE53EB8EF54A**，用于历史9819项绑定的重验，不宣称完整源码冻结。

直接Debug/Release构建均0警告，新30项均通过。标准第一轮在可选gRPC的NuGet漏洞数据源查询失败（NU1900）且未进入CTest；原日志保留，没有关闭审计。第二轮保持9项选定源码一致，联网重跑成功：Release/Debug各14/14（236.95/244.39秒）、服务各175/175、PresentationCosts各86项、Python96/96（9.698秒）。原失败并未被覆盖。

静态复核发现原始 `args.Contains` 会把 `--loop-stream --startup-costs` 中作为输出文件名的值误判为观测开关。第二轮终止后先归档9项输入（`pre-startup-option-fix/manifest.json` SHA **F8EF61C738F8666AAC2F1FDBD7ECCC26B5C186BC01361F47FAD08B595A7F90B9**），再修改为跳过五种带值选项的预扫描，补3项参数检查及第9个实际CLI用例。只改Editor.Program和对应Rendering.Tests；没有在运行中的门禁上改源码。

最终第三轮同源构建0警告，Release/Debug各14/14（234.93/242.17秒）、服务各175/175、PresentationCosts各89项、启动单元各30项，Python96/96（9.579秒）。最终候选为Release `f49a1375b1f348abaa251028317e997b`、Debug `a6a11db6b6a64b2d879bc8a13bb844a9`。两配置均验证512次追加0B、空区间0B及正例80B，没有修改原v2测试源码。

#### 真实CLI与独立证据

初版计划 `startup-cli-first/plan.json` SHA **650352DC6A474824F0A12F2CC3F1F55CF7586F2F6AC5E2A3A06E6CDAB2BA5C69** 固定运行9进程。前8项正确完成；第9项错误地期待不带 `.ncloop` 后缀的输出文件名通过，实际按既有校验退出2，未输出启动报告。**原计划仍为失败，日志、退出码和错误期待均未重写；不是产品故障，也不是性能采样。**

另建单进程合法夹具：将同一包的插件复制到新项目的 `--startup-costs` 目录，作为 `--plugins` 的值，不提供独立观测开关。计划 `startup-cli-value-second/plan.json` SHA **8B2CB54DFDB0DF00105E8B0B19D8EAA82B3F91BD3F1BA95FF207FBA6CFBF9614**。实际EXE退出0、完成4帧且无启动报告，证明参数值不误开观测；没有改产品路径/文件后缀校验，也未重跑前8项挑选结果。共10个实际进程，9项有效功能检查通过，初版第9项夹具失败单独保留。

独立 `verify-startup-first.py` 实际重哈希12581项文件：通过157项归档保持9819项历史绑定，核验10个最终包及10个中间版本包、两配置6份服务启动报告、4条各4帧的完整流、所有CLI输入/日志和原失败。12个记录结构反例拒绝；有符号原始分配差值保留、不钳零、不当内存验收。第一次仅语法检查时使用系统默认GBK读取UTF-8脚本失败；改用显式UTF-8读取后通过，未改变脚本内容或任何测试结果。

最终证明 **`out/ecs-e5d-startup-verification-first.json`** SHA **BFF417A2539F0110F5FB2CB7691FC920EA536277335D46D86892C8EFB8E8A777**。明确 `functionalGatePassed=true`、`originalCliPlanPassed=false`、`originalCliFixtureFailurePreserved=true`、`correctedValueCasePassed=true`，以及 `fullRegressionPassed=false`、`performanceAccepted=false`。原UTF8 v1失败、v2历史证明、四项IDE修改及正式EXE C50742AD…均保留。14699、18893、67828均已正常终止，无活跃门禁或测量会话。

本片相关功能门禁完成，未提交/推送/部署，不放行E5-D整体、E/F或M8.3。下一步用明确范围的新候选观测做有界启动/首帧归因；旧36进程数据仍属于旧产品，不与新数据混用。任何后续编辑/构建前先归档受影响的12581项绑定。完整冻结/回归/正式队列、观测扰动、首次选择/代表性路径、内存/目标机/人工/自包含/整帧/1h仍未完成。

### 11.55 E5-D 启动成本分离与渲染准入诊断

本片没有修改产品/正式测试源码。受检NoGCRegion v2仍只用于已批准的UTF8分配微测试及其边界控制；以下进程全部正常GC，不改变GC/JIT策略。原6,808B失败、E5-C v1拒绝及所有历史证据保持原判。

#### 固定真实宿主队列

`out/verification/ecs-e5d/run-startup-attribution-first.ps1`先重验12581项历史绑定，复制最终Release包`f49a1375b1f348abaa251028317e997b`的144项清单文件及清单，每个进程使用独立的相同9文件静态四边形项目。原包/夹具不写入；只允许新项目自己的运行日志。预登记SHA **A678903A7F619B4DA5FEB3F95D0791057D62DC5EA5B718A0F67FD273B747FE76**。

固定CPU/DX11 × off/startup/combined × 三轮，共18个真实`NcmaEngine.exe`进程，每轮六种场景次序旋转两位，不根据结果追加采样。combined为启动观测加全程循环流及presentation分段；各进程隐藏1280×720、120帧、上限30fps。18个全部exit0，共2160帧；12个启动观测进程记录24次调用，6个combined进程保留720条全程循环记录且无丢失。presentation前缀保留384次调用，**1056次调用遗漏**明确报告。

startup-only的三轮结果如下，均为wall ms，包含各段内部JIT/IO/观测，不是独占CPU耗时：

| 模式 / 阶段 | 第一轮 | 第二轮 | 第三轮 |
| --- | ---: | ---: | ---: |
| DX11 父启动调用至Main观测开始 | 39.1031 | 39.6507 | 44.0431 |
| DX11 Main准备 | 106.1949 | 117.8349 | 107.7927 |
| DX11 服务启动 | 1000.0264 | 955.7134 | 1023.5234 |
| DX11 Renderer段 | 479.0689 | 460.1988 | 487.5913 |
| DX11 GuiCreate段 | 28.8695 | 26.0680 | 25.6718 |
| CPU 服务启动 | 376.8636 | 392.2890 | 369.7798 |
| CPU GuiCreate段 | 26.6363 | 28.9678 | 29.5386 |

Main与服务调用间空档也保留：DX11为9.1631/11.6031/10.5939ms，CPU为7.3414/7.4681/7.6207ms，不塞入相邻段。Renderer段包含设备构造、窗口surface、九包完整着色器准入、服务/缓存构造和GUI绑定，**不能称为纯设备创建或纯D3DCompile**。DX11创作服务101.51–118.09ms、场景同步87.94–99.85ms、文档82.84–89.84ms，亦未忽略。

combined的DX11父启动至首轮开始1160.4196/1171.6900/1169.6832ms，首轮131.3339/104.6648/108.3913ms；CPU为552.8678/554.7791/513.5798ms及61.6750/60.7925/53.7826ms。服务结束至循环开始空档约0.75–1.41ms。首轮DX11 GuiBuild33.47–43.10ms、SceneSubmission24.62–30.84ms、NativePresent13.32–21.90ms；Observer段8.98–10.20ms包含被调入口JIT等，不能当纯观察器成本。未关闭首轮慢帧风险，不能用后续Present返回间隔代替首轮。

全部12组观测开/关配对保留在证明中。startup相对off的累计全线程CPU差值：DX11 -156.25/0/-171.875ms，CPU -46.875/+140.625/-62.5ms；combined相对off分别+140.625/+468.75/+31.25ms和+234.375/+328.125/0ms。这是包括启动/关闭的粗粒度进程CPU，不能推导固定每帧开销或把负值认定为无扰动。预读/哈希/复制已触及文件，**首次进程不等于冷磁盘**；父启动区间也包含启动器/OS/CLR/JIT。CLI未追踪实际模块加载路径，不能宣称完整OS闭包、截图或World状态等价。

独立证明`out/ecs-e5d-startup-attribution-verification-first.json` SHA **C9320EC6A41B42578ED18AFDB000D2F48AC40143E453B93F98DB7A4CE7CB26AB**，绑定12954项、保留全部12581旧绑定，验证实际包/夹具、启动时间戳精确分区与显式空档、off无报告及全部循环/分段记录。78801/7347均terminal exit0。

#### 同产品隔离渲染探针

为进一步分开Renderer内部工作，在`out/verification/ecs-e5d/renderer-startup-probe-first`新增仅使用公开API的独立驱动，不改产品或引入friend入口。复制的30份产品DLL与同一最终Release包完全一致，驱动构建0警告/0错误。固定19段：文件预检/插件/窗口/GUI/设备/surface/完整首次准入/服务/缓存/GUI绑定，再逐项重复九种包的准入。第一次仍调用完整`RuntimeShaderFileSet.InstallSelection`，不跳过未用变体；重复阶段不重新安装默认选择。

预登记SHA **CB5F87DEAFB8F3FD76EDBFF2CD46C5932C9E9945F226EA9018D0385172BE0D21**。固定三次新进程，全部exit0，3次首次完整准入及27次包重复准入。无Draw/Present/reference工作组，检查验证错误为0、最终原生LiveResources为0；实际加载模块路径/哈希采集在所有测量段之后。

| 探针wall ms | 进程1 | 进程2 | 进程3 |
| --- | ---: | ---: | ---: |
| 设备构造 | 197.6943 | 213.6324 | 187.2871 |
| 九包首次完整准入 | 297.8199 | 259.7656 | 265.8626 |
| 九包重复准入合计 | 2.0650 | 1.9396 | 1.9680 |
| GUI创建 | 30.2436 | 30.9419 | 29.0722 |

实际设备为**NVIDIA GeForce RTX 5060 Ti**，validation=1、vsync=0，实际.NET8.0.28/CoreCLR/CLRJIT/D3D11/D3DCompiler及产品模块已绑定；不是目标GTX1060/RX580硬件。孤立公开API调用仍包含嵌套JIT/IO/校验，不能直接当作真实宿主的独占因果分解，更不能将首次与重复差值全归于编译。

初版核验器只因Windows路径大小写字符串不同而失败：`C:\Windows\System32\d3dcompiler_47.dll`与`C:\WINDOWS\SYSTEM32\D3DCOMPILER_47.dll`实际为同一文件，大小4517376、SHA **F642940FFB5DCA7E548BA2270686798CF6B4C74B60E88BBB22F0991868CAE3E5**。原脚本/失败日志保留；另建second核验器，仅在`os.path.samefile`且大小/哈希全部相等时接受同文件别名，报告两个拼写。没有修改样本、驱动、门槛或重跑探针。

最终`out/ecs-e5d-renderer-startup-probe-verification-second.json` SHA **75EA1B828D5E8BF5068490357FD7CEB5CAED57286B32C01F0C8F1B8F3EDF0707**，绑定13011项、保留12954项历史、记录一个路径别名和原核验失败。明确`diagnosticOnly=true`、`performanceAccepted=false`、`actualHostExclusiveCauseProven=false`。

#### 下一片实现边界

源码核对发现`RegisteredStages::ValidateCode`在每个renderer首次参考缓存缺失时运行D3DCompile+D3DReflect；输入字节码仍逐次做反射、签名/资源/成员布局比较及蒙皮线程组/步长检查。普通/环境参考缓存上限10/4，不能扩大或绕过。既有GUI已使用`tools/gui-shaders/CompileGuiShaders.cpp`及CMake受检构建期生成嵌入字节码，可作为机制参考，但不可直接套用GUI的entry/profile/flags。

下一片评估**仅将固定参考着色器的编译移到构建期、嵌入DXBC**：绑定实际参考源码/变体/entry/target/编译flags，保留全部九包启动准入、实际输入反射及所有结构校验、错误传播、缓存上限、运行时创作编译与源码复制API，不改ABI、不延迟未用变体。候选尚未实现，也没有宣称收益；需归档受影响的13011项绑定后再编辑/构建，补源码到生成物闭包、构建失败与全部准入负例、双配置功能门禁及固定新旧对照。

所有本片进程已结束，无活跃会话可轮询。正式EXE SHA **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**及四项IDE修改保持不变，未提交/推送/部署。完整冻结（含SDK/HLSL/工具）/全回归/正式队列、首次选择/代表性路径、观测扰动、内存/目标机/人工/自包含/整帧/1h仍待完成，不放行E5-D整体、E/F或M8.3。

### 11.56 E5-D 固定参考着色器构建期编译候选

11.55完成了启动成本分离，本片据此实施候选，不继续重复旧队列取绿。编辑前重新核验全部13011项旧绑定，将442项受影响源码、构建产物和候选索引保存在`out/verification/ecs-e5d/pre-closed-references-first`；清单SHA **15A86206B2951401F7B18550694F6828B95262D03A7514B006D51F52AFD338CD**。这是历史输入保留，不是全仓库源码冻结。

#### 设计与实现

新增`ClosedShaderSources.h`，集中原Scene、Skin、UI、Post参考源码和环境变体转换函数。四组源码字节及环境转换逻辑与编辑前归档完全一致；Scene/Skin/UI的原业务实现仅改为引用共享源码。运行时源码复制和用户创作编译API保留。

`ClosedShaderDefinitions.h`给构建工具定义16组闭合参考：普通场景含开/关阴影和阴影pass共6组、Skin 1组、UI 2组、环境开/关阴影4组、Post 3组。每组固定完整源码、入口、目标profile和原sourceName。编译参数继续使用strictness、warnings-as-errors、optimization-level3；生成物与运行时flags用static_assert绑定。

新增构建宿主工具`NcmaClosedShaderCompiler`，只链接D3DCompiler，不创建设备、不读取项目着色器、不启用includes/macros。全部16组成功后才打开输出头文件，编译警告/错误、重复键、输入/字节码越界和输出失败均使构建失败。CMake将生成物作为Renderer及原生测试的显式前置依赖，字节码嵌入DLL，不增加部署文件或运行时缓存文件。

`ClosedShaderReference::Find`使用生成物中的**精确源码/入口/profile/sourceName**查找，未知或不匹配返回空，绝不回退到运行时编译。只替换`RegisteredStages`与`PostProcess`的固定期望参考D3DCompile；运行时D3DReflect及原实际输入的签名、资源、常量成员、类型、蒙皮线程组/结构步长等检查不变，缓存预算仍10/4及原Post槽位。九包完整启动准入未改，没有延迟跳过未使用变体，没有修改ABI或C#策略。

#### 已完成的定向检查

- 独立源码核验通过：13011项旧证据经归档保持完整；四组字面量/环境转换完全一致，Scene/Skin/UI业务体按指定机械替换后逐字相等；创作编译服务、实际输入校验后半段、UTF8受检NoGCRegion v2原样保留。
- Debug/Release原生构建通过，两配置各2/2测试（Release13.35秒、Debug13.23秒）。
- 每配置16组生成DXBC与旧路径D3DCompile的结果逐字节相等、反射结构双向相等；64项错误源码/入口/profile/sourceName查找拒绝。该独立旧路径只在测试中编译，不恢复产品回退。
- 构建测试校验同输入生成结果确定；无效输出位置拒绝。复制实际构建工具及源码到独立夹具，注入C++仍有效但HLSL无效的源码后重新编译工具，验证生成失败且既有头文件保持原哈希、新输出不存在；所有夹具和日志保留。

#### 标准候选相关门禁与交付核验

第一轮标准Release构建在可选gRPC的NuGet漏洞数据源查询遇到NU1900，未进入CTest；原`out/ecs-e5d-closed-build-release-first.log`保留，没有关闭漏洞审计。候选源码保持不变，第二轮联网按同一标准流程完成两配置构建（0编译警告）及相关回归：

| 门禁 | Release | Debug |
| --- | --- | --- |
| 标准相关CTest | 15/15，210.09秒 | 15/15，222.19秒 |
| 编辑器服务 | 175/175 | 175/175 |
| 启动单元 / PresentationCosts | 30 / 89项 | 30 / 89项 |
| GUI逐像素参考 | 18项 | 18项 |
| 原UTF8 v2控制 | 512次0B、空0B、正例80B | 512次0B、空0B、正例80B |
| 最终候选 | `05f185337d0a41cc92691e1a6e40a83b` | `0dda84c1906f452aa41a412ddbdd95a4` |

Python96/96通过（9.804秒）。20项选定源码在第一轮、第二轮及结束时内容一致；一次临时文本比较因JSON属性顺序不同报告差异，改按字段、数组次序和哈希核验后无源码差异，原回执未重写。该回执不替代完整源码冻结。

新核验器`out/verification/ecs-e5d/verify-closed-first.py`完整模式已成功运行：绑定原13011项历史、442项归档、两次同源回执、十个实际候选包、新生成头文件、原生/标准回归详细日志、四条各4帧的实际宿主完整短流及原NU1900失败。额外检查构建反例的实际日志：必须明确由HLSL诊断拒绝，不能以工具未启动等无关失败代替。Debug/Release生成头文件SHA同为 **3208870114E05B3FD36EE23C9BDE6303F68B1B75EC4F47B438617926695B09FE**。

十包DLL/EXE实际逐项对比仅6份`NcmaRenderer.dll`变化（每配置Editor、DX11 Player、DX11 Physics Player）；其余产品DLL/EXE不变。Release单份400384→474112B（+72KiB），Debug2341376→2431488B（+88KiB）。这是文件大小，不等同驻留内存或已接受的内存成本，后续性能/内存验收必须保留该代价。

最终证明 **`out/ecs-e5d-closed-verification-first.json`** SHA **B24F2B2CB8ED75D51BF456A9F7BD49EF1E9E970B74F8A036BA1A260BDBC9551A**，绑定**14189项文件**。`functionalGatePassed=true`只指本片相关门禁；`fullRegressionPassed=false`、`performanceAccepted=false`、`originalV1GateRejected=true`、`productionGcChanged=false`、`deployed=false`。5482（第二轮门禁）、59636（交付核验）均terminal exit0，其他本片会话也已终止，无活跃工作可重启或轮询。正式EXE C50742AD…和四项IDE文件均原哈希不变。

本片相关功能门禁完成，**尚无新候选性能测量**。下一步预登记同一旧Release `f49a1375b1f348abaa251028317e997b`与新候选的固定配对宿主启动/首帧及首次选择对照，保留所有慢项和观测成本，不复用11.55的单版本数据冒充优化收益。新的编辑或构建前先归档受影响的14189项绑定。正式EXE仍不替换，未提交/推送/部署，不放行E5-D整体、E/F或M8.3；完整冻结/全回归/正式队列、内存/目标机/人工/自包含/整帧/1h等仍待完成。

### 11.57 E5-D 固定参考候选的真实宿主与首次选择对照

本片没有修改产品、正式测试或验收阈值。11.56相关门禁仍有效，但不升级为完整回归。所有诊断使用正常GC，已批准UTF8 NoGCRegion v2边界不变，原6,808B及E5-C v1失败保持原判。

#### 固定真实宿主对照

先重验14189项绑定，复制旧Release `f49a1375b1f348abaa251028317e997b`和新Release `05f185337d0a41cc92691e1a6e40a83b`的完整144文件包及清单；产品DLL/EXE只有Renderer不同。每次独立复制同一9文件静态场景。预登记SHA **4A41E94B125368795634903913F1568B790B3273A9B619E8981CBF1501048616**。

固定CPU/DX11 × off/startup/combined × AB/BA/AB，共36个实际候选EXE进程；每轮模式顺序旋转，隐藏1280×720、120帧、上限30fps，不根据结果追加采样。36个全部exit0，共4320帧；24个启动观测进程48次调用，12个combined进程1440条全程记录无丢失。分段前缀768次保留、**2112次遗漏**，没有称为完整分段覆盖。

| 模式/指标（wall ms） | 第一对旧→新 | 第二对旧→新 | 第三对旧→新 |
| --- | --- | --- | --- |
| DX11 startup-only服务启动 | 975.3287→705.1853 | 971.9896→687.7312 | 938.8895→715.5600 |
| 同上Renderer段 | 452.5613→192.5340 | 467.9078→190.6664 | 451.6803→205.9294 |
| DX11 combined启动至首轮开始 | 1158.7744→907.8133 | 1099.75→864.42 | 1117.60→890.20 |
| DX11 combined首轮 | 114.7555→114.1021 | 119.88→107.94 | 109.82→105.84 |
| DX11其余Presentation中位数 | 0.7975→0.7407 | 0.79→0.80 | 0.75→0.84 |
| CPU combined首轮 | 58.58→62.83 | 57.3212→66.7022 | 63.4817→58.4209 |

表中保留不同小数精度，完整精确值见证明中的全部18组`productPairs`。DX11服务启动下降约223–284ms；Renderer包含设备、准入及嵌套工作，**不是独占D3DCompile计时或全部因果证明**。新真实首轮仍约106–114ms；CPU combined服务三对均增加约7.68–25.70ms，CPU暖态三对均变慢，DX11暖态两对变慢，不宣称全面无回退。

全部24组观察开/关比较同样保留：粗粒度累计全线程CPU包含启动和关闭，差值有正有负；新DX11 combined相对off增加375/140.625/296.875ms，不能除帧数当固定观察成本。cap30掩盖吞吐差异；哈希/复制触及文件，不是冷磁盘；父启动区间包含OS/CLR/JIT。未在该CLI队列追踪实际模块或截图，不以该队列证明World状态、像素或物理显示FPS。

独立证明`out/ecs-e5d-closed-host-pairs-verification-first.json` SHA **A0A815265CBF72344D37DC15BBAF8E2FF8A10FA8FB8384DC39CFB3318E8F3464**，绑定14929项，完整保留14189项前证据。这里只是诊断，`performanceAccepted=false`、`fullRegressionPassed=false`。

#### 首次选择诊断：实际服务与分段循环

独立驱动位于忽略目录`out/verification/ecs-e5d/selection-host-probe-third`，使用既有可信测试friend身份，不增加公开入口或部署组件。直接引用同一最终Release产品DLL，分别随完整旧/新包运行；真正使用`CandidatePresentation`、`EditorWorkspace.Select`、`EditorRunLoop`和产品pacer。无强制GC、无NoGCRegion、无截图/readback；模块枚举及哈希在计时完成后、卸载前进行。

自然启动已选择Ground，因此这里的“首次选择”是**启动后首次显式选择另一对象**，不是无选择状态下的首次检查器构建。固定29帧，6个转换：第9帧相机、第14帧取消、第15帧重选相机、第16帧灯光、第21帧重选启动网格、第26帧第二网格。分段Run重置delta，断言/序列化在各段间且不计入loop；可能影响JIT/缓存，**不等于连续发布宿主、指针输入延迟或游玩路线**。

2个CPU/DX11冒烟全部exit0并独立核验；再按预登记 **15F4276DECA771A27BA20A8FC72880F57D6F6E236C16BCF9258410E39301C5D4** 完成CPU/DX11 × AB/BA/AB的12个新进程。全12个exit0，348帧、72次操作、696次分段调用全部保留，无遗漏。证明同时检查：

- 选择返回的完整对象DTO逐项等于同一已提交场景对象，旧/新对象数据一致；
- 原World身份、场景字节、历史/dirty等状态不变（只允许Selection变化），Play未激活；
- DX11每进程29次Present，原生验证错误/警告均0；CPU不伪造Present；
- 正常有序关闭、无保留服务；原始记录在收尾前保存，与最终报告逐项一致；
- 实际产品模块从对应克隆包加载，磁盘文件大小/哈希、.NET8.0.28/CoreCLR/CLRJIT绑定；
- `gameplay.dll`由内存流加载，无Location；另记录其FullName/MVID并与项目源PE元数据一致。**这不是内存字节哈希闭包证明**，没有假装它具有磁盘加载路径。

最终选择证明`out/ecs-e5d-selection-host-pairs-verification-third.json` SHA **573B6B571BE27B23FA9A0D563F4564ED5B9933E3FBA4AF4D7343CD59E6331446**，绑定16433项。文件名third对应驱动/样本版本；实际成功离线核验器是fourth，未重写旧核验器。

全部36组选择配对保留，不只报告快项。CPU18组中10组GUI/Presentation更慢；DX11 18组中9组更慢。最大新值出现在第二对DX11重选相机：GUI **4.3869ms**、Presentation **5.2029ms**（旧约0.39/0.90ms）。原因尚未证明，不能称为噪声、也不自动认定由参考编译引起。单帧含pacing，不把约33ms等待混算为选择CPU成本。

原始GUI文本36/36对均不完全相等。独立离线核对各进程真实日志中的唯一Correlation UUID，仅替换其三个带方括号的日志token后，36/36对其他文本逐字相同；原始文本未改。它不证明截图/像素相同，不隐藏原始不等，也不通过泛化删除任意UUID来掩盖对象差异。

#### 辅助失败与修正范围

1. 第一驱动错误要求初始未选择，实际上OpenDocument选择首对象，CPU进程exit1，DX11未运行；原代码/输入/日志保留，不计性能结果。
2. 第二驱动完成选择后，模块记录对内存加载程序集的空Location调用FileInfo失败，exit1且未写最终报告。不能从工具退出推断完整样本已保存；原失败保留。第三驱动先保存原始帧，再显式处理内存程序集身份与来源。
3. 第三驱动构建0警告/0错误，两个冒烟进程成功；初版离线脚本误用只接受v1的循环校验器，拒绝真实v2报告。另建fourth核验器使用已有严格`ecs_e5d_pacing_evidence`，原始样本和阈值不变，不重新运行冒烟。该成功证明SHA **96DD8DD8EB1EFC9C87277B2088D46CD385523D0D07BDCAC8F532A4CD62884A0B**，15962项绑定。
4. fourth主队列脚本中`$Smoke`开关与`$smoke`路径在PowerShell中同名，启动采样前即失败。fifth仅更名路径变量，使用同一第三驱动和固定队列。该失败的Tee日志实际未创建，终端错误保留在对话，另有明确标注的转录说明，不冒充原始日志。
5. 第一离线总审计错误假定上述Tee日志存在，失败日志和脚本保留；second改为明确验证缺失状态及转录来源，复核已有数据，无重采样。第三脚本准备时默认GBK语法读取失败也记入转录，改显式UTF-8后通过，没有源码/样本修正。

最终总审计`out/ecs-e5d-host-selection-audit-second.json` SHA **C4181B829B156DECBE84038CC62C51E3F726C40755F59889E31B4EC3AB79296D**，绑定**16445项**，保留全部旧证明、实际文件、所有慢项及失败。全部会话已终止，无活跃采样/构建可轮询或重启。正式EXE SHA **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**和四项IDE文件保持不变；未提交、推送或部署。

下一片先检查已有首轮分段与重选慢项证据，定义剩余代表性路径和观察边界，再选择有依据的修复或新的有界诊断，不重复本队列取绿。1280×720隐藏静态编辑流程不能替代GTX1060/RX580、1920×1080 Low、30fps上限的代表性游玩验收；既有RTX5060Ti机器不是目标机。仍需内存驻留/工具队列/GPU遗漏、完整源码冻结（SDK/HLSL/工具）/全回归/正式队列、人工/自包含/整帧/1h门禁。E5-D整体、E/F、ECS及M8.3均未完成。

### 11.58 E5-D Player 窗口尺寸与实际输出证据

先复核11.57已有数据，没有重复采样：新DX11三进程首帧GuiBuild31.77–32.70ms、SceneSubmission25.73–27.34ms、NativePresent14.66–18.21ms、Observer8.67–10.36ms。重选慢项仍没有独占原因证明，不自动称为噪声或JIT。进一步检查代表性路径的产品前置条件，发现Player窗口、初始Renderer及UI视口三处硬编码1280×720，CLI不能指定目标尺寸。因此先补这一真实产品能力，既不是修改性能阈值，也不宣称用更小场景代替验收。

修改前核验16445项前证据，将147项可能变动的源码/产物/候选索引保存到`out/verification/ecs-e5d/pre-player-window-first`，清单SHA **A85587EC6ECE568B42D632B0390E43F38A64F3CA96833BFCE07547464E68561C**。历史核验随后使用明确的原路径→归档映射，不覆盖旧记录；本归档和15项同源回执都不冒充全仓库冻结。

#### 产品行为

- 新增`PlayerWindowSize`：请求窗口客户区宽高，范围1..8192；CLI必须同时给出`--window-width`、`--window-height`，无重复、符号、浮点、科学计数或文化相关解析。直接SDK构造/with修改也再次验证；Headless拒绝尺寸选项，在模块/World启动前失败。
- 不传参数时请求仍为1280×720。没有增加Vulkan/OpenGL入口、改变场景格式、GC、权限或原生ABI。
- Renderer从创建后实际`WindowState.FramebufferWidth/Height`初始化；UI从实际framebuffer与ScaleX准备，不再假定1280×720/scale1。正常窗口变化及图形恢复仍走原有路径。
- Player报告增加独立版本1 `output`：请求客户区、初始framebuffer、最后一次成功Present使用的尺寸、累计成功次数、呈现尺寸变化次数及匹配请求尺寸的次数。未呈现不会伪造尺寸或匹配成功；Null不产生该字段。
- 每帧只更新固定字段，收尾才创建报告；失败后的成功clear也按真实Present记录，不因随后故障删除。图形恢复不重置这份宿主累计记录。该字段不是物理显示像素、GPU时间、内部渲染比例或目标性能证明。

调用示例（不是已通过1080p验收的承诺）：

```text
NcmaPlayer.exe --project <project.ncmaproject> --window-width 1920 --window-height 1080 --max-fps 30 --report <new-report.json>
```

#### 已完成检查与实际未达目标情况

新增CLI/SDK尺寸边界、默认值/成对/重复/非法数字、Null启动前拒绝；新增输出记录的空/变化/不匹配/非法输入不改状态检查。真实Player在1920×1080及853×479请求下，通过DX11读回尺寸、字节长度、成功Present计数、循环记录和落盘JSON交叉检查。额外512×288请求的暂停场景显式图形恢复，保持前后精确像素、原Play状态及累计输出计数。

首次Release定向构建因新测试一处Check漏写说明参数CS7036失败，日志保留；仅补说明参数后0警告/0错误构建成功，PresentationCosts **101项**通过（原89加12）。

**本机请求1920×1080实际得到1920×1061，3次成功Present；853×479请求实际853×479，4次成功Present。** 因此测试通过证明“尺寸请求、实际读回与报告一致”，不证明目标1920×1080已实现。`AllPresentSizesMatchRequested=false`如实保留。不得把请求值写成实际值，也不能修改目标为1061。

源码显示平台明确启用`GLFW_SCALE_TO_MONITOR`，GLFW Win32创建后使用监视器/DPI调整以及SetWindowPlacement；这说明请求客户区和实际像素必须区分，**尚未通过独立对照证明本次19像素差值的具体原因**。后续需定位窗口约束并确定精确输出机制；本片没有改SDK/平台，也没有自动引入全屏、关闭DPI或拉伸图像取绿。Low综合预设及代表性游玩路线仍未完成。

独立source-only核验`out/ecs-e5d-player-window-source-verification-first.json` SHA **6176787FE1F0C070553D3D591A5B7AAF7E8A78244295FCCDFD4F15C134EDC3A2**：16496项绑定，保留原16445经147归档、7项既有源码限定变更/2个新增文件、15项first/second相同输入、原NoGCv2、实际读回报告、原失败、正式EXE及4项IDE文件。`sourceOnly=true`、`fullCTestPassed=false`、`fullSourceFreeze=false`、`actualCliVerified=false`、`performanceAccepted=false`、`deployed=false`。

#### 完整回归正在进行

第一轮标准构建会话19265已terminal exit1，NuGet漏洞源访问NU1900，未进入CTest；原日志保留，无关闭漏洞审计/降级警告。第二轮相同源码联网重试会话 **88687仍运行**，脚本`run-player-window-gates-second.ps1`，外层`out/ecs-e5d-player-window-gates-second.log`。

Release标准构建已成功、0编译警告，候选 **1c463e6f05d3487b9b63b06e528ca041**。当前完整CTest共91项（原89加既有GUI/固定参考构建测试），最后读取时前17项通过、主Rendering第18项正在运行；之后同一脚本继续Debug完整91项和Python96。不得重复启动另一份构建/测试或在运行中改15项回执源码。完整流程尚未通过，不能以Release定向101项替代双配置结果。

`verify-player-window-first.py` full模式已准备、尚未执行；等待同一会话终态，核对两配置所有结果、十个实际包、四条完整短流、实际尺寸报告及历史闭包。即便完整功能回归通过，仍不能关闭精确1080p、Low/代表性路线/内存/完整冻结与正式性能等门禁。正式EXE仍为C50742AD…，未提交/推送/部署，E5-D及E/F/M8.3未完成。

后续轮询确认同一88687仍运行，Release已通过58项，包括58.31秒的默认GraphicsRecoveryJoint，正在JointOff；尚未完成Release/Debug全门禁。期间未改候选源码，新增独立`run-player-window-cli-first.ps1`与`verify-player-window-cli-first.py`并完成PowerShell/Python语法检查。脚本SHA分别 **94D13D1F1EE49473C8E7B27048C902655A3782E51FC400038F6E37CF0B9AA56C**、**8FC273708D2FD016EA625ECCDD7E6E04B5D953B179C221F153A99F8888D91877**。

CLI脚本要求上述完整独立证明先成功，再复制受检DX11/Null包及每例独立9文件夹具；固定两配置各8例：默认尺寸、1080请求、奇数尺寸、Null应exit0；缺少高度、宽度0、Headless指定尺寸、重复宽度应exit2且无报告。预登记全部输入、精确命令、预期结果，记录真实PID/退出码/标准流，前后核验原包和复制件；输出报告与v2循环报告交叉检查。该16例用于验证真实入口，不是代表性游戏性能或物理1080p验收。目前**仅准备脚本，尚未创建CLI队列/预登记或启动进程**，不把准备状态记为通过。

后续同一88687会话完成了 **Release完整91/91，962.99秒，0失败**，并进入Debug标准构建。Release测试日志SHA **3510FA3DEA3D713A3A1CFD9D6C26C1CBE4E9E8B93D163CC2B2F49A3811772919**，详细日志SHA **4A997FD02F81B3B9F9EB14A57F85F1DA6717313545A6A5734FAC18DD6E43F8F8**；独立读取确认呈现101项、Player63项、编辑器服务175/175、startup30项、GUI18项、UTF8 v2追加512次0B及控制empty0B/positive80B。

本次完整Release夹具`7f09c655ee3a4728b691d157437f8e0a`中1920×1080请求仍为实际1920×1061（4次Present，匹配标志false），853×479为精确匹配（4次Present）。与定向测试分开保留，不合并采样或声称精确1080p通过。15项候选源码回执仍相同；当前Debug尚未完成、Python/完整独立核验/CLI队列尚未执行，不提前宣称双配置通过。此更新取代上方Release进行中的时点状态，原构建失败与全部记录保留。

随后同一88687完成Debug标准构建，候选`3f10e648b51f48fabd00ff8fbed80b2e`，进入完整91项CTest；最后读取前8项通过、第9项RendererNative运行。Debug测试并非已全部通过，Python和后续核验仍未开始。

#### 双配置与真实 CLI 最终功能结果（取代上述进行中状态）

会话88687已终态exit0，不再轮询或重启：Release完整91/91为962.99秒，Debug完整91/91为1064.57秒；Python96/96为9.576秒，两次标准构建均无编译警告。Debug详细日志再次确认呈现101项、编辑器服务175/175、startup30项，以及受检NoGCRegion v2原512次追加严格0B、empty0B/positive80B控制。原6808B失败及此前构建失败保持失败证据，没有修改生产GC策略。

- Debug测试日志SHA **493BAA5AFF0ACFB28CDAEDAD92223E0B42D3BB9A8B13C13F8127D33A73A5A5F7**。
- Debug详细日志SHA **62D90B98906779AD58354D38B8B3441E4ED202D2BE5B7E7FEC0DB20A1EC55398**。
- Python日志SHA **FF872B9A6A2E68F6F99C9F806A3252C1008E8109E3522E3019E9E9FAA45AF586**。

独立核验会话42036终态exit0，`out/ecs-e5d-player-window-verification-first.json` SHA **D3974B664D2CC91E7BB0F85C025A1598952B560C8093BB3973E7ACF2B3AC4360**，17519项绑定，验证15项同源输入、147项历史归档映射、所有旧失败、十个实际候选包和四条完整短流。两配置包清单各为editor144/playerNull56/playerDx11113/playerNullPhysics67/playerDx11Physics116项；相对上一候选，产品DLL/EXE仅八份Player包中的`NcmaPlayer.dll`改变。`fullCTestPassed=true`，但`fullSourceFreeze=false`、`performanceAccepted=false`、`deployed=false`。

首次CLI辅助脚本在检查历史输入时因`Get-Item`未指定`-Force`，无法取得隐藏归档`pre-presenter-utf8-first/0000-.suo`的长度；尚未创建目标目录、预登记或启动任何Player。只读复核文件为83456B、Hidden/Archive。保留原脚本、原零字节外层日志，以及明确标为工具输出转录的`player-window-cli-first-failure.txt`；不把空日志说成包含原错误。新`run-player-window-cli-second.ps1` SHA **D3E13944AABEC5971D5463E0EC3CEBAC348CB44301ECDC58B64342814E63E8FB**仅增加受检隐藏文件读取及失败证据绑定，固定16例、哈希和验收规则不变。

第二辅助脚本会话62269终态exit0，真实CLI预登记SHA **1FA7AC9F0DCC3D8C26A47218E70E9AE2818D3A98B8C82175E591FBD1DBB525E0**。两配置各4例正常exit0（默认尺寸、1080请求、853×479、Null），各4例错误参数exit2且不生成报告（缺高度、0宽度、Headless尺寸、重复宽度）；没有重采样或自动终止。独立CLI核验会话25266终态exit0，`out/ecs-e5d-player-window-cli-verification-first.json` SHA **8C9FFCCA26AB528F27FAD1F85157648484DB048D75746E7684563DBDCD75F8C2**，18054项绑定，复核原包/复制包清单、项目输入、精确命令、16个进程/退出/标准流、实际输出与循环报告。

| 真实CLI | Release实际尺寸 / Present | Debug实际尺寸 / Present | 请求匹配 |
| --- | --- | --- | --- |
| 默认1280×720 | 1280×720 / 3 | 1280×720 / 3 | 两者匹配 |
| 请求1920×1080 | 1920×1061 / 4 | 1920×1061 / 3 | 两者不匹配 |
| 请求853×479 | 853×479 / 4 | 853×479 / 3 | 两者匹配 |
| Null | 无图形输出报告 | 无图形输出报告 | 不适用 |

这16例仅通过入口、参数与真实输出报告的功能检查，不是精确1080p、物理显示或目标帧率验收。当前全部会话终态，无待轮询构建/测试。正式EXE仍为C50742AD…；4项IDE文件和原失败保留，未提交/推送/部署。下一步先定义并执行有界的自有窗口尺寸诊断，区分创建/DPI/窗口装饰/系统约束，再据证据决定原生窗口能力修复。现有代码阅读不能证明少19像素的根因，不得修改报告伪装成1080p或重复采样挑通过。修改/构建之前归档受影响的18054项绑定；完整冻结（含SDK/HLSL/tools）、正式性能队列、Low/代表性游玩路径、内存、目标硬件、人工、自包含、整帧及1小时等门禁仍未完成，E5-D/E-F/M8.3保持未完成。

### 11.59 E5-D 窗口尺寸约束的有界诊断与下一片边界

本片为实质诊断进展，不是重复采样争取通过。使用最终两配置Player包内原封不动的`NcmaPlatform.dll`/`glfw3.dll`，独立C++探针位于`out/verification/ecs-e5d/window-constraint-first`，仅在该目录编译，不重建产品、改变正式EXE或改写此前18054项输入。预登记固定两个进程（Release/Debug各一），每进程四个自有隐藏窗口：1280×720、1920×1080、853×479、独立1920×1080无装饰对照。每窗口记录创建、普通重设、一次指定干预，共24条状态；没有Renderer、测时或目标机性能验收。

探针从确切DLL路径加载并检查实际加载路径；使用现有原生私有借用接口取得**自身**窗口，借用期间阻止销毁。仅在本探针进程中临时包装其WndProc，记录`WM_GETMINMAXINFO`进入/GLFW处理后/诊断干预后的最大拖拽尺寸，之后恢复原WndProc、归还借用、销毁窗口，检查模块资源为0并正常卸载。没有操作用户的其他窗口，也没有把诊断回调加入产品。

会话45780终态exit0，探针`/W4 /WX`构建无警告、两个进程均exit0；独立核验32071终态exit0，`out/ecs-e5d-window-constraint-verification-first.json` SHA **96407D17F74FE35DFD7941186554E7A9688C1F367C8796D7B958067CB90C9D99**，绑定18077项输入/输出，重新核验全部18054项历史输入及当前源/驱动/产品DLL/24条状态。没有改采样、合并旧性能结果或将诊断当正式回归。

#### 本机观测及因果边界

两配置全部24行均为隐藏窗口，所在显示器1920×1080、工作区1920×1040、DPI96、内容缩放1×1。两配置对应状态完全一致：

| 案例 | 创建/普通重设的客户区 | 指定干预后客户区 | 外框及最大拖拽证据 |
| --- | --- | --- | --- |
| 1280×720控制 | 1280×720 | 放宽至所需尺寸后仍1280×720 | 所需外框1296×759，系统上限1940×1100 |
| 1920×1080 | 1920×1061 | 仅将最大拖拽高度扩为1119后1920×1080 | 所需外框1936×1119；原外框1936×1100；非客户区高39 |
| 853×479控制 | 853×479 | 放宽至所需尺寸后仍853×479 | 所需外框869×518，未触及系统上限 |
| 独立1920×1080 | 1920×1061 | 去除装饰、重设后1920×1080 | 最终外框等于客户区1920×1080，无需扩上限 |

普通重设时回调进入和GLFW处理后上限均1940×1100，证明不是GLFW主动把该上限改小；同窗口诊断只将上限高度改为1119，即得到请求的1080客户区。`1119 - 1100 = 1080 - 1061 = 19`，且`1100 - 39 = 1061`。这组直接状态及反事实对照支持**当前配置中系统最大拖拽外框约束**导致差异，不再仅是源码推测；不能外推成所有DPI/多显示器的普遍保证，也不能声称已经证明真实Renderer输出或物理显示完整覆盖。

#### 下一产品片的实施边界

1. 保留普通带装饰窗口及默认1280×720行为；不全局调整Windows/DPI设置，不通过抬高普通窗口上限使1119像素外框越出1080屏幕来伪装修复。
2. 新增显式、版本化的Platform创建能力：**无边框、以像素为单位请求客户区**，禁用该窗口的内容自动DPI放大；不直接复用V1保留位，不绕过C#资源所有权。C++只负责窗口机制，C#负责运行选项及模式策略。版本/容量/无效模式/尺寸/保留位必须先校验、失败不得发布句柄；精确初始像素不能兑现时明确失败并释放创建资源，不能静默降级。
3. Player CLI/SDK可显式选择该模式；普通模式仍默认，Headless拒绝窗口模式。报告同时保留请求模式/尺寸与实际呈现尺寸，不将“borderless”自动等同于独占全屏、改变显示器分辨率或物理屏幕已验收。
4. 现有Platform 1.1精确等待服务须支持明确的新表版本前缀，仍维持计时器独立资源、OwnerThread、无回调等待及恢复时保留。不把新版本请求隐式降级成旧模式。
5. 先保存受影响18077项绑定，再改原生契约/插件、托管包装、Player及测试。检查新版本短表/未知版本/模式拒绝、错误尺寸及资源清理；真实1920×1080和853×479像素读回、同尺寸图形恢复、默认模式不变、CLI真实入口/错误参数拒绝。高DPI/多屏未实际覆盖时明确保留人工门禁，不用本机DPI96证明。
6. 最终候选重新记录源输入、双配置受影响及完整回归、实际包/搬移CLI；不能复用旧91/91当新二进制的通过证明。即使像素尺寸实现成功，Low/代表性路线、观测边界、内存、目标硬件、完整冻结与正式性能队列等仍需独立完成。

当前**仅诊断与边界已落实，产品修复尚未开始**。全部会话终态；正式EXE/4IDE及历史失败不变，未提交/推送/部署。E5-D/E-F/M8.3未完成。

### 11.60 E5-D 显式无边框像素窗口候选

本片已实现11.59产品边界。修改前核验18077项输入，归档42项可能变化文件到`out/verification/ecs-e5d/pre-borderless-first`，清单SHA **26C759C78E9761677065AA50E9DA393C1E4DEA477A29167AB37236705E4EC027**；额外保存原PluginLoader，SHA **96925F9FC4DE72248BF1E987E007B07F27719A1B860F079A3A408424ACA53042**。历史状态通过明确归档映射核验，不改写旧证明。

#### 实现与边界

- Platform显式1.2表152B，追加像素窗口创建；独立40B描述携带模式，原V1保留位仍必须为0。1.0/1.1表尺寸及默认创建不变，未知1.3拒绝。
- BorderlessPixels为主显示器原点的无装饰、不可拖拽缩放、禁内容自动DPI放大的窗口，不改变显示模式/全局DPI/置顶状态，不是独占全屏。实际初始Framebuffer尺寸不匹配即销毁候选、不发布句柄、返回明确失败；后续OS变化仍如实报告。
- C#窗口包装显式要求1.2，创建失败归还租约；加载器请求和返回表两处白名单扩至1.2；精确等待仅允许明确1.1/1.2前缀，独立管理窗口和计时器资源。
- Player增加CLI/SDK `--window-mode windowed|borderless-pixels`，未指定仍普通1280×720；Headless、未知/缺少/重复模式和非法SDK枚举拒绝。报告保留请求模式、实际尺寸与Present计数，不把请求当成实际结果。
- 新原生测试以**测试编译期**Framebuffer查询替换注入尺寸不匹配，验证销毁、不发布句柄、0资源；产品不导出测试接口。真实1920×1080/853×479、无效描述/版本/线程/短表、借用/计时器共存、陈旧句柄及普通窗口属性恢复共67项检查。
- 托管检查旧表不得静默降级、失败释放租约、1.2资源独立寿命、未知1.3拒绝，新增两种真实Renderer像素读回。原普通窗口尺寸与恢复测试保留，另加无边框恢复后的精确画面和Play状态。

#### 修复过程与定向结果

第一轮新原生测试的Windows/GLFW头文件顺序引发APIENTRY重定义，被`/WX`拒绝；保存原失败源码/日志，仅修正顺序，不关闭警告。第二轮Release原生2/2与托管构建0警告通过，但加载器原1.1白名单拒绝新版本，真实呈现失败；保留失败和原测试源码，修正请求/返回两处版本范围，并新增未知1.3拒绝项。两次失败不改判。

最终定向会话78309终态exit0：

| 配置 | 原生CTest | 托管构建 | 呈现检查 | 无边框真实读回 |
| --- | --- | --- | --- | --- |
| Release | 2/2，1.08秒 | 0警告/0错误 | 113项 | 1920×1080、853×479，各4次Present |
| Debug | 2/2，1.97秒 | 0警告/0错误 | 113项 | 1920×1080、853×479，各4次Present |

两配置报告均为BorderlessPixels、尺寸全匹配、exit0、验证错误/警告0；夹具分别`fcacc799dbe14663b344e08fc5231847`/`bcf5e3a2095a48ba8c0bbf7e71e6dc65`，位于`out/verification/ecs-e5d/borderless-targeted-{release,debug}-third`。这是原生像素读回功能检查，不是物理显示完整覆盖/代表性路线或目标性能验收。

#### 完整回归仍在运行

唯一会话 **94914**：`run-borderless-gates-first.ps1`，外层`out/ecs-e5d-borderless-gates-first.log`。23项回执`out/ecs-e5d-borderless-source-first.json` SHA **3E6C20B334D46AE22BE442DA6879F2AED9B9012DBBF0FC2BB4E368E8BF20176A**，只是选定输入，不是完整源冻结。标准Release/Debug各完整92项CTest（新增原生PixelWindow测试）后执行Python96，不部署。Release标准构建已成功，候选 **ab04ad4314cd4fd29492c556849f396e**；最后读取18项通过、主Rendering第19项运行。勿重复启动或改候选，尚未宣布完整双配置通过。

source-only独立核验60862终态exit0：`out/ecs-e5d-borderless-source-verification-first.json` SHA **8FB88C24C59EEDC1766329A46A0CAAC9392F97D164B98884A1A931F2583820B9**，18154项绑定，检查旧18077经42映射、额外Loader原本、限定源变更、23项回执、两轮失败、双配置定向/原生67项以及NoGCv2/正式EXE/4IDE历史证据。明确sourceOnly=true、fullCTestPassed/fullSourceFreeze/actualCliVerified/performanceAccepted/deployed均false。

`verify-borderless-first.py`完整模式已准备、**未执行**，要求两配置92项、113呈现、67原生、64Player、UTF8v2控制、Python96、十个实际包与四条全程短流；94914成功终态后才可执行。之后须准备固定新候选搬移CLI（新模式/普通窗口/Headless/非法模式），目前尚未准备或执行，不能复用上一候选16例。

正式EXE仍C50742AD…，未提交/推送/部署；高DPI/多屏/物理显示、目标硬件、Low代表性路线、观测扰动/内存、完整冻结（SDK/HLSL/tools）、正式性能队列、自包含/整帧/1小时等仍待完成，E5-D/E-F/M8.3未完成。

后续经实际轮询确认94914仍运行：Release前42/92通过、ShadowJointProducts第43项运行，Debug/Python尚未开始；23项候选输入再次逐项哈希一致。当前是同一会话的已核验等待，未启动额外构建、测试或诊断进程。

已准备`run-borderless-cli-first.ps1`（SHA **9661C365D9A99C573B1D02F6AEA5DB913392CEF96F4A15D3E9F7DB11CFC937B4**）与`verify-borderless-cli-first.py`（SHA **E89982894F8BAC48D6313DA4F5F1893F323CC40169DAE683B6C71CD27521D401**），PowerShell Parser/Python AST仅语法检查通过。两配置固定各16项：保留原8项，再加无边框默认/1080/853、显式普通默认、未知模式/Headless模式/重复模式/缺失模式，共32个真实搬移EXE进程，预期16成功、16参数拒绝。无边框成功例必须验证1.2、请求与实际尺寸一致、无尺寸转换，普通例如实报告实际尺寸；全部交叉检查循环Present计数。四个复制包及每例独立9文件项目需前后哈希一致，失败不自动终止/重跑。脚本强制先取得完整双配置独立证明，当前**没有创建CLI目录、预登记或启动进程**；准备状态不算通过，原“尚未准备”表述由本条取代。

后续同一94914会话完成 **Release完整92/92，976.38秒，0失败**；测试日志SHA **91218122439162691D045C59F05DCCEB5D41C627A64FD3F7AE562406C3706BEB**，详细日志SHA **711B55DC2A1F80FB35645580F60222BA50B2E00BDFB1FF200032852D5B6F05E6**。独立读取确认原生67项、呈现113项、Player64项、编辑器服务175/175及受检UTF8v2 512次严格0B/控制empty0B与positive80B，23项回执再次哈希相同。

完整Release夹具`ca598c540a214a15a38b89df0bba3190`中：普通1920×1080请求仍实际1920×1061且匹配标志false；普通853×479、无边框1920×1080、无边框853×479均精确匹配，各4次Present，验证错误0。这证明新模式路径实际呈现尺寸正确且旧模式差异仍被如实报告，不证明物理显示、目标硬件或性能。

94914仍运行，已进入Debug标准构建，尚未完成Debug完整92项/Python96，完整独立核验及32例搬移CLI仍未执行。本条取代上方Release进行中的时点状态；无重复测试、无候选源码变更，正式EXE/历史失败保留，不提前关闭E5-D或后续门禁。

随后同一94914完成Debug标准构建，候选`8d3a92c91fc24060ae4117cce4778523`，完整92项CTest已开始，最后读取前18项通过、主Rendering第19项运行。会话再次轮询确认仍live，不重启；Debug完整结果、Python及后续独立核验/CLI仍待完成。

后续等待中只读核对代表性内容前置条件：仓库非SDK FBX位于`tests/assets/fbx`，属于测试夹具；`Ncma.Samples/ActionSample.cs`明确生成两骨骼、八顶点程序化诊断角色，README也声明非真实用户FBX验收，地面/目标仅数值碰撞体、非可见环境。因此不能将当前动作诊断示例直接标为正式代表性负载。已通过异步问题请用户选择“先制作可玩动作演示，再由用户确认负载”或“使用用户提供的角色/场景/动画路径”；**尚未收到答复，不从自动继续消息推断同意**。该未来内容选择不阻塞当前94914回归、独立核验和32项CLI；现有SceneShadowQuality.Low也不代表尚未实现的综合Low预设。

#### 最终功能结果（取代本节上方运行中状态）

94914、26358、49563、78068均已终态exit0，不再轮询或重复运行。Release完整92/92（976.38秒），Debug完整92/92（1085.49秒），标准构建均0警告；Python96/96（9.323秒）。Debug测试日志SHA **E742E27459DE3A79C5364BABD5A9439CEF3DED276AAE7C89C153E432A73E541F**，详细日志SHA **1DB4ACF8C4872C814AB8E1ADFB44561559AF9C175524CB9689EC6CDBC46F92E7**；Python日志SHA **5FE942DC82FBB013AFF9AEAC27F2FF85AFB7737E437F40BE3DAE425AA14ABC45**。

两配置均再次确认编辑器服务175/175、UTF-8测试v2原512次调用严格0B、空区间0B及正控制80B；受检区域仅用于该分配微测试及其控制，不修改生产或其他完整服务测试的GC策略。进入/退出失败不跳过、不吞异常，原6,808B失败永久保留、不重新判为通过。

完整独立证明`out/ecs-e5d-borderless-verification-first.json` SHA **A9878BBF3002F8F79A8800C5B63DC90D1BF0D0923DBDB61A34CFA9C5BB4AA6E1**绑定19181项，包含十个实际包、四条完整测试短流、23项源回执及经归档映射的历史失败。原source-only证明保持原范围，不改判为完整证明。

固定32项搬移真实CLI已执行：16项成功exit0，16项非法参数按预期exit2，全部进程终态，无自动重试。独立证明`out/ecs-e5d-borderless-cli-verification-first.json` SHA **551F2B774FA830B1F6E4ACBC182DFCAB4EE50AC397086B8D1E30D390EFA31480**绑定19897项，核验四个复制包、每例九文件项目、真实输出和循环Present计数。两配置无边框1280×720、1920×1080、853×479全部精确匹配；普通1080请求仍如实报告1061，不被新模式覆盖。不是物理屏幕/高DPI/多屏或目标硬件性能验收。

额外原生DLL字节诊断`out/ecs-e5d-borderless-native-bytes-first.json` SHA **30EDF1C400D95F2A24156D9CE47A15682073826F40111626F0CBD25B3DD58488**保留六组实际前后字节差异。Release GLFW有6个差异字节、Release GUI有4个，代码段相同；但试图进一步限定为COFF/导出目录时间戳的`verify-borderless-delivery-first.py`在第60行断言失败（49098终态exit1），**没有生成最终delivery-first证明**。保留原脚本和工具错误记录，不把失败修饰成通过，不宣称这些文件仅有已证明的时间戳差异；其他DLL差异也未作性能等价归因。该附加分类失败不改写此前独立功能/CLI通过证明，但相应二进制归类结论尚未成立。

本片功能通过，E5-D整体仍未完成。正式EXE及四项用户IDE改动保持不变，未提交/推送/部署。后续修改前保存受影响19897项绑定；综合Low预设、代表性内容选择、完整冻结（SDK/HLSL/tools）及正式性能队列、内存、目标机、人工、自包含、整帧与1小时稳定性门禁仍待完成。

### 11.61 E5-D Low v1 场景画质策略与事务入口

上一片完成真实功能门禁，本片开始补齐Low策略，不重新采集既有窗口队列。修改前再次逐项核验19897项文件；其中没有本次需修改的当前Program.cs或托管bin/obj路径（历史同名文件位于归档中），因此`pre-low-authoring-first/manifest.json`记录0项需迁移的既有绑定，SHA **9AB853AD3D8A5E6D8D3F99806D2EB04D2B0626C29965060AE769091CCD4E4D14**。另独立保存修改前测试Program.cs，SHA **8FED81210B80DE9C7B76802F1DC64E2536535C3356799642CAA72CA85D7AAD4B**，不把0项记录虚构为产品文件已归档。

#### 已实现的产品边界

- `Ncma.Rendering.Scene/SceneQualityPresets.cs`提供精确版本键`ncma.quality.low.v1`。使用现有Low阴影：512×512单图、官方软阴影最多9次深度读取，不采用High近似PCSS；无投影主光仍依现有策略省略阴影通道。
- 保留后处理Enabled、Tone模块/版本和曝光，只清空可选Feature栈。先验证原设置及已注册模块，再移除Feature；未知版本/模块、损坏参数不被Low掩盖。空栈不会调用Feature构建器。材料、环境、模型、模拟、物理、角色动画均不由该策略更改。
- `RenderingEditorAdapter.QualityRequest`只构造原`ncma.scene.transaction`，不授予权限、不执行GPU、不新增World副本或独立历史。两项配置保持已有单例所属对象；缺项使用调用者明确传入的现有对象，拒绝空/不存在目标、重复单例和旧revision。一次事务实现原子提交、一次Undo/Redo；原对象/组件权限仍逐项检查。
- 输出分辨率、窗口模式、内部渲染缩放和帧率上限不是本预设的隐含副作用。当前选择原生分辨率，不声称已经实现0.75–0.85缩放，也不把512阴影预算视为整帧显存/帧率保证。

#### 测试与证据

定向会话76472终态exit0：Release/Debug各7组通过，两次构建0警告/0错误（11.86/9.78秒）。覆盖Tone/曝光保持、CPU编译图中512阴影资源、无Feature构建、幂等值、只读请求、分离所有者、原子提交/单次历史、缺项与关闭态、权限/对象/组件拒绝不部分写入、延迟提交revision冲突、重复/未知/跨线程拒绝。

完整编辑器服务会话72244终态exit0：**Release182/182、Debug182/182**，包含原175项及新增7组。受检UTF-8 v2再次保持512次严格0B、空区间0B和正控制80B。日志SHA分别 **44F6A57C1F671755B8368E4DD3F2BA65A3251A583C48EFBF58CF4BE12E639D80** / **1224023BE9600C55838BF7DF5065032069DF499E4133CA9EF650EBEAC8CDCA4E**。

独立核验70461终态exit0：`out/ecs-e5d-low-authoring-verification-first.json` SHA **F30DA4AFC74A2B6E27BF6B6A41E74E7339E41A4B8FA45D0332A50B852D14E7EF**，20059项文件绑定，包括19897项原证明重新哈希、本片源码、预编辑Program、脚本/六日志及双配置完整测试输出目录。`fullCTestPassed=false/performanceAccepted=false/deployed=false`，不复用上一候选92/92当作新候选全回归；正式EXE仍C50742AD…，原失败与四项用户IDE改动未改变。

下一片：接入显式编辑器操作，通过现有Workspace及stamped UI路由提交，不直接给UI/AI额外权限；补实际视口Low阴影/后处理与Undo/恢复检查，再完成新候选完整门禁和实际宿主验证。本片尚无界面按钮、Low GPU画面或性能结果，不标综合画质交付完成。代表性内容问题仍未答复，但不阻塞独立功能开发；目标机、完整冻结/正式队列、观测扰动、内存、人工、自包含、整帧/1小时等仍待完成。未提交/推送/部署，E5-D及后续阶段未完成。

### 11.62 E5-D Low v1 编辑器按钮与真实视口验证

本片为实际产品与测试进展。编辑前重核20059项绑定，将当前托管输出及受影响源码共167项保存到`out/verification/ecs-e5d/pre-low-ui-first`，清单SHA **1CBF10ABD67621145BDA87F503B056E9D27C79A80AFA2AACF43C4E0EFB66B9A7**。后续核验使用该明确映射，不把历史源文件哈希与新构建混用。

#### 产品与缺陷修复

后处理目录区域新增“应用场景画质：Low v1”按钮及范围说明。按钮仅在可写、无草稿且注册了场景阴影组件时启用；动作沿用Presenter的Frame/ViewGeneration/DocumentGeneration/Revision、Kind、Enabled检查，再通过既有Workspace场景事务提交两个配置。没有新增MCP写权限或直接World写入。

第一轮启动辅助脚本使用了PowerShell只读变量`$Host`，在两项Release构建成功后、测试执行前失败；原脚本/构建日志及明确标注为工具输出转录的`low-ui-first-launch-failure.txt`保留。另建第二版脚本使用`$hostLog`，不改测试标准。

第二轮发现真实交互缺陷：当Low点击事件先生成、同帧随后开启草稿时，动作抛异常进入通用错误处理，导致草稿被取消。保留`out/ecs-e5d-low-ui-panel-release-second.log`和对应失败版Presenter/测试源码；修复为在动作入口发现草稿时直接忽略该点击，既不提交也不取消草稿。新增检查覆盖伪造Kind、旧revision、同帧排队点击、重建后的禁用按钮、Play禁用及文档不变。原失败不改判。

#### 已通过的验证

- 第三轮会话96646终态exit0，四次托管构建均0警告/0错误；Release/Debug后处理面板各11/11通过。
- 真实源代码宿主后处理检查Release110项、Debug108项通过，API验证错误/警告均0。计数包含原测试按实际Player呈现次数执行的逐帧断言，因此两配置总数可不同，不是跳过Low检查。夹具分别`low-ui-host-release-third/bb91dd93475543e19fdffd0e220d2b22`、`low-ui-host-debug-third/d6d931bf73f94ab0849e33ac32d98096`。
- Low检查先用Medium阴影+Bloom作为真实视口基准，再发送带实际界面戳的按钮事件；已安装的阴影图为512×512，PreparedPost保持原Enabled/Tone/EV、Feature栈为空，原生PostStats.Bindings与ResidentBytes均0。一次Undo/Redo恢复对应完整文档字节和画面。
- 在Low画面捕获处注入测试异常，确认真实GPU活动帧仍被保留；经现有受检图形恢复入口重建后，旧Renderer已关闭，Low像素、文档和历史精确保持，再用两次原历史Undo恢复宿主夹具初始状态。未修改产品故障或恢复语义。
- 图像为**591×404测试视口**，不是目标1080p、物理鼠标操作或人工视觉验收。两配置原始Medium+Bloom像素SHA **09D0DFCDBDCE438E11B300397EA2140D808BD048DD65116BBB17309F13E11675**；Low及恢复后SHA均 **1C67602BC7DC55D79C58971264B82C272EEEDBAF6C764A443D9F488B740D78FB**。三张PNG已保存。独立PNG解析器验证CRC、尺寸、像素格式、每行过滤及解压后原始SHA，共6图通过，证明SHA **C6E73ADA20733299C2915787441AD20E84249727C20AD4D3170B84010E3068CB**。
- 完整编辑器服务会话80927终态exit0，Release/Debug各184/184。UTF-8 v2仍512次严格0B，空区间0B、正控制80B；正常服务GC边界不变。日志SHA **0E197580AD56668FAF1C279C84F6C97ED0A7E415CBCD0070DA5C5335F385CE99** / **B5FE835D86D2E33CC5BD4E6D539CB7BEB5262562D1DA8F02AF106E97C6548FA1**。

最终核验5169终态exit0，`out/ecs-e5d-low-ui-verification-first.json` SHA **92AFDA9DF6783A4C6BAE2B08964ADC91448699ADC75A1E6EAC7947D83FF2787A**，20560项绑定包含经167归档映射的20059项原证据、最终源码/双配置完整测试输出、实际原生插件、真实宿主夹具、两次失败、像素解析和本轮日志。全部会话终态，无需重启或重采；该证明仍明确`fullCTestPassed=false/performanceAccepted=false/deployed=false`。

下一片需对最终源码执行标准构建、新候选完整CTest及实际搬移产品验证；不能把旧92/92或本次源宿主当作新发布包门禁。正式EXE仍C50742AD…，四项用户IDE变动及历史失败保留，未提交/推送/部署。代表性内容仍待人类确认；1080p Low/30fps目标机、完整冻结与正式性能队列、观测扰动/内存/人工/自包含/整帧/1小时等门禁仍未完成。E5-D及后续阶段保持未完成。

### 11.63 E5-D Low 最终候选完整门禁进行中

**2026-10-23最终功能结果（覆盖下方所有运行中/准备状态）：** 恢复时90150句柄已不存在，原日志已有完整终态标记，故没有重启测试。Release完整92/92（969.69秒）、Debug完整92/92（1120.72秒）、Python96/96（12.661秒）通过。Debug测试/详细日志SHA分别 **B3C92FFA552AB85227291C199A1ACFAFEE9C531A6DB14C510EB337A7D7EAA0BE** / **AC3998A74392F9D1B17E1FB94C642745D47A39914C5E7110D3E4667E8BCC5476**；Python日志 **B6421B7C94A760FEFA70F51B06B12B43264E0667FC152F7B5DB67E8A13B2E1F8**。

- 独立核验1628终态exit0，`out/ecs-e5d-low-gates-verification-first.json` SHA **6979ED07790687A7637834030753432D3058F3E9BDD81EDE8E6430F455923B9C**，21738项绑定检查通过：十个实际候选包、31项相关源码、历史20560项经352归档映射、双配置184服务/UTF8v2/原生67/Player64、新Low像素及输出尺寸证据。31项仍不是完整工具链冻结。
- 辅助作者程序在首次编译前补充`Ncma.Assets`命名空间，当前源码SHA **965C5526C694BAB7D1935B5FFE82347752BD317E5E61576F846BD8F51A8E828C**，覆盖下方89C5501B准备版；未改变产品或门槛。11240终态exit0，构建0警告/0错误；双配置各从受检搬移编辑器包加载9个实际Ncma程序集，通过正式事务生成Low场景并验证精确UndoRedo及保存重载。两份场景SHA相同，**E1B3683D489F754A2FD1E9F3FD6497818F1BAA56F36AFBB9EEB880EEA6AB4A00**。
- 固定8个真正`NcmaPlayer.exe`进程全部exit0：双配置普通1280×720、像素1920×1080、像素853×479均实际呈现4帧且尺寸一致；headless均无呈现。没有引入产品CLI测试开关、程序集回退或改写报告请求值。实际启动/参数/日志/退出回执保存在`out/verification/ecs-e5d/low-cli-first`。
- 独立核验4349终态exit0，`out/ecs-e5d-low-cli-verification-first.json` SHA **50473CB412991448E9064B39608EFC7E388B8252DE5B9F2165029AD5EB7B8211**，22511项绑定，验证6个产品副本、加载程序集、原4对象场景仅增加两项Low配置、其他资源字节不变、固定8项进程与完整短流/真实输出。没有将这些短用例当作GPU阴影图尺寸或代表性性能证明。

本片全部会话已结束，原6808B等历史失败保持原判定；正式EXE仍C50742AD…，四项IDE修改保留，未提交/推送/部署。Low功能链路完成，但E5-D未完成：代表性动作内容仍待人类选择/确认，完整源码与SDK/HLSL/工具冻结、正常GC整帧/观测扰动/内存/正式性能队列、GTX1060/RX580目标机、人工、自包含、1小时等门禁仍开放。后续修改/构建前先归档受影响22511项绑定；不可继续轮询旧会话或重复已完成用例取绿。

**最新等待与后续工具准备：** 同一90150权威轮询仍运行，Debug已58/92通过，第59项GraphicsRecoveryJoint运行；31项输入及正式EXE哈希不变。准备了独立搬移Low核验工具，尚未编译或执行，`low-cli-first`输出目录仍不存在：

- `out/verification/ecs-e5d/low-cli-author-first/{Program.cs,LowCliAuthor.csproj}`，SHA分别 **89C5501BC5C6E974FC01B7EA37D6F65D9F84A2D0A9F202E75C14AF28F7311B04** / **0FD12FA5588AEE6650F000B8B597693311497F49E567534429D8001AD3064E7B**。只加载搬移编辑器包中的正式Ncma程序集；原4对象场景经原事务应用Low，要求一次历史/精确UndoRedo/保存重载，拒绝缺失程序集回退。该辅助程序不是产品入口。
- `run-low-cli-first.ps1` SHA **5B58F8DD70DCB6D06FB33BA8A1D24B66692EF8B616BE40A61F69EC9282C79046**：先要求双配置完整核验证明并重查全部绑定，再复制双配置editor/playerDx11/playerNull共6包；固定2个配置作者、8个真实Player进程（普通1280×720、像素1920×1080、像素853×479、headless）。输入计划先于对应执行，保留单进程回执，超时不自动终止或重试。
- `verify-low-cli-first.py` SHA **B9B8341B7AC0E97008B1B0A9E8A8E790513D9DC53EAB11A0C0CC82B5C71B66ED**：独立复核包/加载程序集哈希、场景仅新增两项设置且其他资源字节不变、固定8项参数/进程/退出、完整短流及实际输出尺寸。该证明不代替实际GPU阴影分辨率、代表性动作路线或目标性能验收。

目前仅PowerShell语法、项目XML和Python AST检查通过；C#未编译、正式输入计划与实际进程尚未创建，不能宣称这些新用例通过。等待90150成功终态后先运行原`verify-low-gates-first.py`，再执行以上新工具。Debug完整门禁、Python及最终独立核验仍未完成。

**后续进度：** 标准Debug构建已成功，候选`1f869a3d23d64e46956a5e2e62ae0a59`；同一90150已开始Debug完整92项CTest。仍未完成Debug/Python/最终核验，不重复启动。

**最新完成项（覆盖下方Release运行中记录）：** 同一90150流程已完成Release完整92/92，0失败，969.69秒；测试日志SHA **679B326F05D922B501935676C4198A897EE7A9D89A94FF0AFD49D935A57DF496**，详细日志SHA **F697E13B0B9890FAB8F9E2539F4C08FBC29DBF6927A86AD82FDA12DD91579D10**。独立读取详细日志确认编辑器服务184/184、UTF-8 v2原512次严格0B、空区间0B及正控制80B、原生像素窗口67项和Player64项。31项源码回执及实际文件仍一致。90150仍运行，已进入标准Debug构建；Debug完整回归、Python、最终独立核验及新实际搬移Low检查尚未完成。原6808B失败不改判，未提交/推送/部署，不作性能或E5-D完成结论。

**最新等待状态：** 同一90150句柄再次确认运行中；Release主Rendering通过（172.31秒），最新46/92项通过，SceneConstructionSafety第47项运行；Debug未开始。31项源码哈希不变，未重复启动。完整核验工具已修正准备阶段的日志解码：历史CTest详细日志含1116个本地编码字节，诊断文本改用保留原字节的surrogateescape，结构化JSON、成功标记及门槛仍严格。原F99C2CB0…版本保存为`verify-low-gates-before-log-decoding.py`；`test-low-verifier-log-decoding.py`验证实际函数的原字节往返与缺失标记拒绝，已通过。当前`verify-low-gates-first.py` SHA **04C1D7AEC326403F8E0A4140BBC29A69E79161B325E7D95CAD06B990912D0C49**，完整核验仍未执行。以下保留启动时记录；该修正未改产品/测试源码或验收定义。

本片推进标准构建及完整验证，不将11.62相关回归升级成整阶段通过。修改/构建前重新检查20560项绑定，保存352项当前托管源码/输出、原生构建产物和包索引到`out/verification/ecs-e5d/pre-low-gates-first`；清单SHA **B3DF126C0532DBD8A76F2B54CF08BFDE9284F460E814552C816A9BF72AA09A8C**，归档会话71657终态exit0。

唯一运行会话 **90150**：`out/verification/ecs-e5d/run-low-gates-first.ps1`，外层日志`out/ecs-e5d-low-gates-first.log`。使用标准`Build.bat -Configuration … -SkipTests`完成原生/托管/着色器及候选包构建；该开关仅把测试交给随后独立的完整CTest流程，并避免执行旧性能队列及自动部署。未禁用NuGet检查、编译警告或测试。依次执行Release完整92项、Debug标准构建及完整92项，最后Python96项。

相关源码回执`out/ecs-e5d-low-source-first.json` SHA **6358220E9DCFF22519E1CB8CFC0529A56BC04F3201A4680444F08734C0C62E63**，覆盖原无边框23项及Low策略/UI/测试8项，总31项；每次构建/测试前后重新核对。这只是选定输入，不是完整SDK/HLSL/tools/工具链冻结。运行期间不要修改这些源码或重复启动构建/测试。

截至本次权威轮询，90150仍运行。**Release标准构建已成功**，候选`out/package/m2-7/Release/0296cc3799554c17ac9b5678810609c1`，正式入口未动；完整CTest前18/92通过，主Rendering第19项正在运行。尚无完整Release、Debug或Python结果，不宣称双配置成功。

已准备`out/verification/ecs-e5d/verify-low-gates-first.py`，SHA **F99C2CB0C36B74A1366C44EEC95D702C594EB2EBB94BF74E82A5B6F209E40091**，仅UTF-8 AST语法检查通过，**未执行实际最终核验**。第一次只读语法检查使用系统GBK默认编码而解码失败；改用明确UTF-8读取后通过，源码未修改、未发生产品测试失败。该工具要求完整双配置92/184/原生67/Player64/UTF8v2/Python96，检查十个实际候选包、前后字节差异、原20560项经352映射、31项源码、新完整CTest中的Low视口PNG原始像素、输出尺寸和完整短流；不会因为准备完成就生成通过记录。

90150成功终态后，先检查所有测试与日志，再运行上述独立核验，随后执行新候选实际搬移产品检查（尚未执行）。当前正式EXE仍C50742AD…，原失败保留；未提交/推送/部署。Low实际591×404源视口证据仍有效，但不能代替新包/1080p目标机/性能验收；代表性内容、完整冻结、正式队列、内存/观测扰动/人工/自包含/整帧/1小时门禁和E5-D后续工作仍未完成。

### 11.64 E5-D 完整冻结前的仓库输入覆盖审计

2026-10-23完成只读准备审计，不重复11.63已通过的产品测试，不改产品/测试/构建输入。新工具`out/verification/ecs-e5d/audit-freeze-readiness-first.py` SHA **0E57E336251203889A54179E78F61B6108F8CC5F94EE317DF934EF02BF83FBD1**。1项正例、9项负例、8项范围策略检查通过，覆盖漏项、增项、重复、哈希/尺寸变化、布尔尺寸、越界路径和非法哈希；Jolt的Build目录及SDK的bin/DLL/压缩包不得被宽泛忽略。

99174终态exit0，原22511项证据全部重新校验一致。对engine（包含全部SDK）、managed、python、scripts、tools、tests、gameplay及8个根构建/启动文件进行两次实际枚举和哈希比对，共 **6824文件、1150988540字节**。显式记录124个排除子树（托管项目直接bin/obj、Python缓存及嵌套版本控制元数据）；其余根条目完整列出但不冒充已覆盖。SDK中可能存在的构建产物保守保留，因此这是偏宽的当前文件清单，不是已确认的最小构建依赖集。

结果`out/ecs-e5d-freeze-readiness-first.json` SHA **86981F372BFDB4AD60AF18AF2AA66B742BAA585CFC27077E91B1A79A6CDEA198**。实际Release/Debug `compile_commands.json`各281个编译单元，清单各覆盖279个；各有两个生成输入位于本次源码清单范围外的out构建目录中，即`engine/sdk/glfw/src/glfw.rc`和`engine/sdk/JoltPhysics/Build/CMakeFiles/Jolt.dir/cmake_pch.cxx`。只读追踪确认GLFW `src/CMakeLists.txt:111`通过`configure_file(glfw.rc.in …)`生成资源脚本；Jolt `Jolt/Jolt.cmake:509/511`配置预编译头，实际生成的`cmake_pch.hxx`引用仓库`Jolt/Jolt.h`。两配置当前glfw.rc同哈希46101FDB…，但生成来源定位不等于完整依赖闭包已冻结。

报告明确`fullSourceFreeze=false/toolchainFrozen=false/performanceAccepted=false`。还须绑定外部编译器/Windows SDK/.NET/NuGet/Python环境、生成输入与传递头文件、out中的测量驱动/合同及最终工作量，并在随后对应构建/回归/正式队列**之前**形成新冻结；不能将这份当前清单追认为11.63构建的历史输入证明。代表性动作内容的人类选择仍待答复，目标机/内存/观测扰动/人工/自包含/整帧/1h及E5-D仍未完成。未提交、推送或部署，所有历史失败保留。

### 11.65 E5-D 原生依赖记录审计与完整性风险

2026-10-23只读检查现有构建记录，没有执行配置、编译、回归或采样。工具`out/verification/ecs-e5d/audit-native-inputs-first.py` SHA **F02A427F4F7703694E5D54304CF8689E82BD0C52F824E1D8CE5581198F676767**；解析器2项正例及5项负例通过，拒绝孤立依赖行、截断计数、重复输出、未知状态及垃圾文本；STALE如实保留，不自动当作通过。

会话49411终态exit0，重新核对6824项清单字节后读取双配置实际`ninja -t deps`，原始stdout/stderr保存在`out/verification/ecs-e5d/native-inputs-first`。前后`build.ninja`、rules、compile_commands、CMakeCache和.ninja_deps哈希保持一致。结果`out/ecs-e5d-native-inputs-first.json` SHA **F4658A4E870B2C6FB7245C2DF878A764E17C777A4AE39CEFAFD52594449ED8D7**绑定969项实际文件，包括生成glfw.rc、PCH入口/头文件、记录中的头文件以及缓存指定的cl/link/lib/rc/ninja/cmake/ctest/dotnet可执行文件。未读取失败；可执行文件哈希并不覆盖其全部DLL、库和运行环境。

双配置各281个编译单元均找到记录，全部被Ninja标记VALID，无STALE；唯一记录依赖为Release644、Debug685，各仅9项外部头文件。然而**Release有32个、Debug有9个零依赖记录**。其中Release的`RendererPlugin.cpp`为0，Debug同入口为229；`CompileClosedReferences.cpp`为0对2。实际源码存在明确include，例如ClosedShaderReference.h引用生成的NcmaClosedShaderBytes.h；Release记录中没有这一生成头的依赖。不能因此宣称整个依赖闭包完整，也不能据此直接断言产品已陈旧或某种语言环境是根因。

下一步先在全新out目录做有界、独立的编译器/showIncludes采集诊断，绑定当前源码、工具和命令，保留原对象文件和.ninja_deps；当前CMake针对本地化include前缀的处理只作为待检假设。不得先重建全部产品或重采样以覆盖原风险记录。完整外部工具链/链接输入、托管restore/runtime、测量驱动和最终工作量仍待补齐；`fullSourceFreeze/toolchainFrozen/performanceAccepted=false`。本片未修改产品源码、未提交推送部署，E5-D及后续仍未完成。

### 11.66 E5-D 隔离编译器依赖输出复现

2026-10-23在`out/verification/ecs-e5d/native-dependency-probe-first`进行固定四次诊断编译：Release/Debug分别编译CompileClosedReferences.cpp、RendererPlugin.cpp。从实际compile_commands读取原参数，仅将Fo/Fd改为新目录，并增加`/showIncludes`和诊断`/sourceDependencies`；使用现役vcvars64和VSLANG=1033，没有改原工程、原对象、Ninja数据库或全局环境。执行前登记命令/环境/原对象哈希，计划SHA **FE3EDD41DFE61A64599106C7A272CDACFF10101E4D809BB3A38BC1D864C52C55**；驱动SHA **0C5DABFE0C9162B29467D9FD27FF2FC9F9390011B2013F4912681212F0AC623B**。

94094终态exit0，四次编译均exit0。两个配置的着色器工具各330个依赖，渲染器入口各602个；原始输出均为UTF-8中文前缀，与当前rules.ninja前缀精确匹配，GBK/英文前缀计数均0。独立逐路径比较确认showIncludes文本集合与编译器结构化Includes集合一致。原四个对象文件及前轮969项绑定前后哈希不变。结果JSON SHA **F0233604B8BF5751B28742DB7E0367A96AC5C74CE74AB86C84EABD33CBFC01BD**，独立验证`out/ecs-e5d-native-dependency-probe-verification-first.json` SHA **044897B9C4D7704562272FCD79653D2C7ABEEDD856CE1F8FA59FE60C0B8F317E**，绑定1310项实际工具、头文件、命令和原始输出。

本片没有复现“当前include前缀不匹配”，但没有证明原Release零依赖记录的原因，也没有证明其已修复。新增sourceDependencies属于诊断扰动，本片尚未走原Ninja编译/依赖解析路径；原数据库仍保持原样。下一步应做有界的原showIncludes-only控制与隔离Ninja解析对照，再追溯历史记录来源，不直接重建全工程覆盖异常。`originalFailureRootCauseProven/fullDependencyClosure/performanceAccepted=false`；未提交推送部署，E5-D及完整冻结等门禁仍未完成。

### 11.67 E5-D Ninja 原路径与前缀修正不重建的对照证据

固定五次隔离诊断编译已执行：双配置的CompileClosedReferences与RendererPlugin各一次正确前缀，另一次Release着色器工具使用错误英文前缀。参数去掉11.66新增的sourceDependencies，只保留原showIncludes路径；使用实际Ninja、现役编译选项和原源码，输出与数据库均位于新`out/verification/ecs-e5d/ninja-dependency-probe-first`。环境仍为标准vcvars/VSLANG1033，未覆盖原工程输出。

四个正确前缀用例全部编译成功，Ninja记录着色器工具2个、渲染器入口229个仓库头文件；与11.66结构化依赖中的仓库路径集合一致。错误前缀用例同样编译成功，但记录0依赖。随后仅把该私有build.ninja中的前缀改正，实际`ninja -n -d explain`仍返回“no work to do”，诊断对象及依赖数据库哈希不变。这证明**前缀错误导致漏记后，仅修正解析前缀不足以使旧对象重编译和修复依赖库**的机制；不证明原仓库历史上必然发生过同一过程，也不直接断言已发布产品陈旧。

首轮30837在五次编译完成后以exit1结束，原因是辅助脚本按LF替换而生成文件为CRLF，尚未执行前缀修正或dry-run；原脚本、日志及before文件保留。独立收尾脚本只替换前缀字节，验证反向替换精确恢复原字节，再执行dry-run并独立读取所有五项实际回执；**没有重跑五次编译或修改产品**。核验`out/ecs-e5d-ninja-dependency-verification-first.json` SHA **DECB09B7155E1012B6F23D0F563E8BEA16C2A01EB15B38BEAC4E2135819DA7EB**绑定1380项，包含原证据、实际原始依赖、失败和控制步骤；原构建对象/依赖库/来源字节保持不变。

下一步可据此加固构建契约：让依赖解析规则身份参与编译命令或等价的失效机制，并增加前缀变化会触发重编译、已知本地头文件必须入库的回归。修改CMake及构建之前先归档受影响22511证据与6824清单项；新产品字节必须重新绑定后续冻结/验证，不能复用旧性能。当前只是诊断闭环，构建加固未实现，未提交推送部署，完整冻结/性能及E5-D仍未完成。

### 11.68 E5-D 依赖解析身份进入编译命令

2026-10-23落实11.67支持的构建加固。修改前重新核验历史Low、源码清单及Ninja诊断合并后的 **29734项唯一文件绑定**；本片唯一修改的既有产品输入CMakeLists.txt已逐字节归档，映射`out/verification/ecs-e5d/pre-dependency-hardening-first/manifest.json` SHA **65D64B9226DB14C61F999D00035DB7AD6D096A3D2669E7970DC590B3CAA62C55**。既有对象、依赖库、候选包、正式EXE及IDE文件未覆盖。

新增`engine/build/cmake/NcmaDependencyTracking.cmake`：仅对MSVC+Ninja，使用显式策略版本及全局/C/CXX include前缀的长度分帧身份生成SHA-256，将未使用的`NCMA_DEPENDENCY_PARSER_<hash>=1`定义加入C/C++编译命令。在SDK子目录和引擎目标建立之前启用，RC和其他编译器不注入。首次采用及前缀身份变化会改变编译命令，避免只改Ninja解析变量却复用旧零依赖记录；不声称生成物字节等价，也不把诊断机制等同于历史零依赖根因。

新增持久化`NcmaDependencyTrackingTests`及独立C/C++夹具。每次新目录保留全部输出，固定验证：错误前缀下两个实际对象记录0依赖；仅修正前缀、源码不变时两条真实编译命令重新执行且已知tracked.h各入库；相同配置重新生成不重编译；修改夹具头文件后两个对象再次重编译。编译使用/W4 /WX。原生前缀通过CMake include文件传输，保留UTF-8与两个尾部空格，避免-D缓存字符串裁剪。

首轮测试拒绝了Ninja返回的合法相对头路径，原失败源码39447359…及日志保留；修正为相对构建目录解析后，独立Release/Debug测试均通过。随后在全新目录配置实际主工程（不构建/替换原产品），两配置各281条生成命令中280条C/C++携带指纹，唯一RC不注入；通过主工程注册的CTest各 **1/1**（Release **3.88s**、Debug **3.91s**）。此次注册测试独立确认使用带两个尾部空格的真实前缀；前一轮直接-D调用的短前缀通过结果不冒充这一精确字节验证。

只读证据复核的首版只接受LF、第二版只接受正斜杠RC路径，分别拒绝了实际CRLF和Windows反斜杠；第三版隐式GBK解码拒绝UTF-8编译日志。三次核验失败与原脚本保留。第四版仅允许文本行尾LF/CRLF、按实际Windows路径检查精确glfw.rc及RC命令、显式使用保字节UTF-8 surrogateescape读取诊断文本；JSON仍严格解码，不修改产品、原始测试数据或重复成功测试。

35672终态exit0，独立核验`out/ecs-e5d-dependency-hardening-verification-fourth.json` SHA **A88DD9399DC76745719C1ECD67A95AC54B3C9F4C4CB791A0D1EBA40D3A1FE1F5**绑定 **30198项**。逐项重验旧证据（旧CMake按归档映射读取）并绑定当前新源码、实际生成命令/依赖记录、失败、双配置注册测试及正式EXE；旧对象/包未变，所有会话均已结束，无待重跑测试或存活测量进程。本次运行中核验日志未自引用入哈希清单，避免写出结果后改变被绑定文件。

本片只完成构建策略及隔离回归，不是全新产品完整门禁或性能验收。旧Low候选92/92和Python96/96仍属于旧候选，不能重新标记为新构建通过。下一步继续外部工具链/链接与托管依赖闭包、最终工作量和完整冻结，再进行匹配的新候选标准构建/全量回归/实际依赖检查及正式性能队列；目标机、代表性内容、人工作业、内存/观测扰动、自包含、整帧与1h仍未完成。未提交推送部署，E5-D/E-F/M8.3保持未完成。

### 11.69 E5-D 外部工具链、实际 NuGet 内容及主机清单

本片只读补齐11.68之后的外部输入证据，不修改产品或重跑已通过的测试。64502终态exit0，`out/ecs-e5d-external-inputs-second.json` SHA **3C51446159BDD002723D21A2C07CE90F068BD623D44FB69B351723BC022F45FC**绑定 **49728项**，包含重新核对的旧30198项、61份实际托管restore资产清单和新外部输入。外部目录两次枚举/逐文件哈希一致，共 **19768文件、2490895960字节**；8项相对路径拒绝控制及正例通过。没有静默吞掉目录读取错误，全部登记搜索目录当前存在。

使用实际vcvars64/VSLANG1033捕获限定的INCLUDE/LIB/LIBPATH及工具目录，而非导出整个环境。保守覆盖MSVC 14.44.35207的x64编译器/链接器/辅助DLL/资源、环境脚本、全部显式头文件/库搜索目录、Windows SDK 10.0.26100.0 x64工具、CMake模块/Ninja、实际选择的.NET SDK **9.0.315**、已登记8.0.28/9.0.17运行时与引用/apphost包、Python 3.12.8标准库和私有worker依赖。双配置新图中17种显式外部.lib均按实际LIB顺序解析到文件；工程生成库独立记录，未冒充已经重新生成。目录覆盖是偏宽的输入范围，不是实际进程加载/DEFAULTLIB追踪，也不覆盖整个Windows/GPU驱动依赖。

61个托管项目的restore资产均引用9.0.315的运行时图；只有两个gRPC项目有外部NuGet依赖，共5个唯一包。二者锁文件的身份/版本/内容哈希与实际restore资产一致，locked mode和NuGet安全审计保持开启。首轮10508失败于误将签名包内容哈希和完整归档SHA-512当作同一值，原脚本、日志、部分清单保留；这不是已证明的包损坏。

NuGet官方实现将签名包内容哈希与完整归档哈希分为不同接口。查阅[官方PackageArchiveReader源码](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs)后，实际加载本机SDK的NuGet程序集，对全部5包调用GetContentHash、ValidateIntegrityAsync及GetArchiveHashAsync：实际内容哈希匹配锁定值，签名包完整性检查成功，归档哈希匹配缓存.sha512。再用Python独立计算实际归档SHA-512、逐个比对解包资产与ZIP成员字节。`out/ecs-e5d-nuget-content-first.json` SHA **4E61A486270B3273B05D7328EF38E7748537A44DD9322A1D3D680B5CC2E34056**绑定实际SDK程序集及包哈希；**这不是证书信任、撤销检查或新漏洞审计通过声明**，没有禁用正常构建审计，也没有下载/替换包。

最初受限CIM只读查询被拒绝；获准的精确只读查询随后成功，没有修改系统配置。主机快照`out/ecs-e5d-build-host-first.json` SHA **9F199B95536CCDF76EDC84145B1427ADBFCEEDDAF42023F8651CC85DD00DD666**：Windows 10 19045.6466，Ryzen 5 5600X 6核12线程，RTX 5060 Ti驱动32.0.15.9649，同时列有OrayIddDriver；物理内存报告34266583040字节。不将此机或虚拟显示驱动当作GTX1060/RX580目标机、物理显示或性能验收。

还发现不能忽略的Python启动输入：基础解释器存在3个全局.pth；普通site启动会加载全局`_distutils_hack/__init__.py`。限定启动观察`out/ecs-e5d-python-startup-first.json` SHA **B122C135E80DB632D13A63EB2B15D1F258511A49AD4963848C09DC4B60E4441B**绑定这些文件并记录搜索路径/相关环境，使用-B仅避免本次观察写字节码，未改变正常构建入口。它不是96项测试的完整动态import闭包。外部清单明确排除了基础全局site-packages和缓存，私有worker依赖则已覆盖；不能把“Python工具主要使用标准库”推导为环境已完全冻结。

下一步先收束基础Python启动/实际工具导入范围、最终源码/生成物来源/测量驱动与工作量，形成组合冻结再运行匹配的新候选标准构建和完整门禁。完整进程/链接审计与正式性能队列仍需自己的证据。代表性动作内容仍待人类选择，目标机/内存/观测扰动/人工/自包含/整帧/1h未完成；旧测试不冒充新候选，所有会话终态，无部署、提交推送或E5-D完成声明。

### 11.70 E5-D Python 完整目录与新回归输入登记

68113终态exit0，完成基础解释器整个安装目录的两次实际枚举与逐文件哈希复核，不仅限于已加载模块。`out/ecs-e5d-python-installation-first.json` SHA **E32B03DF9F95D3FC2506893A841A0DC49658B9DB419226F74A2226F3233F45B8**覆盖 **56507文件、2226098786字节**，包含全局包、3个.pth及 **19426个已有字节码文件**，不忽略可能作为输入使用的缓存。用户site目录与默认python312.zip当前不存在，缺席状态也登记；审计仍保留正常site加载，-B仅避免审计自身写字节码，没有修改标准构建或测试解释器参数。本报告只复核新增目录和所引用旧报告的身份，未冒充再次重验旧49728项；随后组合步骤才执行旧证据全量重验。

31173终态exit0，组合步骤重新核对旧49728项以及上述56509项（含报告/扫描器），归档标准构建可能覆盖的 **87个**已绑定输出。归档`out/verification/ecs-e5d/combined-build-inputs-first/archive.json` SHA **59CD35C918A9925958DDE411D0122B27DB1870D413B64AC7A3B6005060880D61**，没有删除文件或修改外部安装、正式EXE及IDE设置。

当前仓库源边界为7个源目录加8个根构建/启动文件，共 **6830文件**；继续保留SDK/HLSL/工具原始输入。补入Git完整安装、Windows PowerShell目录、私有Python worker整个目录、实际字体msyh.ttc、已选系统图形/CRT DLL、vswhere及隐式父级配置的缺席检查。与先前外部工具和完整Python安装合并，`out/ecs-e5d-combined-build-inputs-first.json` SHA **51FBF85AA16FE9F19E2E5981638F5EDD8414A4F6D0D4BBBB57BCDF56321F7D8E**登记 **89285个**文件。生成的glfw资源/PCH入口与头、GUI/场景着色器头各配置另列基线；它们是之后必须核验来源及实际字节的生成输出，不冒充无需重建的主源码。

固定新回归计划`out/ecs-e5d-combined-gates-plan-first.json` SHA **9FE5C952EE8139870D761B5740E05BC785BBC98A18E91684EBB6A2A460C7B08A**绑定检查器E378B8FC…、执行器DCDC4314…、登记器31EA05C3…。计划按Release→Debug运行标准Build.bat -SkipTests（仅分开构建与测试、不禁用警告/安全审计、不部署），随后每配置完整 **93项CTest**，最后原 **96项Python测试**。构建前、构建后、完整CTest后及最后均重新校验输入字节、源/解释器/工具目录成员、缺席项、SDK与Git HEAD及归档；保持正常运行/GC，仅原人类批准UTF8 v2微测试保留受检NoGCRegion。首次失败即停止并保留证据，不重试取绿。

登记不等于执行成功。执行器PowerShell解析与检查器Python AST已通过；97896独立只读前置核验已终态exit0，89285输入、成员/缺席/SDK/HEAD/归档及正式EXE检查通过。获准正常NuGet联网审计后，**58016**启动已登记`run-combined-gates-first.ps1`，外层日志`out/ecs-e5d-combined-gates-first.log`；当前仍在Release-before输入复核，标准构建及完整93项测试尚无结果。不要重复启动，不修改绑定源码/SDK/工具/驱动；该执行器运行于已绑定Windows PowerShell，后续核验日志须按实际BOM/编码读取，不假定所有日志UTF-8。

该边界服务于新候选功能构建，不把`fullE5DFreeze=false/representativeWorkloadFrozen=false/performanceAccepted=false`改成通过。全门禁后还必须检查实际全编译单元依赖记录、生成物、候选包和搬移入口；代表性内容选择、正式性能工作量、进程/链接、目标机/人工/内存/观测扰动/整帧/1h仍未完成。未提交推送部署或结束E5-D。

同一58016后续状态：Release-before及Release-built两次89285输入复核通过；标准Release构建成功，候选`out/package/m2-7/Release/d7cc610dd2284d408994fac26145b532`，构建日志SHA **4ADC540F8ACA9D09226597244C90CBF7C625B6060445FEB8916390AA33719A50**，未发现编译警告行。完整CTest最后核对为 **43/93已通过、ShadowJointProducts第44项运行中**；Debug及末尾Python尚未开始。此记录取代上段“仍在Release-before”的时点状态，不是完整Release门禁通过声明。保持原会话和已登记输入，不重复执行。

为终态验收准备独立脚本，全部位于忽略的out目录，不修改冻结输入：`verify-combined-gates-first.py` SHA **08564F790BC921E6DCF6850D3AB0BCE7AFB868961FE699F8794955524B42B706**须先看到固定执行器完整终态标记，再检查双93/Python96、全部输入检查点、87归档映射、十个实际包、Low原始PNG解码/历史恢复和真实输出尺寸/短帧流。`verify-combined-native-first.py` SHA **223F7FF3170BE48CCD576EDE2636FFE15D273B74F983B1AF08C0AA8ED6205024**须在上述证明之后，只读检查实际双配置281编译单元的依赖记录、关键头覆盖及10个生成物基线；零依赖项如实列出，不将VALID等同于完整闭包。两脚本当前仅语法检查，尚未执行全审计。

实际日志解码函数的控制测试`test-combined-log-decoding-first.py` SHA **661E857FCB2CE379B9815E9F791AA6C25D6F2FCEFC6F54B70687996C930E7B1A**通过UTF-8/带BOM/UTF-16双字节序及保字节旧诊断正例，并拒绝缺失标记、截断和无BOM UTF-16等6个负例。只修正证据读取方法，不修改测试数据、阈值或重复产品测试。

还准备新的`combined-cli-author-first`及`run-combined-cli-first.ps1`/`verify-combined-cli-first.py`，保留原Low工具和已完成结果不动。新执行器SHA **F4E94C368DEB4F56FED811AA99D5BB7E7B94E60B3351C0BAD6C3A62FFDDAE3D4**、新核验器SHA **B70E6E42D3BAF1602120278E386D1CB47F6F8E296A048C0D73E7469B941EA371**：必须先有匹配新候选的完整93门禁和原生/生成物证明，才能复制6个包、隔离加载两配置真实托管程序集，通过原事务/精确UndoRedo/保存重载生成Low，然后执行固定8个真实Player进程（windowed1280、pixel1080、pixel853、headless各双配置）。新核验器处理实际WinPS UTF-16日志并规范化输入路径，场景内容、调用参数、验收阈值不变。PowerShell解析/XML/Python AST检查通过；C#尚未编译、未复制新包、未创建combined-cli-first案例目录、未运行任何新CLI。不冒充512GPU读取或代表性动作内容、物理显示、目标性能通过。

同一58016继续推进后的新结果：Release完整 **93/93通过，887.89秒，零失败**；汇总日志SHA **F64568547A01630908D4F5E425DAD23BDB4BA972A80E276BCFEA44506495BDEF**，详细日志SHA **1A7247CDA4DA36A701E9541F42E5AFCE97CCB7F1377155FC6665E736096B16F6**。独立只读解析确认93个唯一通过项（含新依赖追踪和像素窗口测试），以及Services184/184、原生像素窗口67项、Player64项、Low草稿/Play保护；已批准UTF8 v2仍为原512次严格0B，空/正对照0B/80B且受检退出。原6808B失败仍保留且不改判。随后Release-tested检查点通过89285输入/目录成员/缺席/SDK/HEAD/归档复核，正式EXE保持原哈希。

此时同一58016已进入Debug-before检查点，仍为真实存活会话，不重启或重复已完成Release测试。Debug标准构建/完整93、末尾Python96及终态完整证明尚无结果；预备全量/原生/搬移核验脚本仍未执行。上述Release功能通过不代表目标机或代表性内容性能验收，也不放行E/F/M8.3、提交推送或部署。此状态取代本节较早Release-running时点记录，其他未完成项保持不变。

后续Debug状态：Debug-before检查通过，同一流程完成标准Debug构建，候选`out/package/m2-7/Debug/51f432bd2afa4109b9e03353bc7753e0`；构建日志SHA **DC0FC168EEB7A3A5D1D6BBB754D9FDF5F6867C021AEBD3875613A0B91D057195**，无编译警告。Debug-built再次通过89285输入和成员/缺席/SDK/HEAD/归档检查，正式EXE保持原哈希；随后完整93项CTest已启动，最后核对 **17/93通过、UiSampleSmoke第18项运行中**，58016仍真实存活。本次实际进展是Debug构建及新测试启动，不是Debug完整通过、Python或终态全量审计。保持同一会话、绑定输入和验收顺序，已通过Release不重跑，预备核验器/搬移工具均未执行；尚无提交、推送、部署或性能完成声明。

### 11.71 E5-D 双配置93项通过与Python执行器续跑

Debug完整CTest **93/93通过，1005.88秒**，汇总日志SHA **DF8E00128E3CFD87038DD775F62749783981F257F1D85EBF9DB331F9EA5832D1**、详细日志SHA **3988BA3966A2A2781EB451FBE8A92B38A7A4627339F08223512431D0C8E4603F**。独立检查93个唯一通过项，并读到Services184/184、像素窗口67项、Player64项、Low草稿/Play保护；UTF8 v2为原512次严格0B，空/正对照0B/80B、受检退出。Release93/93（887.89秒）不重复。两配置测试后89285项输入检查均通过。

原会话 **58016终态exit1**，不是完整执行器成功：Windows PowerShell在ErrorActionPreference=Stop下，将`unittest -v`写入stderr的首条成功记录“... ok”包装为NativeCommandError，从执行器第41行退出。原Python日志为0字节，不声明该次Python完整通过；外层失败日志SHA **EB78E949404FE1A2FA72CB4FF4BF22C453A5DE42ECD06888EF644BB6A44B3884**，原执行器DCDC4314…及原登记计划保持不变。最初受限只读CIM查询被拒绝；获准的精确查询只查看本轮执行器/检查器/unittest命令行，未找到匹配遗留进程，没有终止进程。

仅新增out内的Python续跑工具，不修改产品、测试用例或注册输入，不重跑已完成的两套CTest。`finish-combined-python-second.py` SHA **12BA0A4EE3955050351319B2926DBC7B22FE27D3997345F2CD2125BC9BDCA516**，`run-combined-python-second.ps1` SHA **268A9505DE3F878BDCE04A07CE1FA285C873B2F907D840C3714CC8510E7554E0**。仍由同一Windows PowerShell和Stop策略启动；子进程stdout/stderr直接写入独立原始字节文件，显式检查原生退出码，记录PID/实际命令/环境覆盖/退出结果；超时不自动杀进程或重试。固定控制验证stderr+0正常、stderr+7如实保留非零、缺失可执行文件拒绝，三项均通过。

新会话 **68458**先通过原89285检查器，再使用原解释器、原`-m unittest discover -s python/tests -v`、原PYTHONPATH及正常运行模式执行原96项测试；实际 **96/96通过，9.700秒，原生exit0**，完整stderr逐项结果与stdout均保存。仅观察/检查驱动自身使用-B，实际unittest未添加该参数、未改变GC、断言、测试数量或阈值。当前会话仍在最后输入检查阶段，最终回执尚未形成；不能提前称组合产物核验完成。

原预备`verify-combined-gates-first.py`要求不存在的原执行器成功标记，保留且不执行。新增v2独立核验器`verify-combined-gates-second.py` SHA **AB717B691D9DFCBBE8F49A1755312B7D18C8CB2F5D42530CB48C01BFFF5AC251**目前仅AST检查，须在续跑终态成功后才能执行。它重新核验原双93/六个输入检查点、原失败与空日志、续跑原生退出/96项实际日志及前后检查，并保留既有包/PNG/实际尺寸/历史归档核验；预期写出首份组合完整证明`out/ecs-e5d-combined-gates-verification-first.json`，schema2明确`originalPipelineSucceeded=false/pythonContinuationSucceeded=true`，不伪造原流程成功。该证明当前尚未写出；之后才执行已准备的原生依赖/生成物及新搬移CLI检查。

本片原失败、旧6808B拒绝结果和正式EXE/IDE文件均保留，无源码/产品修改、部署或提交推送。目标机、代表性内容、完整性能冻结、进程/链接、内存、观测扰动、人工、自包含、整帧及1h等门禁保持未完成；E5-D及E/F/M8.3不放行。

随后68458终态exit0：最后89285输入复核通过，续跑回执`out/ecs-e5d-combined-python-second.json` SHA **F3099BBE691F10214658DE7DA66CD27CB5DFD22A3331B1FBEF07EF9BBE541362**已形成，明确原执行器失败、仅Python续跑成功、未重复CTest、未完成产物审计。原生进程退出码和原始双流文件均绑定；这不是将原空日志改写为成功。

当前 **84011**运行v2独立产物核验（PID18124），输出分离至`out/ecs-e5d-combined-gates-verifier-second.stdout/.stderr`；尚无终态或首份组合完整证明通过结论。没有再次构建、重跑测试或开始原生/搬移检查。只有这一核验终态成功并核对证明后，才进入后两项。

84011随后终态exit0，首份组合完整证明`out/ecs-e5d-combined-gates-verification-first.json` SHA **FB0D4B3944598E4E2E8C133A1F2445D106C2ED2DB87989C547D17A875A2944C4**绑定 **113588项**文件证据。明确schema2原执行器失败、Python续跑成功；重新核验89285登记输入、原49728证据按87归档映射、两配置各93及Python96、十个实际候选包清单/文件、失败证据、六张Low PNG实际CRC/解压像素与Undo/恢复。相对旧候选的包内DLL/EXE条目记录42处哈希变化，未声称二进制等价或把差异归因仅时间戳。

实际两配置Low宿主均512阴影、0可选Post绑定、精确UndoRedo/恢复；591×404测试图像不冒充1080目标机画面。两配置普通1920×1080请求仍如实报告1920×1061；普通853×479和无边框1920×1080/853×479均实际匹配，各4次成功Present，短帧流验证通过。这是实际新功能/产物证据，不是代表性游玩性能、人工鼠标操作、物理显示或新搬移CLI验收。

已按顺序启动只读原生依赖/生成物核验 **36894**（PID16420，预备脚本223F7FF3…未改），输出`out/ecs-e5d-combined-native-first.stdout/.stderr`。当前仍存活，未有终态及实际全编译单元依赖通过声明；不配置、不重建，不启动第二份核验。它通过并检查结果后才运行新候选固定8项Low搬移Player。正式EXE、IDE及所有原失败保留，未提交推送部署。

### 11.72 E5-D 实际依赖记录与搬移检查执行环境

36894终态exit0。`out/ecs-e5d-combined-native-first.json` SHA **DAAC4B584EFB9B81B9572DF091FE1D0DA86A82E79B4A9475D13E5117B88308C3**绑定 **89314项**：两配置各281条实际编译命令对应281条VALID记录；解析器2正5负控制通过。ClosedShaderCompiler的2个、RendererPlugin的229个已知仓库头依赖均实际入库。双配置全部10个生成输出（GLFW资源、PCH入口/头、GUI和场景着色器字节头）与旧基线一致，读取前后图/依赖数据库哈希不变；没有配置、重编译或修改产品。

每配置9条零依赖记录如实保留，不以VALID推导完整闭包。随后只读核对：CompileGuiShaders与RendererValidationProbe源码只引用系统头；其余7个Jolt单元实际命令使用/Yu、/FI，Ninja图有显式PCH头/产物依赖。两配置PCH自身各32条记录均包含Jolt.h、Memory.h、IssueReporting.h、Vec3.h；4个DebugRenderer源码的额外头位于JPH_DEBUG_RENDERER条件内，而实际两配置相关缓存选项为OFF、命令未定义此宏。该源码/图/记录证据支持零记录的局部解释，不冒充新编译器全预处理追踪、外部系统头完整闭包或进程/链接证明。

首个新CLI执行器启动后在前置证明检查即退出1：子Windows PowerShell未识别Get-FileHash。尚未创建combined-cli-first目录，未复制包、编译author或运行Player。原脚本F4E94C36…保留，stdout为空，stderr SHA **C3BFB4181E1447B648318170310BF129431739A34403E0E307E4C8F57C87D771**保留，未将其当作引擎功能失败或成功。

有界环境诊断发现必须区分两种启动方式：直接&启动的WinPS5.1加载Utility3.1且Get-FileHash存在；与失败一致的Start-Process启动继承了宿主PowerShell7模块路径，实际自动加载Utility7，Get-FileHash不存在。显式导入Windows自己的Utility3.1后，该函数恢复且独立计算原生证明哈希正确。诊断脚本/双流文件保存于inspect-cli-shell-first及out/ecs-e5d-cli-shell-first.*，未修改系统模块或设置。

新增`run-combined-cli-second.ps1` SHA **429A17E63F8BDBEB4C1CF7440B41E5D85ED9ACD966C40D0F301C962DC4C85841**：仅在测试子进程内将PSModulePath限定到已登记Windows PowerShell模块目录，显式导入并验证Utility/Management的准确路径，Stop策略不变。相同Start-Process启动的inspect-cli-shell-second实测通过（WinPS5.1.19041.6456、两模块均Windows本机路径、哈希一致）。固定8例、两个隔离author、产品源码/断言/阈值和独立核验器B70E6E42…不变，新的输入登记会绑定首轮失败及诊断。

当时唯一活跃会话 **56520**（PID19244）执行修正后的搬移流程，日志out/ecs-e5d-combined-cli-second.stdout/.stderr；该时点仍在逐项复核先前证明输入，案例目录尚未创建。后续终态与独立核验见11.73，不继续按此历史时点重启流程。

### 11.73 E5-D 搬移 Player 原生退出回执与日志编码核验

56520随后终态exit1：完成六份包复制、一次辅助C#编译和两配置隔离作者事务后，首个Release-windowed Player的原生ExitCode为null。程序自身输出exit=0、报告完成及4次Present不能替代原生退出码，因此该次保留为未验收，原包、作者结果、场景、日志及空退出回执均未覆盖。

固定退出控制复现Windows PowerShell捕获问题：旧方式对已知退出0和7均得到null；在等待前保留Process.Handle后准确取得0和7。控制回执SHA **31C70BC8F98BA160A8F3AB7A83784D790CF8BB04B725AF2E7156A13CFF6ABDDF**。仅新增续跑执行器`resume-combined-cli-exits-second.ps1` SHA **4E38B99701E133DD9177C2A8068F02390EF697F6F72DB0A1EE12CF2992EB489D**，重验并复用全部包、两份作者结果及八份项目输入，不重建或重新生成场景。新报告独立写入`combined-cli-first/exit-capture-v2`；97840终态exit0，固定八例全部取得原生退出0。每配置涵盖普通1280×720、无边框像素1920×1080、853×479及headless；实际启动总计9次，其中历史未验收1次、新回执8次，不隐藏首个失败。

独立核验v2（18359）终态exit1，准确停在第72行辅助编译日志“零警告”摘要读取断言，原脚本SHA **53070E46A11FBC721CF663C5A8DCC105E9CBCD72C57761BF794FC57BF8F0BB9C**及双流日志保留。实际`author-build.log` SHA **6C3B4C144D72E624B048E4B714C813FF78922E5DD9219BFDCE909FA3C5326465**是UTF16存储，中文UTF8原生输出先经WinPS CP936错误解码而产生有损文本。没有通过再次编译取得新日志，也没有把原生exit0单独当作零警告证明。

同一WinPS启动与编码路径的有界控制分别发送已知UTF8零/一警告和错误摘要，控制回执SHA **9684C4DBC74925D8E048AA6C93814D5D975FEE74F4F4EED4AC177F7DF26CBA82**。捕获的两个零摘要与实际编译日志各精确匹配一次，一摘要不同；保留LF被合并成单行的原始控制输出并严格分解。实际v3摘要读取函数通过原日志正例及六个负例：非零警告、非零错误、缺失、重复、未知标签、CS诊断代码。仅支持这份日志的零摘要语义核验，不声称恢复完整中文日志；警告门槛不变。

只读独立审计 **65297终态exit0**，执行`verify-combined-cli-third.py` SHA **91438D3CB1EE380DCA4F111D9E95425709903128AFD699AC4162547259AA4EED**，输出`out/ecs-e5d-combined-cli-verifier-third.stdout/.stderr`。最终`out/ecs-e5d-combined-cli-verification-third.json` SHA **6C10CD7737AC989A48709A66E41714303BDCCDB50A64013E65BA11661C555F9D**绑定 **114426项**。它确认原失败及编码控制，再重验原完整证明、原生证据、六份搬移包清单和实际程序集、两作者事务/精确UndoRedo/重载、仅两项场景设置变化、八例原生退出/输入不变/输出尺寸与短帧流。两配置的1280×720、1920×1080与853×479均精确匹配，各4次Present、无尺寸变化；两headless案例各6tick、无图形输出。全部验证错误/警告为0。原失败没有改判；此组搬移功能核验完成，所有相关进程终态，不重复运行。

双93/Python96及前序证明不改判、不重跑。所有新工具均在忽略out目录；产品源码、正式EXE与四个IDE文件不变，无提交推送部署。该组案例使用四对象诊断夹具，不是代表性动作内容、目标硬件性能、物理显示或新512阴影GPU验收；完整性能冻结、进程/链接、目标机、内容、人工作业、内存/观测扰动、自包含、整帧与1h等仍待完成，E5-D/E-F/M8.3不放行。

### 11.74 E5-D 离线帧间隔指标算术原型

在独立审计运行期间，核对当前LoopCostCapture/完整流核验器：已记录Present返回间隔及缺失计数，但尚未冻结代表性路线的平均FPS、1% Low及Hitch计算合同。先在忽略out目录新增隔离算术原型，不修改已验证候选、现有Python模块或目标预算文件，也没有采样新产品进程。

`present_metrics_prototype_first.py` SHA **71C8E0FBEE87862B92229BB579D7E19D7FE2D5B465C7AD55AA4F7B70E2DDEEC0**草案定义：对全部N个间隔，以`N×frequency/sum(intervalTicks)`计算平均；1% Low使用最慢`ceil(N/100)`个间隔的平均时长倒数，而非瞬时FPS平均或P99倒数；Hitch取全部间隔最大值。使用精确有理数比较原阈值30/27/80，不暗加四舍五入容差。原型仅接受100至1000000个正整数间隔；100只是本原型的1%尾部算术下限，不是已获批准的正式测量时长。没有过滤慢帧、预热裁剪或挑选最佳运行的接口。

`test_present_metrics_prototype_first.py` SHA **07966C35C1FAB568513143E6CD264FA41F0F802D4F824494B4EE65F922EE8448**通过 **11项测试，0.031秒**，原始测试日志SHA **32E5345D264F6E5CA7CF5B174A34CA18EB845E0FCA19F04EF30E71A62CD511A8**。覆盖精确30边界、略低30必须失败、27与80边界、201样本向上取3个尾部、非瞬时FPS算术平均、不修改输入、极大整数精确运算及非法类型/值/长度拒绝。

这是**草案算术模块**，不是采集器或正式验收器：`coverageVerified/workloadFrozen/targetPerformanceAccepted`全部false；它不知道是否有丢帧、输入是否被裁剪、是否来自目标硬件，更不把Present返回当成物理显示。因此不对刚完成的短CLI报告计算性能达标，不将11项合成测试当作引擎性能通过。原限帧实现采用最小准入间隔向上取整，严格平均≥30与调度开销的关系仍需以冻结窗口实测审视，不能放宽预算掩盖。

下一步先在隔离工具中接入已存在的完整流验证、输入/输出哈希及缺失/截断/故障保留负例，避免仅有算术函数就允许不完整数据生成“通过”。之后明确工作量/预热/窗口/跨恢复边界与统计合同，再决定是否纳入正式工具源；修改既有产品/源码/构建输入前归档受影响114426绑定。代表性内容选择仍未收到人类答复；目标机、内存驻留、观测扰动、全进程/链接、人工、自包含、整帧及1h等分别保持未验收，无部署或提交推送。

### 11.75 E5-D 完整帧流适配与历史数据离线检查

新增隔离`full_stream_metrics_prototype_first.py` SHA **312CB2D4964D27E87D2E2995AA0D9BF0D31ECD97864138A073574C56E1E90F9D**，复用已绑定哈希的现役stream/pacing/loop验证器和11.74算术原型。输入为不可变字节，报告限制4MiB、流限制1GiB；在out证据子目录独占创建快照，失败也保留，不覆盖或删除既有文件。严格拒绝重复JSON字段及链接/路径越界；现役完整流验证通过后再次比对快照字节，从同一字节计算全部间隔，无预热裁剪/慢帧过滤/恢复后重置。

报告区分“给定流内的记录覆盖”与“真实工作量边界已验证”：仅前者可true，来源、路线、物理显示和目标性能仍false。所有Outcome计数、没有Present的循环数、首个/末个Present及两端未展示时长单列；启动/关闭和给定流外的空档仍未知。没有足够Present间隔的Null/短流不能生成FPS结果，不能将零绘制成本报告为无限FPS。

测试`test_full_stream_metrics_prototype_first.py` SHA **22808711ECC77D9E395C3EDC916F967D6CD40053E5B70D8B9414C3EE3A611254**通过 **6组，0.469秒**，日志SHA **D8965E32C53E376CEA3802B6EEF375109374E3D430C74EFB53587277637C3EFD**。包括600条实际序列化夹具、超过512条前缀、Pascal/camel报告、八种缺失/损坏、重复字段/缺换行、路径越界/不可变输入、已有快照不可覆盖。故障后恢复的140ms间隔被完整保留，Hitch比较明确失败，没有为了变绿排除该帧。全部测试快照保留于`metrics-adapter-tests-first-3tfh1q0z`。

固定离线驱动`run_metrics_historical_first.py` SHA **B73A1C2B51CF21DD9D86AC05FD846B963A8B44008389A20D27D1A8FF49ACA93E**读取原`loop-stream-cli-second`六份历史报告/帧流。先按11.73已完成114426证明核验12个输入的字节和长度，处理后再核对未变；未重新运行任何Editor/Player，也没有重新生成历史证据。Player报告仅显式投影loopCosts，原报告哈希与投影哈希分别保存。

最终`out/ecs-e5d-metrics-historical-first.json` SHA **55F664CF1468D591C38B75A735D6F297F26A303F3DA8C174169D51CACDF06321**绑定 **32项**输入/工具/快照。四份图形流通过完整校验并计算指标：每配置Editor600循环/599间隔，Player1595循环/1594间隔；两份Null各800循环、零Present，准确拒绝FPS算术。**这些是旧120fps开发夹具，不是当前Low/30fps或目标硬件数据**；即使数值大于30，也不能改判目标性能通过。历史Editor有限窗口平均略高于设置上限的数字如实保留，不在本片推断显示或调度原因。

本片工具仍为隔离草案。下一步将已验证的指标读取逻辑整理为受版本约束的正式Python工具及CLI，补明确的帧率上限/测量范围字段、完整证据输入及错误输出测试；新增正式源码后必须登记新候选输入边界，旧93/Python96不冒充新增测试通过。代表性路线/预热/时长和原目标阈值在正式采样前冻结；内容选择仍待人类答复。正式EXE仍C50742AD…，无产品修改、采样、部署或提交推送；E5-D及其他未完成门禁不放行。

### 11.76 E5-D 正式 Python 指标工具与完整回归

修改前按11.73完成的114426证明精确核对两个受影响既有文件（stream验证器及其测试），原字节保存到`pre-present-metrics-first`，归档manifest SHA **CCD053D809DC7E66C3D0F2A6E2C57B35EF267D764BB3065AD5B084913021DD29**；两个新模块/测试路径确认不存在。这里只检查受影响项，不声称本片又重验整个114426清单。未覆盖旧候选包、原日志、正式EXE或IDE文件。

新增`python/src/ncma_tools/ecs_e5d_present_metrics.py`（SHA **68F5D40BFD77E6E991BB7EA0EEBC69E57B7FE2DEFD643EC1C6D128CF01CADEC1**）及正式测试。统计版本固定为`ncma.present-return.v1`，保留11.74的精确有理数口径，不裁剪预热、不筛慢帧、不重置故障/恢复间隔。输出包含完整原报告及帧流SHA、实际帧率上限、Outcome计数、前后边界空档和覆盖范围；阈值比较不构成验收，工作量/来源/物理显示/目标性能标记均false。

现役stream验证器新增`verify_bytes`并与文件入口共享同一检查体：原文件入口以实际已打开描述符取长度，字节入口只接受不可变bytes，避免校验和计算读取不同文件内容。原版本、范围、计数、封尾、队列丢失、前缀和逐帧断言不放宽。原有原型所绑定的旧验证器哈希不会改写，旧证明按本片两文件归档读取，不直接以当前路径冒充历史。

CLI使用方式（工作目录为仓库，PYTHONPATH指向python/src）：

```powershell
python -m ncma_tools.ecs_e5d_present_metrics full.ncloop player-report.json --player
python -m ncma_tools.ecs_e5d_present_metrics full.ncloop editor-loop-report.json
```

只读输入，不启动引擎、不创建输出文件或提供AI写权限；stdout为单份结构化JSON。成功exit0；缺参数/未知参数为`invalid_arguments`、缺文件/损坏/不完整或无足够Present为`invalid_evidence`并exit2。Player完整报告须显式成功、无关闭/验证错误，不把失败运行静默移出正式队列；外层未来验收仍须保留所有拒绝结果。摘要计算以捕获字节为准，不能证明调用者没有先裁剪原始工作量。

59813终态exit0，原解释器执行完整`unittest discover -s python/tests -v`，**106/106通过，10.735秒**；原始日志SHA **19647325CDC6BE9AA6E6F980A3584877F5E84FE07A91A953414F96A3CAD36A95**。新增9项指标测试、1项不可变字节入口测试；真实CLI的五条成功/拒绝路径作为测试子过程执行。包括精确30/27/80边界、略低阈值拒绝、尾部向上取整、大整数、600条/512后缀、140ms故障恢复保留、重复/非有限JSON、失败Player、Null拒绝及输入文件不变。

另用正式CLI离线读取固定六份原历史文件，不运行新Player。`out/ecs-e5d-present-metrics-cli-first.json` SHA **B649A977BF5F9BAF585972F06A9298403CAC9F612FE405DFEB3B5E277636B8C1**绑定 **42项**：当前四个源/测试文件、完整106日志、两归档、12份旧报告/流及实际CLI命令/PID/原生退出/stdout/stderr。四图形exit0、两Null按预期exit2；原报告/流读取前后字节一致。实际输出显式frameRateLimit=120，不能将这些旧开发夹具标成新Low/30fps或目标性能。

本片已修改Python工具源，因此旧双93/96和旧组合文件清单仍是先前候选边界，不改称新工具整引擎回归通过。本片完成的是新工具完整Python106与六条离线CLI；最终冻结/完整门禁需纳入当前新源。C#/C++产品及正式EXE未改，无部署或提交推送。下一步核对11.7尚缺的真实托管/原生驻留和观测边界，再在代表性内容确定后冻结工作量与统计合同，运行匹配的正式队列。目标机、人工作业、自包含、进程/链接、整帧和1h等保持未验收，E5-D/E-F/M8.3不放行。

### 11.77 E5-D 共享进程内存采集服务

核对11.7现状：原`EcsDomainCosts`只有测试驱动前后两个进程快照；Renderer已有Scene/Skin/Resource/Environment/UI/UI Target/Pipeline/Post等分域逻辑字节统计，但它们不是物理显存驻留，也未证明跨域不重叠。不能把进程Private减去托管估计当作原生堆，也不能将分域值未经分析直接求和。

先实现共享基础服务`managed/Ncma.Application/ProcessMemoryCapture.cs` SHA **34C6B28ED0B93B2556AFB6AF45E95BBEED5414625783F777EB00419CE847AFA8**。Owner线程最多四个固定、递增、一次性的BeforeStartup/RuntimeReady/BeforeShutdown/AfterShutdown检查点，允许失败路径跳过点、不允许事后补点或重复取样。返回复制数组，不保持Process资源、定时器、后台线程、原生插件或文件写口；当前没有Editor/Player接线，不会改变默认运行路径。

每次记录进程working set/private/OS进程生命期peak、`GetTotalMemory(false)`原值及有效标记、最近GC索引/堆/碎片/commit与读前后各代计数、观察线程/PID/起止时间/原始线程分配。读取是顺序非原子的；最近GC数据可能陈旧，OS peak不是当前工作窗口峰值。受支持OS读取失败留下明确失败类型和null计数，不填零、不自动重试；不强制GC，不推导native-only或物理VRAM。MemoryAccepted/CompleteLifetimeCoverage/NativeOnlyBytesAvailable/GpuPhysicalResidencyAvailable全部false。

修改前仅对受影响的Application.Tests/Program.cs按旧114426证明精确核验并归档，manifest SHA **4A5839AF0C54FB5D0EE0BD4256B98C83812BA6B456238E68B41C3A3178980840**；两个新文件确认缺席。新构建使用已实际求值验证的`--artifacts-path out/verification/ecs-e5d/memory-service-build-first`及各配置`ArtifactsPivots=Configuration/net8.0`，不以现有managed/bin/obj或候选包为输出目标。

Release/Debug构建均成功、零警告；Release首次命令未配置完整文件日志，保存实际终态工具输出块`memory-release-build-terminal-tool.json`，明确**只含终态块，不冒充完整原始构建日志**，不为补日志重编译。Debug保留完整stdout/stderr。两配置完整Application.Tests均通过：新增内存 **51检查**、原17项application/services及9组场景UI路由，还有原循环/流/启动/恢复等测试。现役测试仍使用其明确配置的原Gameplay.Sample夹具，实际文件也绑定；不声称这是新的搬移发布包闭包。

测试包含真实OS读取、顺序/重复/非法/跨线程拒绝、部分检查点、复制隔离、负托管原值/无GC记录标记、计数不可用及序列化。四个标签在这里是单元测试顺序调用，**不是实际引擎生命周期**。保留全部八份原始观测：Windows进程计数可用；Release首次4.1954ms、Debug首次3.911ms，其余约0.8324–1.9361ms；原始观察线程分配首份4400B、其余3768B。不据此推断根因或最终观测预算，也不声称服务零分配；这些成本支持不默认逐帧读取的设计。服务和新测试不强制GC；完整测试程序其他既有生命周期测试的显式GC保持原样，不把整个测试程序当正常游戏性能场景。

限定核验`out/ecs-e5d-memory-service-first.json` SHA **B7376976BBDC5FC3344DC262AB67C0DDA92EA86838853F4C604134BB5E362F3B**绑定 **818项**，含新802个bin文件、三个源码/测试、归档、实际测试日志/观测与正式EXE；标明hostIntegrationImplemented=false/nativeResourceByteLedgerImplemented=false/completeEngineGates=false。未启动新产品性能队列、未部署/提交推送，正式EXE仍C50742AD…。后续重叠修改/构建前保存受影响的新818及先前证据；旧114426中Python两个文件和Program分别使用已登记归档。

下一步将默认关闭的`--memory-checkpoints`接入真实Editor/Player启动、就绪及关闭边界，区分完整关闭与失败保留，补默认不启用、失败和实际入口测试；不在模拟/Present热循环里采样，不增加AI权限。之后单独接原生逻辑字节账本，逐域保留generation/适用范围/缺失，不伪造物理显存或跨域总量。内存预算、代表性内容、目标机、正式观测扰动、完整进程/链接、人工、自包含、整帧及1h仍未验收，E5-D/E-F/M8.3不放行。

### 11.78 E5-D Editor/Player 生命周期内存接入与实际入口验证

本片完成11.77的真实宿主接线。Editor和Player新增显式`--memory-checkpoints`，默认false，不创建采集器、不增加报告字段。成功路径依次记录BeforeStartup、RuntimeReady、BeforeShutdown、AfterShutdown；只在原有生命周期边界读取，不在模拟、渲染或Present循环内采样，不强制GC、不调整原有关闭重试策略或AI权限。

Player报告中的可选`memoryCheckpoints`保留原JSON枚举字符串协议；Editor输出独立JSON行，内部枚举是原默认数字协议。两者同用共享服务，保留原始计数、有效标记及观测成本。Player启动失败没有RuntimeReady，关闭失败没有AfterShutdown，显式RetryShutdown也不会重写已经交付的不完整报告。Editor项目加载失败仅有BeforeStartup；Start自身已清理的初始化失败不事后捏造BeforeShutdown。**AfterShutdown仅表示宿主服务链关闭后，发生在诊断流收尾/报告输出/进程退出前，不代表整个进程生命期已覆盖**。

修改前按旧证据核对并归档七个受影响源码，`pre-memory-hosts-first/manifest.json` SHA **632396CF3FBF2C9C07D1070C407BE56F494C3EA9E7BCB0DDF27CACA5F0A67445**；不是重验全部114426文件。使用新的`memory-hosts-build-first`输出根，不覆盖先前服务测试产物或正式发布包。

执行过程保留两项失败，不改判或抹除：

- 首轮92528终态exit1：Release两项构建成功，新Player内存测试通过，但执行器传了发布包插件目录。既有FBX根运动测试按CTest构建布局寻找`NcmaImportKernel.dll`，因此FileNotFound失败。没有修改原测试、补拷DLL或降低检查。
- 第二执行器前置检查exit1：误认为旧搬移包证据绑定了当前构建插件目录；实际该路径不在清单，KeyError发生在创建目的目录/启动测试前。保留原脚本及实际工具输出转录（明确不是重定向原始日志）。

修正后的第三执行器55939终态exit0，严格使用CTest登记的`out/build/windows-ninja-{release,debug}/m2/plugins`。这些当前原生依赖作为新输入记录，不声称它们具有旧证明的等价性。复用已经成功的两个Release构建，只构建一次Debug两个项目；四项构建均零警告/零错误。两配置完整 **Player各66项、Editor.Services各185/185项**通过；含新增两项Player默认关闭/状态不变/启动失败/关闭保留测试和一项Editor参数/早期失败测试。原UTF-8 v2保持512次严格0B，无任何断言或NoGCRegion范围变化；原6808B失败仍不接受。

随后执行固定14个真正的源构建apphost子进程（每配置7个）：CPU Editor默认关闭、CPU Editor显式开启、DX11 Editor显式开启、Editor缺失项目、Null Player默认关闭、Null Player显式开启、Player重复参数拒绝。实际退出为10个0、2个缺失项目4、2个重复参数2，全部符合计划；未把负例当作产品成功运行。成功开启的真实进程提供四点记录，PID与实际子进程一致，Windows计数可用；关闭模式没有字段。两配置Player开启/关闭均三tick且StateSha256一致。Editor DX11是两帧参考smoke，不是代表性动作场景；本片没有单独验证开启内存采集的图形Player生命周期，也不冒充搬移发布包验证。

独立核验`out/ecs-e5d-memory-hosts-first.json` SHA **A2B10175D44D6F9408A4435E65A1A5EECC4BF81A3B30C529FDD332B325F7957D**绑定 **2276项**，重新核对先前818项基础服务证据，绑定当前源码/独立bin、实测依赖/fixture、全部子进程退出和stdout/stderr、14条CLI结果、归档、两项失败及正式EXE。源码/依赖读取前后不变；没有重验整个旧114426清单。正式`out/bin/NcmaEngine.exe`仍为C50742AD…，所有本片会话已结束；无部署、提交或推送。

当前仅通过限定托管回归及源入口功能检查，不把旧双93/旧96或当前结果提升为新候选全引擎通过。MemoryAccepted/CompleteLifetimeCoverage/NativeOnlyBytesAvailable/GpuPhysicalResidencyAvailable始终false；没有推导原生堆、物理显存、泄漏、预算或目标机性能。下一步分别加入原生逻辑资源分域记录，保留generation/可用性/未加载状态，避免只为观测初始化未使用域，也不未经证明跨域求和。重叠修改前保存受影响的2276项输入；代表性内容的人类选择、指定目标机、正式观测扰动、完整进程/链接、人工、自包含、整帧及1h等仍未验收，E5-D/E-F/M8.3不放行。

### 11.79 E5-D 只读原生资源分域账本

核对真实实现后调整11.78的读法：现有统计属性首次读取会绑定域API，且原生SkinStats会调用Resolve推进GPU计时。因此没有把这些属性串接成观测器，而是新增独立的 **Renderer query22/v1**：24字节函数表、152字节固定POD快照，复制八个域已有逻辑字节和资源计数。原有query1–21与统计语义不变，未知query23拒绝。

快照包含renderer generation、pureUI/fail-stop/active-frame标记，固定八个域为网格、蒙皮、纹理/材质/场景目标、场景管线、环境、UI图像/列表、UI目标、后处理绑定。原生读取无资源/Kernel初始化、无GPU调用/等待/计时Resolve、无分配、无计数写回；读取必须由模块owner线程执行，拒绝重入/错误句柄/空指针/短输出。短缓冲区不写输出。fail-stop时允许复制保留的计数，不隐式恢复设备。

这里原生直接知道所有域计数，不采用“托管API未绑定则无法读取”的间接状态。零值表示当前计入该域的逻辑资源为零，**不证明实际驻留或内存分配为零**。Skin的输出网格同时进入mesh账本，域间确有重叠；不跨域相加。字节未完全覆盖固定kernel、shader、driver、swapchain、ImGui及分配器开销，不是物理VRAM、进程native-only堆或泄漏证明。C# `RendererSession.CaptureResourceLedger()`显式返回复制数组，TotalBytes=null，PhysicalGpuMemoryAvailable/CompleteNativeMemoryCoverage/MemoryAccepted=false；当前不自动逐帧调用，尚未接入宿主内存报告。

修改前将CMakeLists、RendererPlugin及Rendering.Tests入口三个原文件与旧证据匹配后归档，manifest SHA **18B95828AA04093BAFAC051E8E6438B31DBE4A49E24FBC492083752280E27B34**。采用全新的`resource-ledger-native-first/{Release,Debug}`和`resource-ledger-managed-first`构建根，不覆盖旧发布包/managed/bin。首次74722终态exit1：原生Release成功，但新增托管测试误用了不存在的ScenePipelineStats属性，编译CS1061；失败源码和日志保留，改为实际PipelineStats。后续发现旧原生负例把query22视为未知，随新接口改测query23，并补原生ABI/状态测试；额外归档native测试原文件与失败新测试，manifest SHA **6D9BFEA7D6A93B6A3DC1A37344B50DD1087688863E1191FB5EBE29F5E8D251B0**。

第二执行器57186终态exit0，Release/Debug各原生与托管构建通过、零编译警告；完整NcmaRendererNativeTests通过，含新增ledger ABI/线程/重入/空域不初始化/计时状态不变/fail-stop及active标记测试。托管新增 **37检查/配置**通过：真实建立八域资源，逐域与现有统计交叉核对，确认网格/蒙皮重叠，复制隔离、销毁归零（仅逻辑资源）、新generation reset及pure2D仅UI图像4字节而其余域为零；原生验证0/0。既有GPU工作计时 **21项/配置**、主GPU计时 **24项/配置**回归通过。新增CTest `NcmaRenderingTests.ResourceLedger`已登记且两份生成CTest文件核对；本片直接运行这些限定测试，**没有声称全量94项CTest通过**。

独立核验`out/ecs-e5d-resource-ledger-first.json` SHA **E9CB77BBBA0A525A4E10C7389AABE0C2D319AD17825490C002BB0CE37E508743**绑定 **3461项**，重新核验上一轮2276项不变，包含当前七个选定源码、两次归档/首次失败、原生与托管实际输出/生成文件/日志、测试子进程退出、八域观测与正式EXE。不是完整源码/依赖冻结或全进程模块追踪。所有会话已结束，无部署、提交或推送；正式EXE仍C50742AD…。

下一步将独立资源账本接到Editor/Player既有显式内存报告，分别记录无renderer、接口不可用/读取失败与有效零计数，不把缺失填零；覆盖正常运行、fail-stop、资源关闭与设备代次变化，避免自动重试或观测改变原有关闭行为。本片功能测试不等于目标性能/内存预算/观测成本已验收。代表性内容、人类目标硬件、正式观测扰动、完整进程/链接、人工、自包含、整帧及1h仍待完成，E5-D/E-F/M8.3不放行；重叠修改前保存受影响的3461项证据。

### 11.80 E5-D 资源账本宿主接入与恢复边界验证

新增 `Ncma.Rendering.RendererMemoryCapture`，接入既有默认关闭的 `--memory-checkpoints`；通用Application层不新增渲染依赖。Editor/Player只在显式启用时读取资源账本，分别记录启动前、运行就绪、关闭前、Renderer释放前、服务关闭后，以及图形退役前/重建后的真实边界。真实恢复报告包含旧、新renderer generation；失败关闭不补造AfterShutdown，不自动重试保留资源。

观测器限owner线程，固定32条前缀，后续只累计遗漏数，不继续原生查询。状态明确为NoRenderer、Available、Unsupported、ReadFailed；缺失/失败的Ledger为null，不冒充有效零资源。已交付报告深复制域数组，后续显式关闭重试不会改写旧报告。关闭状态只表示服务链边界，不是进程退出、泄漏或物理驻留证明；逻辑域有重叠，不求总量。耗时/分配记录不包含最后的Sample对象分配、报告复制和序列化，不能据此宣称完整观测开销已验收。无强制GC、GPU等待、隐式资源初始化或默认逐帧读取。

修改前保存十个受影响源码，`pre-ledger-hosts-first/manifest.json` SHA **EAD3E7A09AE3A25D56F7A8C3A7330BD675CCE3FB407CB8D4C098BA2A42705A7F**。使用新的托管输出根与隔离原生fixture：经核验的原构建插件加本轮query22 Renderer，并明确不是规范发布包。首轮1329终态exit1：Release三项构建及前五项测试已成功，Services既有原生动作序列对照测试因fixture缺少NcmaAnimationTests.exe失败。保留原输出；第二执行器78837仅补齐原构建中的该EXE及其精确NcmaNative.dll依赖，复用已通过步骤，不改产品或测试断言。

Release/Debug各三项构建零警告/零错误；每配置通过：

- 完整Player **66项**、完整Editor.Services **185/185项**，保留原UTF-8 v2的512次严格0B断言；历史6808B失败仍不接受。
- 资源观测 **9项**、实际旧原生接口不支持 **8项**；覆盖线程、无renderer、真实UI资源、释放后读取失败、复制隔离、32条界限及遗漏。
- Editor图形恢复 **37项**、Player图形恢复 **38项**；覆盖实际设备代次变化、关闭失败/拒绝/异常/重入、失败报告不补AfterShutdown及后续重试不改旧报告。

固定18项实际apphost入口检查包含每配置CPU Editor关闭/开启、DX11 Editor开启、旧Renderer Editor开启、Editor缺失项目、Null Player关闭/开启、DX11 Player开启、Player重复参数拒绝。首轮在Release图形Player以dependency_failed退出3：源构建目录没有发布包默认提供的后处理库，而原Low场景确有后处理设置。保留该失败报告及全部输出。修正的是隔离项目fixture：从已核验历史包复制精确shader/postprocess库并写入显式项目选择，**场景字节、几何、Low设置不变**，没有删除后处理组件或跳过预检。第二执行器复用前七个成功进程，继续剩余检查；最终14项成功exit0、两项缺失项目预期exit4、两项重复参数预期exit2，均符合计划。Null开启/关闭StateSha256一致；旧插件报告Unsupported，默认关闭无resourceMemory字段。不是搬移发布包、代表性动作路径或目标性能验证。

另存当前CMake原文件，manifest SHA **C872661AE13338FC84F429C34249B43C38B755AAF07720AEA0EC393DD541CAF1**，登记 `NcmaRenderingTests.RendererMemory`（60秒）。首个配置执行器因cmd引号错误在调用vcvars前退出1，原脚本/输出保留；第二执行器用独立bat和新目录完成Release配置，核验生成条目。现共95项CTest，**未运行全量95项**，不覆盖此前专项测试结果或沿用旧93项作为本候选全量通过。

独立核验 `out/ecs-e5d-ledger-hosts-first.json` SHA **0462583BAAA8E2C0397BC00F76E5FBF8A52E9F2E4CBFDE6DF81927ED3B40CAF5**绑定 **5354项**，通过十个源码及一个CMake归档映射重验前轮3461项，检查本轮源码、二进制、所有原始结果/失败、真实CLI报告、依赖选择和同场景字节、生成CTest及正式EXE。所有会话终态，无部署/提交/推送；正式EXE仍C50742AD…。

下一步建立当前候选源码/依赖边界，运行规范构建和全量门禁，再开展正式观测扰动及内存测量。当前没有完整源码冻结、完整进程模块覆盖、内存预算或性能验收；代表性内容的人类选择、GTX1060/RX580目标机、人工/自包含/整帧/1h等仍待完成。E5-D/E-F/M8.3不放行；后续重叠写入前保存受影响的5354项证据。

### 11.81 E5-D 内存候选完整门禁准备

上一片已完成限定回归，本片转入规范候选的输入登记和完整门禁。`prepare_memory_gates_first.py` 已实际执行（会话83190），通过七份已有归档映射核对旧114426项和最新5354项证据；将76项会被规范构建覆盖的原输出复制到独立归档，manifest SHA **406D4E8D12E5EE4EB90218A78D56286F3FF01CC6C596934E6634B2D17CCF5CF4**。无删除、无覆盖旧证据、无产品源码变化。

当前同一进程仍在登记当前源码清单和重新核对原工具/SDK/Python输入；最终边界文件尚未被接受，不宣称全边界已完成。三个执行器语法检查通过，仅表示可解析。后续固定顺序为Release规范Build.bat -SkipTests、完整95项CTest，再Debug同样完整95项，最后完整Python106项；各构建/测试前后核对输入，保存原始stdout/stderr和真实进程退出，首次失败停止后续。SkipTests仅将规范构建与随后完整CTest拆开，避免原脚本的旧性能采样和自动部署，不关闭告警、NuGet审计或现有测试断言。

此时完整门禁执行器尚未启动，新候选构建/双95/Python106均无结果。必须先等待83190终态成功并检查登记边界，再运行该固定执行器；不重复准备，不在运行期间改变候选源码或已登记驱动。门禁后仍需实际新包、生成字节及原生依赖记录审计，不能用此前混合隔离fixture或旧包替代。正式EXE保持C50742AD…，无部署/提交/推送；代表性内容、人类目标机、正式观测/内存、全进程、人工、自包含、整帧和1h等仍待验收。

后续进展：83190已终态exit0；最终 `out/ecs-e5d-memory-gates-inputs-first.json` SHA **BF61FE6787A7B8E98FAA7B2BA8CA626189F5313504645C719BE904CFB49F734E**登记 **89297项输入/6839项源码**。这是当前功能构建文件边界，FullE5DFreeze/RepresentativeWorkloadFrozen仍false。经过正常NuGet网络权限审批，实际启动已登记的 `run_memory_gates_first.py`（唯一活动会话7473），计划文件绑定上述边界；当前在Release-before检查，没有声称规范构建或完整测试已成功。原83190不可重启或继续轮询；下一轮先轮询同一7473，不变更候选与三个已登记驱动。

独立后验脚本 `verify_memory_gates_first.py` SHA **7F3254880EC48A5D748A726A1246C2F995077518595A34BF5E17C95FBA2CEAD3**已准备并仅通过语法检查，尚未执行。它要求完整流程终态成功后检查实际12步退出、每配置95个独立通过名称、Services185/Player66/UTF8v2原512严格0B、资源观测原始结果、十个实际新包manifest及文件成员/哈希、旧证据映射与正式EXE。不借语法检查宣称后验通过；生成产物、原生依赖闭包和搬移CLI仍需后续独立检查。

最新同一7473状态：Release-before子进程12196实际exit0，已核验89297项输入/目录成员/归档/SDK/HEAD及正式EXE；现在进入Release规范构建。尚无本轮构建或全量CTest成功结论，继续等待同一流程。

后续实际进展：Release-build子进程20860终态exit0，规范候选为 `out/package/m2-7/Release/63bb16c71ef144698e19b56d96debf67`，包含五类包。原始构建stdout SHA **9F0B16F20EB46A799F889254280C2A5D13EB76E73D3568668EC2B4D5230C1096**，stderr为空；25处托管警告汇总均为0，未检出非零编译警告。Release-built子进程10344随后exit0，重新核对全部89297项登记输入。现在同一7473进入完整95项CTest，最后读取为前19项通过、主Rendering第20项运行中；没有完整Release/Debug/Python通过结论。正式EXE哈希再次核验仍C50742AD…。

另外准备只读原生后验 `verify_memory_native_first.py` SHA **7A1B5B2451C96BAE9627D82324058A402452ACBB10671E9FFE072951D849F934**，仅通过语法检查，未执行。它必须等完整独立功能证明生成后，才读取双配置实际Ninja图/281条编译依赖记录、运行既有解析器负例、核对关键头文件覆盖及确定性shader生成字节；检查前后图文件不变，不执行配置或构建。不会把Ninja记录等同于完整进程/系统DLL链接闭包。实际新包搬移CLI还未准备或运行；当前继续等待同一7473，禁止重复测试或改变冻结源码。

后续同一7473已确认主Rendering通过，最近读到Release前31/95项通过、SceneUiInteraction第32项运行中，仍无完整配置通过结论。等待期间仅新增隔离验证执行器 `run_memory_relocated_first.py` SHA **D3CFB4F247C029E88F38453880AF15411EDE04D3EC8499716EB88A4CC5A465B0**，语法检查通过但未执行；没有复制包、创建目标目录或启动额外宿主。它先要求完整功能后验及原生后验成功，再从其证据指定的当前包复制每配置Editor、Null Player、DX11 Player六个隔离产品，沿用字节不变的历史Low场景。固定16项真实apphost：每配置CPU/DX Editor、Null/DX Player，分别关闭/开启内存观测。图形Player显式1920×1080 borderless-pixels、cap30、三目标tick，只验证真实窗口尺寸和生命周期字段；不是代表性动作内容、性能采样或物理显示验收。核对真实退出、默认字段缺失/显式NoRenderer与Available、八域计数元数据、Null状态一致及前后输入字节，不补造内存/性能通过。全部执行后还需独立CLI后验，当前尚未实现该后验；不提前降低剩余门禁。

最新同一7473：Release前62/95项通过，GraphicsRecoveryJointOff第63项运行中。其中新增登记ResourceLedger第54项0.73秒、RendererMemory第55项0.36秒通过，属于当前规范构建的实际完整测试流程；完整Release/Debug/Python仍未完成。没有改动冻结产品输入，也没有重复运行已成功测试。

已补齐独立搬移后验 `verify_memory_relocated_first.py` SHA **216F7AF75003993AC8527F04AD33FD7C4BF05ECDE8B79041977C745E665D8A40**，检查实际退出/进程PID、顺序检查点、缺失与有效计数、八个唯一域、generation、默认字段缺失、无总量/无性能接受声明及实际1080输出。其可导入验证函数已运行离线控制：从旧5354项证明绑定的12份真实源入口报告取数据，全部有效报告通过；10种副本损坏（伪造接受、总量、状态、缺失ledger、零generation、重复域、遗漏关闭点、错误PID、错误序号、错误观测总数）全部拒绝。控制记录 `memory-relocated-audit-controls-first.json` SHA **9FEEBDE39929182B742B7C2795BA1F3C02C79259DC01A23BE98D6770CE8C755B**；原报告未改动，没有启动新宿主。这是核验器控制通过，不是新包搬移通过。正式执行顺序仍为同一7473终态成功→完整功能后验→原生后验→固定16项搬移→独立搬移后验；现阶段不部署、不提交推送、不放行E5-D。

Release完整结果已产生：CTest子进程3488实际exit0，**95/95通过，907.64秒**。原始stdout SHA **EFB2D9CA84BDEE88F015CBCDF5ECCC8EB22E69ADB431EEBF9ADF3DAC1F1F4343**，复制详细记录 SHA **F80373AFFBF75C557936C578D4A2CBB046A46BC43289A2F89F91B517C701F27E**。独立读取详细日志确认37项资源账本、9项资源观测、Editor恢复37/Player恢复38、Editor.Services185/185和Player66通过，原UTF-8 v2保持512次严格0B，空控制0B/正控制80B且受检退出；历史6808B失败仍不接受。随后Release-tested子进程17104也终态exit0，89297项输入复核通过；同一7473现在进入Debug-before检查，尚无Debug构建/全量测试及Python106完成结论。禁止重启已完成Release或用单配置通过冒充全候选验收；新包独立后验仍须等待整体终态成功。

后续Debug进展：Debug-before子进程10392终态exit0，再次核验89297项输入；Debug-build子进程17212终态exit0，规范候选位于 `out/package/m2-7/Debug/d462812174884e8a93182e70102035ff`，原始stdout SHA **9D022636BC3FB20048288EBC4B9CDA21D99AE5738C2F59B7C4428BACA2FD7CF6**，stderr为空。25处托管警告汇总均为0，未发现非零编译警告。随后Debug-built子进程18672也终态exit0，构建后89297项输入复核通过；同一7473进入完整95项CTest，最近读取为前19项通过、主Rendering第20项运行中。Debug全量和Python106还未完成；不重启Release、不重复构建、不修改冻结源码、不提前运行独立后验。没有替换正式EXE、提交推送或性能/内存/E5-D通过声明。

Debug完整结果已产生：CTest子进程22396终态exit0，**95/95通过，1048.82秒**。原始stdout SHA **59AFBF5105F7C5CAF58301F789E3B184C0637E0335E7086B96BB2660BF166063**，详细记录 SHA **FA690879373CDFCD7A37A71E0C9850142D8AE9C38673D66843E92060DF51985C**。独立读取确认Debug资源账本37项、资源观测9项、Editor恢复37/Player恢复38、Editor.Services185/185、Player66通过，UTF-8 v2仍为原512次严格0B，空控制0B/正控制80B及受检退出均通过。两配置完整CTest至此均通过，未重跑已成功用例。当前同一7473继续Debug-tested输入复核，Python106及最终复核仍未完成，整体执行器尚未终态；独立功能/实际新包、原生依赖与生成字节、固定16项搬移及其后验均须按既定顺序等待。没有将双CTest通过提升为内存/目标性能或E5-D完成。

完整流程现已结束：Debug-tested16208、Python23304、final24780均实际exit0；Python **106/106通过，11.165秒**，原始stderr SHA **9B8EF74AAB8CA90A0F0F70B43C44A00002C6E0D8FF4AE1FF14D8B75497537C75**，独立计数106个通过名称并以OK结束。最终复核确认89297项输入及目录成员/缺失项/归档/SDK/HEAD、正式EXE不变。7473整体终态exit0，执行记录含12步且无非零退出，不再轮询或重启该流程。

随后实际启动独立功能/新包证据核验（会话99910），`run_memory_full_audit_first.py`固定校验并调用已准备的 `verify_memory_gates_first.py`（SHA7F325488…），以二进制stdout/stderr日志保留真实核验过程。该第一次核验现已终态exit1：成员相等断言把编辑器既有 `out/user/logs/editor-candidate.jsonl` 运行日志当作未声明发布文件，未生成完整证明。原脚本、失败输出与退出记录保留，stderr SHA **FFCBE6C6141ACA93CA67B3B21019FD6A4A6299228B85D6C81C6C8102365E2B18**；不重跑原核验器、不删除运行日志、不修改产品以适应错误断言。

只读对照 `Ncma.Editor.App/Program.cs`、`ApplicationLog.cs` 与既有 `scripts/EditorDeployment.ps1` 确认：应用目录日志是既有行为，部署校验本已允许有界日志。实际十包只有两个Editor包带这一额外文件，原部署校验对两包均通过。新增独立分类模块 SHA **C0B81010BF0981A9B14F8543863EA986F940AF2502E37D5180FD1E7C6B79CCF5**仅允许已观察到的准确主日志路径，检查重解析点、1MiB上限、严格UTF8/JSON字段、序号/时间/级别/字符串界限/UUID；Player及任何其他额外路径继续拒绝。真实十包/两个日志与15项合成负例控制通过，证据 SHA **6FCA02FF25A549018A1399E615547D42352A07B624A831DCE2892C2BB09CBFC3**。日志以单独快照绑定，不宣称其作者身份已获密码学证明，不弱化不可变发布文件的哈希检查。

第二版 `verify_memory_gates_second.py` SHA **0F25B88E72842EB147488AF5EDA70A09083E47F916F55370531C7A18F0A38CD2**经AST检查，现由会话46176执行，输出独立 `memory-full-audit-second` 原始日志；尚未完成。其结果schema2会显式保留originalAuditSucceeded=false。另准备新搬迁执行器 **671FDE46A7A8390D4CAB7832D922414D4A3F2E0298C157C0DEC3ABA36BC68A8A**：仅复制manifest及所列发布文件，历史日志留在源包并哈希绑定；新搬迁后验 **277230995F19E6132B430E079911A0AB9A9D45011C5BC7391718817A9B5903C4**单独分类新生成日志，原内存核心函数AST与已通过12正例/10负例版本完全一致。原未执行工具全部保留，新工具只完成语法/结构核对，尚未运行实际搬迁进程。

第二版核验现已终态exit0（46176，子进程24860），完整证明 `out/ecs-e5d-memory-full-gates-first.json` SHA **1D1D4F1F96CA31D510F0D42B43D4C6BA15A37E5EAF5CFD1DF0332F9CB29E387A**绑定120,724项，十个实际包均完成检查；两个独立Editor日志各422B/两行。originalAuditSucceeded=false明确保留原失败，memoryAccepted/performanceAccepted/deployed仍false。

随后 `run_memory_remaining_first.py`启动（44292），按原生依赖/生成字节核验→固定16项新包搬移→独立搬移后验执行；脚本SHA在启动前及各自执行前固定验证。该次执行的终态、原生结果与搬迁失败修正见11.82，不再按本条历史时点重启。双95/Python106与完整新包功能证据不等于内存/目标性能、完整进程/人工/自包含/整帧/1h或E5-D完成。

### 11.82 E5-D 内存候选新包搬迁与独立功能交付核验

原生只读核验子进程21084退出0，`out/ecs-e5d-memory-native-first.json` SHA **47B60FC607A9F5FE9BB8EB4003A6624A8C89616EFBC9902500A45F0C4264EE4D**绑定89,330项。双配置各281条VALID依赖记录，关键Shader编译器2项、Renderer230项头文件覆盖既有基线，GUI/Renderer确定性Shader头字节未变；Ninja图在读取前后不变，未重新配置/构建。**每配置仍记录9个零头文件依赖单元**：GUI Shader编译器、RendererValidationProbe及7个Jolt单元。它们未被删除或补造为非零，当前证明只声明实际记录/关键覆盖检查完成，不声明全进程/系统DLL/所有头文件闭包完成；后续需要单独归类。

首轮搬迁执行器18136退出1：Release CPU关闭/开启内存观测两个用例退出0，DX关闭用例PID20032退出2、stderr为 `invalid_configuration: application_initialize_failed`，执行器停下，没有启动后续用例。原六个副本位于 `memory-relocated-first`，计划、原始输出、失败退出以及复制脚本 **671FDE46…**全部保留。只读定位到复制脚本错误地把用于判重的lowercase键作为目标文件名，导致 `Flat2D-0-0.ncshader` 被复制为小写；`RuntimeReadPin`既有精确大小写保护因此拒绝Shader预检。产品资源、场景、校验规则不变。

新增路径拼写核验模块 **0E8F351BDADBD03CBDE481FF05B35F26BEE54F7FD86F42D62B6842F14B01C59F**，对真实六个源包通过、六个错误副本准确拒绝，另四个合成负例拒绝；控制记录 SHA **7C80544C49B2527B63488FB7F1D0D8D4DB6B1AF4587B2FB804A612C1DEED4C73**。第三版复制器 **BDDA1B718C60D3AE93ABCC106A95A07CCDD335A6E78E019AC28154F866D4AA83**在新的 `memory-relocated-second` 目录按manifest原始路径复制，只用小写键检测冲突；源/新副本都做精确路径验证，发布payload与运行日志分别核验。旧副本不覆盖不删除，并作为失败证据哈希绑定。未重跑完整CTest或已成功原生审计。

新顺序执行器53316终态exit0，实际搬迁子进程24572、独立审计子进程24712均exit0。固定16项实际apphost全部退出0：每配置Editor CPU/DX、Player Null/DX，各自关闭/开启内存观测。默认关闭不输出内存字段；显式开启的进程PID、顺序检查点、NoRenderer/Available状态、八个有序逻辑资源域、generation及未接受内存/性能声明均通过。Null开关前后同为3ticks、stateSha一致；四个DX Player各3次Present，实际初始/最终1920×1080、0尺寸变化、0验证错误/警告。只验证短功能路径，不是代表性动作内容、物理显示、30fps稳定性或目标机验收。

独立证明 `out/ecs-e5d-memory-relocated-first.json`（首次成功写出，schema3）SHA **42D86D960A152B6E1ED7C1D12126BA698B8F060E25EFFECFBA14FECBF13A9EA9**绑定2,044项，并链接完整功能证明和原生证明。审计脚本 **D0BD18D72074947EB590F02D24CCFF08ED20EB77C4B4A31DC3537CFFBF189EA9**确认原核心函数AST未变、旧12正例/10负例控制有效，核对六个源/副本清单文件哈希与路径、原失败、新16份原始输出、同Low场景字节及前后输入不变。新Editor运行日志各8行（Release1676B/Debug1674B），作为运行输出独立快照，不作为不可变payload。memoryAccepted/performanceAccepted/deployed均false。

本片所有会话均终态，无剩余运行进程需要轮询，不重复成功测试/测量。正式 `out/bin/NcmaEngine.exe` 再核验仍为 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**；无引擎产品源码变更、部署、提交或推送。下一步继续零头文件记录归类、观测开销/内存覆盖；代表性内容选择仍待用户答复，目标机、完整进程、人工、自包含、整帧和1h门禁未完成，不关闭E5-D/E-F/M8.3。后续任何产品/构建变更之前，须按新证明的受影响输入先归档。

### 11.83 E5-D 生命周期内存观测开销固定对照

先复核当前双配置实际图与11.73已有解释：两项零直接头记录源文件只引用系统头名称；七项Jolt使用 `/Yu`、`/FI`，原Ninja图显式依赖PCH头和产物。当前PCH各32条记录包含Jolt.h、Memory.h、IssueReporting.h与Vec3.h，DebugRenderer两个缓存选项均OFF，相关编译命令无该宏。该局部解释已经存在，没有重复编译器诊断、没有补造零记录或声称完整预处理/系统头/进程闭包。

随后在不改产品的前提下执行固定Release对照，执行器 `run_memory_observer_pairs_first.py` SHA **CD8FD267C64D587156D4099A717A4F82F170840F07297EF66E97BCE75FF04150**。启动任何宿主前写入计划，四组Editor CPU/DX、Player Null/DX分别按AB/BA/AB执行（A关闭、B开启 `--memory-checkpoints`），共24个进程、12对；每例新建仅含精确大小写manifest payload的副本，Player使用原历史Low场景，源和副本哈希绑定。正常GC、不增加NoGCRegion、不重试、不丢弃慢样本、不变更目标门槛。Editor固定120帧，图形模式cap30；Player目标120tick，图形模式明确1920×1080 borderless-pixels。计划是开发级观测扰动实验，不是代表性动作场景或目标机验收。

42792终态exit0，执行器子进程20040及24个实际apphost全部exit0。所有Editor各120次循环，所有Player实际120tick；Null各120循环且六个stateSha一致，DX Player各61次Present（请求/初始/最后呈现均1080p，0尺寸变化与验证错误/警告）。合计2,526条完整循环，无遗漏。内存开关两侧使用相同Loop观测，Editor还使用相同Startup观测；保留实际PID、阶段顺序、资源代次、8域计数、默认字段缺失及采集内部时间/分配。外部进程墙钟包住启动至退出，包含运行时启动、报告IO/序列化及观察父进程调度，不称为纯采集函数开销。

独立分析核验99233终态exit0（子进程20068），`out/ecs-e5d-memory-observer-pairs-first.json` SHA **6161C8865BBE22497C3142549500A1F51157C7CAE00152D48C6D7F31A54FE5B6**绑定3,337项。原内存验证函数与正式v2循环验证器核对全部输出，再分析每对进程耗时、启动两段、首循环和去除首循环后的全部循环；不裁剪异常值。新增分析算术控制5组正例/2项负例通过，记录 SHA **9DA53F5AA36E982640B29D95DAF8C94DC9A2AE82C9BDDF8697D66F4D2AA2D1AE**。

| 组别 | 四次进程采集内部累计ms（三例范围） | 资源账本内部累计ms | 整进程开启减关闭ms（三对原值） |
| --- | ---: | ---: | --- |
| Editor CPU | 7.1681–8.2853 | 0–0.0002 | +45.2057、+3.5834、+14.6842 |
| Editor DX11 | 7.5968–8.6450 | 0.7517–0.8456 | +25.7961、+68.0699、+11.9980 |
| Player Null | 7.0909–9.0566 | 0.0001–0.0002 | −3.6084、+21.8782、+7.2558 |
| Player DX11 | 7.9376–8.5594 | 0.5109–0.5385 | +13.8945、+12.0344、−18.3878 |

进程采集记录的内部累计分配每组分别为15,160/19,896/14,584/20,088B；图形资源账本各896B，非图形读取区间0B。**这些不含观测器构造、最终样本创建、报告复制/序列化和调用方开销，不能宣称完整服务零分配或内存预算通过。** 整进程差值正负混合，未证明独占因果开销；均值/首帧差异保留，未以“噪声”删除或以较快样本替代。新观察仍不是GTX1060/RX580目标硬件，也不是1小时泄漏或完整路径帧率验收。

最终归档核验 `out/ecs-e5d-memory-observer-summary-first.json` SHA **B9816135CA9C3B7B7E1329DE214E652F4AB44831DF0587379BF1C8168D518195**绑定3,397项：重验全部3,337项、控制/驱动/独立核验原始记录、当前Ninja图/PCH32/相关源解释及正式EXE。所有会话终态，没有产品源码修改、构建或重复完整测试；正式EXE保持C50742AD…，未部署/提交推送。下一步完善观测器整个调用链的成本边界、原生/进程内存覆盖；代表性内容仍待用户确认，完整进程/目标机/人工/自包含/整帧/1h门禁继续保留，E5-D/E-F/M8.3未完成。

### 11.84 E5-D 可维护的内存观测 API 与输出成本校准

本片将校准纳入现有 `Ncma.Rendering.Tests`，不是改运行时接口或继续仅依赖忽略目录中的临时测量程序。改动前按120,724项功能证明复核并归档 `CMakeLists.txt`、`managed/Ncma.Rendering.Tests/Program.cs`，归档清单 SHA **1B44A077BD84421A59D3288368D0ECABDEA702F5A68CBC44CAF2C7D81A019C0A**；新增 `MemoryObservationCostTests.cs`、CLI `--memory-operation-cost-tests` 和CTest `NcmaRenderingTests.MemoryOperationCosts`。原完整证明的这两个原路径以后须通过归档读取。运行时源码、C ABI、GC策略、预算和发布包未修改。

每个配置固定8轮（4轮无渲染器、4轮实际DX11），每轮17操作：两类构造、各四次Observe、两类Capture、两类Editor JSON字符串、Player内存字段片段UTF8、实际stdout两行及CreateNew文件写入/Flush(true)。采用实际64×64纯UI Renderer和1×1图像，检查观测前后generation及八域计数不变；进程快照和嵌套资源数组的复制隔离均校验。窗口/Renderer准备位于校准区间外；原生资源读取已为身份基线预热。这里的checkpoint是校准API调用，不冒充真实宿主启动/关闭；真实生命周期仍以11.82/11.83入口证据为准。

区间包含被调用API以及返回对象/数组创建，排除delegate准备、收集器自身行记录/最终报告；保留原始线程分配、有效性位、前后GC计数，不强制GC。Editor输出使用原两种匿名包装的JSON表达式和Console.WriteLine；Player只序列化/写入内存字段片段，明确不称作整个PlayerReport。首轮仅为本进程该探针第一次测量，不宣称实际冷启动/JIT的独占成本。成功、异常前缀均CreateNew保存，不覆盖证据。

隔离构建执行器98873终态exit0，8个步骤全0：Release/Debug构建均0警告/0错误，各新增 **48检查、136条操作记录**；既有RendererMemory各9检查、ResourceLedger各37检查通过。首次登记辅助执行器在CMake启动前退出1，保留 `memory-calibration-registration-first`；仅用ver命令的只读控制复现Python直接以正斜杠 `C:/Windows/System32/cmd.exe` 启动失败，而反斜杠或cmd.exe成功。第二份执行器只修正cmd路径并使用新隔离目录，配置及 `ctest --show-only=json-v1` 均exit0，确认总数96、准确flag和TIMEOUT60。**这是实际登记核验，不是执行注册CTest或当前完整96项通过**，不得复用旧95项冒充新回归。

Release本次校准：两类构造分别704B/320B；Process快照704B，Resource快照424–936B；首次Editor进程/资源序列化分别43.0011ms/22.1390ms，后续观察到显著降低；Player内存片段首次4.9300ms。Debug对应首次45.9269ms/22.9471ms/4.3760ms。两配置全部校准区间分配读数有效，未观察到区间内GC次数变化；这不证明GC暂停不存在或任何路径零分配。数值是该受限探针事实，不能直接归因到shipping宿主启动、外推代表性游戏或改判目标预算通过。尤其构造/深拷贝/首次序列化不在先前内部采集计数中，不能漏算或跨不同测量区间直接相加。

独立核验 `out/ecs-e5d-memory-calibration-first.json` SHA **1A3AB1D55802E963777C6E0044250940736685E079757478F7E0050CE45561C9**绑定5,773项：重验旧3,397项不变、两份源归档、新源码/全部隔离托管输出/真实原生插件、8步退出、stdout与8份片段/每配置136条记录、首次辅助失败及第二次实际CTest登记。两配置分别验证8种损坏副本，共16负例全部拒绝，没有改写原报告。fullRegressionPassed/memoryAccepted/performanceAccepted/deployed均false。

全部工作进程终态，正式EXE仍C50742AD…，无部署、提交或推送。下一步可依据首次序列化成本评估限定的类型化/源生成内存JSON路径，但必须保持实际宿主输出语义/字段、关闭时零新增工作及失败路径，并重新验证；不以本校准直接声称已找到实际宿主性能根因。原生/进程完整覆盖、代表性内容/目标机、人工、自包含、整帧与1h门禁继续开放，E5-D/E-F/M8.3未完成。

### 11.85 E5-D 源生成诊断 JSON 与限定入口验证

本片实施限定的序列化候选：Editor两条内存诊断使用带明确外层字段名的类型包装与System.Text.Json源生成上下文，内层仍PascalCase/数字枚举/紧凑JSON；Player完整类型报告使用camelCase/字符串枚举/缩进上下文。保持现有可选字段WhenWritingNull、默认关闭的采集分支、采样时机、GC策略、文件预算、目标校验、CreateNew和Flush(true)，不增加运行时反射兼容入口。原反射实现仅作为测试中的独立字节对照。

修改前核对并归档5个文件：CMakeLists、Rendering.Tests的Program/MemoryObservationCostTests、Editor.Program和PlayerReport；清单 `pre-memory-json-first/manifest.json` SHA **1C371FC104AC5C56BC2A90DFF65A076FB94E97D31E2F55E72658CA53FBC616CE**。原证明中这些源路径须通过该归档读取。首次辅助脚本SHA8E82D5BC…在查找旧搬迁报告的哈希条目时KeyError，构建/测试尚未启动，保留原脚本及工具输出转录；第二执行器从报告所属的搬迁证明读取其哈希，复核并复用16个已复制原生插件，不重建历史结果或放松哈希要求。

双配置隔离构建均0警告/0错误，执行器64854终态exit0、26个步骤全部0。新增ReportSerialization各 **23项**，覆盖默认关闭字段缺席、原有null、已知/未知枚举、嵌套所有诊断种类、转义Unicode、整数边界、有界32样本前缀、非有限浮点拒绝、1MiB预算、实际写入/Flush及不覆盖已有文件。每个配置再读取8份经过哈希确认的历史实际Player报告，原字节→原类型→原序列化逐字节相同，再与源生成和实际Write结果相同，各报告4项；不是把旧宿主运行算作新宿主通过。成本校准每配置 **64检查/136操作**（新增16个Editor逐字节断言位于测量外），既有RendererMemory9/ResourceLedger37通过。新CTest登记ReportSerialization后总数97，隔离配置及列表核验通过；**完整97项尚未执行**。

本次首次Editor进程/资源JSON分配均为128,912/81,216B，比11.84原探针首次346,576/300,888B降低；Release耗时43.3989/24.4799ms、Debug52.5512/24.9052ms，**耗时未改善，不宣称整体性能收益或真实宿主根因**。Player内存片段仍用原反射路径，现已不再被此前Editor反射序列化预热，首次为Release34.5348ms/513,560B、Debug32.9389ms/543,376B；不同初始化顺序不可作为相同冷边界直接比较，也不是完整PlayerReport成本。保留所有记录，没有重采样取快值。

实际源码入口固定计划原为16例。Release Editor CPU/DX开关4例、Null Player开关2例通过后，图形Player off返回exit3/dependency_failed、tick0/modules空，原运行器停止。隔离复制来自托管构建输出，确认缺少完整部署manifest及assets；渲染场景预检需要部署资源，但入口不暴露原始异常，故不将这一观察升级为精确异常根因或源生成产品失败。保留失败报告/退出/日志及所有输入，不补写清单绕过发布验证，不修改场景负载，不重复该失败用例。

后续仅完成独立的Debug Editor4例、Null Player2例，合计 **12个源码入口成功、1个失败、3个图形Player未运行**，原宿主流水线仍failed。成功入口默认off没有两种内存字段，on验证实际PID/线程/顺序、图形Renderer生命周期/8域计数、关闭边界；Null开关各3tick且状态hash一致。4份新成功Player报告及1份失败报告均通过逐字节原序列化对照。图形Player实际完整包验收留到新候选规范构建之后，不能称为所有实际宿主通过，也不能以旧完整95项或旧搬迁结果代替。

独立核验 `out/ecs-e5d-memory-json-first.json` SHA **137B00BFF578A657CD037B810FBA8E288FDDF561BB3D930FDBC5D8A44BA99B81**绑定8,598项，含旧5,773项经归档映射、修改源/新生成程序集、构建/测试退出、实际入口及失败、登记输出、正式EXE；16个损坏副本被拒绝。runtimeSourceChanged=true；fullRegressionPassed/canonicalPackageAcceptance/memoryAccepted/performanceAccepted/deployed均false。全部执行器已终态，无运行中的测试待轮询；正式EXE仍C50742AD…，未部署/提交/推送。

下一步：归档将受规范构建影响的既有证据，按规范双配置构建→完整97项CTest及Python门禁→完整候选包内容核验及实际图形Player验证，再评估真实宿主全报告成本。用户已明确要求代表性动作内容**先跳过，整体流程确认后由用户手动确认**；现在不重复追问、不擅自替用户确认负载、不以四对象夹具作为正式性能验收。这个延期不阻止其他功能/构建工作，也不豁免目标机、完整进程/内存、人工、自包含、整帧和1h验收。E5-D/E-F/M8.3仍未完成。

### 11.86 E5-D 源生成候选的规范门禁准备

前一轮实现与专项验证属于实际进展。本轮先复核历史120,724项完整功能证明及最新8,598项专项证明，分别应用原两文件归档和五文件归档的精确路径/hash映射；可能被规范构建或CTest覆盖的435个文件已复制到独立证据目录，逐项复核副本hash。`memory-json-gates-inputs-first/archive.json` SHA **791769076A2D0695AA9BBB836C81253F4FEFADE6B64F87E08B1238AD77A75AFF**。它是历史可变输出归档，不是新候选完整输入冻结或性能验收。

首次准备辅助脚本在创建归档目录及启动构建之前因旧归档字段为original、新归档字段为path而KeyError，原脚本保存为 `prepare_memory_json_gates_before_archive_schema.py`，SHA **AC8BF9722E61641F1B81E559CF09465C7DFC768062F79371658C12F52748D39B**，工具错误转录独立保留。修改仅按各自SHA确认的清单选择实际字段，原hash/路径校验不变。当前同一准备进程 **18715/PID3824**（本地15:57:44启动）已多次确认存活，继续生成源码/外部工具输入边界；记录时输入边界JSON尚未生成，**未启动新的规范构建、CTest或Python测试**，不得重复启动准备器或变更产品输入。

已准备后续驱动：Release再Debug，分别规范 `Build.bat -Configuration ... -SkipTests` 后完整97项CTest，最后Python106。SkipTests仅将标准构建与随后完整CTest分开，避免旧剖析/自动部署路径，不关闭NuGet审计、警告或测试门禁；每个阶段前后核对输入和正式EXE。构建需要NuGet联网时申请现有权限，不降低审计要求。

后置独立核验器 `verify_memory_json_gates_first.py` SHA **9BACE546E283DBC44732ECA107379DFB803B444F16EDF0204963F148F433AB9D**，已作AST语法检查，尚未执行；要求实际双97/Python106退出/日志、原UTF8v2严格512次0B及控制、服务185/Player66、23项序列化/64项成本记录，并审计实际10个候选包全部manifest内容、准确大小写及唯一获准运行日志。它不接受准备清单或旧95项替代新结果。

完整包入口驱动 `run_memory_json_packages_first.py` SHA **F3004E992C889785DC7D71E5F491C649124B07204D424A4B048578B172425BE7**同样仅作AST检查，尚未复制包或启动宿主；必须在前述全量证明产生后，复制6个完整manifest payload及不变测试夹具，固定执行双配置Editor CPU/DX开关、Player Null/DX开关共16例，核对实际退出、PID/生命周期、Null状态及图形实际1080尺寸。其成功也不替代目标机/代表性内容/物理显示或性能验收；原源码宿主失败与3个未运行记录永久保留。

更新：准备进程18715已终态exit0；输入清单 `out/ecs-e5d-memory-json-gates-inputs-first.json` SHA **FCFDBB2B741673A3F2FA5EBC0C8EC978428C522CA874CC37187CAC42F7DE3C00**含89,304项输入、6,843源码文件及435归档。已核对清单元数据与三个执行驱动hash；完整E5-D冻结/代表性工作量冻结标志仍false，不能把功能构建输入边界当作性能验收冻结。

唯一规范流水线获准NuGet联网审计后启动，**会话68989**、父Python PID18696（本地16:06:27启动）。Release-before子校验PID18952已退出0，89,304项输入/工具树/归档/SDK/HEAD/正式EXE核对通过；Release标准Build子进程20856已退出0，实际日志记录shader/post/shadow资源烘焙及候选 `out/package/m2-7/Release/666727861cf944199da14be885fd2ea7` 成功暂存。正式发布入口未触碰。

同一68989已由会话接口再次确认存活，Release-built构建后89,304项复核退出0；Release完整CTest现已 **97/97通过，927.76秒，原生退出0**。独立读取97个唯一测试名与明细中的Editor services185/185、Player66、pixel67、ReportSerialization23、MemoryOperationCosts64/136、UTF8v2原512次严格0B及empty0/positive80控制。汇总日志SHA **8580C203A9E5AD2C24D4DA5B18A8FEB13F715308CB21285A31A48AFEF9A5B8DD**，完整明细SHA **DF7E888CB84966D83E6EBD617EC05F4FBEF4EBDDC9D339A3C8D3789B80ED0478**；没有重跑较慢/失败样本或放宽测试。

最近确认同一68989仍存活，正在Release-tested测试后输入复核，Debug尚未开始。仍无Debug、Python、完整候选包独立审计或搬迁入口结果。逐阶段日志和退出记录在 `out/verification/ecs-e5d/memory-json-full-gates-first/`，计划绑定上述清单hash。继续轮询同一68989，不重复Release构建/测试、不重启流水线、不编辑89,304项输入。双配置97项及Python106全部终态成功后才执行完整核验器→16例完整包入口→独立结果核验，当前这些步骤均未执行。单配置完整功能通过不替代双配置、包验收或性能验收。

等待测试期间已准备 `verify_memory_json_packages_first.py`，SHA **FEAA57D45E46198DE6EDC150869F8795193C8C8ACCEE7C035748775E78B6DD38**，仅AST语法检查，尚未执行。该后置检查重新构造固定16例的真实命令，逐项比较原生退出、原包与复制包manifest/payload字节/大小写、运行日志白名单、实际PID/生命周期、Null状态一致性及DX11实际输出尺寸；对实际内存报告的PID和验收标志变异逐个拒绝，并绑定原源码宿主exit3失败证据。它不是新运行结果，更不将原失败改判为通过。

本次末次更新：Release-tested及Debug-before的89,304项复核均已退出0，原输入与正式EXE不变。同一68989正在Debug规范构建，尚无Debug构建完成结果。继续该进程，不重跑已完成的Release。

后续实际结果：Debug规范候选 `out/package/m2-7/Debug/3a671dd9486c4a808fbf08b3830d2410` 构建成功，完整CTest **97/97通过，1045.09秒，原生退出0**；汇总SHA **4F206DDE2EA47BD897EDC79A3FC64C1F4DDDE685D995D78126F5430FB8DB271C**，明细SHA **F388B0DF84934A2F141A171D313A2C11711A497424ED476A671FCF090A57B5A2**。独立读取Editor services185/185、Player66、pixel67、序列化23、成本64检查/136行，以及UTF8v2原512次严格0B与empty0/positive80控制。随后Debug-tested复核、Python **106/106（11.095秒）**、final复核均退出0；68989已终态exit0，执行记录12步全部成功，不能再轮询或重跑该流水线。

已核对预备核验器SHA9BACE546…，仅启动一次独立审计，会话24386：重验历史归档、冻结输入、实际日志和10个完整候选包。当前仍在运行、未产生完整审计结论；16个完整包入口及其独立核验尚未运行。原图形源码入口exit3不改判，正式EXE仍C50742AD…，未部署/提交/推送。代表性动作内容按用户明确回复暂缓，整体流程确认后由用户手动确认；不以现有夹具替代，不影响独立功能工作，也不豁免后续目标机/性能验收。

正式EXE、源生成产品代码和此前结果未改动，未部署/提交/推送。代表性内容维持用户指定的延期，其他未完成门禁保留。

### 11.87 E5-D 源生成候选完整功能与实际部署包验证

2026-10-23，本片继续11.86的同一流水线，没有重启构建、重复测试或修改产品源码。68989最终exit0；Release97/97（927.76秒）、Debug97/97（1045.09秒）、Python106/106（11.095秒），执行记录12步全部成功，所有89,304项输入复核通过。双配置原512次严格0B的UTF8v2及empty0/positive80控制保持通过，原6,808B失败不改判。

完整独立核验会话24386退出0，`out/ecs-e5d-memory-json-full-gates-first.json` SHA **193762F3ED58F9A73115CE4A0A5F470AB0DFEB8F01F5B0F8624AD27C4AA8A83E**，绑定 **130,014项文件记录**。重验冻结清单、435份可变输出归档、历史证明与失败、双配置各97个唯一测试名/原始明细及Python106；审计两种配置下editor/playerNull/playerDx11/playerNullPhysics/playerDx11Physics共 **10个实际候选包** 的清单、大小、哈希、路径大小写及受限运行日志。该证明不声称原生依赖闭合、完整E5-D冻结或性能验收。

随后才启动已预定的完整包16例，会话28425退出0。从上述包复制六套完整manifest payload及不变的两份项目夹具，固定运行两配置×Editor CPU/DX与Player Null/DX×内存报告关/开；没有补丁式绕过资源校验、重试或从旧源码入口挑选成功结果。全部16个实际进程原生退出0。

- 四个图形Player：请求、初始帧缓冲、最终成功Present均 **1920×1080**，3次Present=3个renderedFrames、尺寸变化0、验证错误/警告0。实际报告tick为4；不将命令的`--ticks 3`误写成实测tick3，也不从短时3帧推断帧率/性能达标。
- 四个Null Player：实际tick3，内存开/关状态hash相同，无图形output报告。
- Editor/Player默认关闭时无新增内存报告；开启时实际PID、线程/生命周期、资源代次与关闭态均通过既有严格核验。只接受受限Editor运行日志额外文件，未放宽整个目录。

后置独立核验退出0：`out/ecs-e5d-memory-json-packages-first.json` SHA **E78F6F9157A80E2DFF07A040CF1CD365AC790024CBA617296124E3F29280879C**，绑定 **1,371项文件记录**；重新构造16条实际命令，逐一核对退出记录、原包/搬迁包字节、夹具、原始stdout/report、实际尺寸及计数，并拒绝16个PID/验收标志篡改负例。原源码克隆Release图形Player的exit3/dependency_failed证据仍保持失败，不能用本次完整包通过改写旧结果。

当前全部会话已结束，无需继续轮询或重跑。正式EXE仍 **C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61**，本片未部署、提交或推送。下一步核对**当前候选**的原生/生成依赖，再界定实际宿主完整报告的观测成本；对应新辅助脚本/队列尚未准备或执行，旧候选证据不能直接移作本次证明。后续源码/构建修改前仍须归档受影响的当前证明条目。

代表性动作游戏内容按用户明确要求**先跳过，整体流程确认后由用户手动确认**；不再追问、不代替用户选定负载、不以现有夹具冒充正式性能验收。独立功能工作可继续，完整进程内存、完整冻结、GTX1060/RX580目标机、人工、自包含、整帧和1h等验收仍开放，E5-D/E-F/M8.3保持未完成。

### 11.88 E5-D 当前原生依赖与完整宿主对照

2026-10-23，本片没有产品源码修改、重新构建或重复完整回归。只针对11.87的当前候选补原生记录核验，并执行预先固定的新旧完整宿主对照。代表性内容继续按用户决定延期；本片所有负载是开发诊断，不是正式动作游戏性能验收。

**当前原生记录。** 新核验器 `verify_memory_json_native_first.py` SHA **3D0D8038CB0AF71E72FD3587E54A333745F32D3D2DE22C2E8C68114458B2CA09** 实际执行，会话57268退出0；没有调用configure/build，只读取`ninja -t deps`。证明 `out/ecs-e5d-memory-json-native-first.json` SHA **B97A5E290EBA0314C16566F05093C5DFC74D077DE6D5F3D2C2B22103AA773120** 绑定 **90,340项**：重验89,304项输入边界及10个候选payload；双配置各281条VALID记录与编译单元一一对应，关键闭合着色器编译器实际2项依赖覆盖探针2项仓库头，Renderer实际230项依赖覆盖探针229项仓库头。各32条PCH头与9条零直接依赖记录重新检查：2个单元直接包含系统头，7个单元具显式PCH构建前提，不能因此宣称系统头或完整预处理闭合。各5个生成输入与先前已验证字节相同，包括GUI/Renderer着色器头；审计前后构建图和依赖库hash不变。解析器2正例/5负例通过。此证明明确不接受整个进程/工具链闭合。

**固定真实宿主队列。** `run_memory_json_host_pairs_first.py` SHA **2A4BAA15E63F638902CD36AD1D7F3F2E1E709C805DE6CBBBE885318EC46A7777**；独立核验器 `verify_memory_json_host_pairs_first.py` SHA **AE934760F2692580CBD985D2BF8514B64995897152AE077718FC1EA43B3DBB9A**。启动前完成AST检查及分析3组正例/2个负例；只在当前原生证明成功后启动一次队列。旧Release候选 `63bb16c71ef144698e19b56d96debf67` 对新 `666727861cf944199da14be885fd2ea7`：Editor CPU/DX、Player Null/DX四组，各内存诊断关/开，每设置固定旧新/新旧/旧新三对，共 **48个新进程、24配对**。每进程全新完整包副本，Player使用相同已验证Low测试夹具；120帧或目标120tick、图形限30fps、相同循环/启动观测，正常GC、不重试、不删样本。队列保存所有命令、PID、起止时间、原始stdout/stderr/report和预注册输入hash。

93443与后置核验96659均退出0。证明 `out/ecs-e5d-memory-json-host-pairs-first.json` SHA **DC4B053F887130BE43EDA93CD5AB527213AB4D720536332D07DA8EE32C795691** 绑定 **6,647项**：48个原生退出0、所有实际包清单/复制字节/项目/日志/报告、5,052条循环记录且无遗漏，12个Null Player均实际tick120且状态hash相同；12个图形Player均实际tick120、每个61次Present、输出1920×1080、0验证错误/警告。24个错误PID负例拒绝。分析保留真实帧数/tick差异、首次循环和全部后续循环，不剪裁异常值。

进程总耗时差（新减旧，毫秒；正值表示新候选更慢）：

| 组别 | 内存诊断 | 三个固定配对差值 | 新候选更慢 |
| --- | --- | --- | --- |
| Editor CPU | 关 | -26.01、-14.44、+2.02 | 1/3 |
| Editor CPU | 开 | -2.84、+49.64、+27.84 | 2/3 |
| Editor DX11 | 关 | -95.89、+21.71、-27.93 | 1/3 |
| Editor DX11 | 开 | -52.81、+67.06、+19.73 | 2/3 |
| Player Null | 关 | +36.79、+19.09、+49.72 | 3/3 |
| Player Null | 开 | +0.40、+14.22、+32.60 | 3/3 |
| Player DX11 | 关 | +10.17、+22.42、-26.44 | 2/3 |
| Player DX11 | 开 | +57.31、+24.01、+132.00 | 3/3 |

合计 **17/24** 配对新候选进程总耗时更高。它包含CLR/模块启动、调度、循环、关闭、序列化和输出IO，**不是独占JSON成本，也不是因果证明**。Editor DX首次循环旧511.07–549.78ms、新512.59–545.02ms，均保留，不宣称Hitch/目标预算达标。Player DX首次循环旧31.59–34.37ms、新35.53–43.77ms；不能未诊断就归因于某一组件或以“噪声”免除。

实际清单字节比较：Release editor仅NcmaEngine.dll/pdb变化；两个Player包仅NcmaPlayer.dll/pdb变化，其余payload集合/字节相同。Editor DLL 693,760→773,120B（+77.5 KiB）、PDB 88,020→133,928B；Player DLL 295,936→522,240B（+221 KiB）、PDB 28,044→162,256B。这是交付体积代价，不是驻留内存差。源码生成此前的局部分配下降不能扩展成完整宿主收益。

全部会话已结束，正式EXE仍C50742AD…，没有部署/提交/推送；原失败记录与全部新慢样本保留。下一步针对**完整`PlayerReports.Write`**设计有界归因：固定实际完整报告数据，明确准备过程与首次/后续调用边界，区分序列化、初始化和CreateNew/Flush输出；不能重复本队列挑好结果，也不能用既有仅内存片段微测试代替完整报告。该新诊断尚未准备/运行，没有预先认定根因或修改GC、IO契约、源生成策略。修改产品/构建之前须归档受影响的当前证明；完整冻结、完整进程内存、目标机、人工、自包含、整帧/1h及E5-D/E-F/M8.3仍待完成。

### 11.89 E5-D 完整 Player 报告归因与只写生成候选

2026-10-23，本片完成新的有界诊断，并据证据修改产品上下文，不重跑11.88的48进程队列。所有相关进程已正常结束；当前只完成新源码的专项门禁，不将上一候选的完整97项通过结果挪用。

**完整方法诊断。** 私有 `player-report-cost-probe-first` 引用精确旧/新Player程序集，运行真实已编译的公开`PlayerReports.Write`，并在另一批新进程拆分ValidateTarget、获取编码上下文、完整序列化、CreateNew/Flush。使用11.88实际Null关/Null开/DX开三份完整报告，包含全部循环及可选内存字段；通过JsonDocument与受检记录构造器重建DTO，校验计算属性，不调用JSON反序列化器预热。DTO、反射、JSON读入的准备在区间外，所以**不是完全未初始化CLR或实际游戏过程**；拆分含反射getter开销，不能把不同进程的区间相加当作完整宿主因果解释。普通GC，无强制回收或NoGCRegion。

会话25378退出0；6个验证进程先完成同字节输出、不覆盖现有文件及未知字段/计算属性篡改拒绝，再固定运行36个测量进程（每组旧新/新旧/旧新；每进程8轮首次/后续调用）。独立核验退出0：`out/ecs-e5d-player-report-costs-first.json` SHA **388B1856B011714803415894D62233E6351FF9CB5DCAA1C53E726D186BEEDDFA**，绑定 **2,887项**，720条操作记录、294份逐字节一致输出、24个控制检查；实际加载的Player位置/hash/MVID及.NET 8.0.28 CoreLib/System.Text.Json字节均保留。原始负分配差值/GC计数有效位不修正。

实际完整首次Write：旧139.80–174.01ms，新152.88–182.38ms，结果仍有快有慢，没有整体改善结论。新上下文首次获取137.29–158.04ms，随后完整序列化7.76–15.43ms；旧编码选项获取0.49–0.69ms，成本主要出现在后续首次反射序列化142.76–151.85ms。首次输出Flush也有2.56–13.53ms差异，均保留。新完整首次Write线程分配787,504–950,200B，旧2,121,488–2,479,064B，分配下降不代表端到端速度已改善。

**隔离生成策略实验。** 同一私有helper在当前真实公共DTO类型上生成Default、Serialization-only、Metadata-only三个上下文；只改变生成策略，采用每个输入固定ABC/BCA/CAB顺序、27个新进程、各8轮拆分测量。编译零警告，95137退出0；独立证明 `out/ecs-e5d-player-report-generation-first.json` SHA **F0C90B77F2FF250D52A4A46A690481D04D6B2A7E1456C321494CDAC40C6E8500**，绑定 **2,070项**，864条操作记录、216份同字节输出，全部原始样本保留。

| 私有生成模式 | 首次上下文获取 | 首次完整序列化 | 根生成源码 / 属性元数据 / 快速写出 |
| --- | --- | --- | --- |
| Default | 137.04–156.65ms | 7.81–22.04ms | 51,710B / 有 / 有 |
| Metadata-only | 137.16–151.09ms | 10.38–19.09ms | 45,150B / 有 / 无 |
| Serialization-only | 8.52–12.03ms | 16.57–39.51ms | 9,890B / 无 / 有 |

编译器实际生成源码确认Serialization-only没有根PropertyMetadataInitializer/ConstructorParameterMetadataInitializer；仍保留相同快速写出路径。该有界对照支持去除输出专用上下文中不需要的读取元数据，但**不是正式Player宿主性能证明**，也未用三份数据替代完整契约测试。Serialization-only首次序列化本身增加的成本如实保留。

**最小产品修改与专项门禁。** 编辑之前，以既有完整证明193762F3…核对并复制两份原源码，归档 `out/verification/ecs-e5d/pre-player-writeonly-first/manifest.json` SHA **ACF759BAD1EBAE9C63B8E759A97530D19EEF4F66E185931819937D0F6DCDC707**。今后验证旧完整证明的这两条原路径须使用归档，不因正常源码更新误判历史证据损坏。

- `PlayerReportJsonContext.cs`显式`GenerationMode = JsonSourceGenerationMode.Serialization`，新SHA **1451C993D2E82EDA5558781EEFF3449338F0C6428DCECCEC03429E0B9BABCA05**。仅输出用途，不增加反射回退、后台预热、GC策略、字段或额外采集；`PlayerReports.Write`本身及原有两次ValidateTarget、1MiB预算、CreateNew/Flush不改。
- `ReportSerializationTests.cs`新增“快速写出存在且根属性元数据为空”的检查，SHA **E6727CD785027A8D25B964BE646BB08F35C216340D99D7F887D7293ED0C706DF**。原完整字段/空值/未知枚举/Unicode/全部诊断/边界数组/非有限数/预算/禁止覆盖测试保留。
- 会话28019获准正常NuGet联网审计，使用全新`player-writeonly-managed-first`隔离目录：Release/Debug构建0警告0错误（24.76/14.12秒），各 **24项契约检查**及 **6份实际报告×4检查**通过，共16步均退出0。没有触碰现有规范构建输出目录或包。

独立专项证明 `out/ecs-e5d-player-writeonly-first.json` SHA **C90AB72881A305F9729C949D14924F535FF36FBE356DF9103F5CFAB19CA471B4**，绑定 **7,259项**：重验2,887/2,070项诊断证据、两份原源码归档、修改后源码、全部实际构建/测试输出树及报告字节。只映射核验旧完整证明中两个变化的源文件，**没有声称重新核验旧全部130,014项**。隔离ReleasePlayer DLL为370,688B，比前一规范候选522,240B少148 KiB；这仍不是最终包体积或驻留内存验收。

当前全部会话已结束，没有提交/推送/部署；正式EXE仍C50742AD…，原失败及慢样本全部保留。修改后实际编译Player完整Write/宿主成本尚未测量，不能把私有策略数字移作产品性能结果。下一步先固定核验实际修改后Write路径与基线，再归档受规范构建影响的证明条目，执行新候选双配置完整97项/Python106/实际包门禁；相应下一轮辅助工具尚未准备。代表性动作内容按用户决定延期到整体流程确认后手动确认，目标机/完整冻结/完整进程内存/人工/自包含/整帧/1h及E5-D/E-F/M8.3仍未完成。

### 11.90 E5-D 修改后实际 Write 验证与新规范门禁准备

最新完整门禁结果（2026-10-23）：会话24558已终态exit0，不再轮询或重启。Release97/97（907.30秒）、Debug97/97（1016.68秒）、Python106/106（11.599秒）及全部12步退出码均通过，最终守卫PID27548再次核验89,307项输入。Debug CTest PID27376 exit0，stdout SHA **88C9E5EA12B8C72ADE4EA37A9CC0B5BC3EE5BBE69C1F13550B23C72CA381BC4F**、详情SHA **36BA8A671CEBD080FB9453B58992C73886397E1ADA96477F0192962295F23BC8**；Python stderr SHA **8E4FFC7E58AA96F8B75F6A99D7591E849E9943D41EF828C83D14E195D92FBB1A**。两配置各97个唯一通过名称，Python106个唯一通过名称；Debug详情独立核对服务185/185、Player66、报告序列化24、UTF-8 v2原512次严格0B及空0B/正对照80B。原失败证据不改判。

门禁结束后仅启动一次顺序驱动 `run_player_writeonly_remaining_first.py`（SHA **E5B8B59E261497B70CE062B964948D5F2AD190A31576CE2EA4317923220B2F68**），当前会话 **80983** 正在完整独立审计；原始日志、启动记录和退出码保存在 `out/verification/ecs-e5d/player-writeonly-remaining-first/`。它要求12步成功和新输出不存在，核对四个辅助脚本精确SHA，依次运行完整审计→16个实际完整包入口→包后置核验→当前原生依赖审计，失败立即停止、不重试。本次尚无这四步成功结论，不以功能全通过代替包/原生/内存/性能验收。正式EXE未替换、无提交推送，新增E6仅更新规划；以下保留本片准备和中间状态历史。

2026-10-23，继续11.89的实际产品方法验证。本片未进一步修改产品源码。复用已经验证的DTO准备helper，在同一前一规范候选 `666727861cf944199da14be885fd2ea7` 的依赖字节中，仅把新组的NcmaPlayer.dll/pdb换成专项已通过的只写版本；其余产品和helper字节相同。这是受检混合诊断组成，**不是规范完整部署包**。调用的是真实已编译公开`PlayerReports.Write`，非私有生成上下文，仍为正常GC、DTO准备后首次/后续调用，不是未初始化CLR或实际游戏运行时。

固定三份实际完整报告（Null关、Null开、DX开），先6个验证进程，再18个测量进程，每输入旧新/新旧/旧新、每进程8次写出。会话5671退出0，独立核验退出0：`out/ecs-e5d-player-writeonly-costs-first.json` SHA **EB8A9989C4C4C591BFF16B3F4A75043C22CD49FD9C47EA93AE2B224E6CB667F2**，绑定 **1,668项**；144条操作记录、150份逐字节一致输出、24项不覆盖/数据篡改控制全部通过，实际加载程序集hash及.NET运行库字节核对。驱动SHA **6C0586FD28DD4917E3117F9126246C201F4000D5FBF6557A163B855E90845421**，核验器SHA **DCDC98E3C36F7B1A3AC79102992EB787FFA039BF7574007F91D0AB0DF1FC613A**。

| 完整报告 | 基线首次Write，ms（三个原始样本） | 只写候选首次Write，ms（三个原始样本） |
| --- | --- | --- |
| Null / 内存关 | 166.45、184.11、157.93 | 35.81、31.17、33.09 |
| Null / 内存开 | 161.27、162.28、178.13 | 43.60、42.99、39.34 |
| DX / 内存开 | 170.59、155.12、171.74 | 48.57、47.96、49.71 |

9个固定首次配对均改善；线程分配基线762,920–950,256B，新287,880–441,312B。后续调用中位数并非全面改善，例如Null关首对2.12→3.80ms、第三对2.16→3.66ms，保留所有慢结果，不以首次收益掩盖后续数据。该方法诊断包含原验证、完整序列化、CreateNew和Flush；不能移作整帧、磁盘通用性能、实际宿主启动或目标机验收。11.88的17/24进程总耗时较慢记录仍保留，不回写成通过或重采样。

规范门禁前准备已启动，**会话77537**执行 `prepare_player_writeonly_gates_first.py`，正在重验前一完整证明130,014项、原生90,340项、最新专项7,259项与本次方法1,668项。对先前完整证明中的两个变化源码使用ACF759…归档映射；规范构建将覆盖的实际输出先校验再复制，之后建立新的源码/工具输入边界。当前尚无新的边界清单或规范构建结果，继续同一进程，不重启、不编辑待冻结的产品输入。

后续驱动已准备并仅完成语法检查：完整核验 `verify_player_writeonly_gates_first.py` SHA **1BDAF305E1E4EB55D454160702C7FC7686C68E88A184E278E846DEF9FE6365A2**，完整包16例驱动SHA **20CED32BD6510A6E25CD11F0294A47F3CD80D813521CD0FA3CDD80F54607E46D**，包后置核验SHA **D491FA65184E67374F1EEF1D0AC9C948F35BC3B7652EFCDC9846EC2307AA4205**。它们尚未执行；必须先完成准备和规范双配置构建/各97项CTest/Python106，再审计真实新包，才能启动固定16例实际完整包检查。完整核验要求新增序列化24项，不沿用上一源码的23项标记；不降低任何GC/审计/回归标准。

准备会话77537随后已终态exit0：四份历史证明重验完成，实际归档22份可变输出；归档清单SHA **7E590DC7E2E43E5EE32FB6FEB85118D2DE1F648EBC6FDBBE3220A2C1E845E282**。新边界 `out/ecs-e5d-player-writeonly-gates-inputs-first.json` SHA **F911030D5FC54F0826717B6361CB4E20D58F212D25DA5421FB5037B92B8D76EC**，含 **89,307项输入、6,843份源码**；功能构建边界不等于完整E5-D性能冻结。准备/输入守卫/运行驱动SHA分别DC6B10A8DF0FA90B54E8F4836CFACA35A103662D768197EF4E722CD0D102B330、0206B03E2B68A40B956BE9F464BDA0165182739B71A7EA06A973FA8030EC48F2、C323D9B2A77653FB994B9DA62F454F10897EDAA57FE98A32092307F1CCFB7D0D，已与清单绑定核对。

已获准正常NuGet联网安全审计后，启动唯一规范流水线 **24558**，正在Release-before输入守卫；日志位于 `out/verification/ecs-e5d/player-writeonly-full-gates-first/`，尚无本候选规范构建或完整测试结果。77537不再轮询/重启；继续24558，不编辑89,307项输入、不启动第二轮。标准`Build.bat -SkipTests`只把构建与后续完整CTest分开并避免自动部署，不跳过安全审计或关闭警告。两配置97项、Python106和所有输入守卫终态成功后再执行预备完整核验→16例实际完整包→后置核验；当前后置步骤均未运行。

正式EXE保持C50742AD…，无部署/提交/推送。代表性内容按用户决定延期；完整冻结、完整进程内存、目标机/人工、自包含、整帧/1h及E5-D/E-F/M8.3仍未完成。

后续守卫状态：Release-tested（PID14004）已终态exit0，完整97项测试后的89,307项输入复核通过；同一24558仍在运行，已进入Debug-before守卫。Debug构建、完整测试、Python和后置审计仍待完成。

Debug后续结果：Debug-before（PID24860）exit0；Debug标准构建（PID16364）exit0，候选 `dbc0ee07e2784eddb9a47428db9e2598`，stdout SHA **D870A1B41EA984A5D906E582D924BCCFDECFE6AFE28E1D591E22C7D5B6DD23B6**、stderr为空；实际构建日志有25处零警告摘要，无非零摘要或编译警告。Debug-built（PID21156）exit0再次核验89,307项输入。同一24558已启动完整Debug97项，截至本次读取前19项通过、主渲染第20项运行中。Release完整结果保持，不重复执行；Debug全量、Python、最终守卫、完整独立审计和16个新包入口仍未完成，不作全候选验收/部署/提交/推送或E5-D完成声明。

后续验证等待：同一24558经实时轮询确认仍在运行，Debug已35/97通过、零失败标记，主渲染160.87秒通过，场景UI联合产品第36项运行中。等待期间仅新增忽略目录辅助脚本 `out/verification/ecs-e5d/verify_player_writeonly_native_first.py`，SHA **643BC3DF81B921A0A40B45F36EE9591E8E5FF310D36DD805AE0B5FFF1B714DB4**；AST语法检查通过，**未执行原生审计、未生成相应结果目录或证明**。它必须在当前双配置97项/Python106完整证明和16个实际完整包独立核验成功后执行，不能绕过前三项后置步骤。检查当前89,307项边界、10个实际包、每配置281条Ninja有效记录、关键仓库头覆盖、32条PCH记录、9条零直接依赖的限定分类，以及5份生成文件相对上一原生证明B97A5E29…的字节一致性；只读Ninja不构建，不把仓库头覆盖表述为完整系统/进程依赖闭包，不推导原生DLL二进制等价或性能达标。冻结产品输入和正式EXE未改动，既有失败、较慢样本及全部未完成验收保留。

2026-10-23后续核对：同一24558仍在运行，Release-before（PID3944）和Release-built（PID3548）均exit0，89,307项输入构建前后核验通过。Release规范构建PID23940 exit0，候选8ac7a00e370643beb9ce2ce9bdc71865，stdout SHA E529386223EABF0B86EF92A7FDC7C121EBB24B92E60046D58FF04A2358487290、stderr为空。随后完整Release CTest（PID18088）终态exit0，**97/97、97个唯一通过名称、零失败，907.30秒**；stdout SHA **075A7B7FE498B624007F648074AA8C01BD3E00915943591563E0384AD1E785C0**，完整详情SHA **DB76DC90A7B93E7C084CBE95914506C648F8D05ED8CF5A31E99B83E922986669**。独立读取详情确认服务185/185、Player66、序列化24、内存操作64检查/136记录、UTF-8 v2原512次严格0B及空0B/正对照80B，主渲染158.91秒和图形恢复联合检查通过。当前接续Release测试后输入复核，Debug、Python、完整独立审计及16个新完整包入口尚未完成，不能移作双配置全量或性能通过。两个候选源码及三份预备后置脚本SHA仍与登记值一致，正式EXE保持C50742AD…。继续同一流水线，不重跑Release、不重启、不改冻结输入。用户再次明确代表性内容先跳过，整体流程确认后由用户手动确认；不以现有夹具替代正式性能负载。
