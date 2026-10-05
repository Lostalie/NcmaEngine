# M3.6 GUI 1.3 opaque image 契约

日期：2026-10-06。Renderer 公共 ABI 1.2/query1–5 不变；GUI additive minor1.3。

## 固定布局与数据

GUI table104、item96、event72、frame48 字节保持冻结。ItemKind13=Image、14=AssetButton；使用新语义必须成功协商 GUI1.3。其他 item 的 reserved words 必须全零。

Image.reserved[0..3] 是 GPU target value 的低/高32位、generation 的低/高32位。value/generation 非零；只允许该 renderer 的当前 live view target。它不是 SRV 指针、COM 对象、任意 texture 或跨 renderer 通用句柄。rect 是逻辑屏幕绝对 x/y/w/h，四项有限、0<w/h≤16384，x/y允许超出当前窗口并由GUI裁剪。C# `GuiImageToken` 不拥有 target；`GpuViewTarget` 仍拥有生命周期。

AssetButton 的 value text 是 canonical lowercase UUID36，native drag payload 固定37字节含 NUL。Image phase3：value=0/text=`u v` 是0..1规范化点击；value=1/text=UUID36 是资源拖放意图。GUI 不解码项目文件、执行 import 或写 World。C# 复核 frame/view/document/revision、enabled item、typed catalog 和权限；拖放只准备放置计划。

## 租约、顺序、线程

所有 GUI/renderer 操作均 owner-thread。GUI.Draw 先验证完整有界批次/文本/UUID/target，再 pin target；每 GUI frame≤64个 Images、每 target≤64 pins。target Destroy 在 pins存在时返回 BUSY；RenderGpu 后或后续 Begin/Destroy 丢弃 CPU draw 时释放 pins。重试已保留的失败 draw 先返回 BUSY，避免覆盖 pin 数组；下次 Begin 负责弃帧。

native-only `ncma_renderer_gui_image_v1` 在 frame=0 做 CPU resolve/retain，在 frame>0 只允许当前 active renderer 与 target 的 exact submitted frame；retain只能frame0。查询出的 SRV 仅 GUI.cpp 内部临时借用，绝不进入 managed ABI。release 必须匹配 live target/pin，重复/foreign/stale拒绝。renderer DLL 在 GUI renderer borrow 释放前不能 shutdown/unload。

正确顺序：Platform poll → GUI.Begin → managed presentation → GUI.Draw(CPU pins) → scene/resource submit 到 target → GUI.RenderGpu(exact frame resolve，绑定 swapchain、不重新 clear) → Renderer.Present 一次。GUI 不自行 Present；旧/未初始化 target 不允许呈现。失败 preflight 不提交 GPU；GPU故障仍遵守 renderer fail-stop，不声称图形回滚。

关闭顺序：场景 GPU资源 → GUI(释放 pins和renderer borrow) → viewport target → renderer → window/module。target 的 GPU完成/销毁仍由现有资源接口保证。resize先创建候选、旧 target 无 pins后释放再安装；异常销毁候选，保留原租约。最小化不提交零尺寸 target。

## 范围

当前仅 DX11实现/验证，Vulkan不宣称可绘制。普通跨帧缓存缩略图还需要明确的多视图提交与缓存生命周期协议；本契约禁止把其他帧 target 任意伪装成当前提交帧。接口不暴露 GPU live pointer、Agent allocation 或任意 shader/command执行。
