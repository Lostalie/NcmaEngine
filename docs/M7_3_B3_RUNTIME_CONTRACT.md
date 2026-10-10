# M7.3-B3 IBL 图像验收合同

本片是 B2 独立环境 Scene 的自动验收补全；未增加 native ABI、生产 shader/环境算法、项目字段、Agent endpoint 或正式 Editor/Player 配置。正式宿主 IBL 接入属于 C；旧 ambient 不是 IBL。

## Oracle 分层

- `EnvironmentImageOracle.cs` 独立实现 cube face 地址/边缘重投影、bilinear/trilinear、NoV/roughness LUT、线性 split-sum、ACES/sRGB，不调用 production shader 或 Cook 实现。输入是 NCE1 的复制数值；它验证 GPU 消费，而不是独立验证生成这些数值的 Cook 算法。边缘重投影用于本片采样区域，不宣称通用 cube 三面角点精确模拟器。
- 常量环境用解析 E=piL/prefilter=L，BRDF 调用已有纯测试均匀半球 quadrature（256×512），与 native Hammersley/GGX importance sampling 独立；roughness .4/.7/1 图像允许 <=4 个8-bit阶，实际1。roughness .045 窄峰不以未收敛的 uniform quadrature作精确积分证据，仍用软件采样及原 A 数值/能量测试验证消费。
- 方向源 `L(d)=center+gradient*d.z`；Lambert E=pi(center+2gradient*n.z/3)，roughness=1、N=V=R prefilter=center+2gradient*r.z/3。实际中心像素、metal0/1、旋转0/pi/2/pi解析检查。split-sum仍是近似，不证明任意环境的完整微表面积分或多次散射。
- 软件像素 <=3阶、独立积分 <=4阶、LUT系数误差 <=.02 的门限在执行前固定；不在失败后扩大误差。本轮软件/积分最大1阶，LUT最大向量距离 .0005584955。

## 实际图像范围

353项：黑/常量/HDR4,2,.5/方向环境；48组metal0/.5/1和roughness .045/.4/.7/1；28组方向旋转/强度，零强度与Off全图相等、常量旋转全图相等；sRGB base、linear ORM AO0/64/255、法线/TBN手性/法线scale、自发光同图；倾斜法线覆盖±X/±Y加旋转±Z cube face，四种粗糙度。完整 source-free NCE1重解码和真实整组 Shader admission；用户环境贡献乘.5等于官方强度.5，用户 source-free NCS1重解码后全图相等。

实际静态不对称FBX -> NCA写入/重新读取/解码 -> GPU，解析投影三角形内部有独立IBL像素检查。ASCII sausage5个clips×4姿态、Binary4×4，共36个start/mid/inclusive-end/loop左界姿态；从重新读取的NCA得到mesh/rig/clip，数值Pose kernel负责TRS采样，独立CPU four-weight位置/逆转置法线/切线正交化烘焙静态mesh，与 registered compute skin + geometry + shadow + sRGB/ORM/material/IBL同场景整图比较。不是第二个动画采样器或原始DCC算法oracle；原ufbx数值/权重与M6图/Movement/Notify测试完整保留。CPU/GPU全部12个vertex标量检查，ASCII最大1.78814e-7/Binary1.43051e-6，全图最多1阶；去掉IBL和去掉casters的对照均有显著像素变化，不仅统计draw。

NCA写入/重读在同一测试进程，不称独立apphost重启验收。测试直接使用可信注册与独立Scene kernel，不把它称正式World/Editor/Player环境绑定，后者仍需C的资产闭包、permissions/Undo/transactions。

## 成本与生命周期

warm32后测量64次真实static submit/present；不包含oracle/readback/cook/重配置/临时batch。线程allocation0，环境upload/cook/readback0，mesh/material create/upload0；一次scene ABI/帧，copied33792B，constant53248B。Tone shader声明仍是旧C400，但该Scene共享C416 buffer，两次实际UpdateBuffer各416B，统计与真实上传一致。elapsed是本机该样本CPU墙钟（异步GPU/VSync off），不是完整性能或GPU时长门禁。

320×192显式target/scene重建与renderer resize，再恢复256；原环境资源保留且独立像素检查通过。解绑后generation2更新，8次scene创建/绑定/实际像素/关闭，退pin；最终所有环境Live/Resident=0。API错误/警告0/0。B1/B2原生诊断异常/timeout/原子失败和pin生命周期原测试不删。

## 未关闭门禁

C正式Editor/Player、项目/NCP环境闭包、可撤销配置及纯UI/Null部署排除仍待实现。无HDR/EXR文件解码器、用户HDR/FBX/材质人工验收、目标机/自包含/完整性能/1h通过声明。无推理/Python gameplay/代码沙箱/新增Agent执行权限/旧格式兼容；DX11 only，Vulkan/OpenGL下一版本。
