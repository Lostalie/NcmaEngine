# M2 测试状态与提交门槛

日期：2026-10-04。状态：现有已注册自动回归全部通过；完整 M2 验收未通过。
这是测试状态记录，不是最终 M2_DELIVERY_REPORT.md，也不授权默认入口切换或旧代码清理。

## 1. 本次执行结果

按 Debug→Release 顺序运行完整 Build.bat，没有 SkipTests/SkipManaged/SkipPython。
两次退出码均为 0，现有全部 30 个不同 CTest 注册项通过（8 native + 22 managed/组合）。

| 测试 | Debug | Release |
| --- | --- | --- |
| 已注册 CTest | 30/30 | 30/30 |
| Editor Services（CTest 内部用例） | 40/40 | 40/40 |
| Player（CTest 内部用例） | 22/22 | 22/22 |
| Python unittest | 41/41 | 41/41 |
| managed/native smoke 与 Python inspect | 通过 | 通过 |
| DX11 legacy/kernel/managed 图像 max/mean | 0/0 | 0/0 |
| DX11 reference/GUI/customization 32 循环 API errors/warnings | 0/0 | 0/0 |
| retained runtime 历史比较 | 13 项×3 轮完成 | 13 项×3 轮完成 |

两个只读审计 audit_passed=true、runtime_profiles.reviewRequired=false，h8_accepted=false。
历史 fixture 比较不是同时间旧/新入口完整性能验收；没有承诺动作游戏帧预算。
没有发现需修复的现有自动测试失败，也没有修改测试断言或放宽门禁。
新代码无编译 warning/error；Git 全局 ignore 权限和 LF/CRLF 提示是本机环境提示，未修改用户 Git 配置。

## 2. 证据与源码身份

| 配置 | 完整日志 | 审计 |
| --- | --- | --- |
| Debug | out/verification/m2-stage-debug.log | out/verification/m2-8/Debug/dd9da8dc1cba4f49b7755a83217f126a/audit.json |
| Release | out/verification/m2-stage-release.log | out/verification/m2-8/Release/50638450259c45c08cdec80f80f5098f/audit.json |

完整日志 SHA256：

- Debug：E9FD833A38C9D88F64DCBF3EEB63C99AF6EEF40859FC3459F582055ADF4D1D53
- Release：96F43BE30241B2077335FAE889592E73ECA0B9E8E39A5A1ADA5A67822BDB82C8

HEAD：a1ac9d444063721ed27d5791b059530c49286489，dirty=true。
两配置源码清单 SHA256：85a231cf1652f67f41f65b03e4c84d6b7c50c4b8e610cf4f8c010dc96311ae0d。
该清单不含 docs/SDK/生成目录，不是全仓库快照或 clean-commit 发布证明。
环境：Windows 10 build 19045；.NET SDK 9.0.315，TFM net8.0/runtime 8.0.28；Python 3.12.8。
完整 30 项以 canonical 两轮日志为准；每份归档 LastTest.log 只含最后 22 项。

## 3. Release 物理稳定性追加回归

针对历史上出现波动的零分配断言，额外运行 32 个独立 Release Physics 测试进程，全部退出 0。
每个进程保留原有 2D/3D×0/64/1024/4096 body 共 8 个 workload、32 样本，
allocated==0、Sequence==36、LiveBodies==count、LiveJobs==0 的断言未修改；
原有负例、32 次 world 循环、32 次模块循环和 PhysicsServiceTests 同时运行。
另逐份核验全部 256 条原始 workload 的上述计数以及 Errors==0。
这不是一小时可见编辑器/Play/客户端稳定性验收。

证据：out/verification/m2-stage-physics-repeat.log；
out/verification/m2-stage-physics-repeat/ 下的 round-01.log 至 round-32.log，
以及 round-01/physics-results.json 至 round-32/physics-results.json。
汇总日志 SHA256：64F6AA7E21A3B9F520D7CAD4605F4E7C6BA8AF70606E1563AC87F1E57283CAB3。

## 4. 仍阻止“全部 M2 验收通过”的范围

