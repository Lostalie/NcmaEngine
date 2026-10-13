# M7.3-C3-C 正式产品联合验收实施方案

2026-10-14。上一阶段 local/remote main `7de102e3f8e6ddeb692f013eade47e1f23cf72e1` 已核对。按现有C3-C范围完成，双配置各490项产品检查、8个探针及完整顺序Debug/Release通过；见[交付](M7_3_C3C_DELIVERY_REPORT.md)。这是M7.3自动候选链闭合，不关闭人工/用户素材/完整性能门禁。以下保留实施方案，代次切换实际采用独立源项目的显式Off/刷新/重新绑定，详见[合同](M7_3_C3C_RUNTIME_CONTRACT.md)。

## 产品闭包与独立进程

从本配置 Build.bat 刚生成的 packages.json 取 Editor / DX11 Player / Null 的受检目录，按 manifest 精确复制到新的测试目录并再次校验。保留原部署和所有失败夹具，不修改受检源包。项目只携带场景、NCP、C# gameplay；不存在作者 ncmeta/ncenv/NCA/FBX 源闭包。作者夹具文件在运行期间独占锁定，不能回退读取。

实际搬移 apphost 使用原 CLI 跑 Editor smoke 与 Player ticks/report；坏 manifest/default shader/default NCE 或坏 NCP/资源身份必须拒绝，不能伪称成功。Player 报告核对 session/tick/frames/validation/shutdown，区分部署完整性拒绝与 pre-gameplay 资源拒绝。

另外用仅存在于 Rendering.Tests 的独立子进程探针验证画面与生命周期。探针在专用 AssemblyLoadContext 中强制从受检搬移产品加载所有 Ncma 生产程序集，缺文件立即拒绝、禁止退回仓库 build DLL。记录实际程序集路径/hash、native模块来源、部署完整性以及真实共享宿主的截图/计数；测试框架本身不是发布产品的一部分，也不新增生产 CLI/MCP 权限。默认资源根必须指向受检产品而非测试程序集目录。

## 联合画面与生命周期

静态受控场景采用正式默认 shader、项目NCP中的合成中性环境及定向环境，独立软件 cube/mip/LUT oracle 校验已知像素。旋转、强度、Off、显式资源身份/代次切换通过原已提交操作进入正式 Editor；检查失败清屏和恢复、Undo/Redo、Play/Reload/Stop、视口尺寸变化与关闭。

实际 ASCII/Binary FBX 导入为 NCA 后封装 NCP，以相同正式默认 shader 运行动画蒙皮、阴影、PBR 与环境；检查有效图像、姿态/环境变化、渲染计数、API0/0和关闭。原 B3 独立 CPU 蒙皮/软件与积分全图、金属/粗糙度/法线/AO/emissive 353 项继续保留作为算法证据，不把同一 shader 的截图当独立算法 oracle。

记录暖机后有界 CPU/托管分配/GPU有效样本/上传与资源计数。区分完整 Editor UI 成本与共享渲染静态零分配证据，不设未经测量的 FPS 承诺，不将短测量称为完整性能/1h验收。

## 轻量产品与失败边界

搬移 Null 产品执行相同运行包的精确 Headless ticks；无Renderer/Platform/GUI/Pose加载和环境GPU，部署不含Shader/NCE。保留独立UI受检18文件 apphost门禁并明确验证环境资源数为0；不能因编辑器资源部署扩大纯UI闭包。

坏NCP、错误UUID/generation/hash、坏默认闭包、无源运行、关闭后可重新打开/资源卸载均保留独立证据。目标机/.NET自包含和人工UI/DPI/真实第三方MCP/用户HDR-FBX素材不由这些检查关闭。

## 交付

定向失败修复后冻结源码；完整顺序无Skip Debug再Release Build.bat，包括旧全部测试、smokes、新/移除格式、Python43/inspect、三轮profile、audits/部署恢复。核对两次清单/备份/journal/apphost/原生Shader与NCE哈希，更新合同、交付、路线图和AGENTS。提交推送且远端一致后，才将M7.3自动候选链闭合并进入M7.4；整个引擎目标仍开放。DX11only，Vulkan/OpenGL下一版；M7→M8→M9。
