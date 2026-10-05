# M3.1 资产基础实施记录

日期：2026-10-05。状态：功能补齐，最终 Debug/Release 回归通过，G1 已关闭。

## 已实现范围

新增 Ncma.Assets、Ncma.Assets.Authoring 和 Ncma.Assets.Tests，接入 NcmaEngine.sln、CMake/CTest 和 canonical Build.bat。基础资产库仅依赖 BCL，不依赖 World、Editor、native 或 Python；作者服务依赖唯一 Editor.Core 历史，不拥有第二套 World/Undo。

- `.ncmeta` JSON v1 保存持久 UUID、类型、来源/hash、导入器/设置、子资产映射、依赖及可空成功 generation。严格检查必需字段、重复/未知字段、枚举、预算与路径。
- `.nca` 数据容器 v1 使用 `NCA1` magic、16-byte header、72-byte block entries，整数 little-endian，UUID 按 RFC 字节序编码；校验块范围、连续布局、排序和 SHA-256。它不是已经实现的 mesh/rig/clip 内容编解码器。
- 只读 AssetCatalog 持有独立副本，检查跨根/子资产重复 UUID、来源大小写冲突和类型引用；tombstone 保留身份但不能解析为活动资源。索引可由描述重建，未知来源文件不自动生成身份或被删除。
- AssetProjectPaths 拒绝路径穿越、ADS、保留文件名、非规范大小写和链接/reparse；AssetCatalogScanner 有描述/树条目预算，未恢复 journal 阻止索引读取。
- AssetChangeQueue 接收 watcher 意图，不写文件或 live World；队列最多 128，溢出要求重新扫描，意图带 project UUID/generation。AssetProjectAuthoring 在宿主消费时校验会话/路径，关闭撤销旧授权。正式 Editor 已挂载只读项目服务。
- Editor.Core 增加可信启动注册的可逆命令参与者。memento 计入原有 64 项/16 MiB 历史；重试缓存中的参与者数据亦受 16 MiB 限制。Prepare/Authorize/Validate/Publish/Compensate 回调受 World read-only guard 保护。
- `ncma.assets.metadata.edit` 支持元数据创建/导入设置；`ncma.assets.files.edit` 提供源文件和元数据联合移动、复制。移动保留根/子 UUID 和派生版本；复制使用获批的新根 UUID、生成新子 UUID、按 source key 重映射内部依赖，清空成功 generation 等待真正导入。默认只读，校验 scene/session revision、共享 AssetRevision、项目 generation 和宿主精确 metadata/source/root UUID 授权。撤权对缓存重放和历史同样生效。
- 多文件事务使用同目录 staging/backup、CreateNew/flush、有界 journal 和不覆盖目标的句柄 rename。发布失败补偿；补偿失败或 World 安装后的完成故障冻结并 invalidates history，不冒充完整回滚。未决重启回滚，持久完成决议后重启只清理生成文件，恢复必须再次得到精确 grants。
- 冻结格式文件位于 `managed/Ncma.Assets/Schemas/ncmeta-v1.schema.json` 和 `nca-v1.layout.json`，嵌入 assembly 并测试字段/枚举/布局。JSON Schema 不替代 duplicate/路径/跨资产唯一性/文件系统检查。
- AssetProjectAuthoring 持有唯一项目写锁、共享时钟、完整索引、启动恢复和 watcher 生命周期。关闭立即撤销旧参与者授权；刷新失败保留上一份完整索引。正式 EditorSessionOwner.ConfigureAssets 默认无写权限，UI History 共用 EditSession。资产面板/交互 grant/MCP 留给 M3.6/M3.8。

Windows 私有作者存储适配器锁定父目录和文件，检查句柄类型、reparse/hard link、文件身份/hash；目标出现外部新占位即使字节相同也拒绝覆盖。依据 [CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) 和 [FILE_RENAME_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info)。这不是对恶意同权限进程的安全沙箱；非 Windows 作者事务目前明确不支持。