| 门禁 | 未通过/未执行内容 | 所需证据 |
| --- | --- | --- |
| H3/H4/H5 人工 | 真实键鼠、输入法、焦点、跨屏 DPI、窗口/图标/对话框；可见 FBX/动作/Play/关闭 | 按 H5 逐项实际操作、环境、日志/截图 |
| H5 第三方 MCP | 可见应用中的审批、UI Undo、MCP Redo、精确删除、撤权和关闭 | 客户端名称/版本、脱敏配置、逐项结果 |
| H7 Ready/Production | 自包含目标环境运行、Explorer/Rider、维护窗口正式切换与恢复 | 目标环境与部署 journal、真实启动/恢复记录 |
| H8 自动矩阵余项 | 完整同 fixture 旧/新入口 UI/MCP/GPU/Physics/GC/native-live 性能矩阵；旧策略/机制测试与消费者逐项核定 | 完整测量、替代矩阵与精确清单评审 |
| H8 长期稳定性 | 一小时真实编辑/Play/客户端稳定性 | 操作时长、行为、资源/错误记录 |
| M2 后统一清理 | 前置门禁满足后清理旧入口/桥/构建/部署并双配置复测 | 精确移除清单、恢复方法、完整回归 |

当前发布脚本明确 --self-contained false，manifest 的 production/manualAcceptance/selfContainedVerified=false。
本机安装 SDK/runtime 的 framework-dependent 包测试不能替代无运行时目标环境验收。
本次没有收到新的人工验收记录，不能将合成/隐藏窗口测试冒充真实人工操作。
详细步骤见 [H5 人工清单](M2_5_H5_DELIVERY_REPORT.md)、[H7 方案](M2_7_IMPLEMENTATION_PLAN.md)、
[H8 方案](M2_8_IMPLEMENTATION_PLAN.md)及[旧测试替代矩阵](M2_8_TEST_REPLACEMENT_MATRIX.md)。

## 5. 当时的提交/推送状态（已被后续授权更新）

用户条件是“所有测试通过后提交推送”；按既定完整 M2 门禁，当前条件尚未满足。
因此本次没有 stage、commit 或 push，没有清理旧代码或切换生产入口。
origin 为 git@github.com:Lostalie/NcmaEngine.git，当前分支 main。
用户 .vs/.user 工作区改动保留，不应作为 M2 实现提交。

需用户选择：继续按全部自动/人工门禁提交，提供人工记录并完成上述缺口；
或明确将此次提交门槛限定为现有已注册自动回归通过，允许提交候选实现且保留 M2 未完成状态。
后一选择只改变此次提交条件，不自动关闭 H5/H7/H8、不授权提前删除旧入口。

## 6. 后续授权与提交后验证（2026-10-05）

用户先授权 H7 检查点提交后测试，再授权 H8/M2.8 提交推送及重建 EXE。
H7 候选为 e104663；独立检出暴露 Eigen 必需头文件未入库，由 f3fce5a 修复。
修复后独立 H7 Debug 完整验证通过（29 CTest、28 Python）；未运行独立 H7 Release。
以上第 1–5 节保留历史证据，不能据其旧 HEAD 或“没有提交”描述判断当前仓库。

本次将提交 H8/M2.8 候选后运行完整 Debug/Release Build.bat，无任何 Skip，
并推送 origin/main；实际结果、提交号和 EXE 身份在完成后追加。
只提交源码/文档/冻结 fixture，不提交生成包或用户 .vs/.user 设置。
H5 人工、H7 Ready/Production、H8 全体验收仍未完成；默认入口保留，不清理旧代码。

18ae5ca99262b3988cf4c53d4eadc1790e38c1dd 已提交完整 H8 候选。
其提交后 Debug 和 Release -CleanNative 完整 Build.bat 均退出 0，
日志为 out/verification/h8-postcommit-debug.log、h8-postcommit-release.log；
每配置 30 CTest、40 Editor Services、22 Player、41 Python 通过。
Debug 历史 commands:1:64 median 为 0.6768/0.5656/0.5722 ms，历史为 0.4744 ms，
触发 time review；分配未变。这是待复核的历史性能变化，不是功能测试失败或正式性能验收。

Release 原生重编译暴露测试命令行 /DNDEBUG 与 /UNDEBUG 的 D9025 警告。
修复改用测试专用 forced-include TestAssertions.h，在任何源码头文件前取消 NDEBUG，
保留 Debug/Release 全部原有 assert 行为；未屏蔽警告、未改断言、不影响生产宏配置。
修复提交后将重跑双配置完整验证；上述首次日志保留，最终结果在下方追加。
