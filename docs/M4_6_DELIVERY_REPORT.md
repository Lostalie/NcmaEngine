# M4.6 调试、只读检查与可启动样例

## 实现边界

C# `CharacterPlayRuntime.ReadDebugFrame` 复制最后已提交的 Play/World/tick、角色位置、
ground/contact 数量、root 时钟和 desired/accepted 位移、动作 instance、health 和最后量子事件。
仅 owner-thread/safe boundary 可读；fault 返回失效标记和空数组，不泄漏私有异常或旧数值快照。
contact 计数来自当前数值步的复制 receipt，不在诊断时再执行 solver/query。

正式 Editor 的 Character Debug 面板默认关闭，可显示复制诊断及精确 UUID 审批。
`ncma.character.inspect` 和 `ncma.combat.events` 是默认拒绝的只读 MCP 工具，带闭合输入/输出
schema、稳定名称、readOnly 风险注解。可信 UI 审批绑定 Edit session/document generation、
Play/World、endpoint、1–32 个角色 UUID 和完整配对客户端集合，固定60秒，随 Stop/reload/
endpoint/配对集合变化或撤销失效。每次调用重新检查，不因重复 requestId 绕过审批。
这是共享配对 audience 范围，不是 per-client ACL。只返回所请求且批准的角色；事件中未批准的
target/damage 隐藏。无 native handles、私有路径、Step、输入注入或 Agent 移动/战斗入口。
Box-only 目标不属于此角色检查范围；事件是最后提交量子快照，不保证 render 轮询消费所有事件。

正式 Play composition 接收已准备的资源 lease，禁止在 owner callback 中重入 owner getter。
原有无资源 composition 合同保留用于其独立场景，不是旧 host/类型兼容层。

## 样例

完整 Build.bat 生成 `out/samples/m4-action/<configuration>/action-<uuid>/action.ncmaproject`，
通过独立 `Ncma.SampleBuilder` / `Ncma.Samples` 离帧工具创建；两者不加入生产 Player 部署。
每次生成独立新目录/CreateNew，不覆盖历史样例。使用普通 `.ncmascene` 和 typed NCP1 `.ncpak`，
显式 physicsEnabled、胶囊角色/数值地面与伤害目标、跟随相机、Health/Root/Action 配置。
两骨骼 body/hand 与 Idle/Run/Attack/Dodge 四段不同的程序化 clip 明确标为诊断素材，
不是原始 FBX 导入、cold cook、正式资产发布或真实用户素材验收。材质为导入槽默认显示；
地面/目标只有数值 collider，未提供环境网格。

生成器：`dotnet run --project managed/Ncma.SampleBuilder -- <输出父目录> out/managed/Ncma.Gameplay.Sample.dll`。
用 `out/bin/NcmaEngine.exe --project <样例工程路径>` 打开；WASD 移动、Space 跳跃、J 攻击、K 闪避。
独立 Player 使用同一个 `--project`，可加 `--headless --ticks 120 --report <新报告路径>`。
0角色样例不加载 Physics；Headless 不加载 GPU/pose/platform。physics 默认仍为 false。

## 自动测试与开放项

新增 Editor 测试验证真实配对 MCP 协议/schema/只读注解、默认拒绝、精确 scope/会话、
未知/重复输入、目标隐藏的事件、不前进 tick、60秒到期、revoke/同 requestId、配对撤销/endpoint
替换/reload、UI禁用审批与显示内容确认、错线程/候选步读取拒绝、实际数值后 fault 空诊断。
新增 Player 测试启动0/1角色 packed Headless/DX11；验证显式物理、未用模块不初始化、
graphics validation 0/0、authoring 文档不变。现有 M1–M4 回归全部保留。

完整 `Build.bat -Configuration Debug`、随后 `Release` 均 exit0：各12 native +20 managed CTests，
Editor64、Player54、Gameplay53/fakeMovement38、Python43，smokes/严格格式与旧格式拒绝/inspect/
三轮保留 profiles/只读 audit/checked 部署和恢复均通过；新代码零编译警告。
日志 `out/verification/m4-6/Debug.log`、`Release.log`；Release 可恢复备份
`out/deployment/c184ffe96dea4d8e8d0aa5c99c1651cf/backup` 不加入 Git。
样例路径必须是规范 project-relative package 路径；测试保留已知 imported-material-slots-only 诊断，
不把默认材质显示宣称为完整材质导入。推送且验证远端 SHA 后再推进 M4.7。
真实可见第三方客户端、窗口/DPI/键盘操作、
用户 idle/run/attack/dodge FBX、目标机环境、性能预算与1小时长稳仍开放。
K6 自动门禁不关闭这些人工门禁，也不关闭 M2/M3 或宣称 Vulkan/通用2D/AI传输完成。
