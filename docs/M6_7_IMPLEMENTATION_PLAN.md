# M6.7 分层遮罩和缓存姿态实施

基线M6.6远端ea4ef33421f7f298c7dbbb41b5da971dc5388862。按A、B、C顺序实现，
各切片完整无Skip Build.bat Debug/Release通过后提交推送并核对main，再前进。

## A 遮罩身份和数值插件 自动候选通过

AnimationBoneMask保留mask/skeleton UUID、精确骨架内容SHA256、稳定完整路径和显式逐骨权重。
准备期按parent-before-child构造唯一路径映射，重复/未知骨、层根权重非0、失配hash/骨架拒绝。
不做姓名模糊匹配、子树自动扩展或重导入silent fallback；未列出骨权重0，caller只拿复制数组。

独立可选原生layer1.0在现有context/rig上工作，40B request/24B stats/32B table，
owner线程、32requests、65536inputTRS、32768mask/outputs，POD/caller buffers无托管引用。
mode0 override，mode1显式reference additive；平移差分、scale正uniform比值，
旋转base × shortest slerp(identity,inverse(reference) × layer,mask × weight)，再父骨组合。
输入/输出缓冲不能别名；全批验证和scratch结果成功后复制输出/计数。数值kernel不拥有图、clock、World或movement。
原pose1.0和blend1.0表与计数保持；layerSupport默认false，先协商再创建context，无第二资源/插件owner。
释放所有clip/rig后close context再卸载，关闭失败保持pin。

A自动候选只验证数值/身份，不改变当前graphv3，不接正式图/作者/Agent权限/Player行为。
覆盖独立System.Numerics oracle、Quaternion符号/0/1/半权重/父骨、失配/别名/foreign/thread/scaleoverflow、
32×1024暖调用分配与原接口回归。完整门禁见M6_7_A_DELIVERY_REPORT.md。

## B 严格图和真实NCA求值 自动候选通过

Override/Additive、CachePose与严格新格式替代旧格式；原始拒绝文件保留，不增加compatibility/migration。
NCA准备核对骨架hash/完整骨骼路径及显式参考clip/time。预编译缓存依赖和scratch生命期；
同实例/state/tick/候选参数共享采样一次，失败和跨identity/reload不能复用。
根和事件取基础层，覆盖层不能获得Character/Health权威。共享真实pose、skin、shadow和唯一Jolt路径，
全部解释器/完整未引用行验证同步升级，中断缓存是全混合committed alpha1 pose。
测试实际NCA、独立CPU/native/GPU oracle、root stripping、故障/Reload/Stop/资源基线及Headless。

## C 作者和AI同源工具联合验收 自动候选通过

typed遮罩骨/层/参考/cache编辑和读诊断，精确已批准骨架/文件/resource/hash/审阅者/TTL边界。
UI与Agent共享closed语义、draft/diff、单history durable transaction、Undo/Redo，序列只运行独立实例。
真实stdio拒绝/撤销/过期/重新配对/queued stale/重导入、作者取消/保存/预览与正式Player联合通过后提交。
不新增推理服务、任意代码执行、Agent审批或live参数/姿态内存写权。

## 验收边界

每阶段完整Debug/Release、smokes/formats/inspect/三轮profiles/audit/checked部署及SHA核对。
人工可见审批、用户FBX、目标机器/性能/1h门禁仍开放；不把数值测试、程序化NCA或sharedshader oracle当正式验收。
