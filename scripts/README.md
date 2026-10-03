# 物理引擎 SDK 下载指南

## 自动下载脚本

### PowerShell 脚本 (推荐)
```powershell
.\DownloadPhysicsSDKs.ps1
```

## 手动下载

如果自动下载失败，请手动下载：

### Box2D (2D 物理)
1. 访问: https://github.com/erincatto/box2d/releases
2. 下载最新版本的 `box2d-X.X.X.zip`
3. 解压到 `engine/sdk/box2d/`
4. 确保头文件在 `engine/sdk/box2d/include/` 目录

### Jolt Physics (3D 物理)
1. 访问: https://github.com/jrouwe/JoltPhysics/releases
2. 下载 `Jolt-X.X.X.zip` (或最新版本)
3. 解压到 `engine/sdk/jolt/`
4. 确保头文件在 `engine/sdk/jolt/include/` 目录
5. 确保库文件在 `engine/sdk/jolt/lib/` 目录

## 目录结构要求

```
engine/sdk/
├── box2d/
│   ├── include/
│   │   └── box2d/
│   │       └── box2d.h
│   └── lib/
│       └── box2d.lib
└── jolt/
    ├── include/
    │   └── Jolt/
    │       └── Jolt.h
    └── lib/
        ├── JoltPhysics.lib
        └── JoltPhysics_static.lib
```

## 验证安装

配置完成后，重新编译项目验证物理引擎集成是否成功。
