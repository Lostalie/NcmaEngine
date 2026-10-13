# M7.3-C3-B1 环境属性与准备状态

2026-10-14。起点为已核对 local/remote main `8c20a4794af5fd4b6dfe00458a758b2716d0703e`。本片仅完成 C3-B 的现有绑定属性编辑与状态呈现；预设发布/目录授权是 C3-B2，整个 C3-B 和 M7.3 不在本片关闭。

## 业务与权限

Inspector 在已注册环境组件的场景中提供显式添加关闭配置、已绑定环境的强度 0–16、Y 旋转 −180°–180°、关闭，以及 UUID、generation、完整 SHA256。保持全场景最多一个环境配置；不创建 Transform、父子关系或隐式对象。通用组件 JSON 编辑仍保留原严格合同。

`RenderingEditorAdapter.EnvironmentFieldOperation` 只构造原 `set_component` 操作。验证 UUID、有限数值、白名单字段与范围，转换角度到弧度，保持已绑定身份不变；不审批、不读写文件、不申请 GPU。UI 使用现有 Begin/Update/CommitInteraction，拖动只是草稿，释放提交一个历史条目，取消不改变提交文档。Agent 使用相同 `ncma.scene.transaction` 和既有会话、版本、精确对象/组件 scope、授权有效性及原命令 Undo 校验，没有新 MCP capability 或扩大权限。

现有 Off 合同必须为无资源身份的全零值。关闭会清除 UUID/generation/hash/参数；可通过唯一历史撤销恢复。不保留隐藏的上次资源身份或过期授权，也不把强度零伪装成 Off。关闭时启用和数值控件禁用，须待 C3-B2 审核资源选择后启用；本片明确显示预设发布尚未接入。

编辑冻结、过期 GUI 帧/版本、无效事件种类、非有限值和越界值仍拒绝；保存/重开只保存正式场景值，没有运行时 handle。组件存在性索引依托 EditorWorkspace 版本缓存，未变更页面不重新序列化或反复扫描整个文档。不是完整 UI/ECS 性能验收。

## 渲染状态

正式宿主在共享资源同步/环境 view 准备后复制准备状态给 Presenter；包含 World UUID、World revision、Play session、是否成功/失败及值配置，不含 native handle。Presenter 仅显示匹配当前 World/revision/Play 的结果；文档变更、Undo 导致 World 重建、Play 切换后旧成功或错误只显示等待。

失败显示“失败；视口已清空，详见控制台”，不在属性中输出文件路径/异常文本。完整异常仍由既有控制台记录；共享服务保留最后有效资源但不提交旧画面或旧 picking。成功重试清除失败显示，Play 明确标记编辑冻结。未运行宿主的纯管理页面显示等待，不声称 GPU 验证通过。

## C3-B2 接续边界

实际检查发现 `RuntimeReadPin` 禁止活跃资产写入/rename/delete，现有 `AssetDiskTransaction` 仅允许作者 assets 路径；其 participant 事务不同时修改场景配置。预设引入必须设计资源租约/原子发布/Undo 与失败恢复的明确协调，不能简单添加 File.Copy、放宽所有磁盘参与者路径、释放 Play 资源或绕过独占读锁。本片不改磁盘事务、pin、ABI、shader、cook、默认预设、ncmeta 或 MCP 黄金 schema。

C3-B2 仍需：可信预设选择、可逆项目文件发布、独立有界目录检查、精确读/写批准与到期撤销、保存重启/Play/失败恢复联合测试。C3-C 仍需正式产品联合验收。人工 UI/DPI/第三方 MCP、用户 HDR/FBX/材质、目标机/自包含、完整性能/1h 验收继续开放；没有推理服务、HDR/EXR 解码器或新 Python 游戏脚本。仅 DX11；Vulkan/OpenGL 下一版本。
