# M6.1 动画图基础交付记录

2026-10-07 候选实现了 `.ncmaanim` v1 图资产 DTO、严格 codec、类型/结构验证、稳定依赖声明与拥有编码副本的
AnimationGraphDocument。方案见 [M6计划](M6_IMPLEMENTATION_PLAN.md) 和 [格式契约](M6_1_GRAPH_CONTRACT.md)。
顺序完整 Debug/Release Build.bat 均以0退出，自动候选通过。按用户授权提交推送本阶段，
最终本地/远端 SHA 以 Git 和交付回复核对；未关闭整个 M6 或任何既有人工验收门禁。

## 实施范围

基础 Clip/Blend/Parameter/StateMachine/Output、Float/Int/Bool/Trigger、固定引脚、UUID身份、AND条件/
转换优先级、无环数据依赖及可循环的可达状态机已实现。严格字节/深度/数量/Unicode/scalar预算；
未知/重复/缺失字段/旧格式拒绝；编码规范排序和发布副本隔离。没有新增原生/Editor/Python依赖。

已核对旧原型的执行消费者：CMakeLists.txt 和 ArchitectureTests.cpp；NcmaEngine.vcxproj/
NcmaEngine.vcxproj.filters还保留IDE展示项。engine/source/runtime/animation/AnimationGraph.h/.cpp
及构建/专属断言/这两个IDE展示项已删除，
由 Ncma.Animation.Tests 的正式图结构测试替代；数值库、ActionAnimationWorkspace、PoseKernel、
FBX/NCA、其余原生架构测试和用户资源保留。删除可从本阶段 Git 提交前版本恢复，没有旧类型别名。

## 测试状态

新增10项图专项，进入现有 NcmaPoseManagedTests/Build.bat：合法图 round-trip/依赖、规范顺序与副本隔离、
严格格式、有限数值/null/数量/深度/UTF8、全局UUID、引脚错误、DAG环/不可达、多状态循环/入口、
类型条件/Trigger中性默认值、依赖深度边界。

| 回归 | Debug | Release |
| --- | --- | --- |
| 原生 CTest | 12/12 | 12/12 |
| 托管 CTest | 22/22 | 22/22 |
| 动画图与既有pose/clock | 25/25，其中新增图专项10项 | 25/25，其中新增图专项10项 |
| Editor/Player | 69/69和56 | 69/69和56 |
| Gameplay/fake Movement | 53/53和38/38 | 53/53和38/38 |
| Python | 43/43 | 43/43 |

日志：`out/m6-1-debug.log`、`out/m6-1-release.log`。两个配置均运行managed/native smokes、
当前格式/旧格式拒绝、inspect、保留profile三轮、包审计和完整检查式部署，无Skip标记。
新代码编译零警告/错误；fake Movement不是新增Jolt验收，profile不是性能达标证据。
Rider/VS项目删除显示项后XML解析通过，无旧AnimationGraph.cpp/.h源码/构建引用；NMake仍调用Build.bat。

Release `out/bin` 安装manifest的101文件再次验证哈希通过；其余是manifest自身和许可的用户日志/设置，
部署journal为Complete，generation为`819a5279be4c457f8fc6f2a5bca05cb0`。
Ncma.Animation.dll SHA256：`896553418A965E5BE16B9A9A2056999CAC017CABC510FFAF6CCDE9FDAA7DA553`。
Debug备份保存在`out/deployment/1ca6192af7b349b9a0b7aec0fb591ecd/backup`，
Release之前的安装保存在`out/deployment/819a5279be4c457f8fc6f2a5bca05cb0/backup`；不提交生成包/备份/IDE个人设置。
Debug/Release最终审计均audit_passed=true而h8_accepted=false：
`out/verification/m2-8/Debug/883525579aec480a90bd926c99a789ad/audit.json`、
`out/verification/m2-8/Release/89f7329f919b402992513dd80ee0b23e/audit.json`。

## 未实现和后续

M6.1只发布合法作者数据。真实资源解析与generation租约、编译/执行/时钟、原生混合接口、角色/Player接线、
正式图文件事务、可见节点编辑器、MCP图权限和图运行包均未实现。旧实验室不是替代交付。
M6.2开始后也须先测试再提交推送。整个M6与M4 K7/M5后续、人工窗口/DPI/IME/第三方MCP、
真实用户素材、目标环境、性能和长稳门禁未关闭。
