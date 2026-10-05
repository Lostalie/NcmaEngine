# M3.3-B/C/D 交付记录

日期：2026-10-05。A checkpoint e745f9f80f0ffa371d215711a6e840253bd0a633 已推送。
最新用户授权：B/C/D 合并完成，测试失败补齐并复测，最终顺序完整 Debug/Release 通过后统一提交推送 M3.3。
实现已完成，最终顺序完整 Debug/Release 回归均 exit 0，G3 资源绘制门禁关闭；不是完整场景后端验收。提交与远端身份在交付时核对。

## 已实现

- B：工具侧 Windows WIC memory PNG/JPEG解码、格式/尺寸/字节/颜色profile/orientation检查；C#owned TXR1完整 mip、sRGB/linear/normal语义、Y方向、odd-edge/alpha-aware/normalized mip。RHI immutable initial mip上传。
- C：严格 JSON1 MaterialDefinition/MaterialSet；UUID引用、base/metal/rough/normal/AO/emissive、数据通道、Opaque/AlphaMask。scene-render独立v3 纹理/材质/离屏target和最小GGX PBR。来源槽only→显式损失/default转换；作者覆盖与immutable MAT1分离。
- C编辑：MaterialCommands 使用唯一 EditSession可逆命令，精确 path→UUID 和依赖类型grant、monotonic asset revision、文件hash冲突检查、同目录恢复journal；默认拒绝，Undo/Redo/重放重查授权。项目启动恢复与关闭撤销grant。
- D：C# UUID/asset generation/SHA/type/variant cache与lease，静态/显式绑定几何、纹理和材质依赖pin，Play/history可持旧版本，新版不原地替换。预算预检、最后材质先释放再释放texture、active/failed/device generation安全。
- D扩展：公共 Graph/ResourceOutput/ResourceGeometry capability契约，预设/参数/可信Feature/Stage/整个管线替换同路；目前一个typed GPU stage，不承诺通用多阶段/shader注入。
- v1/v2 与旧 Renderer1.0/1.1/reference shader和帧语义保持不变；v3 通过现有1.2 query独立协商72-byte表，不加 native World或高层作者策略。

完整布局/预算/线程/关闭规则见 [渲染ABI](M3_3_RENDER_ABI.md)。

## 验证范围

自动专项涵盖：严格codec/布局/版本/短buffer/格式/stride/mip/预算/NaN/奇异model/错误inverse-transpose/foreign与过期key；
PNG RGBA顺序/top-left/alpha、JPEG、取消/owner-thread/解码炸弹预算；sRGB与线性mip、odd-edge、normal normalized、alpha-aware mip。
实际DX11 quad的逐像素UV/sRGB oracle、GPU mip minification、AlphaMask、TBN sign/Y翻转、非均匀model法线；
AO通道和独立CPU ACES/sRGB oracle、metallic/roughness实际可见差异、emissive数值；公共Feature/Stage/pipeline替换真正改变像素。
材质槽batch、具名missing/unsafe fallback、UUID/不可变代际hash/cache复用与预算、绑定姿态cache、resize/swapchain/离屏与完整资源drain。
8轮GPU创建/释放，每轮64测量稳定帧，共512帧，不重复上传；统计只指提交线程，不宣称进程/native/driver零分配或通用FPS。
原生resident极限与device故障为白盒状态注入，不宣称真实驱动reset或256MiB压力验收。
同一shader公共GPU数值验证不是独立完整PBR oracle；上述AO/颜色/normal解析oracle及M2冻结参考均保留。

已修复中间失败：首个GPU提交缺少RHI要求的同槽sampler；修复六槽binding，无放宽验证。
恢复测试发现journal仅接受.ncmeta；仅新增明确.ncmaterial/.ncmatset白名单，并保留原版本/身份/范围/hash约束。
一个测试误将“准备读取当前文件”当冲突点，改为prepare后外部修改再validate；未改变冲突策略。
绑定cache初稿引用不存在的SourceSkeleton；改为hash实际保留的sourceMesh/palette/vertices/indices，未伪造rig身份验证。

