# M3.6 资产浏览与离屏编辑视口方案

日期：2026-10-05。状态：未实现。依赖 G5。目标是用户在正式 C# 编辑器完成角色资产工作流。
继续 C++ Dear ImGui 展示插件，不更换 ImGui.NET，不要求先引入 docking/多窗口。

## 1 编辑工作流

资产浏览显示 root/subasset UUID、种类、来源、generation、导入状态和依赖诊断，列表分页/虚拟化。
导入面板显示经批准的文件、设置、候选进度、取消、身份冲突计划和受影响引用。
Inspector 选择 mesh/materialSet/clip、相机/光参数与有限覆盖，都是同一编辑命令，不直写 World。
拖入 FBX 资产创建含 Transform/SkinnedMesh/ClipPlayback 的扁平对象；static asset 创建静态网格对象。
源 FBX 拖放本身不绕过许可先解析或直接写文件，先预检/复制确认，再进入 G2 任务。

## 2 实施切片

### A 资产列表与导入面板

浏览器与状态页使用 C# 复制视图，native 只返回稳定 UUID/字段意图，禁用状态不是最终授权。
为失败/缺文件/重名映射/不支持变形器/丢纹理提供可定位诊断，不显示“成功”后静默损失。
大报告分页、队列背压、取消/关闭与迟到结果沿用 G2；历史预算/pin 在 UI 中可检查。
项目切换先取消工作与撤权，再替换会话，不能让旧任务创建新项目对象。

### B 真正离屏视口

scene-render 增加离屏 color/depth target，先画 scene，再用 GUI 合成该图像，最后 Present。
建议 GUI ABI additive minor 新增 Image 语义项与 renderer-owned image token，版本/size 明确协商。
C# 不传 ImTextureID/COM/SRV 指针；native GUI 只经附着的 renderer service 解析有租约的 opaque token。
渲染目标在 GPU 完成前保留，resize/close/最小化令旧 token 失效，不能复用为别的纹理。
GUI 1.2 的 canvas 是 presentation-only lines，不冒称已有 texture image 或交互视口。
按视口 framebuffer/DPI 尺寸重建，不每帧重分配，也不让 GUI 和 Renderer 各 Present 一次。

### C 选择与编辑意图

选取首切片可用 CPU ray/bounds 命中；需要精确表面时使用点击触发的 ID buffer 读回，不每帧同步读回。
返回 object UUID、view/document generation、revision 与 frame；旧帧/Undo 后选择必须拒绝/重做。
Orbit/Pan/Zoom 属编辑浏览状态，不写场景相机；只有显式“应用到场景相机”才走 Undo 命令。
变换操作复用草稿 preview/commit/cancel，一次拖动一次历史，不给 native gizmo 写 World 的指针。
最小落地可先数值 Inspector + 明确拖入位置；完整复杂 gizmo 不作为无证据能力。

### D 场景与预览隔离

已有 FBX CPU 预览/Action Lab 先保留独立数值诊断，不复用其私有时钟驱动场景角色。
Editor clip scrub、Play clock、场景 Undo 与 asset Undo 按统一历史政策区分；Play 不修改 Edit。
共享资源只读且 pin generation；项目保存只存 UUID/设置，重启可从 source+派生重建。
缩略图由受控离屏资源批量生成并存 ignored 缓存，不为每列表项建立窗口/设备。

## 3 测试与退出门禁 G6

- 正式入口导入/取消/报告、拖入角色、选择资源/clip、保存、关闭、重启、重新加载。
- 资产/场景操作 Undo/Redo，失败映射/缺纹理、旧帧选择、Play frozen、项目切换迟到任务负例。
- 离屏像素参考、GUI 合成顺序、图片 token 的 foreign/stale/释放/resize 检查和 GPU validation。
- 真机键鼠、输入法、焦点、跨屏 DPI、最小化/恢复和 viewport/Inspector 一致性人工记录。
- 资源/target 数量与上传量稳定；asset list 不每帧读盘/解析整报告，正常编辑保持有界。

先完成自动矩阵及完整双配置，再填写单独人工清单；隐藏窗口通过不代替 G6 真机验收，也不关闭既有 M2 人工项。
构建/回归使用 Build.bat -Configuration Debug，再 Build.bat -Configuration Release，不从未初始化终端直接调用 Ninja。
