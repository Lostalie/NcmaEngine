# M7.1-C3 独立纯 2D 注册着色器方案

前置 C2 `0cf8e0533da36eeaba42c4b3301b1358f0b38f6b` 已推送，本片开始前独立核对远端 main 一致。本版本只有 DX11。

## 范围与实现

1. 提取现有 UiKernel 的同源 VS/PS；C# 的 Flat2D ShaderCatalog、ShaderCompilerService 同时服务官方和可信用户定义，不引用 Scene3D/Skinning 描述或资源。
2. 独立 query11/API1，保持 query1–10 不变。复制源、显式创建、显式替换；固定顶点52字节、VS Frame16、PS Image/Linear 以及严格签名/输出。全部资源/成员以实际 DXBC 反射核验，不信任用户自报布局。
3. 候选验证与分配、诊断和有界 GPU drain 全成功后一次发布；失败释放候选，保留旧着色器和所有资源。存在 presentation lease 时拒绝更换。缓存目标旧像素仍对应旧内容 revision，宿主显式刷新；generation 只在成功后增加。
4. 独立 UI 示例在事件循环之前可信准备、创建注册默认着色器。沿用已有按变化重绘和驻留列表。正式 Editor/Player 默认切换及 shader 运行包仍属于 C4。
5. 真实纯 2D 像素验证覆盖顺序、纹理、透明叠加、圆角和裁剪；保留所有原生、M6/M7 前置回归。原示例 smoke 保留，增加精确16文件、仅4原生 DLL 的 source-free apphost 副本运行与 hash 验证；不复制字体、Editor/Gui/Scene/Physics/Animation/Character/import 或用户资源。

## 验证顺序

先定向构建、真实 GPU/原生故障/独立 UI 示例与部署闭包测试；冻结源码后顺序执行完整无 Skip Build.bat Debug、Release。包含新/移除格式、Python inspect、profile、audit、checked deployment/hash/journal，失败保留并修复复测。成功后提交推送、核对 remote SHA，再进入 C4。

不能将 shader 反射称为安全沙箱，不能将此示例包称为 C4 持久化 shader 运行包。共享 Rendering/Assets/Runtime 程序集仍含通用契约/代码；本片验证无 3D 专用部署依赖和 GPU 初始化，不声称这些 DLL 已物理裁剪全部 3D 代码。人工、用户FBX、目标环境、自包含、完整性能及1小时门禁保持开放。
