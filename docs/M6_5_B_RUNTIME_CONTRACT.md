# M6.5-B 可中断过渡运行契约

## 边界与启用

本切片增加可信C# host的显式interruptTransitions开关，默认false。
`.ncmaanim`严格v1、十种NCA标签、NCP1路由、原生pose1.0/blend1.0和其他ABI不变。
不增加live Agent控制/自主批准、Python推理、代码执行或外部传输。
持久事件/策略、可见轨道编辑和获批序列工具属于M6.5-C，尚未交付。

## 中断语义

- 在已提交边界读取当前目标状态的有序条件；priority升序、UUID稳定次序，单量子至多选一边。
  退出相位继续使用同一目标Clip时钟，重入清零目标状态Clip时钟，新过渡使用新边完整Duration。
- 未结束的过渡被选边中断时，非零Duration捕获上一成功配方的完整local TRS，alpha=1。
  不捕获最后渲染的插值帧，因此结果与渲染频率/是否Headless无关。零Duration直接切换。
- 一个固定大小双缓冲缓存替代历史过渡链；重复中断只采样当前配方及当前缓存。
  Frozen行携带精确generation，普通Clip/Blend行generation必须为0；不创建第二时钟。
- Prepare只写候选缓存；所有骨骼finite、规范化四元数、正uniform scale、完整写入才成功。
  Commit在对应World成功提交后发布新缓存/配方/事件和trigger消费；Abort不替换成功缓存。
  失败Play拒绝历史数据为当前成功数据，Stop/Reload替换实例并使缓存失效。
- 结束量子的已提交配方仍可含weight1 Frozen行，Frame继续显示该配方generation；
  下一无过渡配方不含Frozen，Frame generation=0。缓存不可按旧generation复用。
- 事件归属仍只有新目标状态的递归左侧主Clip；冻结源不发送回调/重复事件。

## 资源、数值和根运动

Scene准备只接受实际保留lease中的graph/hash、model/skeleton/clip UUID与同generation闭包，
不使用请求自报duration/骨骼信息。解码和复制发生在准备阶段，不在tick读文件或hash/compile。
纯C# GraphPoseSnapshotSource只在中断时运行有界数值采样/shortest quaternion混合，
没有World/solver/GPU访问，也不是第二pose时钟。Headless无需pose/native渲染模块。
原生显示继续在相同rig/context执行Clip采样和pose blend，Frozen输入是复制的完整TRS。
主画面、阴影、拾取沿用同一姿态palette和当前frame身份。

**冻结视觉源没有前进Clip区间，其根位移/yaw意图为零。**新目标按过渡权重贡献当前区间根意图；
不是保留源根速度或惯性。仍送到原有唯一MovementCoordinator/Jolt，水平替换输入、垂直政策不变。
视觉去根使用完整当前混合姿态，不使用碰撞接受位移，避免重复位移。
数值求解开始后的故障fail-stop，不声称物理/文件/外部IO可事务回滚。

## 上限和释放

32场景实例、总32768骨骼；每实例最多1024骨骼/两个local TRS缓存。
每numeric provider最多65536 TRS scratch、128 Clips、262144 keys；唯一provider场景/预览总scratch
最多262144 TRS，prepare超限拒绝。generation最多2^53-1，拒绝精度/计数耗尽。
provider共享只限串行owner thread，实例缓存独立；公开复制只接受当前成功committed generation。
独立预览显式previewInterruptions启用，同一60Hz语义，不推进Edit/Play World，不执行碰撞根运动。
资源关闭次序仍为派生GPU/pose，Stop绑定，释放pins；失败保留所有权，禁止强制解锁。
暖路径零分配是测试观测，不代表已通过目标性能/长稳预算。

## AI边界

已有精确获批ncma.animgraph.runtime只读输出增加Frozen行cacheGeneration和
frame frozenPoseGeneration，保持闭合schema、session/world/tick/resource/endpoint/audience检查。
Agent不能读写local cache、设置开关、Step/参数或fault恢复；未新增grant。
Local UI与Agent仍消费同一复制观察，C将实现独立序列工具而非live控制。
