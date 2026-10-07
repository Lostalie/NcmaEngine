# M5.7 顶部工具栏待确认记录

历史记录：后续用户已授权按统一效果图继续整个M5.7，并授权最新修复测试通过后提交推送。下面的顶部-only停止条件不再适用，原GUI1.4契约及历史证据保留。当前布局/状态/待验项以[M5.7交付](M5_7_DELIVERY_REPORT.md)为准。

2026-10-07。用户只授权先实现顶部工具栏，确认后才允许进入下一块区域。状态：样式、专项检查与完整顺序Debug/Release回归已通过；未提交推送，等待用户视觉确认。

## 本次切片

现役Editor增加64逻辑像素顶部条：深蓝背景/细分隔线，左侧实际NcmaEngine.ico与品牌，中间打开/保存/Undo/Redo/现有动画工具/本机设置入口，右侧运行或继续/暂停/单步/停止。

按钮根据现有服务和Play状态启用，复用C#命令、revision、授权与过期事件保护。未实现的模块不添加假入口。小窗口优先保留运行组，不够宽的工具入口仍可从原控制区访问；极小窗口恢复现有可滚动布局。

新增GUI1.4的有界ToolbarBegin/Button/Brand/Divider/End，原表布局和reserved含义保持。复制ICO在可信启动时检查/解码并由GUI持有图像资源，原生没有Editor命令执行或文件路径授权；字体与其他区域主题不改变。

对象列表、Inspector、现有Play/Status区域和视口矩形不改变。原场景文件控件保留在顶部旧区域剩余空间，可滚动；不重解释旧ToolbarHeight，不移动其他区域。为了不扩大本次范围，旧Play控件暂保留，同样使用同一业务路由。

## 检查与证据

新增ToolbarTests覆盖520/800/1280/1920宽度、按钮边界、其他区域矩形不变、纯Build不修改World/revision、旧/伪造事件拒绝、共享Undo、冻结禁用、非法ICO/越界视图拒绝、实际图标与GUI/GPU合成。

真实截图为out/verification/toolbar-review/toolbar-preview.png；完整画面为同目录full-preview.png。这是内部真实渲染，不是生成的设计图，也不等于用户已确认或真机DPI/输入法已验收。

第一次完整Debug回归中，SceneDocumentTests的File.Replace出现此前同类IOException；日志out/toolbar-debug-verified.log及生成输入保留。独立新目录复测36/36通过。没有强制覆盖/删除失败目标，没有更换场景保存原语，也没有确定外部锁/杀毒软件原因。后续完整回归结果另行补充，不隐藏原失败。

后续自动检查发现包审查器仍要求GUI1.3，与新GUI1.4候选清单不匹配。已同步审查规则与fixture：新候选严格要求1.4；仅部署前只读核对旧out/bin安装允许既有1.2/1.3，不增加运行时回退或跳过包检查。原失败日志out/toolbar-debug-final.log保留。

最终顺序Build.bat Debug/Release通过，日志分别out/toolbar-debug-final2.log和out/toolbar-release-verified.log。各12项native及22项managed CTest、Editor65、UI19、Player56、Gameplay53/fake Movement38、Python43、smoke/严格格式/inspect/profile/包审查/检查过的部署全部通过，新代码零编译警告，顶部GPU检查零validation错误/警告。Release已部署out/bin/NcmaEngine.exe，恢复备份out/deployment/c952601342bf4172bb251e6dc81a7cde/backup。自动结果不是用户视觉确认，也不关闭人工门禁。

## 确认边界

用户确认前不改对象列表、Inspector、UI画布、AI侧栏、底部Console等区域，不实现Renderer query7，不开始其他M5.7小片段。此次修改不关闭M4/M5或任何未完人工/性能/目标环境/长稳门禁，不提交推送。
