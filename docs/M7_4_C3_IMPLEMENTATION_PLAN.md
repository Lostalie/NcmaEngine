# M7.4-C3 专用后处理编辑与 AI 合同

2026-10-14。C2 本地/远端 `1531253314ac3f8f1a22254a2c6b777e5b6d2cba` 开工核对一致。保留4项无关IDE修改。上一轮实质完成C2，本轮不缩减C3或D范围。

## 实施

1. 为可信启动注册模块提供有界、owned、CPU-only目录描述（名称、UUID/版本、Tone/Feature、参数范围/整数、先决项/合法顺序）。官方/用户用同一合同，不接受远程源码或回调。EditorSessionOwner 对原scene事务、草稿、恢复、Undo/Redo和Play副本执行同一注册语义检查；未知模块、超界参数和无3D的启用配置在提交前拒绝，不仅等渲染报错。
2. Inspector 增加可选Off配置、Tone选择、EV、Bloom及注册Feature参数/合法添加删除/顺序。复用GUI带戳事件、交互草稿和唯一历史。对照通过明确的启用/关闭事务及撤销恢复，不偷偷改GPU/World；展示实际提交后的world/revision/Play准备状态，草稿不能冒充已渲染。原通用组件JSON仍受同一语义门禁。
3. 独立只读 `ncma.postprocess.modules.list`，有界分页，无文件路径/源码/GPU句柄。默认不可见；本地人类审阅具体模块UUID和全部已配对客户端后限时授权，绑定session/generation/revision/endpoint/audience epoch，撤销/重配对/Play/过期失效。目录读取不授权原事务；AI仍需精确对象/组件/请求审批，使用现有scene.transaction及Undo/Redo，不新增编译/推理/插件加载权限。UI保持未接入推理服务真实标识。
4. 全部准备只在正式宿主离帧边界。C2共享Runtime、文件租约、GPU候选原子交换、失败清屏保留。默认Off/纯UI无3D不变；不改native ABI/NPH1/NCS1/Shader。C3不替代D真实FBX/IBL/shadow/Bloom/UI搬移产品联合和full-pipeline替换。

## 验证与交付

- 纯CPU目录owned副本、注册语义/原事务/原始JSON/错误版本/预算、草稿取消/单次撤销/重做、Play冻结/重入/错误对象、默认Off、不触碰文件或GPU。
- GUI事件驱动实际控件、官方及用户模块参数与顺序、过期/伪造事件、world/revision/Play状态戳、对照与恢复。
- 真实MCP stdio列目录默认拒绝、精确读取审阅、分页/输出schema、TTL/撤销/重配对/端点替换；真实事务默认拒绝、人类审批、精确scope、过期/撤销/Undo。不能用直接Invoke替代stdio联合。
- 正式Editor真实DX11图像：面板提交/Off对照/Undo/Redo、准备错误清屏/显式恢复、Play/Reload、用户模块；API0/0。保留全部C2与历史回归。
- 定向修复后冻结源码，顺序无Skip Debug/Release Build.bat；核对manifest/备份/journal/实际apphost、更新记录、提交推送并核对远端。失败现场保留。不得把首轮间歇File.Replace复测通过写成根因修复。

人工UI/DPI/第三方可见MCP、用户素材/HDR解码、目标机/自包含、完整性能/1h以及source-recovery分配调查均保留。只实现DX11；Vulkan/OpenGL下一版本；M7→M8→M9。
