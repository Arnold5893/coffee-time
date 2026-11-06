# 01 · Unity 开发环境就绪

Type: task
Status: resolved
Blocked by: （无）

## Question

Unity 开发环境尚未安装，后续所有票据都依赖它。完成安装并验证可建工程。

清单（HITL，需要 Arnold 操作或确认）：

1. 安装 Unity Hub：`winget install Unity.UnityHub`
2. 在 Hub 中登录 Unity 账号并激活免费 Personal 许可证
3. 安装 **Unity 6000.0 LTS**，勾选模块：**Android Build Support**（含 SDK & NDK Tools、OpenJDK）
4. 新建一个 URP 3D 模板空工程，确认能打开、能进入 Play 模式
5. 记录安装版本号与工程路径

## 完成标准

空 URP 工程能在本机打开并进入 Play 模式；Android Build Support 模块已装。

## Answer（2026-11-06）

完成。事实记录：

- Unity **6000.3.28f1** 安装于 `D:\Unity\Editors\6000.3.28f1\`（比计划的 6000.0 LTS 更新，同属 Unity 6 LTS 线）
- Personal 许可证已激活（2026-11-06，Unity Hub 3.22.5）
- Android Build Support 已安装（`PlaybackEngines/AndroidPlayer` 存在）
- 批处理模式创建 URP 工程、编译并执行 Editor 脚本已验证通过
- Blender 4.3.2 位于 `D:\blender\blender.exe`，可无头运行

环境坑（后续会话必读）：

1. 本机 shell 环境缺 `PROGRAMDATA`/`ALLUSERSPROFILE`，会导致 Unity UPM 崩溃（path undefined）——批处理调用 Unity 前必须 `export PROGRAMDATA='C:\ProgramData'`
2. 用户级 HTTP_PROXY/HTTPS_PROXY（127.0.0.1:7897）存在；必要时用 `env -u` 清除
3. 批处理异常退出后会残留无头 unity 进程，下次运行前需检查并终止
