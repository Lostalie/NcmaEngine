# M6.2 图求值和只读 AI 契约

M6.2增加纯C# AnimationProgram/AnimationGraphInstance，及活动编辑器的获批图检查。
这是数值采样/混合计划和提交语义，不是已接入World角色、native混合或GPU Animator。
格式沿用严格`.ncmaanim` v1，不恢复旧图原型。阶段计划见 [M6](M6_IMPLEMENTATION_PLAN.md)。

## 编译和实例

离帧编译首先复制/验证图，构造拓扑次序、紧凑索引、参数槽、状态根/比较条件和按priority排序的转换。
编辑坐标/名称不进入运行指令。Clip描述必须齐全、UUID去重、同Skeleton UUID与同非零generation，
时长0.001到600秒；描述只作元数据验证，不证明真正文件/资源已被pin，M6.3接入实际租约。

程序不可变、可跨实例共享。实例为owner-thread-only，有独立instance/session/world/tick、
requested/committed参数、每状态clip时钟和有界scratch/计划。所有资源/native句柄和World写权威不进入它。
时钟按状态上下文独立，状态重入从0开始；共享子图同上下文每次求值只产出一份采样，跨状态不共享时钟。
允许全图的Blend与一个StateMachine组合，不让混合源/目标抢用同一时钟。

Prepare接受当前精确stamp和0.001到1秒固定量子；输出opaque instance/sequence/sourceTick token。
Commit须同session/world、tick恰好+1，并且来自尚未消费的当前token；Abort不改变committed状态，
requested触发器保留。可信主机应仅在对应World提交成功后Commit，这个API本身不授予World推进权。
非法参数/结果容量/外来token/错误stamp不会提交前缀。零delta不Prepare，读取Frame/CommittedPlan即可，
没有暗中tick、Notify、输入消费或历史。参数在已Prepare期间不可变化，防止提交覆盖后到达输入。

Float/Int/Bool/Trigger类型严格；触发器只有被获选转换使用且Commit成功才清除，不被选择的触发器留存。
speed为0到8、blend权重0到1，输入异常拒绝，不偷偷钳制。loop用unwrapped时钟，非loop钳制终点；
单次最多32次clip边界，浮点溢出/精度无法推进则拒绝。热路径无JSON、文件IO、历史或AI等待。

## 状态转换和输出

每量子最多一次新转换，priority数值小的优先。条件AND，普通Float/Int比较、Bool等值与Triggered。
非loop exitTime为达到相位的持久条件，loop以跨过相位边界为准（初始0相位可在首量子选择）。
Blend状态的相位基准为沿a输入找到的首个Clip；当前不冒充完整同步组/BlendSpace相位。
转换在量子边界选择，目标本量子从0推进；有duration时源/目标同时推进并输出Blend配方。
转换完成的下一量子才可再次选择；中断策略/可见源姿态缓存/Notify将在M6.5补齐。

Clip配方包含节点/片段UUID、previous/current unwrapped时间、duration和loop；Blend配方包含已产出
sourceA/sourceB索引和float权重。它不包含骨骼数组/native指针，不上传GPU，也不直接应用根运动。
CommittedPlan/Frame只读取上次成功提交，Prepare失败仍可读取旧成功状态（必须由真实host标记自身fault）。
实例最大指令3×nodeCount+1，时钟/slot容量(nodeStateCount+1)×nodeCount；预分配是有界正确性方案，
不是已通过真实角色内存/CPU/GPU预算或优化ECS。部分warm测试零分配不代表整个引擎零分配。

## AI 和本机检查

项目模式下“工具或AI → 动画图检查 / 只读 MCP 审批”打开本机检查窗。通过文件对话框明确选择项目
assets内`.ncmaanim`；使用已有HANDLE/父目录保护读取，拒绝越界/链接/旧格式/待恢复journal。
打开只做严格结构验证，复制、规范编码、hash及分页数据在本机事件时准备；失败保留旧快照，撤销旧grant。
这是读取快照，不是节点编辑/文件保存入口；外部文件变更不会悄悄替换已review内存副本，重新打开须重新审批。

`ncma.animgraph.inspect`：graphId、section、可选offset/limit（0到4096/1到32）；section为summary、
nodes、parameters、links、states、transitions、dependencies。`ncma.animgraph.validate`只接受graphId，
返回structural_only/resourcesPrepared=false及缓存结构验证；它不验证真实片段字节或编译/执行图。
请求仍用Editor Core契约2 envelope；closed输入/输出schema、确定错误码、没有路径/shell/加载参数。
MCP读取不扫描/读取/解析/hash磁盘、不编译、不推进Play、不采样pose、不改变Undo。

默认graph_not_visible。可信UIreview精确图UUID/hash、所有骨架/片段依赖UUID、完整数据及配对受众，
人工勾选后批准60秒。批准绑定Editor session/generation/revision、图publication（同字节重开也更新）、
endpoint对象/UUID、AudienceRevision及受众UUID集合。传入修改过的page/fingerprint拒绝；每次查询包含
重复requestId均重检；撤权/expiry/编辑/Play/draft/重开/endpoint替换/配对生命周期变化不可复用旧scope。
共享受众不是每客户端ACL，图审批不授予资产源文件、其他图、图写入或运行控制权限。

Endpoint新增主机侧AudienceRevision，配对/撤权/连接/断开/重连事件单调推进，检测同UUID集合复现；
不更改MCP/IPC wire协议，也不增设Agent配对/批准工具。全部AI读取功能禁用后Player行为不受影响。

## 自动与人工验收

测试覆盖编译资源描述/副本、参数/触发器、提交与abort、优先级/exit、时钟边界、真实World成功/失败步
测试适配、30/60/144调度、warm分配、owner线程；真实stdio helper→配对→默认拒绝→本机GUI审批→
读取/验证→撤权→同ID重试拒绝；磁盘独占锁下仍读缓存、不同图拒绝、期限、篡改/陈旧/伪造GUI事件。
人工文件对话框/DPI/真实第三方客户端仍单列开放，不把测试生成的原生GUI事件称人工验收。
