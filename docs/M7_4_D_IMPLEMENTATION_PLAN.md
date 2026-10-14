# M7.4-D 定制示例与产品联合验收

2026-10-14。开工本地/远端main均为 `d263f44c56b942b8fd6d24a8a32e2312fe5e726c`；上一轮C3为实质进展。4项无关IDE变更、失败现场和备份保留。

## 完整范围和顺序

状态：D1的98项实际检查及完整顺序Debug/Release首轮通过，见[示例](M7_4_D1_CUSTOMIZATION_EXAMPLES.md)、[交付](M7_4_D1_DELIVERY_REPORT.md)。D2联合图像582次检查/120图、独立后处理整图最大误差1/API0/0及完整顺序Debug/Release通过，见[D2合同](M7_4_D2_IMAGE_CONTRACT.md)、[D2交付](M7_4_D2_DELIVERY_REPORT.md)；D3-A正式宿主可信管线定制、599项源/搬移检查及完整顺序Debug/Release通过，见[D3-A合同](M7_4_D3A_RUNTIME_CONTRACT.md)、[D3-A交付](M7_4_D3A_DELIVERY_REPORT.md)；D3-B正式Player被动HUD/受检NCU1闭包、336项检查及完整顺序Debug/Release通过，见[D3-B合同](M7_4_D3B_RUNTIME_CONTRACT.md)、[D3-B交付](M7_4_D3B_DELIVERY_REPORT.md)；D3-C已完成Editor场景HUD、可信交互路由、作者不可变准备与同一历史的选择发布，以及完整FBX/IBL/阴影/Bloom/UI搬移联合；64份冻结源码完整顺序Debug/Release各12原生/39联合组/Python44通过，正式Release受检部署完成，见[D3-C交付](M7_4_D3C_DELIVERY_REPORT.md)。D3/D/M7.4自动候选链闭合；人工、用户素材、目标环境及完整性能门禁保留，下一阶段M7.5软阴影档位。

- D1：可编译公开C#示例：线性HDR Feature插入、几何stage替换、直接构建完整scene graph的pipeline替换（不是转发默认pipeline）。使用已有版本化图/程序/资源合同，不接触D3D、不添加新native ABI或远程代码加载。验证CPU-only、真实像素、独立标量/Half oracle、共享场景工厂与环境/后处理结合、失败原子性/owner/非重入、resize/关闭及暖态成本。
- D2：实际静态/ASCII和Binary FBX→NCA→动画skin/PBR/IBL/shadow/HDR/Bloom/显示域UI联合。复用已经验证的独立软件积分/材质/滤波数值参考，但以联合图像和消融证据证明组合，不用同Shader自证。包含默认与定制路径、曝光/颜色域、UI不重复Tone、奇数尺寸/资源代次、失败恢复和成本。
- D3：受检搬移Editor/Player/Null，作者源文件不可用，程序/环境/模型/UI闭包与错误闭包pre-gameplay拒绝；正式原命令/Undo/Reload/Play/resize/关闭，可信启动定制与默认同路。记录实际产品程序集/资源hash、CPU/GPU/分配/上传/资源预算，不把无origin-frame GPU时间写成独立逐帧数据。

每片定向测试与修复后冻结源码，顺序完整无Skip Debug/Release Build.bat、所有原测试/工具/三轮profile/audit/部署恢复，验证清单/备份/Complete journal/实际EXE，更新文档并提交推送核对远端后进入下一片。不能因D1示例通过就关闭D、M7.4或产品联合门禁。

## 边界

D3执行切片：A可信启动管线、B正式Player被动HUD、C正式Editor HUD/可信动作输入/作者发布与完整搬移联合均已通过自动门禁，见[D3-C合同](M7_4_D3C_RUNTIME_CONTRACT.md)、[交付](M7_4_D3C_DELIVERY_REPORT.md)。默认CLI仍不提供交互动作授权；交互UI必须有可信C#启动动作表，否则在gameplay前拒绝。Editor现支持经过闭包验证的场景UiPackage，而不是删除guard后直接放行。完整自动候选不等同人工OS输入和完整性能验收。

整pipeline替换指当前已支持的scene图与公开HDR子图组合，不等于任意未实现GPU操作；未知操作依旧拒绝。stage配置与Shader程序替换分层，程序仍需显式UUID/文件hash/完整反射/审批。示例是可信C#启动代码，不是代码沙箱，也不授权Agent编译/执行/直接World或GPU写入。编辑器/AI只处理已注册模块的有界元数据和原可撤销命令。

本版本DX11only；Vulkan/OpenGL下一版本。默认Off、纯UI无3D资源、Python非gameplay不变。保留人工UI/DPI/第三方可见MCP、用户HDR-FBX素材/HDR解码、目标机/自包含、完整性能/1h、source-recovery高分配调查，以及间歇File.Replace根因未确认。M7→M8→M9。
