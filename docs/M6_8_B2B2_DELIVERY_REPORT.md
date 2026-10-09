# M6.8-B2b-2 交付记录

基线 `b88ea74812e8a2cc51938988b10dadb7946c805c`。范围见
[正式运行契约](M6_8_B2B2_RUNTIME_CONTRACT.md)。最终完整双配置自动候选通过；不是人工/正式目标机器验收通过。

## 定向实现与测试

- graph v5 严格替代v4，正式最后Slot链、复制Montage数据/NCA闭包；删除临时外部绑定入口和专用旧测试。
- sole Animator/token/clock、CPU/native姿态、完整interval root→uniqueMovement/Jolt、Section Notify、
  persisted startup/NCP1/正式Player与独立sequence。无需新的nativeABI、资源标记或时钟。
- pre-global-Slot基础姿态中断cache，避免叠加两次；原double fixedDelta传入root准备，物理边界才float。
- 只读AI summary/Slot/Section有界分页，共用既有精确人工批准；typed作者/AI编排在C待实现。
- 定向core `out/m6-8-b2b2-core-fifth.log` 109/109；新增5组严格persistent/ownership/terminal/
  Notify/Abort/Cancel/Jump/错误recipe，既有1024 warmed cooperation allocation0保持。
- target最终build0警告/0错误；render `out/m6-8-b2b2-render-sixth.log` 全部通过。
  actual NCA1/8/32 root off/on、terminal、Notify、GPU独立预期、Reload、sequence、base-only frozen cache、
  3类pre/postsolver failure、Editor0/1/8/32、16正式source-freePlayer、API0/0。
- 未完成全量前不部署/不提交。完整记录随后补充；4个IDE改动不入提交。

## 保留失败

首次target build旧测试使用被删除的临时API；移除旧测试，替换正式persistent路径，不恢复兼容。
后续target build事件参数/GPUbytebuffer类型错误已修复，所有日志保留。
core前几轮发现旧v4断言，按严格v5及旧格式拒绝更新；terminal测试明确越过完整时长，不靠十进制浮点恰好相等。
render first/second记录共享root fraction失败：Character将double提前转换float；修复调用传原delta，
未放宽任何区间/fraction校验。third记录联合submit失败，后续重测无此失败，未确认其具体原因；
现有提交断言不弱化，保留诊断。fourth正式非物理Player因测试仍带BoxCollider拒绝配置，
改为只在root物理配置中创建地面；没有放宽生产配置。fifth和新增后sixth均通过。

所有原始失败目录/日志、部署备份、SDK/user assets/IDE保持。
本切片不是整个M6.8完成：C typed作者/MCP、M6.9/M6.10待实现；人工/用户素材/目标/性能/1h不关闭。

## 最终完整自动门禁

源码冻结后按顺序、无Skip执行，两轮均首次退出0：

- `out/m6-8-b2b2-full-debug-first.log`：12native/22managed CTests，3.18s/258.97s。
- `out/m6-8-b2b2-full-release-first.log`：12native/22managed CTests，1.35s/223.57s。
- 两配置pose/graph109、Editor105、Player56、Scene36、Gameplay53、fakeMovement38、Python43；
  完整smokes/strict格式与旧格式拒绝/inspect/3轮retained profiles/pre-post audit/checkeddeploy通过。
  新代码0编译警告。完整日志不是仅定向测试；不以Skip部署。
- 两配置 `out/verification/m2/render-{Debug,Release}/m6-8-b2b2-montage-results.json`：
  独立GPU最大顶点误差 `5.986600370988526E-08`；`m6-8-b2b2-joint-results.json` schema2 graphVersion5，
  Editor0/1/8/32两次8ticks，16搬移source-free Headless/DX11Player通过、API0/0。root数值的
  独立证明在专项Editor/sequence，不冒称Player报告逐Actor读回根数值。
- 新真实stdio精确Montage摘要/Slot/Section分页/default-deny/revoke通过，文件锁定时仍读cached快照；
  Edit/history/tick不变。自动批准驱动不关闭可见第三方人工门禁。
- post审计 Debug `out/verification/m2-8/Debug/6995931c910d44fbaf8888a2ba9b09a3/audit.json`，
  Release `out/verification/m2-8/Release/194a125e88a148798475f032fab71870/audit.json`：
  audit_passed=true、h8_accepted=false，pending gates保持。
- 最终Release `out/bin/deployment-manifest.json`101文件逐项检查路径边界/非reparse/类型/size/SHA256一致，
  `out/deployment/editor-journal.json` phase Complete，generation `694b65a313a94a2fa3a38bb516bc0e38`。
  Debug backup `out/deployment/5a924bf0feec450ab014bfe6f6dcb1cd/backup`，
  Release backup `out/deployment/694b65a313a94a2fa3a38bb516bc0e38/backup` 保留可恢复。

按授权提交推送并核对远端main后再开始C。提交SHA以实际提交/远端核对为准，不预编造。
