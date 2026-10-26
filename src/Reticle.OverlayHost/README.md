# 独立全屏覆盖组件

此进程为 Reticle Studio 的独立显示后端。它使用自己的 UIAccess 窗口绘制准星，通过本机命名管道接收编辑器参数，不依赖 Game Bar，也不访问游戏进程。

项目链接桌面后端已有的 `OverlayWindow`、`CrosshairView` 和 Win32 窗口接口，复用准星几何、透明度、物理像素定位、鼠标穿透和不激活行为。`app.manifest` 声明 `highestAvailable` 和 `uiAccess=true`；是否实际获得权限仍以 `GetTokenInformation(TokenUIAccess)` 的运行时结果为准。

正式会话启动参数由编辑器生成：

```text
Reticle.OverlayHost.exe --pipe <管道名称> --session <32位十六进制会话标识> --editor-pid <进程ID> --editor-start <UTC启动时间ticks>
```

组件验证指定编辑器的进程 ID、启动时间、Windows 会话以及管道服务器的真实进程 ID。连接使用受限的显示协议，不接受文件、命令行或任意系统操作。消息必须属于同一会话且顺序递增；断开连接、编辑器退出或超过 10 秒没有有效命令时，组件关闭准星并退出。

组件首先发送 `hello`，其中包含真实的 `UiAccess` 结果。如果权限未授予，发送 `error` 并退出，不创建覆盖窗口。收到 `state` 后应用参数、显示或隐藏窗口，再发送对应序号的 `applied`。`stop` 结束会话。

诊断命令：

```text
Reticle.OverlayHost.exe --diagnose C:\完整路径\新报告.json
```

报告文件必须尚不存在，父目录必须已存在。此模式验证实际 UIAccess 权限；权限为真时才短暂显示准星并检查窗口标志、焦点、位置、偏移、显示隐藏和绘制像素。未授予权限时仅生成拒绝报告，退出码为 2。诊断不会把普通置顶窗口当作独立全屏模式成功。

组件需要可信签名及受保护安装位置。开发目录中的未签名 apphost 通常无法直接启动；通过 `dotnet Reticle.OverlayHost.dll --diagnose ...` 可以验证拒绝路径，但该方式的权限属于 dotnet 进程，不能用于证明 UIAccess 成功。

桌面检查不能证明 CS 的实际全屏优化路径、MPO、帧时间、输入延迟或游戏规则兼容性；这些需要玩家在目标机器验证。
