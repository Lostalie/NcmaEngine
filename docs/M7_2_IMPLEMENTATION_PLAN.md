# M7.2 PBR 纹理、默认材质和切线基实施方案

前置：M7.1 `3345ab9cfecec7d91cb991f8b37f2f4103c72047` 已独立核对远端 main。DX11 only，不增加 Agent 执行权限，不改变原 ABI/资产格式，不重做现有资源缓存。

1. C# 单一 MaterialSurfaceContract 定义六槽颜色空间/通道/缺省采样，显式 ORM/MRA packed 通道助手和关闭枚举的 Default/Matte/PolishedMetal/Cutout 预设。纯值转换，不隐式猜测源文件或导入贴图。元数据与诊断最多六行，不持有 GPU/World。
2. 运行资产、运行包、GPU 缓存和直接材质创建使用同一 role 映射；同 UUID 跨不相容 role 拒绝。存在但错误语义的纹理即使网格缺 UV/切线也拒绝；编辑器显式 missing/unsafe fallback 保留，Player strictMissing 保持拒绝。检查完整候选后创建 GPU。
3. 复核实际变换合同：现有 Resource/Scene/skin 数值路径明确只接受正行列式可逆变换，本阶段保留负缩放/奇异矩阵拒绝，不加入不可达的 shader 负号修正。镜像 UV 的 tangent.w、非均匀正缩放 inverse-transpose normal/线性 tangent/正交化，以真实静态与 compute skin 同图验证，不改 shader/native ABI。
4. 预设输入仅构造原 ncma.assets.material.edit 的闭合 document 命令，UUID/path/revision/依赖/撤权/Undo/Redo沿用唯一历史；不新增 Agent 自审批或任意源码入口。本阶段提供受控 API，不宣称完成材质面板交互。
5. 新测试：纯合同/packed/预设/诊断/历史拒绝；Scene真实color/emissive sRGB、ORM数据线性、GGX独立CPU预期、alpha cutout、normalY与切线符号/非均匀矩阵、负缩放/奇异拒绝、静态和真实compute skin同图；错误语义不得改变资源计数、无逐帧纹理/网格上传。固定采样点容限3个8-bit色阶，静态/skin同图容限1，API0/0。原所有测试保留。首次定向测试误将 skin 和 draw 使用不同帧号，保留失败并修正夹具为同一帧。
6. 定向通过→冻结源码→顺序完整无Skip Debug/Release Build.bat→manifest/hash/journal/备份检查→文档/路线图/AGENTS→提交推送/远端核验。所有失败/SDK/用户数据/IDE保留。人工用户材质/FBX/目标/性能/自包含/1h门禁独立；不将ambient称IBL，下一阶段M7.3。
