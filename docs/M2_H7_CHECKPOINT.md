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

当前尚未执行这次提交后的验证；实际提交号、日志、计数及结果在完成后追加。
通过后推送 origin 的 main；若失败，先保留证据并修复相关问题，再重测。

## 仍未完成

真实输入法/跨屏 DPI/可见第三方 MCP、无预装 .NET 的 self-contained 目标环境、
正式部署维护与恢复、H8 完整性能/稳定性和受审查清理仍待完成。
候选 framework-dependent 发布成功不等于这些验收通过。
