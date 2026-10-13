# M7.3-C3-A 默认环境与部署合同

2026-10-14。C3-A 实现构建期默认资源与严格只读加载，最终双配置门禁见交付记录；配置面板/项目发布命令属 C3-B，完整产品联合验收属 C3-C。

## 可复现预设

`Ncma.Shader.Cook` 生成 `neutral-synthetic-v1`，明确标注 synthetic。源使用固定 UUID、16×8 线性 RGB 0.18、alpha1；环境固定独立 UUID、generation1、原 `EnvironmentCookSettings.Default`（cube16 / irradiance8 / LUT32 / samples512）和现有 numerical Cook。不解码 HDR/EXR、不增加原生 ABI 或修改 shader 算法。

产物为 `assets/environments/neutral-v1.nce` 与 `assets/environments/defaults.json`。索引 v1 闭合8字段，最大8192字节：version、preset、synthetic、assetId、generation、path、byteCount、sha256。路径/预设名/UUID/代次固定，SHA必须大写64位；NCE最大4MiB，继续执行完整原数值格式校验。实际 NCE 的源 UUID/hash/dimensions/Cook settings 必须与 neutral recipe 一致。哈希与元数据不是 Cook 来源证明或签名；CPU `GpuValidated=false`。

## 运行期读边界

`EnvironmentPresetLibrary.Load` 要求调用者提供独立索引 SHA；`LoadDeployment` 从锁定 manifest 中核对 schema1、8192文件上限、重复字段/路径、索引与NCE独立路径/大小/SHA。读取时 pin manifest、索引、NCE及父目录；拒绝 hardlink/reparse、非规范路径和占用写入。owner-thread，取消和任何失败释放全部租约；Dispose 后不再访问 library，已获取的不可变 CPU package 可以继续使用。

服务只读且与 Renderer/World/Editor 无依赖，不发布文件，不挂组件，不授权 Agent，不创建 GPU 资源。实际 GPU shader 和环境准入继续使用现有服务。C3-A 不给已有用户场景隐式安装环境；项目配置/可撤销预设发布将在 C3-B 接入。

## 产品闭包

| 产品 | Shader | 环境 |
| --- | --- | --- |
| Editor | v2，Flat2D 1 + Scene3D 4 + SceneEnvironment 4 + index | NCE + 独立index |
| DX11 Player | v2，Scene3D 4 + SceneEnvironment 4 + index | NCE + 独立index |
| 独立UI | 保留v1，Flat2D 1 + index | 无 |
| Null Player | 无 | 无 |

各3D profile阴影变体完整，共享 skin 字节一致，整组实际反射准入后发布。原五个 shader 包 SHA 不变；Editor/Player 的 defaults.json 显式升级v2，不能再声明旧index SHA不变。Package-M2_7 使用确切两个环境文件名加入 manifest；既有原子部署/备份/journal/恢复保留。Cooker不部署到Player。独立UI完整包仍由原18文件验收保证。

## 自动检查

83 项覆盖独立双产品 Cook 字节一致、原五 shader 冻结哈希、UI/Null 文件隔离、预设字段/大小/版本/未知字段/重复字段、manifest独立hash/大小/缺项/重复文件、腐坏NCE、结构可读但源身份错误的伪预设、读锁/owner/取消/失败清理/12次重开、hardlink与junction、受检产品manifest、实际GPU绑定及场景独立软件 IBL oracle（定向最大像素误差0/API0/0）。

原M6搬移Player测试只把精确shader闭包断言从4+index升级到8+index，其原输入、图像/数值/关停/无Editor依赖断言保留。C3-A不等于C3整体或M7.3完成。人工UI/DPI/MCP、用户HDR-FBX-material、目标机/自包含、完整性能/1h门禁仍开放；仅DX11，Vulkan/OpenGL下一版本。
