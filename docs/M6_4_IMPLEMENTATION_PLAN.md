# M6.4 图工作区、共享事务和获批 MCP 修改

2026-10-08 用户直接要求开始 M6.4，完成、测试通过后提交推送。
按该请求先开发独立作者工作区；不因此关闭 M6.3-C/D，C1 未提交候选须保留并单独记录。
M6.3-B 提供真实 NCA/图姿态/GPU 基础，未接线的碰撞根运动不得出现在预览能力声明中。

## 实施顺序

1. 共享语义编辑：图/节点/边/参数/状态/转换 UUID 操作，closed schema，有界复制草稿，
   未接完只存在临时草稿；持久化和预览程序必须严格完整验证，不改变 .ncmaanim v1。
2. 文件事务：复用 checked storage、AssetDiskTransaction 和 Editor.Core 唯一 history；
   精确图/文件/旧新 hash/依赖批准，保存冲突保留原文件，手势合并入一次显式保存 Undo，取消不写文件/World。
3. 活动 MCP：propose 仅用已批准内存副本生成确定候选/diff，零文件/history/Play 副作用。
   transaction 只引用主机准备且审阅过的精确提案；端点对请求另行批准，Agent 不能审批自己。
   Play 冻结、过期、re-pair、revocation、文档/文件/资源变化重检；Undo/Redo 使用原批准范围。
4. 深蓝独立动画工作区：pan/zoom/multiselect/drag/search、类型引脚连线/删除、参数/状态/
   入口/转换条件、诊断和真实独立角色预览。切换不推进/重启 Play，不把场景改成节点树。
5. 自动验收：UI/Agent 语义一致、真实 MCP 路由和人工审批路径、负例、保存重启、取消/Undo/
   Redo、文件并发冲突/恢复、事件 generation 和画布几何、实际 GPU/截图/资源基线。
   完整顺序 Build.bat Debug→Release，无 Skip，通过后提交推送核对远端。

## 安全边界

图业务和交互属于 C#；ImGui 只渲染有界复制展示并返回意图，不存图历史/图实例。
批准 graph UUID 不等于批准路径/目录或新增 clip。文件新建/覆盖与精确依赖各自审阅。
新建图的 UUID 由本机或稳定请求提供，dry-run 不随机重新生成元素 UUID。
只有 trusted startup 注册语义能力，不能执行表达式、加载程序集/任意 Python 或读 live GPU。
内置推理未接入时仍如实显示未接入；外部 MCP 客户端不依赖内置模型服务。
既有人工、第三方可见客户端、用户 FBX、目标环境/预算和1小时长稳继续待验收。

状态：上述功能已接入自动候选，完整回归/提交状态见 [交付记录](M6_4_DELIVERY_REPORT.md)。
操作入口与两个独立批准见 [使用说明](M6_4_EDITOR_GUIDE.md)。本请求不自动开始M6.5。
