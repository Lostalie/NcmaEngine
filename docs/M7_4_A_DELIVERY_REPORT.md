# M7.4-A HDR Tone 预设交付记录

2026-10-14。开工前 local/remote main `c05eaaec6391dfd6f2b6b4c79ee0497dae99eb8f` 一致。见[分片方案](M7_4_IMPLEMENTATION_PLAN.md)、[运行合同](M7_4_A_RUNTIME_CONTRACT.md)。最终冻结源码的完整顺序无Skip Debug/Release、受检部署与实际apphost校验均通过；M7.4-A自动候选完成，人工/完整性能等门禁保留。

## 实现与范围

纯 C# 三种 Tone 声明预设 ACES fitted / Reinhard / Linear Clamp、有界 EV 转倍率、同一公开目录组合入口。Geometry/Shadow 和独立 Skin 保持原合同；native ABI/算法/默认 Shader、正式宿主默认选择、场景格式和权限没有改变。声明、编译、source-free 包和实际 GPU 准入分开，官方/用户无特权旁路。

输入 scene-linear RGBA16F、先曝光再 Tone、一次精确 sRGB 编码；实际 backbuffer 的游戏 UI 在 Tone 后合成。没有新增 Bloom、正式设置面板、HDR/EXR 解码、Agent Shader 执行或推理服务。M7.4-B/C/D仍待实施，M7.4整体未完成。

## 测试与失败记录

新增538项检查（不是538个独立测试程序）。其中420个profile/shadow/curve/强度/曝光组合使用独立double标量oracle、先binary16量化、逐像素RGBA比较，最大误差1个8bit色阶。线性0/.0001/.003/.18/1/4/16与.01/.015625/1/4/16倍曝光覆盖暗部、中灰、HDR、高亮饱和。

12种profile/shadow/curve组合无源码包重新准入等图、真实显示域UI叠加；用户BGR同一Compose/Prepare；坏输出/撤销/帧内/跨线程拒绝后原管线与图像不变，曝光失败保持旧plan。三尺寸resize/close、纯UI无3D安装与资源验证。原M6/M7、真实IBL/ASCII-Binary FBX/skin/shadow联合和正式产品测试保留。本片新Environment曲线为Off绑定；新增完整IBL/动画预设联合归D。

- 初次定向失败：夹具使用未支持的64阴影尺寸，修正为原合同支持的256，未改生产约束。
- 第二次定向失败：夹具误以为纯UI应拒绝CPU Shader编译。改为验证拒绝Scene安装且无3D资源；编译不等于GPU初始化。
- 第三次定向538项通过、最大误差1、DX11 API0/0。三轮编译0警告0错误；原日志/PNG在`out/m7-4-a-target-*`及`out/verification/m7-4-a/target-*`保留。

## 完整回归

冻结3份源码后顺序无Skip Build.bat：

| 配置 | Native CTest | Managed CTest | Python |
| --- | --- | --- | --- |
| Debug第一轮 | 12/12，3.76s | 22/22，480.21s | 43/43，1.182s |
| Release第一轮 | 12/12，1.93s | 22/22，355.11s | 43/43，0.880s |

Debug Rendering221.43s / Editor77.29s / Player67.55s；新增538项/最大误差1/API0/0。日志`out/m7-4-a-full-debug-first.log`与`out/m7-4-a-full-release-first.log`。原格式拒绝、MCP、smokes/inspect、三轮历史profile、审核和部署恢复均保留。

Debug部署generation `b835e388427d40efafd2cd932e585921`：113manifest /116实际 /116备份，Complete journal，逐路径/尺寸/SHA一致。实际`NcmaEngine.exe --validate-package`使用Start-Process -Wait核对exit0，3份冻结源码未变，原生Renderer与9个默认Shader包及索引和上一片同配置一致。post-deploy audit `4bde5918bb43422096a29db136d938fd` 为audit_passed=true、h8_accepted=false。

Release Rendering151.28s / Editor74.50s / Player66.14s；新增538项最大误差1/API0/0，原B3 353项及C3-C搬移产品490项完整保留。产品证据`out/verification/m2/render-Release/environment-products/9278c4ea6ba64be7a68601514aa545c8`。

Release部署generation `16edd29a5404424489a0168bfc865107`：113manifest /116实际 /116备份，Complete journal，逐路径/尺寸/SHA一致。实际apphost exit0；原生Renderer和9默认Shader包+索引与上片同配置完全一致。post-deploy audit `988d587c67c542cba16fe7c0e5569a43` 为audit_passed=true、h8_accepted=false。Release后再次验证Debug备份116文件及3份冻结源码SHA，均未变。

- Release `NcmaEngine.exe` SHA256：`DCBA9F67FB9E6F9123081F676916451E569744C4C7452B081995C68A45E17C64`
- `NcmaEngine.dll`：`0105D4DBFCE69ECDAE197621482F9936F59AD8C257BCA57FD9E3919ACBE4446F`
- `Ncma.Rendering.dll`：`26D5813115FE2F43CD68757D3A2111F333DBF18DAA7B343F99E82F5559644EB7`
- 原生Renderer：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`

## 成本与未关闭门禁

文档同步后inspect exit0、Python43/43（0.839s），冻结3份源码SHA再次一致；提交前远端main仍为开工基线。提交范围仅本片3份代码/测试与方案/合同/交付/路线图/AGENTS，不含生成部署、备份及4项既有IDE/.user变更。下一片M7.4-B必须在本片提交推送和远端SHA核对后开始。

6组暖机32帧后64帧静态提交零托管分配，无管线/网格/材质重建和不可变纹理/网格重传。Debug Scene3D三曲线耗时10.4959/10.2398/9.6195ms，Environment为10.8936/10.6852/10.0583ms；64帧常量上传51200B /53248B。这是含提交/Present的CPU墙钟总时长，不是GPU平均或目标性能验收。编译/cook/readback/oracle/配置不在静态采样中。

Release六组仍均0分配；Scene3D三曲线64帧为6.5144/5.8435/9.4973ms，Environment为10.5295/7.9426/8.3608ms，常量字节同Debug。采样说明和性能门禁不变。

完整Editor并非零分配；M7.3-C3-C源项目故障恢复约1.31MB/帧仍待性能调查。人工UI/DPI/第三方MCP、真实用户HDR/FBX/材质、HDR解码、目标/.NET自包含、完整性能/1h仍开放。DX11only，Vulkan/OpenGL下一版；M7→M8→M9。

## 冻结源码

| 源码 | SHA256 |
| --- | --- |
| managed/Ncma.Rendering/SceneTonePresets.cs | 4DA1905BF4F4979E6BF8BFEF7895CE480DE8D3070E5C3585A12EE71F5F5E80D7 |
| managed/Ncma.Rendering.Tests/TonePresetTests.cs | 7C7A17AB46FA2889EED9AC1FB7F5A9F8C8637C7708AF87B2A374D726AE39C6A4 |
| managed/Ncma.Rendering.Tests/Program.cs | 1F218C8C1D048B0E8A3F347C8193B0121AAA682EDECB4A160BBF26718A42BC28 |
