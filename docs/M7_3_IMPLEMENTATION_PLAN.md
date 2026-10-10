# M7.3 环境光与 IBL 实施方案

基线：M7.2 `a9162d067646dd4f80208ec6f0b6f68db21fc2b5`，2026-10-10开工前核对远端main一致。当前ambient不是IBL。DX11 only；Vulkan/OpenGL下一版本。

## A：环境资产与离线数值预计算（自动候选完成）

最终123项与完整顺序Debug/Release/checked部署通过，见[合同](M7_3_A_RUNTIME_CONTRACT.md)、[交付](M7_3_A_DELIVERY_REPORT.md)。B1资源及B2新Shader/场景绑定均通过完整自动门禁，见[B2交付](M7_3_B2_DELIVERY_REPORT.md)；B3完整独立图像验收/C正式宿主未实现，整个M7.3未完成。以下是A历史范围，不升级为完整IBL或用户HDR文件验收。

- C#拥有复制的linear RGBA32F等距圆柱源（宽=高×2，高2–256，RGB0–65504，alpha1）、源UUID/hash、四个显式Cook参数和受控环境配置。源API接收可信解码后的值，本阶段不宣称HDR/EXR文件解码器或用户HDR素材验收。
- 新NCE1 v1不可变包：独立asset/source UUID、非零generation、源SHA256、算法/布局/样本参数、规范diffuse cube/GGX specular mip cube/BRDF RG LUT。Header160，最多4MiB；不含源像素/路径/name/native handle。读取要求独立可信expectedHash，并逐项校验闭合header/layout/有限数值/alpha/系数/完整长度。不把hash称签名/可信Cook来源证明。
- Renderer新增独立query13/API1/32B离线数值表，原query1–12不变；无GPU/World资源或导出对象。复制caller input，全部校验，局部结果计算完成后一次复制caller-owned output。限定尺寸/样本/64M sample工作预算；错误/短缓冲不写output。owner-thread、module寿命、healthy/off-frame/pureUI拒绝、非重入。
- C# Cook service持有module lease，显式可信off-simulation回调，回调后再校验native边界；取消在有界native调用前后检查，不声称调用中抢占。单条8MiB内immutable结果缓存，缓存命中也重新验证权限/寿命/边界；不在tick推理/预计算，不增加Agent endpoint。
- diffuse存物理E（Lambert消费E/pi），cosine hemisphere积分；specular按mip粗糙度、N=V=R的GGX weighted convolution；LUT按IBL Schlick k=roughness²/2积分A/B，不套用直接光hotness remap。公式参考[Brian Karis原始论文](https://cdn2.unrealengine.com/Resources/files/2013SiggraphPresentationsNotes-26915738.pdf)，算法标号1锁定方向/滤波/样本/roughness网格。split-sum有近似误差，不宣称任意环境精确积分或多次散射补偿。
- 独立常量环境E=pi×L、linear HDR无gamma/截断、方向环境解析E、单骨之外不涉及场景；LUT独立均匀半球quadrature与roughness1/NoV1解析A+B=1-ln2检查、有限非负/能量扫描；包损坏/重hash非法值/副本/取消/权限/owner/pureUI/寿命/缓存/无GPU进程测试。保留原全部断言，未知query改为14仅因13新增。

## B：真实 DX11 IBL 接入（A提交推送/远端核对后）

按[B详细分片方案](M7_3_B_IMPLEMENTATION_PLAN.md)顺序执行B1真实环境GPU资源、B2新Shader/场景绑定、B3图像验收；不能以B1资源上传宣称整个B完成。

新增有版本的环境GPU资源和绑定合同，完整候选准入/创建/发布/释放；旧Scene/Shader binding合同冻结，不偷偷扩展旧闭包。官方与user registered shader走同一合同、实际反射与source-free包。C#控制strength/rotation/off；native采样diffuse/specular/LUT，正确线性HDR合成。纯2D/Off不创建环境纹理；默认环境应明确是合成预设。实际GPU常量/方向/金属粗糙度图、独立积分/像素、default-user一致、错误候选保留active、resize/关闭/API0/0。

## C：正式宿主、资产闭包与配置验收（B提交推送/远端核对后）

环境资产接入项目/NCP运行包、精确UUID/generation/hash/pin与checked deployment；Editor和Player共用；配置进入原可撤销命令/权限/历史。默认3D可直接使用，纯UI/Null闭包不带环境包。验证搬移运行包、离线cook/损坏包、缓存增量更新、真实静态/FBX/NCA动画/阴影/材质联合图像与关闭。用户HDR/EXR解码与人工素材范围另须实证。

## 每片交付

定向通过后冻结实现/测试，顺序完整无Skip Debug/Release Build.bat；CTests/Managed/native smoke/Python inspect/严格新旧格式/原M6/M7断言/三轮profiles/audits；checked deployment/hash/journal/备份核验，文档/路线图/AGENTS，然后提交推送/远端核对再进入下一片。自动候选不关闭人工UI/DPI/MCP/真实用户素材/目标机/自包含/完整性能/1h门禁。
