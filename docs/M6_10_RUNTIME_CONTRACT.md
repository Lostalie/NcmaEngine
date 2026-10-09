# M6.10 运行与验收契约

NCP1 是严格数据包，不是签名／安全沙箱或可执行字节码。graph v5 数据在完整闭包验证后离线编译，
不在 simulation/render tick 编译，不引入第二个 Animator／时钟／数值域。
打包规范化图的X/Y为0，不携带编辑布局；其余语义字段/UUID严格完整保留。运行图hash及派生generation
取规范化字节，作者只移动节点不会改变运行包。不兼容接受带非零布局的运行包；合法checksum也必须拒绝。
作者graph仍允许布局坐标，作者文件不被重写。当前是规范化typed数据+加载离线编译，不称native可执行字节码。
一次发布的不可变 AnimationProgram 可供多个独立实例共用。缓存最多一份，以精确 lease.Identity 区分；
disposed/foreign/跨线程 lease 即使已命中也必须拒绝。缓存不延长文件 pin 寿命，失败不替换成功缓存。
catalog/package 全图 preflight 使用不保留程序的验证入口；只为实际消费者准备的图缓存，避免携带
但未使用的图因校验而常驻预编译结构。完整数据闭包仍严格验证，不跳过unused图检查。

C# 仍独占固定步、World 写权限、Montage 控制、Notify、Movement 发布和编辑历史。
纯 Headless 不加载 GPU／native pose；使用 root/physics 时仅按实际需求加载数值域。
纯 2D 不加载动画资源；组合图发布不会将动画设为必选模块。所有 native ABI 原样保留。

验证包内容后再启动 Player；损坏或伪造闭包不得落回作者 catalog。测试仅在新建 out 测试目录
写入变体，保存每种变体及合法原件，释放所有 reader 后恢复合法包并复测，绝不修改用户资产／安装包。

128 次资源试验共享长寿命服务而非 128 个新进程；派生 GPU/pose 在 numerical runtime 前关闭，
故障是 fail-stop，不宣称 solver rollback。Reload 重建并转身份，失效旧帧不得读取。
资源验收比较进入试验前的准确基线，不要求外部调用者的有效资源计数为零。

成本报告带图／包／插件 hash、runtime／OS／配置、固定步、测量范围、GC、CPU／ABI／GPU语义。
同步诊断 drain 时间单列、不得混入吞吐成本或生产 tick。测量不等同目标性能批准。
已有 GPU oracle 与实际 DX11 API 0/0 必须保留；不能把共享 shader 对照包装成独立算法真值。

人工／用户素材／目标环境／性能／1h acceptance=false；M6 自动候选闭环不是正式产品全面验收。
