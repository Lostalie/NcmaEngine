# M7.3-B3 完整 IBL 自动图像验收方案

2026-10-10；开工前本地与远端 main 同为 B2 `4e1b4a597e7a3cee64b24ff4429339a207707b7c`。本片补齐 B 的实际图像证据，不提前实现 C 的正式宿主/资产/可撤销配置。

353项定向及最终完整顺序Debug/Release/checked部署通过，B自动候选链完成，详见[验收合同](M7_3_B3_RUNTIME_CONTRACT.md)、[交付记录](M7_3_B3_DELIVERY_REPORT.md)。提交推送/远端核对后进入C；不关闭整M7.3或人工门禁。

## 实施

1. 测试专用独立 C# oracle：线性光、Schlick split-sum、ACES/sRGB；常量环境使用解析 E=piL、prefilter=L，BRDF 使用独立均匀半球 quadrature，而非 native 重要性采样。方向线性环境的 diffuse、roughness=1 specular 使用解析 cosine 积分。另以包数值的独立 cube/LUT/mip 软件采样验证 GPU 消费；此项不充当独立 Cook 算法 oracle。
2. 实际 DX11 静态材质网格：黑/常量/高动态范围/方向环境，metal 0/.5/1、roughness .045/.4/.7/1、ORM AO、sRGB base/normal/emissive、强度 0/.25/1/4、Y 旋转和 Off。预先固定软件采样误差 <=3 个8-bit阶、独立积分图像 <=4 阶，独立 LUT 系数误差 <=.02；低粗糙度窄峰不使用不收敛的均匀积分作精确 oracle。
3. 同一公开合同下默认、用户（环境贡献乘.5）、源无关 shader/package；保留外部 hash/真实整组准入。实际 ASCII/Binary FBX -> NCA 保存/重新解码 -> clip采样 -> GPU skin/geometry/shadow + 材质/IBL，独立 CPU four-weight position/normal/tangent 烘焙成静态 mesh 作全图 oracle；start/mid/end/loop边界，不能只统计 draw。
4. 环境更新/重绑定、目标 resize/恢复、多次关闭/重新创建，API errors/warnings=0/0。warm静态 submit/present 单独记录 CPU elapsed/线程 allocation/ABI复制/constant/资源与环境上传；不在测量中 capture/cook，不称完整性能或1h验收。

## 门禁

定向失败保留，修复后重新测试；测试并入原 Rendering CTest，冻结源码，完整顺序无 Skip Debug/Release Build.bat。保留全部原 CTest/managed/Python/M6/M7/格式/MCP/smokes/inspect/三轮profiles/audits/deployment恢复。核验 manifest/hash/journal/双配置备份与原 shader闭包，然后文档/路线图/AGENTS、提交推送、核对远端 SHA。

自动候选通过不关闭 manual UI/DPI/MCP、用户 FBX/HDR/材质、目标机/自包含/完整性能/1h。无 HDR/EXR 文件解码器、推理服务、Python gameplay、新 Agent 代码执行/live GPU 权限、旧格式兼容、Vulkan/OpenGL drawing。本片不改已有 native ABI。
