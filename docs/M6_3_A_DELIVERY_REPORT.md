# M6.3 A 数值混合交付记录

候选新增独立pose-blend1.0数值接口，原pose1.0冻结；C#显式启用、同context/rig的有界TRS混合与
模型组合。完整顺序Debug/Release回归均以0退出，A自动候选通过。M6.3其余接线未实现，
分切片范围见 [M6.3计划](M6_3_IMPLEMENTATION_PLAN.md)。

ABI：NcmaPoseBlendRequestV1为32B、stats24B、table32B；版本1.0，新导出ncma_pose_get_blend_api。
最多32请求、65536输入TRS、32768输出骨骼，有限单位四元数与uniform-positive-scale，权重0到1；
原资源关闭/线程/CodePin规则不变。独立计数不是GPU或图性能遥测。失败前不发布输出/counters，
没有World、graph policy、C#引用或STL跨ABI。

新增原生契约/负例和3项托管数值oracle、批次/生命周期、32×1024/warm分配测试纳入Build.bat。
完整日志为`out/m6-3-a-debug.log`和`out/m6-3-a-release.log`；均未使用Skip标志。两配置各12/12原生、
22/22托管CTest，图/pose/clock35/35、Editor72/72、Player56、Gameplay53/fakeMovement38、Python43通过，
managed/native smokes、严格格式与旧格式拒绝、inspect、三轮profile、pre/post审计、检查式完整部署通过。
新代码编译零警告/错误。32×1024骨骼批次在warm后的32次Mix测得当前线程新增托管分配0B；
不包含初始化/整套host/真实游戏或GPU费用，不是目标机性能验收。

Release101项manifest文件重新核验哈希通过，journal为Complete，generation为
`4cee09d5847e41559e0f3e95fa83dc09`；NcmaAnimationKernel.dll SHA256为
`A78DA26F8E4BDE94A165A2D405496C14210C9706C93E6CE7BAFD0B873BC4ACED`。
备份保留：`out/deployment/8c1d8343ef2b451d80563cfd32d8ef4c/backup`（Debug前）与
`out/deployment/4cee09d5847e41559e0f3e95fa83dc09/backup`（Release前）；不提交部署/备份/IDE个人设置。
审计audit_passed=true而h8_accepted=false，最终报告为
`out/verification/m2-8/Debug/965a8f0707e34d708bfebe15f4087b71/audit.json`和
`out/verification/m2-8/Release/e186d05133e54e37b193db3a7e097aa6/audit.json`。既有人工/目标/性能/长稳门禁仍开放。
该切片不新增图参数MCP、场景/Player绑定、根运动运动权威或GUI/GPU图执行。
