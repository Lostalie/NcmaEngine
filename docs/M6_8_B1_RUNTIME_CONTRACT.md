# M6.8 B1 提交式动作编排状态契约

B1是B的固定步状态基础，主机必须使用同一成功fixed-step context驱动Prepare/Commit/Abort。
没有scheduler、World、真实NCA lease、native pose/solver/IO/回调或Agent权限；正式Animator与图/包接线在B2。
M6.8不能因为B1纯状态测试通过就标为完整运行时或正式Player实现。

## 请求和身份

Request带requestID/instanceID/session/world/tick/slotUUID/type/sectionUUID/priority，只有当前成功tick可入队。
准备期间拒绝请求，owner-thread-only。至多64队列，256近期ID ring/cache；相同ID完整同payload不重复入队，
近期ID换payload拒绝，旧tick/其他instance在cache查找前拒绝。近期cache不是256次攻击的终身限制。
Play不足slot minimum或当前priority返回确定拒绝回执；当前不可中断Slot拒绝重启，显式Cancel仍可渐退。
Jump只作用于已播放同Slot的确切Section，不追补跳过事件；默认Play用明确entry，不猜clip/section。

## 候选和提交

Prepare复制至多16Slot状态，处理有界请求并计算区间，token绑定instance/sequence/sourceTick。
Commit只接受同session/world的next successful tick；请求、Slot时间/权重、回执和区间同时发布。
Abort保留请求与原提交状态；失败候选保留历史但snapshotValid=false，不能作为当前输出读取。
本类上下文是数值语义stamp，不是World写权威；正式主机才保证成功World提交与fail-stop/关闭。

## Section 与 blend

每Slot每量子最多32个Section边界，全实例最多16×33区间，预分配copy buffers。
每段记录(previous,current]时间、section/clipUUID和root策略数据；最终Slot变inactive仍保留终端区间，
供后续正式事件/root消费者处理，不因inactive抹掉最后一段。没有同步脚本回调或Health写入。
使用补偿累计消费时间，避免0.032秒/1ms循环的舍入残余虚构第33段，不放宽32边界限制。
BlendIn按已播放时间，最终Section BlendOut按剩余时间；Cancel冻结playhead并从现有weight渐退。
Section循环可控，过预算整体候选失败、无部分提交；有限数值和非前进precision/overflow拒绝。

## 验证边界

定向动画92/92，新6组请求/priority/中断、Commit/Abort、区间/跳转/取消/32边界、owner/实例、
1024近期ID循环和warm1024零分配、queue/output完整拒绝。Metadata仍不是NCA pin或pose/GPU/root执行。
完整Debug/Release/checkeddeploy/提交状态在交付报告记录，B2/C及M6.9/M6.10仍待实现。
