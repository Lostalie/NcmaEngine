# M7.4-D3-A：正式宿主的可信管线定制接入

2026-10-14。基线 `d2884bef9c477ca9ce57d5cdfb2b0b64f1752fb4`。这是 D3 的第一片；不关闭整个 D3、D 或 M7.4。

## 边界与执行

- `PlayerRunner.Run` 和编辑器 C# 启动宿主接受可选 `pipelineFactory`，默认 null 保持原默认管线。只有受信任的启动程序集提供委托，没有 CLI/项目文件中的程序集名、脚本、序列化委托、动态加载或新 Agent 权限。
- `ScenePipelineStartup` 将工厂及其返回对象的 `Build` 一起限制在 renderer owner、非重入、实际 GPU 离帧和精确 committed World 的只读范围。工厂不拥有 World；Undo/Restore 后旧绑定不可继续使用。审批在前后复查，期间禁止卸载 renderer。可信扩展不等于安全沙箱，也不承诺阻断全部外部 IO/副作用。
- 正式启动先执行现有文件/程序/环境准入，再对定制图的阴影开、关两种变体创建实际 16×16 受检绑定；应用现有 Tone/Feature 注册和后处理程序。必须有一个几何阶段、一个最终 Tone、匹配尺寸及匹配阴影变体。随后才加载/启动 gameplay。不是使用“小图能通过”替代真实视口验证。
- 正式视口仍经 `SceneRenderSession`、同一程序注册/环境/后处理路径。定制 plain 场景也在准备阶段建图；晚期失效由宿主清屏并报告失败，不能以旧画面冒充新状态。Player 故障后关闭；Editor 在显式修正/新修订后恢复。
- Null 不调用渲染委托、不加载图形资源；空场景也不因定制入口创建 3D 资源。底层既有公开 SceneRenderSession 使用方式保留。未添加 native ABI、Shader 算法、图形后端或 Python gameplay。

## 自动证据范围

专用 `NcmaRenderingTests.PipelineHosts` 保留旧测试断言，独立于原大型渲染用例的超时预算。

验证工厂及延迟 Build 的 World 写入/卸载/重入拒绝、线程、审批、活动帧、旧 World；原正式 Editor/Player 的默认、stage、独立完整 graph；静态模型 NCP + IBL + 注册 gain/Bloom；plain 无环境/后处理；原有界命令、默认只读权限、Undo/Redo、Play/Reload/Stop、真实 splitter resize、晚期失败清屏/恢复和关闭。

作者生成目录被 FileShare.None 锁定；运行项目只含受检 NCP/运行程序包和 gameplay 二进制。完整回归另搬移清单内 Editor/Player/Null 产品，在独立子进程中以严格 ALC 加载全部 Ncma 产品程序集并逐项验证路径/hash。**这是受检产品 API probe，不冒充未改 apphost 的定制启动**；原有 environment-products 测试继续运行真实 apphost。当前产品没有用于选择任意定制代码的命令行开关。

暖态记录实际宿主 CPU 时间/分配/资源预算，并断言无工厂回调、管线重建、不可变上传。GPU 时间是 last-valid，没有 origin-frame；不是逐帧独立样本。不承诺整个 Editor 零分配或完整性能验收。

## D3 后续未完成

1. 真实 FBX skin/shadow 与完整后处理、定制路径、显示域 UI 的搬移产品联合闭包。D2 的低层 UI 像素证据不是正式 Player UI 文档入口。
2. 先补齐当前缺少的正式 Player 场景 UI 文档加载/合成边界及受检闭包，再做 UI 缺失/错误资源 pre-gameplay 拒绝与默认/定制对照；不得只重复环境产品测试就关闭 D3。
3. 联合原命令/Undo/Reload/Play/resize/关闭、实际资源 hash、成本/预算证据及完整双配置验收。

人工 UI/DPI/第三方可见 MCP、用户 HDR/FBX/材质、HDR 解码、目标机/自包含、完整性能/1h、source-recovery 分配与间歇空 skin-cache/File.Replace 调查保留。DX11 only；Vulkan/OpenGL 下一版本；M7→M8→M9。
