# M7.1-A 交付记录

状态：M7.1-A 自动候选闭环，70项纯托管定向及最终完整顺序 Debug/Release、checked deployment 已通过。
M7.1-B/C 和整体 M7 尚未完成；提交推送状态以 Git 回执/远端 SHA 核验为准。

方案入口 `09e0608` 已推送、远端核验。实现边界见 [契约](M7_1_A_RUNTIME_CONTRACT.md)。
没有 native ABI、实际 Shader compile/reflection、GPU 管线替换、MCP 新权限、推理或 Python gameplay。
源代码保存和 publishing 未接入；已有生产 Shader、Renderer/Player/动画路径保持不变。

## 定向结果

- `out/m7-1-a-target-build-first.log`：Debug build，0 warnings/errors；首轮62项通过。
- `out/m7-1-a-target-tests-first.log`：62项；之后审查补齐枚举精确拼写、非法名称 Unicode 和临界预算。
- `out/m7-1-a-target-build-second.log`：Debug build，0 warnings/errors。
- `out/m7-1-a-target-tests-second.log`：70项通过，不执行 GPU/反射/MCP/发布。

## 最终门禁

源码冻结后先无Skip `Build.bat -Configuration Debug`，退出0；随后 Release，退出0。

| 配置 | 最终完整日志 | native CTest | managed CTest | Python |
| --- | --- | --- | --- | --- |
| Debug | `out/m7-1-a-full-debug-first.log` | 12/12，3.15s | 22/22，317.27s | 43/43 |
| Release | `out/m7-1-a-full-release-first.log` | 12/12，1.28s | 22/22，281.72s | 43/43 |

两配置 `Testing/Temporary/LastTest.log` 均明确记录 M7.1-A 70项、compiled=false；
保留实际参考图/API0/0、M6.10联合NCP1/12坏包/24初始化前拒绝/完整调度轨迹/128cycles、
Editor0/1/8/32与16搬移 source-free Player、smokes、严格格式与removed拒绝、Python inspect均通过。
保留的三轮成本观测只作 evidence，未豁免目标性能门禁。

最终 post-audit：

- Debug：`out/verification/m2-8/Debug/f1bd196c9876415ebf953e49980ab13c/audit.json`。
- Release：`out/verification/m2-8/Release/38b9d5aacd6f45e0b0b214e40dd0ee6d/audit.json`。

二者 audit_passed=true / h8_accepted=false，原 pending gates 保留。
新 C# 构建零 warnings/errors，没有 native ABI 或生产 shader 变化。

## 部署与源码冻结

两配置均使用正式 checked staging；最终 Release 位于 `out/bin/NcmaEngine.exe`。
`Assert-DeploymentContents` 核验 manifest 101个文件 hash/大小/路径；实际104项还包含manifest和允许的本地数据，
未删除用户设置/日志。`out/deployment/editor-journal.json` phase=Complete，generation=`dce0b6975b314ee4989c1228bc1da50c`。
Debug backup=`out/deployment/6f6246052a70472082adb579947f4b85/backup`（保留原Release）；
Release backup=`out/deployment/dce0b6975b314ee4989c1228bc1da50c/backup`（保留本轮Debug），均已检查存在且无reparse。
最终 EXE SHA256：`B8B03A5EDC30D817775FB38CE89DBE21AB9CBA1611D1C7C92420978530C58F88`。

完整 Debug 前冻结、Release前及最终再次核对下列源hash一致；之后仅补交付文档：

| 源 | SHA256 |
| --- | --- |
| `managed/Ncma.Rendering/ShaderContracts.cs` | `475F9DC3B84B5E8E6C9D374194C0CAC95722C98D840AE63B8D328ECFE9A6A618` |
| `managed/Ncma.Rendering.Tests/ShaderContractTests.cs` | `0155752558126C2ADADD0AD2CFA911CDBC6BFC8C65A0F17CC9FF957E4C287CF9` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `38D9F38162C8EC33D80ABD13493F9544F7F35ABBA437105ED7B96847717BCDFF` |

## 下一步与未关闭门禁

提交推送并核对 remote main SHA 后才进入 B 的运行实现。
人工可见 UI/第三方 MCP、真实用户 FBX/材质、目标机器预算、自包含环境、1h 等既有门禁仍开放。
保留所有旧失败/backup/SDK/userdata/无关 .vs/.user 改动，不清除干扰或替测试免责。
