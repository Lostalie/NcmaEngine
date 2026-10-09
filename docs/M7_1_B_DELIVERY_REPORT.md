# M7.1-B 交付记录

状态：M7.1-B自动候选闭环；最终41项真实DX11定向集、原生边界测试及完整顺序无Skip Debug/Release、checked deployment通过。
前置A `87549d09468b8a64e8119f7300949fbf47d5e3d9` 已提交推送并核对remote main后才开始B。
边界见[运行契约](M7_1_B_RUNTIME_CONTRACT.md)。B不安装新GPU shader，C及M7.2–M7.7未完成。

## 定向与保留失败

- native first：IID反射常量链接失败，改为SDK `__uuidof`，不添加兼容stub；second–fifth构建通过。
- managed初次fixed buffer引用错误、后续IReadOnlyList调用错误均修复，之后构建零warnings/errors。
- target first 33项通过；second–fourth的大诊断断言失败，实际308／646字节不能声称超过16KiB。
  保留原日志，分别测实际长度脱敏标志和native合成16384/16385阈值，不扩大预算。
- target fifth真实Texture数组反射名为`Textures[i]`，被名称校验拒绝；严格合并实际连续slot/index后修复。
- `out/m7-1-b-target-sixth.log`：40项通过，真实VS/PS/CS/reflection/stride/cache，既有参考frame1/API0/0。
- 原生 `NcmaRendererNativeTests` 定向通过，保留全部原故障／GPU资源测试。

所有first及中间失败日志/backup/SDK/userdata和无关IDE设置保留。没有调低生产警告规则、放宽反射匹配或删除原M6断言。

## 最终门禁

补充末成员原子拒绝与宏排序后冻结源码，再无Skip顺序Debug/Release，均退出0。

| 配置 | 完整日志 | native CTest | managed CTest | Python |
| --- | --- | --- | --- | --- |
| Debug | `out/m7-1-b-full-debug-first.log` | 12/12，2.97s | 22/22，317.07s | 43/43 |
| Release | `out/m7-1-b-full-release-first.log` | 12/12，1.32s | 22/22，284.03s | 43/43 |

两配置LastTest.log均记录A70、B41项；B实际编译8次/cache hit3次，既有reference frame1，API0/0。
结果位于 `out/verification/m2/render-Debug/shader-compile-results.json` 和 `render-Release/shader-compile-results.json`。
原生末成员不支持的完整输出原子拒绝通过；现有Renderer ABI／query1–7和GPU测试保留。
原有Animation115、Editor123、Player、实际NCA/Jolt/skin-shadow、M6.10联合坏包24拒绝/128cycles/32Reload/32post-solver fault、
Editor0/1/8/32及16搬移Player、smokes、new/removed格式拒绝、Python inspect和三轮成本记录通过。
新代码构建零warnings/errors，原生产shader路径未替换；成本记录是证据而非目标性能验收。

最终post-audit：Debug `out/verification/m2-8/Debug/2382f8ebdf7d4fd797343464720cf011/audit.json`；
Release `out/verification/m2-8/Release/03f1afc921d249e99d383dd35d354bd8/audit.json`。
均audit_passed=true、h8_accepted=false，保留原pending gates。

## 部署与源码冻结

两轮正式checked staging均核验101个manifest文件hash/大小/路径，实际104项含manifest及允许本地数据。
最终Release `out/bin/NcmaEngine.exe`，journal phase=Complete，generation=`ac9eb8f61bd6429aaa2055659600a75e`。
Debug backup=`out/deployment/2d75c24937f743518afb5250827a498e/backup`，Release backup=`out/deployment/ac9eb8f61bd6429aaa2055659600a75e/backup`，
均104文件、存在且无reparse，未删除用户日志／设置。

最终EXE SHA256 `66C2D9FC402AF0150EF027B4C52D76D4308D72C9CBDFF45EBB6F1232B9D1BEF4`；
`plugins/NcmaRenderer.dll` `57EA1177929B72C53E2B533DDBA4696420AC9BC099DC27416A0216279E9B83B4`；
`Ncma.Rendering.dll` `48F398E57C56BFEB2B8315C9214821C33204A8C897F76CAC39A3001AA36D39E3`。

Debug开始时记录、Release前及最终核对下列7项hash一致，之后只补文档：

| 源 | SHA256 |
| --- | --- |
| `engine/source/plugins/contracts/NcmaShader.h` | `FC3AB81F6859E7A79512BBD75BACDAD460827FA6819FF329D74C7D6F3387214B` |
| `engine/source/plugins/renderer/ShaderServices.inl` | `22EC63D796EC87B0EC32F01E55BE8BA59433D43B15F73EC601812F2F91A73984` |
| `engine/source/plugins/renderer/RendererPlugin.cpp` | `3FBCA2384037D0618D90E4DDB6AF3251A4610D900A054CBA16F3C3676953153B` |
| `managed/Ncma.Rendering/ShaderCompilation.cs` | `C20D8005289E3B83602A62FCA1CC82BB74EFD47A36B31C3613467BFE0BE59C32` |
| `managed/Ncma.Rendering.Tests/ShaderCompileTests.cs` | `BEDB6C8CA7E03CE2E0A8E206E1ED1A5B413C2D796E508AD29E3754B6D7F54593` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `41B867FF836C434840D62B82FFA88DE87ED52D851BE6E2EAEED8C6124D21603D` |
| `tests/plugins/RendererNativeTests.cpp` | `AD375048890A458CD71A219B732FA4D8285FFB9818A1C5FB5198E017BBBC9AA6` |

提交推送状态以Git回执/remote SHA核验为准；本片完成后才进入C，不将复制bytecode冒称可发布GPU管线。
人工可见UI/第三方MCP、用户FBX/材质、目标预算、自包含与1h等既有门禁仍开放。
