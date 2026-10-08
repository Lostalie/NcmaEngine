# M6.7 B 分层图与缓存运行契约

严格graph v4替换v3（以及v1/v2）；每节点required nullable Layer与BlendSpace。没有旧codec/迁移/
fallback，拒绝输入文件保留。Override/Additive具有typed a/b/weight引脚，CachePose具有pose输入。
最多16层、128clips、945配方行；65536TRS/32768bones/32actors等原预算不扩大。

## 准备和身份

C#编译器接收复制骨架元数据；正式RuntimeAnimationGraphAsset只从实际retained NCA/package解析
骨架UUID/content hash/full paths及同model/skeleton/generation clip duration。路径每个名称URI转义，
重复/不明确/未知骨或根权重非0拒绝，不做name fallback；重导入hash改变必须重新审阅作者定义。
Additive明确reference Clip与[0,duration]时间，生成恒定prev=current、nonloop参考行，不新建clock。
图、mask、sample、节点和外部clipUUID不能混用；所有kind不相关字段保持neutral。

## 配方与安全边界

唯一AnimationGraphInstance负责候选/提交参数、clocks、event和cache观察。layer行保留SourceA/B，
Additive SourceC必须指向此前静止reference Clip；所有其他操作SourceC=-1。
AnimationPoseRecipe对包括未引用行在内的全配方做闭合检查，各消费者再核对实际资源及layer/reference。
RootMotionRecipe仅取基础SourceA，事件沿基础层主clip，不发上层/参考clip的回调或伤害。
外层Blend/状态过渡保持原明确规则；全混合pose用于committed alpha1中断缓存及desired根剥离。

## CachePose 和 scratch

CachePose是明确的同实例/state/context候选别名；其依赖最多求值一次，不生成重复数值行。
每个Build清空slot表，候选失败不发布统计；Commit才记录tick/requests/hits，Abort保留旧统计。
程序在off-frame预编译静态cache依赖consumer/lifetime；数值配方有界行生命期表按结构复用，
仅结构变化时重建有界整数映射（不是重新编译作者图），参数/time变化不复用旧数值。
scratch固定上限预分配，slot最后consumer之前不可复用，保留output直到全批完成。
每个数值owner有自己的mutable scratch；共享pose snapshot准备对象只在同owner串行使用，输出复制到各实例。
不跨World/tick/Reload分享可变pose，不宣称整个World或工具零分配、最小化所有scratch常驻容量。

## 正式呈现

可选layer1.0在正式3D PoseKernel启动期协商，纯2D/Headless不初始化GPU/pose。
完整graph pose使用原native Sample/Blend及新Layer，再一次palette更新供geometry/shadow共同使用。
纯graph场景跳过被图结果覆盖的bind-pose预采样；混合clip/graph场景保留原完整prepass。
Additive/override的静态参考、cache采样次数、CPU/native/GPU误差必须有实际NCA证据。
Reload关闭旧World呈现后Stop/frozen-startup/new identity/Paused；fault后不显示旧帧为当前，也不声称solver rollback。

## 作者与 Agent

本切片同步闭合node/schema/inspection/已有语义JSON事务及Canvas pins。typed layer创建和骨骼清单/诊断工具
属于C，按钮尚不开放；没有新增审批/live控制或推理服务，现有exact图/文件/资源/受众审阅保持。
每阶段测试、提交及开放验收项见M6_7_B_DELIVERY_REPORT.md。
