# M6.4 交付记录（自动候选）

2026-10-08用户授权独立M6.4，完成后测试通过再提交推送。C1内部数值检查点保留，
不标记M6.3-C/D完成；Animator + RootMotion仍拒绝，M4唯一碰撞运动权威不变。

## 交付

- C#共享closed语义编辑：节点/边/参数/状态/转换/入口和图名称，确定UUID、复制草稿、完整提交验证。
- 精确文件参与者：checked path/parent/file、原始bytes冲突、journal补偿/恢复和唯一Editor.Core Undo/Redo。
  新建只删除自己已创建且符合memento的文件；禁止目录授权/旧格式兼容。
- 活动MCP `ncma.animgraph.propose/transaction`：默认拒绝、纯内存提案、精确文件/资源及端点请求双人工批准，
  expiry/revoke/audience/revision重检、重复回执、与本机共享历史。proposal不装载资源。
- 深蓝独立图工作区：类型引脚/连线、搜索/pan/zoom/multiselect/drag、类型化参数/状态/条件、分页语义/候选审阅。
  显式保存合并草稿为一次Undo，取消不改文件/history；Play冻结禁止作者写入。
- 真实NCA/GPU独立预览：复用图/pose/skin/shadow路径，独立时钟/参数与主场景隔离；一帧只提交/Present一次。
  批准固定完整依赖闭包，持久Validate重读实际generation；未保存预览不能cook/nest。
- GUI additive1.7（旧112字节表/旧queries冻结）：有界复制画布文字/矩形和指针意图，native不拥有图业务。
  包清单、托管协商和Python检查式审计同步；上次安装GUI1.2–1.6仅允许备份校验，不是新包回退。

## 测试与修复记录

定向Debug编辑器服务81/81通过，9个M6.4案例覆盖真实stdio审批、pure proposal独占文件锁、
共享保存/Undo/Redo/reopen、创建取消、rawbytes并发冲突、伪造review、过期/重配对、外部NCA代次替换、
Play冻结、preview cook拒绝、实际GUI事件/陈旧generation及16帧独立GPU/资源归零。
图画布截图为程序实际输出，像素校验确认GPU预览可见，不以target单独成功代替界面显示。

此前失败日志保留在out：debug-first/second/third/fourth、editor-target-test-second至eighth；
分别修复原测试ABI预期、画布preflight、托管/包审计minor白名单、graph journal恢复后缀、
按UUID代替排序行号、同帧重复提交、file commit World identity断言、负例无改动被正确视为no-op。
截图还发现重叠Panel遮挡，改为在原预览区域插入Image并加合成像素断言。
不把失败归咎未知外部环境，不删除失败证据。

首次最终Debug完整通过；首次最终Release中M6.4全部通过，但旧M3.6测试等待一个被正确拒绝的陈旧放置计划，
导致45秒timeout，保留out/m6-4-release-final.log。已修正测试：最多8次本机显式重新准备，
不重试提交、不放宽生产校验；首轮直接推进资产revision，确定验证拒绝旧计划后才重试。
中间debug-verified尝试用强制扫描推进revision，与后台RuntimeReadPin只读租约的DELETE-sharing约束冲突，
失败日志保留。repair2定向测试进一步确认直接推进clock后必须在重试前刷新完整catalog，否则Play按预期拒绝
asset_catalog_stale。repair3已81/81通过；最终fixture只在启动新任务前force-refresh完整catalog，
完成期间仅Pump，避免无关目录扫描并发；
生产锁和IO拒绝策略没有变化，既有并发写者/父目录置换/锁争用专项继续保留。
这不是未知环境归因，也不以单独重测掩盖完整回归失败。
debug-verified2中Editor81通过，但旧skin缓存测量首次记录224字节，测试原预热seek而测量AdvancePreview；
现固定32帧同一路径预热、记录warmFrames/角色数/mesh类型，8帧测量仍严格0字节，不选择性重采样。
同轮部署fixture的File.Replace失败，93f1088cb1c34ecc8b584247ac7f2c65现场/恢复journal保留；
未证实外部锁原因、不更换更强文件操作、不强删/重用旧失败目录，下一完整轮建立新UUID测试根。
生产部署规则不变。

## 最终完整回归

修正后的完整顺序 `Build.bat -Configuration Debug` → `Build.bat -Configuration Release`（无Skip）
均以0退出，日志 `out/m6-4-debug-verified3.log`、`out/m6-4-release-verified3.log`：

- 每配置12 native +22 managed CTests全通过，Editor81、Player56、pose/clock35、Gameplay53、fake Movement38、Python43。
- managed facade、native/managed/apphost smokes、strict新格式/移除格式拒绝、Python inspect、3轮保留profile与pre/post审计通过。
- 新代码构建0警告/0错误；M6.4 GPU16帧、validation0/0、可见预览像素合成、独立World tick0与资源归零通过。
- Debug post-audit：out/verification/m2-8/Debug/e8e5bd306d2b4935aad5e9770a6941ed/audit.json；
  Release post-audit：out/verification/m2-8/Release/2d6e376774c042c38422113e3bfba833/audit.json。
  audit_passed=true，h8_accepted=false，未关闭此前门禁。
- 最终Release检查式部署到out/bin/NcmaEngine.exe，101安装文件校验，journal Complete；
  Debug备份out/deployment/89f798a51fca437e928aebd622d37b94/backup，
  Release备份out/deployment/f489605e7e944cc1801f90656bed5b23/backup。保留所有失败现场与备份。

M6.4自动候选门禁通过，按授权提交推送并核对远端main SHA；Git回执为实际提交身份。
提交范围不含out生成物/安装/备份、.vs或NcmaEngine.vcxproj.user。保留C1内部数值检查点，未交付C2–C4/D。

## 未通过/未实现边界

真实用户FBX、可见人工审批/第三方MCP、DPI/IME、目标环境/性能和1小时长稳均未通过。
本机独立预览不是Agent live参数控制或获批live图运行帧MCP；C2–C4/D、seek、碰撞根运动、
事件/BlendSpace/遮罩/缓存/Montage和内置推理/Python服务未由本阶段交付。
参考方案/操作入口见M6_4_IMPLEMENTATION_PLAN.md与M6_4_EDITOR_GUIDE.md。