完整Debug回归中既有Player生命周期用例两次在SceneDocumentFiles.Save的File.Replace遇到Windows“无法删除要被替换的文件”；单独17项及中间完整复测通过，没有足够证据把根因认定为特定外部进程。复查其跨用例反复覆盖同一场景fixture，改为每个运行场景独立不可变fixture，所有Player启动/关闭故障与包验证断言保留，生产SceneDocumentFiles不改；原子替换/锁定保存/失败保全仍由SceneDocumentTests验证。保留失败日志并重新完整回归。此测试对象是现役C# Player，不是旧C++入口；源码/构建/打包审查未找到另一个旧版Player，用户已明确保留现役C#Player。后续明确已废弃入口/类型先移除、不补兼容，不能把仍工作的Player测试删除当测试通过。

最终审查另补 cache entry/外部lease/dictionary 在native发布前分配，并拒绝缓存中相同UUID/generation变更hash，材质dependency数组也先分配再发布。以上最终代码再做完整顺序Debug/Release复验，最终日志以Debug-BCD-closure.log与Release-BCD-closure.log为准。

证据位于 ignored out/verification/m3-3/Debug-BCD-closure.log 和 Release-BCD-closure.log；
专项图像和结果产生于 out/verification/m2/render-{Debug,Release}/resource-*.bmp、resource-results.json。
最终保存独立副本与详细完整CTest日志，不用后续targeted日志覆盖作为唯一证明。

## 最终双配置门禁

Build.bat -Configuration Debug 后顺序 Build.bat -Configuration Release，无 Skip，两个最终 closure 日志均 exit 0。
每配置 CTest 原生10/10 + 托管/图形18/18，共28/28；Python42/42，Ncma.Managed、managed/native smoke、
python -m ncma_tools.cli inspect .、完整包/部署测试及可恢复部署通过。新 C++ /W4 /WX 和托管构建0 warnings/0 errors。
Assets38/38、Import24/24、Editor.Core36/36、Editor Services40/40、Player22、Gameplay53/53；
锁定/失败保存保全和严格新格式/旧格式拒绝仍通过，未删除现役Player或其断言。

两配置资源专项均8轮/294,912比较像素、最大颜色误差0，实际D3D11 Debug Layer errors/warnings=0/0；
512稳定帧提交线程托管分配0，无geometry/texture重复上传，最后texture/material/target resident/live=0，mesh/cache也归零。
每配置resource creates54、texture uploaded22,016 bytes（不含geometry/target/共享资源）；
一draw帧136+240=376 ABI bytes。本机512帧循环墙钟Debug47.1847ms/Release33.7895ms，包含Render/Present、VSync off；
最后有效timestamp样本Debug0.116224ms/Release0.104960ms，GPU sampleValid=1；submit样本0.043/0.0216ms。
这些是局部单quad证据，不是总体FPS或性能承诺。
旧M2冻结reference max/mean仍0，A的真实FBX静态/绑定姿态及GUI/自定义图/32 renderer周期仍通过。

最终独立JSON与完整managed CTest副本：out/verification/m3-3/{Debug,Release}-BCD-resource-results.json、
{Debug,Release}-BCD-managed-ctest.log。失败的final/verified日志保留，不当最终通过记录。
Release已部署out/bin/NcmaEngine.exe；NcmaRenderer.dll SHA256
2D3CE0F50219BFDB7FA298B9F64AA7B9FA15522D670224E6812D9B9DAB9189AF
与已测试Release构建一致，Ncma.Rendering.dll也核对一致，保留部署backup/journal。
仅提交源代码/测试/文档；排除out、bin/obj、部署备份和无关.vs/.user设置。
M2 audit_passed=true、h8_accepted=false，既有人工作业/目标环境/完整性能/长稳门禁仍未关闭。

## 不在本阶段声称完成

正式多对象Scene/Editor/Player GPU资源提取与相机/灯光/阴影管线属于M3.4；动画GPU蒙皮属于M3.5；
GUI离屏texture展示、用户导入/Inspector完整流程属于M3.6，端到端cook/自动依赖闭包属M3.9。
ImageDecoder是独立受限工具API，不自动加载FBX随意外部路径，不新增live World写入，不部署WIC/FBXparser进普通Player；
当前没有图像导入任务UI/automatic material texture source转换。G2没有Phong属性数据，因此明确fallback，不声称DCC等价。
Vulkan绘制、IBL、透明排序、完整材质节点、多阶段任意shader插件、动画切线/MikkTSpace仍未实现。
M2人工UI/MCP、自包含目标环境、完整性能与长期稳定验收不因此关闭；audit_passed不是h8_accepted。
