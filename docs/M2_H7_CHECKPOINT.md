# M2.1–M2.7 / H7 候选提交与提交后测试

日期：2026-10-04。用户要求：到 H7 提交，提交后测试。
这是候选源码检查点，不代表完整 H7 或 M2 验收完成。

## 提交边界

- 提交 M2.1–M2.7 的应用服务、插件契约、Platform/GUI/DX11 参考管线、编辑器业务、数值 FBX/动画、独立 Physics、Player/Headless、候选包和对应测试。
- 保留直接 catalog/Export/活动重载测试，作为这些已提交服务的验证；不纳入 H8 冻结参考、性能预检、kernel-only 图像 fixture、审计工具和最终清理实现。
- M2.8 文档仅作为未来验收方案；其实现与交付记录不在检查点中。
- 生产 out/bin 仍为旧 C++ 入口；不 promote、不清理旧入口，不关闭 H5 人工或 H7-Ready/Production。
- 不提交 .vs、NcmaEngine.vcxproj.user、out/bin/obj 等本机或生成文件；工作区 H8 改动保留。

## 提交后验证方式

提交后从实际 Git 提交导出独立验证目录，运行其自身 Build.bat Debug/Release、
CTest、Ncma.Managed/native smoke、Python inspect/unittest。
不能用原工作区混有未提交 H8 改动的测试结果，冒充该检查点的回归。
原有 ABI/格式拒绝、权限/历史/关闭和资源断言保持，不为通过测试放宽门禁。

以下记录区分独立 H7 检出与后续完整 H8 候选验证；不得混用结果。

## 提交后发现与修复

首个候选提交 e10466371589aae71defad5fba15ed91e1c4f577 的独立冷构建未通过：
MSVC C1083，缺少 Eigen/Core。原因是 vendored Eigen 的 core 忽略规则在
Windows 忽略大小写匹配下同时排除了公开 Core 入口和 src/Core 的 165 个 .h 文件。
原工作区这些文件存在，因此以往本地构建未暴露仓库检出缺失。
保留失败日志 out/verification/h7-postcommit-debug.log。
修复仅添加精确 Eigen/Core、Eigen/src/Core/ 和其内容的忽略例外，
将既有 166 个必需头文件纳入版本控制；不更新 Eigen 版本、不下载依赖。

修复提交 f3fce5a4d814790ffc03ff6f0b9be5369c4f5b8c 的独立检出 Debug
完整 Build.bat 已退出 0：29 个 CTest 注册项、28 个 Python 测试及 smoke/inspect 通过。
日志：out/verification/h7-postcommit-fixed-debug.log。
该独立 H7 检出未执行 Release；不将后续含 H8 的 Release 回归归入它。

## 后续授权（2026-10-05 继续执行）

用户随后要求“ H8提交推送，M2.8提交推送 ”及“重新构建Exe”。
因此继续提交完整 H8/M2.8 候选实现，并在该提交后执行完整 Debug/Release 验证，
最终重建 out/bin/NcmaEngine.exe；结果追加至 M2_TEST_STATUS.md。
授权改变提交范围，不关闭人工/生产门禁，不提前清理旧入口。

## 仍未完成

真实输入法/跨屏 DPI/可见第三方 MCP、无预装 .NET 的 self-contained 目标环境、
正式部署维护与恢复、H8 完整性能/稳定性和受审查清理仍待完成。
候选 framework-dependent 发布成功不等于这些验收通过。
