# M6.7 C 遮罩作者与 AI 检查契约

2026-10-09。B远端46b4ea6d47189bdb04f57a4d0c18e0e55e0273f3已核对。
图保持严格v4，原生表冻结；typed层/遮罩作者、实际骨清单与缓存诊断同阶段交付，没有推理或live Agent权限。

## typed 作者与共享历史

主机显式准备真实NCA骨架后可创建Override/Additive。初始遮罩只有实际第一骨权重0，
未列出骨也为0；Additive必须明确输入参考ClipUUID，不猜参考素材。用户连接a/b/weight，设置
参考time、精确骨架hash和逐骨权重；属性每页4骨，真实清单每页4，旧/stamped事件拒绝。
修改走shared closed `layer.bone.upsert {nodeId,bone}` / `layer.bone.delete {nodeId,bonePath}`，
骨架/参考整体定义走原node.upsert。允许临时空遮罩草稿，完整提案/保存/预览拒绝；无假骨填充。
实际资源审阅检查root0/hash/fullpath/reference，文件/资源批准和端点事务批准独立，单history Undo/Redo。
取消不落盘、不推进World，独立真实角色预览仍用相同pose/layer/skin/shadow，不推进live Play。

## 骨架元数据与单独审批

AnimationGraphSkeletons.PrepareTrusted只能由主机在off-frame触发，保留一个真实NCA闭包和复制路径/parent。
没有Agent准备/路径参数或pose内存访问。`ncma.animgraph.bones`注册为ReadOnly，closed graphId/offset/limit，
最多8骨/页、1024骨、path4096字符；返回骨架hash、真实publication、精确graphHash，不返回文件路径/handles。
resourcesPrepared=true仅证明本服务保留实际骨架NCA元数据，不证明全部图运行/GPU/Jolt或用户素材验收。

图读取批准不等于骨清单批准：可信UI审阅完整图、骨架hash、全部闭包代次、所有骨和配对受众，
全部审阅页访问后才可批准。每请求含repeat/cache/queued重检session/edit/graph content/asset revision/
publication/endpoint/audience epoch/read permission/60秒；撤销/过期/重新配对/源改变不能复活旧批准。
Agent不能批准自己，bone grant不授予文件/图写入。草稿开始或变化禁止Agent元数据请求；
可信本机编辑可继续使用同session/generation/graph/skeleton/asset revision保留的骨表，保存仍需再次实际审阅。

## 缓存诊断

独立sequence新增section=cache及闭合CacheHits/CacheRequests整数断言，复用精确用例/图/NCA/受众批准，
每页8、最多256行，输出只含committed tick/requests/hits。UI同源显示，不声称耗时/性能优化已验收。
核心Run的ResourcesPrepared=false；服务true只证明真实NCA pins，不执行World/native GPU/Jolt。
同实例/state/candidate的CachePose别名命中与Source静态reference分开，失败/Abort/Reload不发布新缓存。

## 证据与开放门禁

定向graph/pose83、Editor104通过：实际NCA/真实stdio default denied/双审阅/TTL/revoke/queued/re-pair、
shared提案纯内存/dual事务/Undo/实际骨架拒绝、typedUI取消/全页批准，以及真实ImGui/独立layer GPU预览。
完整双配置、安装hash/journal及提交状态在M6_7_C_DELIVERY_REPORT.md核对，不能由定向结果推断。
人工可见第三方MCP、用户FBX、目标机器、性能/1h仍开放；没有pythonnet/gRPC/ZeroMQ或推理配置。
