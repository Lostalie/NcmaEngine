# 动画 AI 控制：本地 MCP

## 当前 C# 动画图工具与独立预览的区别

正式编辑器 `ncma.animgraph.*` 为精确人工获批的读取/提案/事务/隔离序列，
不等于下文独立Python内置动作预览。M6.8-C2（完整双配置自动候选通过）扩展
`ncma.animgraph.sequence.propose/run`：闭合五字段 fixedDelta/steps/writes/assertions/montageRequests，
旧四字段拒绝。最多64条 Play/Cancel/Jump，由宿主生成隔离instance/session/world/tick，
不控制live角色、Movement、Jolt、Health、GPU或游戏回调。图读取批准不等于用例执行批准；
完整用例/实际NCA发布/资源/受众hash审阅、TTL60及每次含缓存读取的重检仍必需。
run增加 `montage`（Slot状态）和 `requests`（作者数组索引/结果）分页，各次最多8行，
`livePlay=false`、`collisionExecuted=false`。未接入模型推理或自动配置客户端。
准确字段/断言/边界与验证状态见[M6.8-C2契约](M6_8_C2_RUNTIME_CONTRACT.md)和[交付](M6_8_C2_DELIVERY_REPORT.md)。

## 独立 Python 内置动作预览

这是可运行的 stdio JSON-RPC MCP 服务，不只是能力清单。M2.5 已升级独立动画 C ABI 2：Python 工具管理协议、参数校验、独立预览时钟/策略/通知消费和有界历史，
`NcmaNative.dll` 仅提供不可变内置库与 pose/blend/root-motion/Notify 区间复制数值；旧 ABI 1 明确拒绝。

## 范围与权限

- 每个 MCP 子进程拥有一个**独立的内置动作预览会话**，不连接正在运行的编辑器。
- 不修改场景、文件或资产；无网络监听、shell、Python eval、删除工具或模型 API key。
- 默认只读。用户在启动参数加入 `--allow-mutations` 后，允许可撤销的预览修改。
- 工具修改通过 Python 自己的隔离预览命令域；与候选 C# ActionAnimationSession 有相同策略回归序列，共用原生数值内核。
  工具不连接活动场景，也不新增 Python gameplay 或 live World 权威。
- 根目录由启动配置固定，只从该目录下 `out/managed/NcmaNative.dll` 加载；拒绝解析到根目录外的库路径。
- 只支持有限、同步的调用。无后台训练、云上传、异步任务或无限运行指令。
- 状态不保存到磁盘；关闭 MCP 客户端连接/EOF 后释放原生会话。不要在持有 DLL 的 MCP 进程运行时完整重建引擎。

## 启动

先在项目根目录运行完整构建：

```bat
Build.bat -Configuration Debug
```

由 MCP 客户端启动服务，示例配置（按本机 Python 路径调整 `command`）：

```json
{
  "mcpServers": {
    "ncma-animation": {
      "command": "python",
      "args": ["-m", "ncma_tools.cli", "mcp", "--root", "F:\\NcmaEngine", "--allow-mutations"],
      "env": {"PYTHONPATH": "F:\\NcmaEngine\\python\\src"}
    }
  }
}
```

配置模板位于 `engine/config/animation-mcp.example.json`；它未被自动安装到任何 AI 客户端。
删除 `--allow-mutations` 即为只读模式。可以使用任意支持此 MCP 协议版本的客户端，模型供应商不进入引擎核心。

直接调试服务（等待标准输入 JSON-RPC，不会显示交互提示符）：

```powershell
$env:PYTHONPATH = 'F:\NcmaEngine\python\src'
python -m ncma_tools.cli mcp --root F:\NcmaEngine --allow-mutations
```

## 工具

全部以 `ncma.animation.` 开头。每个工具都有输入/输出 JSON Schema、MCP annotations、
`_meta["ncma/mutationRisk"]` 和作用域标签。

| 后缀 | 参数 | 风险 / 行为 |
|---|---|---|
| `inspect` | 无 | 只读：骨架、片段、姿势、状态、窗口、事件、根运动、历史 |
| `set_speed` | `speed`: 0..1 | 可撤销：移动参数和 Idle/Run 切换 |
| `trigger_action` | `action`: Attack / Dodge | 可撤销：动作切换；遵守连击窗口 |
| `set_paused` | `paused`: boolean | 可撤销：暂停标志；MCP 无后台时钟 |
| `step` | `seconds`: 0..1 | 可撤销：显式推进，暂停状态下也能单步 |
| `reset` | 无 | 可撤销：恢复初始预览，不改项目文件 |
| `undo` | 无 | 撤销上一个命令 |
| `redo` | 无 | 重做上一个撤销命令 |

所有修改工具可选 `expected_revision`：必须匹配最近 inspect 的 revision，不匹配返回
`revision_conflict` 且不修改状态。成功的修改（包括 undo/redo）单调增加 revision。
历史最多保留 128 个命令；inspect 不增加历史或 revision。

建议 AI 先 inspect，再 trigger_action，接着 step，并检查事件与 root_delta。
例如 Attack 后 step 0.2 秒，应看到 `Hit.Start`、`hit_window: true` 和向前根位移；undo 应完整恢复时间和位移。

成功输出：`{"ok":true,"snapshot":{...}}`。失败输出：
`{"ok":false,"error":{"code":"...","message":"..."}}`，并设置 `isError: true`。
JSON-RPC 格式、方法名或参数错误通过协议错误返回，不当作成功工具结果。
同样输入序列在相同构建的独立会话中产生同样结构化结果；不保证跨 CPU 浮点逐位一致。

## 协议与测试

支持 MCP `2025-11-25` 生命周期：`initialize` → `notifications/initialized` →
`tools/list` / `tools/call`，另支持 `ping`。不宣告 resources/prompts/sampling 能力。
标准输出仅写 UTF-8、换行分隔的 JSON-RPC，日志写 stderr；单条消息限制为 1 MiB。
未知协议版本返回本服务支持的版本，由客户端决定是否继续。

完整 `Build.bat` 会运行 C++ 动画测试、C# 动画 ABI 测试和 Python/MCP 测试，后者包含
真实服务子进程的 stdio 握手/调用，不依赖 LLM 或外网。这不是第三方客户端兼容性认证。

参考：[MCP stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)、
[生命周期](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle)、
[工具与结构化结果](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)。
