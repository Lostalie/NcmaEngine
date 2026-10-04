# M2.5-E FBX 托管预览切片

日期：2026-10-04。此文记录历史 FBX 切片及其当时测试计数；后续线框/Orbit/报告和动画 ABI 2 已补齐，见 [当前 H5 记录](M2_5_H5_DELIVERY_REPORT.md)。**H5 人工验收仍未完成**。
旧生产入口 out/bin/NcmaEngine.exe 未切换/删除，无提交或推送。

## 实际边界

- NcmaNative 的角色资源 ABI 2：Import/Release/ReadReport/ReadError/ReadIndices/Sample。
  没有 player、pause、时间推进、Undo/Redo、场景或权限策略。
- 每个导入资源不可变，进程内同一 DLL 生命周期的单调句柄作 generation；最多 16 个 live 资源。
  句柄不能序列化，持久化身份仍用 UUID。同源重导入 C# 传原资产 UUID。
- UTF-8 报告/错误使用 caller-owned buffer：required 字节含 NUL；报告最多 4 MiB，错误固定 4096 字节（Unicode-safe 截断、catch 路径不分配）。
  数值 required 为元素数，Sample 最多 6,016,384 个 float。
  返回 0=失败、1=复制成功、2=查询/容量不足；查询不重新导入/求值，短缓冲和无效输入不部分写入。
- Sample 输入 clip index 和显式 time，输出列主序 bone model matrices（每骨骼 16 float）
  然后 mesh-order 全量 CPU-skinned XYZ（每顶点 3 float）。
  ReadIndices 输入 mesh index，复制三角索引；与 ufbx 对照的既有轴、单位、绑定矩阵和采样器一致。
- 角色 ABI 1 的旧 inspect symbol 暂为明确拒绝桩，待 M2 统一清理时删除；不保留兼容行为。
  不涉及动画 ABI 1（动作实验室仍待数值契约迁移）或场景 ABI/格式升级。

## C# 业务与 UI

ImportedCharacterResource 使用显式绝对 DLL 路径和 owner-thread lease；
读入有界报告、验证布局、复制索引与数值样本，Dispose 先释放资源再释放 NativeLibrary。
不使用借用 native 字符串，不靠 GC finalizer 卸载资源。

FbxPreviewSession 是独立预览文档/命令域：
导入、选择片段、Play/Pause、Step、Undo/Redo 都由同一 managed dispatcher 处理。
Undo/Redo 共用最多 8 项状态历史；自动 Tick 不新增历史。
重导入可回退旧不可变资源，历史被淘汰/分支被覆盖时释放不再引用的资源。
失效 preview revision、非法 time/clip、导入失败保留当前状态/历史。
场景 EditSession、SceneDocument revision/history、Play World Tick 与该预览隔离，
没有通过 MCP 暴露路径加载或新增 Python gameplay。

候选 GUI 接入上述控件，片段每页 8 个；Inspect CPU pose 可查看复制数值的前 8 个骨骼位置。
它不是角色 viewport：**CPU 线框画布、Orbit、骨架名称/parent 展示和完整报告面板仍未迁移**。
真实大文件导入仍同步，会阻塞 owner；异步导入/取消尚未实现。

## 部署与消费者

- Build.bat 把 NcmaNative.dll 放入候选 plugins，按需由 C# FBX lease 加载；
  不调用 NativeEntry 或自定义 hostfxr bridge。
- M2.7 Editor 包增加该 DLL 与 ufbx 许可证、deployment manifest 的 character resource ABI 2 元数据；
  Null/DX11 Player 包不包含该 DLL，不增加 FBX/Editor/Gui 运行依赖。
- Python ImportedCharacter/inspect_fbx/CLI 已迁移 ABI 2，报告独立复制，finally 释放；
  仍仅做只读离线检查，无游戏脚本/编辑器历史权威。
- native FBX ABI 测试已迁移；旧 C++ 编辑器直接使用 importer 的视觉对照仍保留。

## 自动验证范围

原生测试覆盖 ASCII/binary、同 UUID 的重导入、矩阵/CPU 蒙皮/索引一致、
buffer count/query/short buffer、不部分发布、NaN/负时间/无效 clip/mesh、
released generation/double release/old ABI 拒绝、16 live resource 上限和不复用旧 generation，以及源文件被改写后资源读取不重新导入。
既有真实 ufbx 每角点 reference 误差仍要求小于 0.002m。

新增 4 个托管业务专项：
复制数据/资源 owner affinity/释放后复制结果；
managed clock/history/失败保护/场景与 Play 隔离；
3 轮每轮 24 次导入的有界历史及 lease pruning；
GUI 事件同一 preview dispatcher 与 stale revision 拒绝。
Python 增加复制报告与 disposed lease 测试。

最终规范构建已完成：Build.bat -Configuration Debug / Release 均退出 0。
每配置 CTest 共 29 项（8 native + 21 managed/editor）全部通过；其中编辑器业务专项 25/25、
Player 专项 22 项。managed/native animation ABI 1 smoke、Python inspect 和 25 项 unittest 均通过。
真实 DX11 reference/GUI 像素比较 max=0、mean=0，API validation 错误/警告 0/0；
真实 DX11 Player validation 同为 0/0。这些自动结果不代替可见窗口、输入法、DPI 和第三方 MCP 人工验收。

证据：out/verification/m2-5-fbx-{debug,release}.log，以及各配置
out/build/windows-ninja-{debug,release}/Testing/Temporary/LastTest.log。
Release 物理零分配门禁修正后连续 32 次通过，证据为 out/verification/m2-5-physics-repeat.log。
本次日志无编译警告/错误；仍有 Git 全局 ignore 文件权限警告，不是编译警告，也未修改用户配置。
Debug/Release 的 Editor 包均包含角色 ABI 2 DLL/ufbx 许可证，Null/DX11 Player 包均不包含该 DLL；
搬迁包导入/采样回归通过。包索引为 out/verification/m2-7/{Debug,Release}/packages.json。

## 当时的下一批（当前状态见 H5 记录）

1. 冻结并实现动画 kernel-only ABI 2，迁移动作实验室 C# / Python 状态、时钟、通知消费和历史。
2. FBX skeleton metadata/canvas/Orbit/完整报告视觉迁移，旧新同序列对等测试。
3. 大 JSON 分页和已有 reference-render 控件已补齐（主交付记录第 5 节）；继续完整材质/阴影展示、对话框/快捷键/偏好/原生日志归并。
4. 可见新入口 MCP/真实输入法/焦点/DPI 人工验收；H5 完成后才推进生产入口切换。
