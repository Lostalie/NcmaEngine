# M3.3 网格纹理材质与渲染资源方案

日期：2026-10-05。状态：未实现。依赖 G2。目标是 DX11 原生资源执行能力，不在 native 内保存场景。
已有 reference ABI 1.1 与图像测试保留；新服务必须绘制实际导入数据而非再次画固定 cube。

## 1 契约与资源类型

建议 additive Renderer ABI 1.2 扩展查询独立 scene-render v1 服务表，最终版本在 G3 前锁定。
旧 FrameV1 112 bytes 和 reference shader contract 不变；新表有 struct_size/version/capabilities。
新增 mesh/texture/material/view-target 资源、固定布局描述与 caller-owned 初始数据。
返回 opaque handle + device generation；UUID→handle 解析只在 C# RuntimeAsset/RenderResourceCache。
resource create 先全校验再构建候选，失败释放候选；执行已开始后的 GPU/device 故障 fail-stop。

## 2 实施切片

### A 网格与 typed draw 数据

实现静态/skin vertex layout、32-bit indices、材质分段、bounds、格式/stride 与 byte/count 溢出检查。
model/view/projection 明确右手数学、column-major、D3D 深度与 viewport；不能借用旧 reference 的 LH 隐含相机。
资源归属检查覆盖跨 renderer、旧 generation、释放后句柄、缺失 buffers 和非法 submesh。
创建后顶点/索引驻留；每帧只提交 draw/resource references，不重新复制整个 mesh。
提前预留 skin palette 输入，G3 暂以 bind pose/static 路径绘制，GPU skinning 由 G5 验收。

### B 纹理解码与上传

离线工具解码 PNG/JPEG 为有界 RGBA 与 mip 数据，建议 Windows WIC 薄解码适配，实施前验证格式/预算。
平台头和 WIC 类型只在工具 native 模块内，不在 Scene/Gameplay 导出；不把解码插件放进常规 Player。
颜色纹理明确 sRGB，normal/metallic/roughness/AO 为 linear，记录 UV、normal Y 与通道映射。
拒绝解压炸弹、超尺寸/字节数、坏 stride、错误格式/重复 mip；缺纹理使用具名诊断 fallback。
外部/内嵌依赖不得绕过 G2 许可，禁止加载 FBX 随意引用的外部路径。
静态 GPU 资源、变化的帧 buffer 与测试专用 staging/readback 分离，避免生产帧同步读回。

### C 最小材质工作流

MaterialDefinition/MaterialSet 保存为 C# 资产，支持 baseColor/metallic/roughness/normal/AO 与 emissive 基础。
先交付 Opaque/AlphaMask；透明排序、完整材质节点、IBL/HDR 环境扩展留后续。
FBX Phong/非标准材质只按明确转换表映射并报告损失；不称任意 DCC 材质等价。
材质默认值、缺 UV、无切线/镜像 UV、通道反转有显式诊断，不能无提示应用不正确 normal map。
切线生成采用版本化确定算法；如未引入/验证 MikkTSpace，不能声称与 DCC 完全一致。
用户覆盖材质保存为独立作者资产，不修改由 FBX 重建的 immutable blobs。

### D GPU 生命周期与扩展

C# 拥有 lease/cache/预算和 typed batch，native 只编码执行；资产没有 COM 指针字段。
释放待 GPU 完成后回收，resize/device loss 明确状态，不强卸载仍有资源的 DLL。
扩展现有 C# public Graph/Feature/Stage 的 capability 验证，默认 3D 与用户管线使用同一契约。
纯 2D/Null 不创建 rig/shadow/PBR/reference 资源；M3 资源 API 不强制初始化三维管线。

## 3 测试与退出门禁 G3

- 非对称、带 UV/材质分段的实际 mesh/reference quad 绘制、颜色空间/通道/normal 验证。
- create 失败资源不发布；ABI布局/版本/短 buffer/foreign handle/大尺寸/溢出/未知格式负例。
- 连续创建释放、resize、缺纹理 fallback、budget 拒绝、device故障状态和退出资源计数。
- D3D11 Debug Layer 实际启用，固定图像与定义容差，API errors/warnings=0；capture 不替代验证。
- 稳定场景不重新上传顶点/纹理；记录 bytes/create count/CPU/GPU/分配，不承诺未经测量的 FPS。
- 保留旧 M2 冻结参考、公共图定制 tests、Null 无 3D 资源测试和全量双配置回归。

资源用途依据 [Microsoft D3D11_USAGE](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_usage)。
G3 通过只表示资源绘制切片完成；多对象场景由 G4、角色动画由 G5 接入，不称完整渲染后端已验收。
阶段测试后执行完整 Build.bat -Configuration Debug，再执行 Build.bat -Configuration Release，无 Skip。
