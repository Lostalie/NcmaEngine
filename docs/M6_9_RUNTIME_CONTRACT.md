# M6.9 精确请求工作流契约

当前最终顺序无Skip Debug/Release自动候选通过；不是整个M6正式验收或推理服务声明。
证据与保留失败见 M6_9_DELIVERY_REPORT.md。

`ncma.workflow` 是只读（相对于scene/assets/World）计划账本，不是执行代理。
输入操作闭合：propose(plan)、inspect/next/cancel(workflowId)、repair(workflowId,input)。
plan 必须 version/workflowId/deadlineSeconds/repairBudget/steps；step必须
id/capability/input/expectation。UUID严格小写D、非零、不重复；递归拒绝重复字段。
只支持已实际注册且在显式白名单内的场景读取/验证/事务、资产metadata读取、
UI文档事务和图读取/验证/提案/事务/骨骼/独立序列。删除、历史、文件open/save、
code/Python/importexec/live工具、递归workflow拒绝，不创建未实现能力占位符。

预算12步、48KiB计划、16KiB单步、16终身计划、2活动计划、0–2修复，
所有发出的最多224个ticket终身保存（16*(12+2)），无无限重试生成新UUID。
next重复返回同ticket；同ticket等待原权限批准不新增receipt。
已记录步骤拒绝再次执行；inspect提供原receipt，不重复跑读取/事务。
普通独立工具调用仍遵循原权限；该限制不是禁止合法客户端脱离workflow使用工具。

元数据授权和计划授权为人类trusted UI方法，TTL60，精确session/generation/catalog、
实际能力风险和输入输出schema hash、endpoint/audience epoch/UUID；元数据不含原文件数据。
计划额外绑定全文hash、发起connection、expected revision。
endpoint前置监视器只可收窄，不授予CapabilityPermissions；后置仅记录真实结果。
EditSession.ObserveReadOnly 禁止World写入、nested Invoke/register。
监视器异常导致后续调用 request_monitor_faulted；当次已提交的实际返回值保留，
不把监视器异常宣称为transaction rollback。须显式重建端点恢复，旧workflow撤销。

deadline从首次批准开始，修复不延长deadline；每次重新批准刷新自身TTL。
原 permission_denied/graph_scope_denied/ui_scope_denied、not_visible/not_approved
为等待审批；其他错误为失败。valid仅固定验证工具bool；passed仅sequence summary中唯一
items[0].passed。不是任意JSON路径或表达式；原工具拥有完整schema/资源/语义验证，
工作流preflight仅验证注册/白名单/闭合信封/输入object/重复/字节预算，不冒充完整JSON Schema。

修复只改当前失败输入，保留其工具/id/已完成步骤和失败回执；新全文需要重新审阅，
原ticket成为不可重用记录。取消/撤销不Undo，取消完成计划不改变其回执。
尚未批准的proposed plan没有执行时钟；资源、文件变化仍由原能力严格核查。
并行计划只是在途账本容量2，没有自动并行mutation或新scheduler；一份EditSession历史。

UI domain61/foreground panel413与MCP共享同ledger，完整计划分页访问后才允许批准；
字段化4行回执前后分页保留request/提交版本/结果hash，不用整块JSON取代步骤摘要。
返回固定status/index/count/hash、原请求JSON文本和最多14项receipt；receipt只有
step/request/capability/status/code/changed/executionRevision/resultHash/assertionPassed，
不保存原错误文本/文件路径/凭据/raw output。计划输入在本机审阅全文，不能作为执行代码。
没有本地持久化workflow恢复承诺，进程重启须新endpoint/重新审批；全部已有审计/失败/backup保留。

未接入推理，无C++/Python gameplay、World/GPU/Jolt/health/live Play/MCP自批准或native ABI改动。
人工第三方可见MCP、用户FBX、目标自包含、性能与1h仍开放。M6.10后续；M7只DX11本版。
