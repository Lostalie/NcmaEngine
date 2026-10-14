# M7.4-D3-C：正式 Editor HUD、输入与作者发布合同

本合同描述已通过完整无Skip Debug/Release及受检部署的自动候选，证据见[交付记录](M7_4_D3C_DELIVERY_REPORT.md)。原人工、目标机、自包含、完整性能与长期门禁不由自动测试关闭。

## 所有权与显示

C# 拥有 UI 文档/状态、布局、输入与应用组合。`SceneUiPresentation` 不拥有 World。Editor 在正式场景视口中合成独立透明缓存目标；Player 直接在场景 Tone 后叠加显示域 UI。无 UI 时不创建 Canvas；无字体时不初始化 Text。一个惰性 `UiTextResources` 共享同一文字服务，作者预览、Edit HUD 与 Play HUD 的状态、列表、run、目标和租约独立。关闭顺序遵守引用依赖，关闭失败不强制卸载。

缓存目标为预乘 RGB，原生 ImGui CachedImage 使用 ONE/INV_SRC_ALPHA，之后立即恢复普通 ImGui 混合状态；display-only 项不捕获输入。没有新 native ABI 布局或 Shader 算法，普通 Image 的语义不变。持有显示租约时不能修改/关闭目标；viewport/DPI/运行时 presentation revision 改变后不能复用旧表面。

## 启动与场景值

NCU1 v1 沿用 D3-B 的严格项目/文档 UUID、外部 SHA、代次、恰好匹配资源和只读 pin。作者 `.ncmaui` 不是运行包，也不会由运行时自动打开。

新增 `ncma.scene.ui` v1 值组件：`version/enabled/documentId/generation/path/hash`，不是 UI GameObject 树或运行时 widget 状态。整场景至多一项（包含 Off）。不存在时使用项目显式 `UiPackage`；Enabled 覆盖项目选择；canonical Off 抑制项目默认 UI。UUID/路径/SHA 持久化，运行句柄不持久化。

Editor/Player 在 gameplay 前检查选择、NCU/字体/图片/动作闭包；图形路径要求受检 Flat2D。Null 只做 CPU 元数据/闭包验证，不加载原生插件或执行 pipeline 工厂，不宣称字体/GPU 验收。默认 apphost 没有任意动作加载开关，未注册交互必须拒绝。

## 输入边界

`SceneUiActions` 是可信 C# 启动表，最多128项，精确绑定 action 与 Activate/ValueChanged/TextChanged 类型；不是 JSON 委托、字符串反射或 Agent 可执行注册表。`SceneUiInput` 接受 UiRuntime 的复制提案，不直接调用 Behaviour 或写 World。

`GameplayUiInput` 每批最多128事件、每项16KiB UTF-16字符、合计64KiB，完整批次验证 UUID、有限值、Unicode、单调序号。输入绑定 UI instance/generation、Play/session/world、tick/revision 和不可猜测 admission。仅固定步 Behaviour/System 能观察同一只读批次；OnUpdate、创建与清理不获得该执行权限。失败不发布前缀，不推进序号；普通多显示帧不丢尚未消费的输入。

平台适配使用实际 framebuffer origin/DPI/viewport。失焦/溢出/重置/切工作区/大小变化/暂停/Reload/Stop/故障撤销输入并清焦点/捕获/组合串；已消费键鼠不再触发场景或物理游戏输入。Play 使用独立 UI 状态，不能写回 Edit/作者文档。真实测试中的合成平台事件证明宿主接线，不替代操作系统输入法/DPI人工验收。

## 两阶段作者批准与原事务

1. 精确项目内作者文件获得本地写权限，资源获得明确批准；字体打包另要求允许再分发的声明。声明不是法律授权或安全沙箱。
2. `EditorUiPackagePreparation.Review` 复制已批准资源并 cook，不重开作者资源路径；审阅绑定项目/文档/场景代次/资源与文档修订、包代次/路径/SHA和60秒指纹。正式面板输入完整指纹后才可准备。
3. `UiPackageGenerationStore` 创建不可变包：独占新 stage、flush、重验批准、无覆盖 rename、读回 hash/语义、再次批准后 pin。每包64MiB、32代次/256MiB总保留预算包含失败文件；冲突不覆盖。失败保留，恢复必须明确指定精确 stage 并重新批准。无自动删除或扫描重试。
4. 准备本身不改变场景。另一次审阅绑定原 Edit stamp、目标 GameObject UUID、before/after引用；用户明确输入新指纹后，`EditorUiPublication` 通过原 EditSession 参与者、精确对象/组件权限和唯一历史提交一个 `set_component`。
5. 包先持久化且不可变，唯一可变发布根是场景引用。原场景 Save 原子持久化它，不存在第二发布清单/第二历史或多文件伪原子提交。失败/取消/Undo保留孤立包，显式提示用户保存场景。

Undo/Redo 重验原授权、复制小 memento 与已 pin 资源，不把大包塞入历史。32 pin/128记录有界。New/Open/Undo文档替换撤销旧审阅；准备授权绑定场景 document-generation epoch，不能因重开同作者文件而复活。参与者仍属于同一 EditSession，新场景需新批准，不永久失效。

提交后 Editor 在原生帧外准备完整候选 HUD 再交换；失败清除显示资格，不把旧图像当新版本。旧资源关闭失败保留依赖、要求明确关闭恢复；不自动越权重试。Play 组合使用传入快照，不能通过 owner getter 重入。没有同步 AI/Python 推理或 IPC，也没有新增 Agent 默认批准/执行权限。

## 验证范围

保留 D2 独立 Half/后处理/显示域整图 oracle，不能把产品图像比较宣称为独立完整光照/光栅化实现。产品联合包含真实 ASCII/Binary FBX→NCA、64动画固定步、skin/PBR/IBL/shadow/Bloom/HUD、default/stage/full pipeline、原命令/Undo/Redo、HUD代次切换、viewport resize、三轮 Play/Reload/Stop、错误闭包和资源关闭。严格搬移产品按 manifest 校验路径/hash，原作者与项目文件独占锁定，产品 API probes 与真实 apphost 分别记录。

暖态记录包含完整宿主 CPU/分配与资源计数，不承诺宿主零分配；无 origin-frame 的 GPU 时间不宣称独立逐帧采样。DX11 only；Vulkan/OpenGL 下一版本；Python 不是 gameplay。原人工 UI/IME/DPI/MCP、用户素材/HDR decoder、目标机/自包含、完整性能/1h及间歇 File.Replace/empty-cache 调查保留。
