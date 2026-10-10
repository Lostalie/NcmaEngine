# M7.3-C2-B 共享场景环境执行合同

2026-10-10；基线 C2-A `d8fd39d3a7a7542f0455b908ce5453cb9d25f991`。本片仅共享 SceneRenderSession，不启用正式 Editor/Player；C1 pending guards 保留。

## 所有权与准备

C# World 保持扁平对象、单个 EnvironmentLightingData 值配置。SceneEnvironmentState 只读取确切 World identity/已提交状态（包括拒绝 restore/component 验证准备窗口）；涉及GPU的 Prepare/Refresh 在 World.ReadOnly 作用域内调用审批，禁止 simulation、foreign owner、renderer 帧内和重入 Submit/Dispose，审批不能写 World。无新增 MCP/Agent/GPU/源码执行权限。

启用环境的构造需要显式可信审批，确切 PreparedSceneAssetLease 中 typed EnvironmentPackage UUID/generation/hash；独立 SceneEnvironmentRuntimeShaders 为 profile3 的 shadow 双变体和相同 shared skin。旧 profile1 不能冒充。源布局仅显式帧外默认服务 Cook；文件模式使用 C2-A v2 完整准入，缺包拒绝、不回退。CPU GpuValidated 始终 false。

RenderResourceCache 按 UUID/generation/hash 拥有环境 GPU；命中仍检查审批/owner/healthy/off-frame。缓存环境不可原地 Replace；直接 Renderer.CreateEnvironment 的独立资源仍保留 B1 显式 Replace。托管总缓存及原生 8-resource/32MiB 限制继续约束；两套预算分别生效。Edit/Play 或独立 NCP 租约可以共享相同 GPU 代次。

## 显式刷新与提交

RefreshEnvironment 接收已准备资产，不读文件。配置相同且 publication 相同仅检查边界；同代 strength/rotation 更换绑定，不重传。资产新代须明确刷新；错误身份/hash/代次、撤销或审批异常保留旧安装值和绑定。World 本身已变成新配置时 Submit 明确拒绝，绝不静默显示旧配置；恢复原值或成功刷新后可继续原图。

PrepareEnvironmentView 为已提交 camera/light/view 明确准备 pipeline、resize 或 shadow variant；先完整创建/绑定候选，再交换旧管线。shader 准备和 skin 资源创建都在受控帧外窗口内。旧 profile 显式开启环境可提升到 profile3，但已有 skin 的实际 compute 字节必须相同。仅改 exposure/ambient 更新已准备 pipeline；ambient 不等于 IBL。

Submit 只消费已准备 geometry、skin、pipeline 和环境绑定；无环境 Cook/文件/纹理上传/准入或配置安装，正常绘制常量上传仍存在。view/配置不匹配须重新准备。环境组件观察按 World revision 缓存；revision 改变时当前正确性实现进行有界对象扫描，并非高性能增量 ECS。静态暖态测量不证明动态大场景零分配或完整性能达标。

Off 不创建环境 GPU；已经存在的 profile3 可解绑，pipeline 先释放 scene pin，再释放 cache GPU/CPU 租约。零引用环境由显式 cache.Trim 退休，其他活跃租约阻止退休。原 geometry publication 自身仍持有初始资产 lease，可能继续 pin 初始环境 CPU 文件直到整组场景关闭；不是即时删除全部旧 CPU 代次。构造/候选失败的零引用缓存资源仍受 cache 所有权和显式 Trim 约束，不称独立无缓存资源。

## 不在本片范围

正式 Editor/Player 启用、pregameplay 全闭包、动态命令/Undo/Reload 协同属于 C2-C；默认环境生产部署与面板属于 C3。无 HDR/EXR 用户源解码、推理服务/Python gameplay、跨进程等待、新原生 ABI 或兼容入口。本版本仅 DX11；Vulkan/OpenGL 下一版本。人工/UI/MCP、用户HDR-FBX-material、目标/自包含、完整性能及1h门禁保持待验收。