事务最多四个精确 assets/ 文件状态。元数据内联；原始来源不放入 16 MiB history，使用 ignored `out/asset-authoring/<projectUuid>/blobs/` 不可变快照。单源 256 MiB，池最多 128 文件/1 GiB；耗尽拒绝，不擅自回收可能被历史/恢复引用的数据。G2 再补通用 generation lease/pin/GC。journal v1 保存事务 UUID、方向、完成标志、有界 memento、各目标/生成路径及前后文件身份/hash；未知、损坏或未批准 journal 保留并拒绝继续写入。这里只删除本事务生成且验证的 staging/backup/journal。

元数据创建只保存经授权的来源/hash，不解析或验证完整 FBX 数据。现有 Character ABI 2、Renderer ABI 1.1 与 GUI ABI 1.2 未修改；`.ncmascene` 仍为既有 JSON v1。

## 验证记录

资产专项测试已覆盖格式、UUID/类型/tombstone、NCA 字节布局/损坏/溢出、非对称 TRS、重启扫描、未知文件保留、实际 Windows junction 拒绝、有界 watcher、唯一历史交错、默认只读、旧 revision、撤权重放、外部文件改写、逐发布点故障、重启恢复、owner-thread/只读保护、预算与 redo 分支。

最终代码已顺序执行 `Build.bat -Configuration Debug` 与 `Build.bat -Configuration Release`，均 exit 0，C# 编译 0 warnings/0 errors。包含最后的项目写锁句柄加固，结果如下。

| 验证 | Debug | Release |
| --- | --- | --- |
| Native CTest | 8/8 | 8/8 |
| Managed/窗口/部署等 CTest，含资产测试入口 | 17/17 | 17/17 |
| 资产入口内专项断言 | 36/36 | 36/36 |
| Managed/native smoke | 通过 | 通过 |
| Python inspect 与 unittest | inspect 通过，42/42 | inspect 通过，42/42 |
| 既有 M2 三轮 profile 与 pre/post deployment audit | 完成，audit_passed=true | 完成，audit_passed=true |

两组 CTest 合计各 25 项，36 个资产案例属于其中的 NcmaAssetTests，不重复计成额外 CTest 项。新增用例包括多文件逐发布点故障、移动/复制 UUID/Undo、启动恢复、同字节新文件身份、并发写与父路径替换拒绝、完成故障冻结、损坏 journal 和扫描条目/总字节压力。

最终日志为 `out/verification/m3-1/Debug-final.log`、`Release-final.log`；初轮日志为 `Debug-completion.log`、`Release-completion.log`。生成夹具位于 ignored bin/ 或 out/。

完整构建按受检部署流程更新 `out/bin/NcmaEngine.exe`，保留 `out/deployment/` 可恢复备份；资产服务已注册为项目级默认只读。M2 audit 仍为 h8_accepted=false，profile 是测量而非性能/长稳验收通过。

## G1 与后续边界

此前四项缺口已落实为代码和专项案例：联合移动/复制、项目生命周期与共享 revision、句柄租约/外部占位拒绝、冻结格式与多文件恢复/预算。最后的双配置回归通过，G1 已关闭；随后开始 M3.2。

G1 通过仅表示资产元数据底座可用，不能称 FBX 已进入场景。完整 FBX 异步导入、GPU 资源/蒙皮、Editor 资产 UI、Prefab/MCP 与 Player cook 属于后续阶段。`.ncmaterial`/`.ncprefab` 类型格式分别由 M3.3/M3.7 落实。M2 人工 UI/MCP、自包含环境、性能/长稳验收继续待完成。

## 工作区与交付边界

本次不提交或推送 GitHub，保留既有 `.vs/` 和 `.vcxproj.user` 修改。测试中的文件移除只针对本次生成的临时元数据、journal/temporary 文件，不删除项目来源、用户 FBX 或旧缓存。
