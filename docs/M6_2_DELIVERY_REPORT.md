# M6.2 图编译与 AI 检查交付记录

实现候选：纯C#编译、提交式实例、采样/混合配方、类型化参数、状态/过渡/有界时钟，
活动Editor两项默认拒绝的只读图MCP及本机文件选择/审批。详细语义见 [契约](M6_2_RUNTIME_CONTRACT.md)。
最终完整顺序Debug/Release回归均以0退出，本阶段自动候选通过；整个M6未完成。

## 测试和修复记录

新增7项图运行专项与3项Editor/MCP专项，纳入现有CTest/Build.bat。首次Debug记录
`out/m6-2-debug-first.log`：编译零警告/错误、全部图运行专项及第一条真实stdio审批读取通过；
第二项授权负例报graph_audience_missing。原因是测试仅initialize/initialized，未执行tools/list建立
真实配对连接；补齐正常生命周期的tools/list（检查工具注册）再review，权限要求未放宽，原日志保留。
同时图检查从UI创作菜单打开时显式切回场景检查区，不变更World/Play/历史。

最终日志：`out/m6-2-debug-retry.log`和`out/m6-2-release.log`；均未使用Skip标志。
两个配置各12/12原生、22/22托管CTest通过；图/pose/clock32/32（新增7项求值），Editor72/72
（新增3项真实stdio/审批/负例），Player56、Gameplay53/53、fakeMovement38/38、Python43/43。
managed/native smokes、严格当前/拒绝旧格式、inspect、三轮保留profile、pre/post审计和完整检查式部署均通过。
warm实例测试256次Prepare/copy/Commit测得调用线程新增分配0B，范围不含编译/host/真实pose/GPU/整个引擎。
新代码编译零警告/错误；fakeMovement和历史profile不能代替真实角色性能/环境验收。

Release的101项manifest文件哈希已再次验证，部署journal为Complete，generation为
`6174bc917fa947fbb40d44b8d4d6cbb4`。Ncma.Animation.dll SHA256为
`04FCB952F1220FBE95FB957D287A050F601CC474AD944893B4121FD2E81DF148`；Ncma.Editor.Services.dll为
`F3F5FD3BAE462203570E104689EDE231AA218CFA8AFD256EA3F34303D4B800C5`。
Debug旧安装备份：`out/deployment/bae469a246a447bbab0787c41b32241b/backup`。
Release之前安装备份：`out/deployment/6174bc917fa947fbb40d44b8d4d6cbb4/backup`；不提交备份/包/IDE个人设置。
最终audit_passed=true而h8_accepted=false，报告为
`out/verification/m2-8/Debug/79a26a43eb064d82947062135120eac1/audit.json`与
`out/verification/m2-8/Release/c445af62b52848af9bc9c1b4f6638074/audit.json`，既有开放门禁不关闭。
完整Release程序在`out/bin/NcmaEngine.exe`，入口为项目模式“工具或AI → 动画图检查 / 只读 MCP 审批”。
正式节点编辑器尚未提供；图元数据读取不意味着片段已可执行。

## 未实现范围

真实generation租约/角色绑定、native多姿态混合、GPU图运行/根运动接线属于M6.3；节点编辑与
图修改MCP在M6.4，Notify/过渡中断和后续BlendSpace/层/Montage仍未实现。
没有新增推理服务、Python通信、Agent liveStep或World写权威。
所有既有人工/用户素材/目标环境/性能/长稳门禁仍开放，warm实例低分配不代表全引擎性能验收。
