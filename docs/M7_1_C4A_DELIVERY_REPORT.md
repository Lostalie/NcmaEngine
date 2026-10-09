# M7.1-C4-A 交付记录

状态：C4-A 自动候选完成，**不是整个C4完成**。无源码NCS1包/纯C#结构预检77项、六种实际编译产物、独立进程native-free检查及最终完整顺序无Skip Debug/Release通过。前置C3 `812ff35f4aaf04959bdf570853945c9d907081c0` 开工前已核对远端main。见[C4总方案](M7_1_C4_IMPLEMENTATION_PLAN.md)、[A契约](M7_1_C4A_RUNTIME_CONTRACT.md)。

## 交付与定向

- 独立NCS1 v1：闭合UI/Scene/shadow/skin角色集合，exact UUID/author hash/同源绑定契约hash/compiler47/flags/bytecode hash；最多8MiB、9条预算、1MiB程序。无HLSL/作者名/路径/Editor/运行句柄，不反向构造假源或ShaderDefinition。
- Cook只用不可变真实编译结果，继续验证C1–C3同源声明与B反射；UI/Skin布局提取成共享属性，不改变原描述/hash/ABI语义。原生插件源码及ABI未改。
- Preflight先取有界自有副本，再核对外部可信选择hash、payload/header/版本/backend/features、完整闭包/规范顺序/最后条目、布局/compiler/代码hash和SM5 DXBC结构与stage。debug/private chunk、重叠/间隙/尾随、巨量偏移/count/length拒绝。枚举10条即停止，不消费无限输入。
- 不变快照可以并发读；输入清空、返回bytes/code/page篡改不影响持有副本。没有文件IO/写盘/发现目录/新Agent或GPU执行授权。
- 六种真实产物：官方UI、可信用户UI、Scene shadow off/on × skin off/on；六个独立dotnet进程只收包和hash，确认native plugins/D3DCompiler/Editor未加载。无GPU资源/帧，compiler调用API0/0。
- 结构预检**不是GPU admission**，始终GpuValidated=false。错SV_TARGET1但容器有效的产物可被读成CPU包，这条边界有明确测试；C4-B仍必须完整实际native反射/链接/绑定验证，不能凭hash安装。未验证DXBC内部checksum或任意指令安全，不宣称签名/沙箱或来源真实性。
- 首轮75项通过；收紧五chunk完整集合后二轮75通过；增加输入所有权与并发读取后最终77项通过。日志 `out/m7-1-c4a-target-first.log`、`-second.log`、`-third.log`，对应managed构建三轮均0warnings/errors。本片无测试失败，所有原始日志保留。

## 最终全量回归

冻结五份实现/测试，顺序无Skip Build.bat：

| 轮次 | 原生 CTest | 托管 CTest | Python |
| --- | --- | --- | --- |
| Debug first | 12/12，4.37s | 22/22，329.66s | 43/43，1.062s |
| Release first | 12/12，2.55s | 22/22，298.13s | 43/43，.914s |

日志 `out/m7-1-c4a-full-debug-first.log`、`out/m7-1-c4a-full-release-first.log`。两轮包括A70/B41/C1 24/C2 40/C3 32/C4-A77、Animation115/Editor123/Player56以及全部原M6 FBX/NCA/Jolt/root/Notify/skin-shadow/Editor0-1-8-32/Player/MCP断言。真实UI示例及16文件source-free apphost仍通过。新/移除格式、managed/native smoke、Python inspect、三轮保留profile、前后audit均通过；不是新的性能或人工验收。

最终post-audit：

- Debug：`out/verification/m2-8/Debug/8234a781ebbe4cfba6bd62ef800a398f/audit.json`。
- Release：`out/verification/m2-8/Release/6e9e787e4fc048c480c2028cb571a49b/audit.json`。

均audit_passed=true/h8_accepted=false。部署manifest101项/实际104文件及Complete journal已独立核验；Debug generation `f304eceadf9144929bf245007818770f` 备份C3 Release，Release generation `11d226eff48b435b881058938621208e` 备份本轮Debug。两份104文件backup经过非reparse枚举校验，未删除日志/备份/SDK/用户数据/IDE。

| Release交付 | SHA-256 |
| --- | --- |
| `out/bin/NcmaEngine.exe` | `32DE3D47C07749C33115D52B862941ACC8B486826C617F8B34919E260EBF4400` |
| `out/bin/plugins/NcmaRenderer.dll` | `62BF119D2E7127A3BEFC6A2E4024C8EACAF29C6594DC8DA184BBBD6F10D07F3B` |
| `out/bin/Ncma.Rendering.dll` | `FD856AF2873437226AA80ED070A2D5357A47BCAFF0C671882BF2D726ABD690EF` |

## 源码冻结与后续

最终部署后再次核对五份实现/测试hash一致；之后仅更新文档/路线图/AGENTS。

| 文件 | SHA-256 |
| --- | --- |
| `managed/Ncma.Rendering/RuntimeShaderPackage.cs` | `E595A31CEF87123EAAD2E3F5FC4204E73CD8905D5782D4FFD519501A7671E659` |
| `managed/Ncma.Rendering/RegisteredUiShaders.cs` | `34E6676626C902D790B1FDC7240BFF5C4387AD194142A70CD47B1D374BA3064D` |
| `managed/Ncma.Rendering/RegisteredShaderStages.cs` | `E5B9A044F36A14CB6FAB5461C54F1F6AA9DFF76A024EDA2505756A6B364F751E` |
| `managed/Ncma.Rendering.Tests/ShaderPackageTests.cs` | `CB77F8F2F538173EF3575B1E335BF8427DBBF446FCF314B9EE53703C78417A3E` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `BA36A7BE74671028A7535345C9B579C9A75B86F73AC6EB07967F1E00EA8C5965` |

提交仅本片实现/测试/文档，排除生成包、日志、备份及四份原IDE改动；提交推送以本记录所在Git提交和独立remote main核验为准。下一片C4-B需要真正的source-free GPU admission和正式宿主准备生命周期；C4-C需要项目选择/文件pin/部署包/最终联合。当前包只是内存产物，未被正式Editor/Player消费，未添加shader项目字段或Agent能力。不能关闭整个C4、C或M7。人工/用户FBX/材料/目标/自包含/完整性能/1h保持开放，DX11-only和M7→M8→M9顺序不变。
