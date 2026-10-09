# M7.1-C4 正式宿主与 shader 运行包实施方案

前置：C3 `812ff35f4aaf04959bdf570853945c9d907081c0` 已推送，开始本片前独立核对远端 main 一致。仅 DX11，所有历史人工/材料/目标/性能/1h 门禁保留。

C4 按以下三个可独立回归的切片执行；每片完整顺序无 Skip Debug/Release、checked deployment/hash/journal、提交推送/remote 核验后才进入下一片。分片不减少原 C4 范围；未通过最终联合门禁前不关闭 C4 或整个 C。

## C4-A：无源码包和纯托管结构预检（自动候选完成）

77项定向、六种真实编译产物/独立进程native-free预检及完整顺序无Skip Debug/Release/checked部署通过，详见[A交付](M7_1_C4A_DELIVERY_REPORT.md)。只完成结构预检，不是GPU admission或宿主切换。

- 明确 NCS1 v1 独立格式，最多8MiB、9条预算、每个bytecode最多1MiB。角色闭包只允许纯UI2条、Scene4/6条，以及可选Skin1条；拒绝遗漏、重复、额外、错误顺序和2D带3D资源。
- 可信 off-frame Cook 只接收 B 编译器产生的不可变 CompiledShader，复用 C1–C3 同源固定布局和完整声明/反射结果。包中不保存HLSL、作者名、路径、Editor/World/native句柄或依赖的作者文档。
- 包保存 exact UUID、原author content hash、版本化绑定契约hash、compiler47/strict+werror+O3、bytecode hash和字节。源格式不转换、不兼容、不恢复。角色/布局即闭合运行描述，不是任意shader依赖图。
- Preflight复制有界输入，检查独立提供的包hash、payload、角色闭包、每条hash/compiler/layout和实际SM5 DXBC结构/stage。无插件、设备、编译器初始化或文件访问，独立进程证明确无native/Editor加载。
- 必须明确 GpuValidated=false：此片不解析完整RDEF资源/输入输出语义，不校验DXBC内部checksum或证明指令安全；实际native闭合反射/链接/资源创建仍是B的必要门禁。包括错输出signature但结构正确的包不能被误称GPU可执行。
- 真实默认UI、用户UI、Scene有/无shadow及有/无skin六种编译产物、损坏/预算/最后条目/重算hash后的结构拒绝、复制隔离、独立进程预检。

## C4-B：正式宿主和 GPU admission（待执行）

- 独立运行描述/bytecode不反向伪造源或ShaderDefinition；加载后必须走同一实际原生闭合反射、完整组验证和C1–C3原子创建/替换，再建立exact renderer/owner/lifetime准备对象。
- 必要时新增独立版本查询用于完整off-frame预验证，保持现有表冻结；不能以自报的BindingContractHash替代实际DXBC反射。
- C#正式Editor/Player共享服务在startup/stopped preparation显式准备默认或可信用户选择；SceneGpuResources、SceneRenderSession、UIcache从准备结果取程序，submit/tick不重新编译。空场景与纯2D无3D强制初始化，Headless不加载GPU。
- 错最后stage/旧设备/revoke/nonreentry/active/超时等失败保持原资源/metadata；GPU设备失效fail-stop。真正的角色集合及像素验证通过后才能宣称宿主切换。

## C4-C：版本化项目选择、部署和联合验收（待执行）

- 可信构建与项目选择传递单独可信的package hash、profile/features/精确依赖，启动前bounded文件/路径/handle读pin预检，实际绑定在GPU创建前全量验收。不同格式或缺失字段直接拒绝，不回退到旧入口。
- Editor/Player checked包和manifest明确包含必要的shader包，独立Player不依赖Editor/source authoring/native handle，不加载HLSL文件、不在tick重建。
- 同源复制有界metadata/诊断供宿主使用；AI新能力必须独立闭合授权，未实现工具保持unsupported。本阶段不授予Agent编译/任意源码/DLL加载/执行/发布或推理权限。
- 全部原静态/动画/skin-shadow/纯UI/Player/Headless/损坏包pre-start/真实apphost/恢复测试和全量Debug/Release通过后才能关闭C4自动候选。用户FBX/目标环境/自包含/完整性能/1h仍独立。
