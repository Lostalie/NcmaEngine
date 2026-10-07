# M6.1 动画图资产契约

状态：自动候选双配置回归通过，测试结果见 [交付记录](M6_1_DELIVERY_REPORT.md)。`.ncmaanim` JSON v1 是新作者格式，
不是旧 C++ AnimationGraph 的迁移层。Ncma.Animation 无新增原生/Editor/Python依赖；本阶段不读写文件、
不解析资源、不执行图、不增加World组件/Agent写权限。后续方案见 [M6总览](M6_IMPLEMENTATION_PLAN.md)。

## 文档和预算

所有字段必填，未知/重复/大小写错字段、数字枚举、未知枚举、非法UTF8/孤立surrogate、null元素、
非v1、非有限数值和旧格式拒绝。编码按UUID排序，条件按参数UUID排序；解码拥有数组副本。
AnimationGraphDocument 保存验证后的编码，CopyDefinition/CopyBytes 返回新副本。

| 范围 | 限额 |
| --- | --- |
| 文件字节与JSON深度 | 1MiB和16 |
| 参数/节点/边 | 64/256/1024 |
| 状态/转换/每转换条件 | 64/256/8 |
| 数据依赖深度 | 64 |
| 名称 | 图256个UTF16单元，其余128，无控制字符 |
| 节点坐标 | -65536到65536 |
| 默认Float参数/条件 | -1000000到1000000 |
| Clip速度/Blend默认权重 | 0到8/0到1 |
| 转换priority/duration/可选exitTime | 0到255/0到10秒/0到1相位 |

根字段：version、assetId、name、skeletonId、entryState、parameters、nodes、links、states、transitions。
全部内部实体UUID互不重复，也不能与graph/skeleton/外部clipUUID别名。片段可以被多个Clip节点复用，
Dependencies返回去重稳定排序的骨架/clip引用；它只是依赖声明，不证明真实资源可用。

## 节点和引脚

节点字段为id、name、kind、x、y、clipId、parameterId、loop、speed、weight。
只有Clip允许非空clipId/loop/speed，只有Parameter允许非空parameterId，只有Blend允许weight。
其余字段必须Guid.Empty/false/0，不能以多余字段藏配置。kind为精确camelCase枚举。

| kind | 输出 | 输入 | 必需条件 |
| --- | --- | --- | --- |
| clip | pose为Pose | speed为Float可选 | 非空外部片段UUID |
| blend | pose为Pose | a和b为Pose必需，weight为Float可选 | 默认weight有效 |
| parameter | value为参数对应类型 | 无 | 引用已有参数 |
| stateMachine | pose为Pose | 无显式边，依赖各状态poseNode | 至少一个可达状态 |
| output | 无 | pose为Pose必需 | 全图恰好一个 |

边字段为id、from、fromPin、to、toPin；端点是节点UUID和精确语义引脚名。不同类型/方向、自连接、
重复输入、缺失必需输入、姿态/标量环以及不能到达Output的节点拒绝。当前严格资产不保存未连完草稿；
将来编辑手势可临时不完整，但不得执行或正式提交。

## 参数状态和转换

参数字段id/name/kind/floatDefault/intDefault/boolDefault；只有当前类型对应的default允许非零值。
Trigger默认false，只能通过运行请求激活，不能持久化待消费事件。条件字段parameterId/comparison/
floatValue/intValue/boolValue；类型对应值之外必须中性。

Float/Int支持equal/notEqual/greater/greaterOrEqual/less/lessOrEqual，Bool只支持equal/notEqual，
Trigger只支持triggered。每转换对同一参数最多一个条件，条件AND，没有表达式或脚本字符串。

状态字段id/name/poseNode；名称唯一，poseNode为Clip/Blend。不嵌套状态机；entryState为空时不得存在
状态/转换/StateMachine。状态机存在时全部状态必须从entryState经转换可达，状态转换环合法。
转换字段id/from/to/priority/duration/exitTime/conditions。源目标必须不同，priority同源唯一；条件
至少一个或exitTime非null；M6.2将限制每固定量子最多一次转换，避免合法状态环在单步无限执行。

## 拒绝和兼容边界

Validate抛AnimationGraphValidationException时提供稳定Code/Subject；Json解析/编码与文本/scalar错误
也可抛JsonException/ArgumentException/EncoderFallbackException。错误不改变输入/不可变发布副本。
RequireExtension只检查新后缀，不是路径审批或安全文件IO。拒绝.ncscene/.ncmascene等作为图文件；
不增加迁移/fallback/旧类型别名。旧原型执行消费者为ArchitectureTests和CMake，已连同专属断言删除；
vcxproj和filters内对应IDE文件展示项也删除，NMake仍指向Build.bat。
数值动画库/ActionAnimationWorkspace/PoseKernel保持独立。
