# M5.1至M5.6 UI基础交付记录

2026-10-06。状态为自动候选实现，完整顺序Debug/Release最终回归已通过，进入用户授权的提交推送。本记录不关闭M5及既有人工、性能、目标环境和长稳验收。

## 实现范围

旧C++ UI文档骨架及对应ArchitectureTests消费者已删除，无兼容类型或旧格式导入。新增Ncma.Ui持有严格.ncmaui v1 UUID文档、候选操作、共享布局和C#运行时控件；Assets.Authoring/UiCommands复用既有检查过的磁盘日志与Editor.Core唯一Undo，UiDraft只有临时候选，没有第二历史。

Renderer保留原模块1.0至1.2与query1至5，新增query6/UI API1.0驻留显示列表和图片。纯UI创建关闭默认深度，并拒绝3D资源创建；有序相邻批次、透明叠加、圆角和矩形裁剪经过实际DX11像素检查。资源先释放显示列表再图片，pin与错误关闭仍阻止卸载。

NcmaText使用独立kind5/ABI1.0，接受显式内存字体并输出复制测量和RGBA覆盖纹理。C#拥有字体UUID/hash/许可声明和缓存；原生没有文件路径、World、GPU或ImGui依赖。按实际整形后的字形检查缺字，支持已验证的中文/组合字符/换行基线。

UiCanvas连接文档、布局、文本、图片和控件。UiRuntime提供C#presentation setter、捕获/焦点/字素编辑和带实例/代次/Play/World/tick的有界事件。独立NcmaUiSample没有初始化World、Physics或ImGui；默认Editor入口和现役Player保留，参考图工作区未实施。

## 测试证据与修复

最终UI检查19组通过，包含动态flags/圆角命中；原生RendererNativeTests另覆盖UI表长度、纯UI拒绝3D、无效输入不初始化资源、旧代次/NaN、pin与重复帧、注入后的fail-stop及释放。独立UI样例在隐藏窗口运行4帧，DX11 validation为0错误/0警告。这不是可见窗口/真实输入法/目标环境或长期测试。

完整顺序验证：

- `Build.bat -Configuration Debug`：`out/m5-debug-verified.log`，12项原生与22项托管CTest全部通过。
- `Build.bat -Configuration Release`：`out/m5-release-verified.log`，12项原生与22项托管CTest全部通过。
- 两套均完成Ncma.Managed构建、managed/native smoke、新格式与移除格式拒绝、Python inspect与43项Python测试、3轮保留profile、包检查与可恢复部署；新代码零编译警告。
- Editor64、Player56、Gameplay53/fake Movement38保留通过；fake测试不是额外Jolt/真实用户素材验收。M2审查仍明确`h8_accepted:false`。
- Release正式入口为`out/bin/NcmaEngine.exe`；本轮恢复备份`out/deployment/1bd05996dde0453987e959d0e5b71498/backup`，不进入Git。

修复记录：

- 磁盘恢复器补充严格.ncmaui.journal类型支持，未放宽其他文件/身份/hash检查。
- 检查实际排版字形，修复逐Unicode字符检查对组合字符的误拒绝。
- 原生测试目标补入UiKernel链接；修复测试内顶点变量命名冲突，未移除原3D检查。
- 静态画面/普通指针移动不触发重复文字整形、布局或顶点上传；动态flags清除旧交互提案。

首次完整Debug回归中，SceneDocumentTests在第二次保存的File.Replace报告一次IOException，无法删除被替换文件。失败日志为`out/m5-full-debug.log`，对应生成输入保留，未强制删除/覆盖、未换用更强的文件操作，也未认定是外部锁或杀毒软件原因。独立复测使用新的生成目录36/36通过；随后完整Debug/Release回归均通过，原失败不隐藏。

## 保留限制

当前为缓存整段文字纹理，不是glyph atlas/SDF/彩色emoji或完整多字体fallback；缺字拒绝。真实IME composition/候选窗、鼠标文本选区与原生caret命中尚未完成。GLFW适配器处理已提交字符；synthetic composition检查不替代真机输入法。

共享UiCanvas可以提供独立UI/活动帧叠加，但正式Editor画布、Player项目UI加载、动作样例HUD、专用UI离屏目标、资产cook/发布剪裁和高级控件仍不能记为完成。M5.7至M5.10继续承担相应接线和验收；专用纯2D裁剪包未完成，不把“没有初始化3D资源”写成“已完成裁剪发布”。

## 使用基线

完成Build.bat后，本机样例入口为`managed/Ncma.Ui.Sample/bin/Debug/net8.0/NcmaUiSample.exe`，Release对应Release目录；不是替换NcmaEngine.exe的正式Player入口。

```powershell
.\Build.bat -Configuration Debug
dotnet managed/Ncma.Ui.Sample/bin/Debug/net8.0/NcmaUiSample.dll --plugins out/build/windows-ninja-debug/m2/plugins
```

样例需要本机msyh.ttc或通过`--font`明确指定适配字体。样例读取本机字体仅用于本地运行，不授予再分发权。Native文字实现依据[DirectWrite内存字体](https://learn.microsoft.com/en-us/windows/win32/directwrite/custom-font-sets-win10)与[Direct2D软件文字栅格](https://learn.microsoft.com/en-us/windows/win32/direct2d/server-side-rendering-overview)的公开契约，Windows外平台未验。
