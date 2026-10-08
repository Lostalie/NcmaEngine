# M6.3-B 固定资产和场景图交付记录

状态：完整顺序 Debug-final / Release 回归均以0退出，B自动候选通过；不得据此标记 M6.3 完成。

实现边界见 [运行契约](M6_3_B_RUNTIME_CONTRACT.md)。C# 的 AnimatorData、精确资源准备、
SceneAnimatorRuntime、内部 host lifecycle 及 ScenePlayRuntime 共用 Editor/Player/Headless。
图规范 content generation 与 NCA rig generation 分开；图通过明确 NCP1 animgraph 路由随真实
闭包运行，原 NCA 十种 tag 不变。新图类别的 metadata schema 和资产读取 MCP 描述枚举同步更新；
没有新增自动可见资产、grant、live Agent 参数控制或推理服务。

帧展示复用已有 pose owner/rig/clips，读取 committed plan，使用 A 数值采样/混合与既有 GPU
palette/geometry/shadow。必须匹配同一 World 和 prepared asset publication，拒绝用相同 UUID
拼接另一份新旧 generation。图预览隔离，播放配置冻结，故障步及 caught 非法配置/参数控制
不推进图 clock；重载明确恢复 frozen startup、保留 tick chronology，建立新身份。

新增专项纳入 NcmaRenderingTests：真实 NCA、缺依赖/骨架、规范复制、包 type/generation 伪造，
source-free 包、正式 Headless Player8步且 native modules=0、共用 host60步/Reload、32独立实例
与33对象拒绝、独立 Edit preview、两 clip 混合数值 oracle、32次 Editor Play/Stop、线程与
foreign asset publication、失败步/catch非法写/catch参数控制、派生 close failure 保留。
机器生成的 animator-scene-results.json 不表示真实用户 FBX/人工验收。

## 修复与原始证据

初次编译因 Scene.Rendering 新增 Gameplay 引用后父命名空间的 SDK 类型遮蔽而失败；使用明确
Runtime.GameObject/System.Numerics.Vector3 别名修复，不新增旧类型兼容。测试源意外残留标记已移除。
初次完整 Debug 的3项失败保留于 `out/m6-3-b-debug-first.log`：

- 图 preview 测试把新 Editor World 的 tick 错设为源 World 的 tick；改为比较本身前后 tick，保留数值误差检查。
- 资产 MCP 两项描述的 golden 随新增 kind 改变；仅更新枚举造成的精确 hash，保留关闭 schema/权限/负例检查。
- metadata schema 原来只列十类；同步新增 AnimationGraph，NCA 原 block tag 范围不扩展。

完整 Debug retry已通过，但其后复核又新增 foreign publication 校验及32/33实例测试，
因此最终门禁必须使用覆盖最后代码的 `out/m6-3-b-debug-final.log` 与随后完整 Release，不能以 retry 替代。
所有原日志/隔离测试目录/可恢复部署备份保留，不删除失败输入，不强制解锁。

## 最终门禁与部署

完整日志 `out/m6-3-b-debug-final.log`、`out/m6-3-b-release.log` 均为0退出，无 Skip 标志。
各12/12原生、22/22托管CTest；图/pose35、Editor72、Player56、Gameplay53/fakeMovement38、
Python43，以及 managed/native smoke、严格格式/旧格式拒绝、inspect、三轮profile、pre/post审计和
检查式完整部署通过。新增代码编译零警告/错误。Animator专项结果分别位于
`out/verification/m2/render-Debug/animator-scene-results.json` 与 Release 同目录：
formalPlayerTicks8/nativeModules0、headlessTicks60/actors32/33拒绝、PlayStop32、blendOracle/closeRetention/
failedStepAndCaughtWriteAndControl/foreignAssetPublicationRejected=true，manualAcceptance=false。

Release 的 `out/bin/NcmaEngine.exe` 及完整101文件 manifest 哈希重新校验通过，journal Complete，
generation `67b9b01fea634671a6f8656fb3c8e519`。最终部署的可恢复备份保留：
`out/deployment/2b4f80e428a04e50aaa3c7791a972f21/backup`（Debug前）与
`out/deployment/67b9b01fea634671a6f8656fb3c8e519/backup`（Release前），以及first/retry历史备份。
最终审计 `out/verification/m2-8/Debug/6757d97ce6ff448781d7743856c5b862/audit.json`、
`out/verification/m2-8/Release/760aea3f2b7f4373ac3190925b5dced8/audit.json` 均 audit_passed=true、h8_accepted=false。
提交仅含本阶段源码/文档，不包含生成包、备份、证据目录及无关 .vs/.user 设置；推送核对远端 SHA 后再开始 C。

## 保持开放

C 根运动/唯一角色数值执行、D 完整图 GPU/获批运行观察联合验收、M6.4后续仍未实现。
图 recipe scratch 尚未做 liveness 压缩/跨对象批次优化；不支持图 seek，也没有 live 图热编辑。
现有图读取 MCP仍只读规范作者副本、structural_only；运行帧 MCP属于 D，不能声称已经接入。
真实用户动作、可见第三方 MCP、DPI/IME/OS、目标环境/性能预算/1小时长稳及既有门禁继续开放。
