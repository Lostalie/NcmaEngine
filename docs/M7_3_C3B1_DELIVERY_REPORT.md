# M7.3-C3-B1 交付记录

2026-10-14。开工核对 local/remote main 为 `8c20a4794af5fd4b6dfe00458a758b2716d0703e`。C3-B1 自动候选完成；C3-B2 预设发布/独立目录授权及 C3-C 联合验收未完成。详见[合同](M7_3_C3B1_RUNTIME_CONTRACT.md)和[分片方案](M7_3_C3_IMPLEMENTATION_PLAN.md)。

## 实现

环境 Inspector 显式添加 Off、现有绑定强度/Y 角度/关闭、UUID/generation/SHA 展示；纯托管 helper 生成原 set_component，沿用唯一草稿/撤销事务与 Agent 精确权限。Off 严格清除身份，不能从隐藏历史自动重新启用；当前 UI 明示预设发布尚未接入。组件存在性索引按文档版本缓存。

正式宿主复制 World/revision/Play 绑定的准备状态；旧状态不冒充新文档就绪，失败提示和清空视口一致，恢复后清除错误。沒有新权限、文件 IO、GPU 编译入口、原生 ABI、Shader、Cook、格式或默认预设变更。未声称完成整套预设制作流程。

## 测试

新增8组 Inspector 回归：草稿/提交/Undo/Redo与角度转换；取消/越界/非有限/伪事件/过期事件；严格Off/拒绝隐式启用；单配置/显式添加；Agent精确scope/会话/版本/有效性撤销与历史；保存重开/Play冻结；状态身份/版本/Play/恢复；未变文档缓存。原124组保留，Editor services **132/132**。

原C2-C正式宿主43项保留并扩展至**51项**：实际GUI事件改变环境强度、独立软件IBL像素、Undo恢复原图、实际坏代次清空视口与Inspector失败提示、修复后恢复。最大像素误差0，DX11 API errors/warnings均0。测试仅是自动输入与像素验证，不代替人工UI/DPI验收。

初次托管编译与定向8组、51项均通过。最终缓存优化后冻结9份源码，完整无Skip顺序Debug/Release再次执行全部测试，无新增编译警告。未为通过测试弱化旧断言。

| Build.bat | Native CTest | Managed CTest | Python |
| --- | --- | --- | --- |
| Debug第一轮 | 12/12，3.76s | 22/22，437.86s | 43/43，1.081s |
| Release第一轮 | 12/12，1.90s | 22/22，318.90s | 43/43，0.889s |

Rendering181.80s/121.17s、Editor75.29s/70.77s、Player67.45s/66.11s。原M6/M7、B3 353、C2-B87、C3-A83、FBX/NCA/Jolt、MCP/格式、managed/native smokes、inspect、三轮历史profile、部署恢复门禁均保留通过。源码冻结SHA在两次完整运行之后一致。

日志：`out/m7-3-c3b1-compile-first.log`、`compile-second.log`、`inspector-first.log`、`render-compile-first.log`、`host-first.log`（后四项均同前缀m7-3-c3b1-）；完整日志 `out/m7-3-c3b1-full-debug-first.log` 与 `out/m7-3-c3b1-full-release-first.log`。正式图像与host-results位于 `out/verification/m2/render-{Debug,Release}/environment-hosts/` 相应生成目录。

## 部署与留存

Debug generation `a271ecdb8d2a4b2b8697079e4f57ba2e`；Release `5a9099bd91124b6f8db0d88f52b98baa`。二者113 manifest / 116实际文件，Complete journal，各116文件备份逐路径/size/SHA核验；Release后再次核对Debug备份。两次实际apphost --validate-package均返回0，PowerShell使用Start-Process -Wait读取实际GUI进程退出码。

最终Release：
- NcmaEngine.exe：`3CA15C7EBA48177A5145960AC4FC84320D2F0A84079C6E62124AB1908C86E9F9`
- NcmaEngine.dll：`EC8BDB9899BBAC084171E40B36AA042FB25EBE1EF65600C58B2743932F8CCA18`
- NcmaRenderer.dll：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`（与C3-A不变）
- NCE：`96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665`（不变）
- 9个Shader包及selection索引共10文件SHA均不变。

post-deploy audit：Debug `151b781cd4664a7f8727e0357b0c158c`，Release `5012b0239d034c169db035fe1d0e2846`；audit_passed=true，h8_accepted=false。人工UI/DPI/第三方MCP、真实用户HDR-FBX-material、目标/自包含、完整性能/1h门禁保持开放。

| 冻结源码 | SHA256 |
| --- | --- |
| managed/Ncma.Editor.App/EditorPresenter.cs | 71177569E4F26918B7A8180DA979EE9EB75C980266CEE8794A87C291DFB732D3 |
| managed/Ncma.Editor.App/EditorPresenter.Environment.cs | 0872F20855E0B694116D86C79E85FDA9AE25A714939198D0CBF2DD6263322619 |
| managed/Ncma.Editor.App/EditorSceneEnvironment.cs | EF4D6F7D3481DE17B82419C13EE9309ACBAFA933E9126BB6637D12DB34DB20D2 |
| managed/Ncma.Editor.App/Program.cs | 41B166F82F46290EFC8F85D3C7CF02D901F06AB51314ABB0DD5D2730AEC8AF49 |
| managed/Ncma.Editor.Services/EditorWorkspace.cs | F8589A8FE2C1524483E23DA6F30B4E22AD699A1FD81C239884CB38270A30AEA9 |
| managed/Ncma.Editor.Services/RenderingEditorAdapter.cs | 70082D5708B53778DA422F3A27D75956F69C77B5A2F9E3317C1B95C72D4FDAB7 |
| managed/Ncma.Editor.Services.Tests/EnvironmentInspectorTests.cs | D3DD5A10E7280C3F852A521A75678B82D0DC8A95067F2271628C203BD062F76A |
| managed/Ncma.Editor.Services.Tests/Program.cs | 1DE7F3C07E3A710B3AE5847BF838B343AD2055936EE6DE4ABB4B7FEE9FB25EAE |
| managed/Ncma.Rendering.Tests/EnvironmentHostTests.cs | A95CEF8100950320529A0F22A3748BBFEEB1E7CFFC57F9422F5C7C8EAFB218DC |

提交排除生成产物、备份与4项既有IDE/.user修改。提交推送并核对远端后进入C3-B2；不宽化read pin或磁盘事务来跳过可逆发布协调。整个C3-B/C/M7.3仍未完成；DX11only，Vulkan/OpenGL下一版本；M7 → M8 → M9。
