# M7.3-C3-B2 环境预设作者业务合同

2026-10-14。基线 C3-B1 `342753a89e00e1e84b75bb1fd6fe90b163464799`。本片是 C3-B2，后续 C3-C 正式产品联合验收与整个 M7.3 尚未关闭。

## 业务与所有权

C# `EditorEnvironmentWorkspace` 负责选择、审阅、精确文件授权、提案与目录读取。源项目宿主才注册服务，NCP、无源项目不提供作者入口。构造服务/空场景不加载预设 NCE；人类显式选择合成中性预设时才加载 C3-A 受 manifest/SHA/read-pin 约束的库。没有 HDR 解码、运行时 Cook 或推理服务。

采用预设分配项目自己的环境 UUID/generation=1；`CloneForAsset` 仅改变 NCE 的资产身份/代次及外部 SHA，数值 payload、源 UUID 与 Cook 参数保持。原 package 不变；`GpuValidated=false`，hash 不构成来源签名或 GPU 授权。

审阅页列出目标对象、精确 UUID/代次/hash/作者路径和 synthetic 标记。取消不创建项目文件，反复取消不消耗保留代次名额。确认可直接应用或只准备供 AI 提议；本地审阅与准备令牌均 60 秒、绑定 session/document generation/revision/asset revision。Play/草稿/失效历史不允许操作；Escape/失焦取消未确认审阅。没有自动修改旧场景或隐式启用 Off。

## 不可变代次与可逆可见性

沿用 NCA 导入的可见性边界：显式确认先写入唯一 staging，再按不覆盖规则发布到精确 `out/assets/<project>/<asset>/<generation>-<SHA>.nce`；完整校验后保留只读 pin。限 32 个代次、64 MiB、16384 个遍历条目；失败留下的 staging/未引用代次也计入磁盘预算，不自动删除用户数据。

真正的项目可见资产由 `assets/*.ncenv` 决定。`EnvironmentPublication` 使用已有 `AssetDiskTransaction`，仅增加 `.ncenv.journal` 后缀，不扩大通用文件路径白名单。已有描述文件只能与精确同一 descriptor 一致，禁止用预设命令覆盖另一个代次。独立目录扫描检查项目/全资产 UUID 冲突、链接/路径、128 环境、64 MiB 元数据与 journal。

Core 的可选可信 `IEditCommandSceneParticipant` 先返回原有 scene operations，在只读回调中检查完整候选与对象/组件权限，再准备磁盘 memento；同一安装协调器提交描述文件与场景。一条历史同时保存两者。权限影响包含请求中的 no-op 目标；Endpoint 审阅不做文件 IO。普通 participant 行为保留。

Undo 删除作者描述文件并恢复场景，**保留不可变 NCE 缓存**；Redo 恢复同一 descriptor/身份。这样不释放使用中的 Play/GPU/RuntimeReadPin，不删除仍被租用的 NCE。此处“可逆发布”不表示所有 staging/cache 文件都会消失。没有新增自动 GC；达到预算需可信显式维护。

发布前或发布中失败走已有 journal 补偿，场景/历史不变；权限发布后撤销也补偿。完成决策失败冻结会话，不声称已安装 World 被回滚。`EnvironmentPublication.Recover` 是可信本地 API，要求精确路径+descriptor 授权；已 committed journal 保留提交，未知占用者拒绝覆盖。恢复磁盘后必须显式重建/重开冻结会话；不自动恢复未保存的内存场景。未增加 Agent 恢复权限或自动绕过启动 journal 门禁。

## Agent 与目录读取

新增 `ncma.render.environment.apply` 只接收本地准备的 proposal UUID 与 expectedAssetRevision。无路径、字节、代码、GPU handle 或授权标志输入。Agent 仍通过原有 Endpoint 审阅：精确对象/组件、请求/会话/版本/60 秒授权与撤销；历史要求原命令权限。UI 和 Agent 共用上述 participant，没有第二套 Undo。

新增 `ncma.environments.list` 是独立合同：offset 0–256、limit 1–32，仅给出批准 UUID 的 generation/hash、registered/prepared 状态和准备令牌。默认空；不返回路径/源文件/数值 payload。查询只读已复制缓存，不扫描磁盘、不更新 World。旧 ncmeta 和三项资产 MCP schema 原样保留。

读取审批固定 60 秒、最多 64 UUID，绑定 Edit stamp、资产版本、Endpoint 实例、当前配对连接集合和 AudienceRevision。撤销再配对同一连接也失效；文档变化、Play、endpoint 替换或资产目录变化均使旧授权失效。共享已审阅 audience，不伪称逐客户端 ACL。文件发布权限、目录读取权限、Agent 变更权限互不隐含。

正式源项目已有 16 项扩展 inspection，本片将上限明确调整为 **17**，只新增环境目录一项；第 18 项仍拒绝。participant+inspection 联合预检后注册，容量/重复名失败不留下半注册命令。未增加 native ABI、shader 算法、Python gameplay 或编译权限。

## 自动检查与未通过的人工门禁

13 组服务/文件/Core/UI/真实 stdio MCP 检查；正式 Editor/Player/NCP 宿主检查包含批准预设后的独立像素、三轮 Undo/Redo、Play/Reload/Stop、保存与原生验证。隐藏窗口下复制 GUI 审阅帧避免持续失焦取消，提交后的渲染仍经过正式宿主；不是人工鼠标/DPI验收。

完整双配置、源哈希、部署恢复证据见交付报告。人工 UI/输入法/DPI、真实第三方可见 MCP、真实用户 HDR/FBX/材质、目标机/自包含、完整性能和 1 小时稳定性仍待验收。DX11 only；Vulkan/OpenGL 下一版；M7 → M8 → M9。
