# M7.3-C2-C 正式宿主接入实施方案

2026-10-10；开工基线 local/remote main `09476c8e809b29119319cb7c8c0b615c725208f2` 一致。C2-A/B已完成；本片不以删除 pending guards 或独立管线测试替代正式 Editor/Player。

1. 共用 SceneEnvironmentPreflight：完整 shader profile/shadow/skin 文件闭包先检查；在确切已提交 World 的 read-only 帧外审批窗口内检查资产代次、实际 shader 准入、GPU环境资源与小型实际场景绑定。保持租约到正式场景接手，避免重复上传；没有文件读/绘制/动画复制/Agent权限。空/Off/Headless不引入3D环境资源。
2. Player在Gameplay加载与初始化前准备，正式BindScene消费同一共享SceneRenderSession；运行中在fixed-step结束后检查环境值并显式Refresh/PrepareView。未准备动态代次/缺包/损坏/错误实际反射明确失败，不静默忽略，也不在simulation中IO。
3. Editor启动和Play composition前同样检查；命令/Undo/Redo沿用唯一EditSession，安全边界同步精确资产。创建/刷新/view准备失败保留最后有效资源、报告、禁用过期World picking/提交；无重试风暴。Play/Stop/Reload与原CPU/GPU租约释放次序一致。UI/graph独立工作区不强制环境阶段。
4. 只有实现后替换C1 pending guards测试：保留并增强缺包/损坏/实际准入失败的pre-gameplay拒绝，补正式Player与Editor实际帧图像、v2用户包、静态与FBX/NCA、动态配置/Undo/Play/Reload/关闭/空与Headless闭包。保留所有原M6/M7/MCP/格式断言。
5. 定向测试修复→冻结源码→顺序无Skip Debug/Release Build.bat→manifest/SHA/Complete journal/备份/apphost检查→合同/交付/路线图/AGENTS→提交推送并核对远端，再进入C3。

2026-10-13：本片43项正式宿主检查、最终顺序无Skip Debug第二轮/Release第一轮、checked部署/哈希/备份均通过，见[交付记录](M7_3_C2C_DELIVERY_REPORT.md)。C2自动链完成。C3默认合成环境部署/面板独立待实现；HDR/EXR解码、人工/用户素材/目标/自包含/完整性能/1h保持开放。DX11only；Vulkan/OpenGL下一版本，M7→M8→M9。
