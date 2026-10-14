# M7.4-D3-B 正式 Player HUD 与资源闭包

基线：D3-A `253ee9946087525e8ca7a56a126bf170688dcb89`，本地/远端已核对。保持四项 IDE 修改、失败证据和备份。

## 实施范围

1. 可选项目 `uiPackage` 显式选择文档 UUID、代次、项目相对 `.ncupak` 路径和外部 SHA-256。独立 NCU1 v1 打包既有 `.ncmaui` 文档和恰好所需字体/原始 RGBA 图片；不把 UI 内部层级引入 World，也不修改 NCP、旧场景或 native ABI。
2. 整包最多 64 MiB，文档 4 MiB、64 个依赖、单依赖 32 MiB。同 UUID 不得跨类型，缺失、多余、重复、错误项目/文档/代次/hash、越界和尾随数据拒绝；字体必须声明可分发及许可证（声明并不证明版权）。共享只读 pin 拒绝路径重解析、硬链接、大小写歧义和读取期间替换；无作者文件或系统字体回退。
3. Player 在加载 gameplay 前完成 CPU 闭包、GPU 字体/图片/布局准备；静态帧仅复用列表，尺寸改变显式准备，场景 Tone/Bloom 后以显示域 overlay 合成；失败保留可关闭资源，不提交旧 HUD 冒充新值。无 UI 不创建 Canvas/Text；Null 只验 CPU 包，不加载插件。
4. 本片为被动 HUD（Frame/Group/Rectangle/Text/Image）。未实现输入路由的交互控件/语义 action 明确拒绝，不伪称可点击。原 UI 编辑器及其命令/Undo 不变。Editor 的正式 scene HUD 视口合成、Play/Undo 联动和完整 FBX/IBL/shadow/Bloom/UI 搬移联合在 D3-C；本片 Editor 遇到显式 scene UI 配置明确拒绝，不静默忽略。
5. 增加格式/资源预算/只读 pin/owner/实际 DX11 图像与字体/暖态复用/正式 Player 启动前错误/Null/搬移包校验测试；保留所有原测试。

## 交付门禁

定向测试修复后冻结代码，顺序完整无 Skip Debug、Release Build.bat；检查原回归、Python inspect、格式拒绝、产品文件 SHA、部署 Complete journal/备份及实际 EXE。通过后提交推送并核对远端，再做 D3-C。不关闭 D3/D/M7.4 或人工、目标环境、完整性能、1h 门禁。保留 File.Replace 偶发失败根因调查。
