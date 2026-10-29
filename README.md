# Reticle Studio · CS 准星工具

在 Windows 上制作自己的 FPS 准星：十字、斜十字、圆环、中心点、描边、颜色、透明度和位置都可以调整，中文编辑器实时预览。

**独立覆盖版 v0.3.0-independent.2** 提供完整安装包，通过单独的 Windows UIAccess 组件显示准星，面向 **CS 全屏、Windows 全屏优化开启**的使用场景，无需安装 Xbox Game Bar 或旧版 Reticle。安装时可以选择创建桌面快捷方式。

![设置界面](docs/editor.png)

## 下载：先选择需要的版本

三个系列分别使用自己的发布页面和附件。独立覆盖版采用 `independent` 版本号、独立源码分支、配置目录和更新系列，稳定桌面版与 Game Bar 预览包继续保留。

| 系列 | 下载入口 | 适用方式 | 安装要求 |
| --- | --- | --- | --- |
| **独立覆盖版 v0.3.0-independent.2** | [独立版 Release](https://github.com/yangr8640-eng/reticle-studio/releases/tag/v0.3.0-independent.2) · [完整 ZIP · 细线实色修复](https://github.com/yangr8640-eng/reticle-studio/releases/download/v0.3.0-independent.2/Reticle-Independent-v0.3.0-independent.2-win-x64.zip) | “全屏 · 独立覆盖（实验）”，目标为 CS 全屏且全屏优化开启 | 一个 ZIP 包含编辑器、组件、.NET 运行时和安装工具；首次安装需管理员权限及明确的预览证书信任操作 |
| 稳定桌面版 v0.2.1 | [稳定版 Release](https://github.com/yangr8640-eng/reticle-studio/releases/tag/v0.2.1) | 全屏窗口化 / 无边框 / 窗口化 | 解压运行编辑器 |
| Game Bar 预览版 | [编辑器 preview.4](https://github.com/yangr8640-eng/reticle-studio/releases/tag/v0.3.0-preview.4) ＋ [组件 preview.2](https://github.com/yangr8640-eng/reticle-studio/releases/tag/v0.3.0-preview.2) | “全屏 · Game Bar（实验）” | 安装并固定 Game Bar 小组件、开启鼠标穿透 |

普通用户下载 Release 页面 **Assets** 中的程序 ZIP。GitHub 自动生成的 **Source code (zip/tar.gz)** 是源码，需要开发环境构建，不能代替完整安装包。查看全部发行包：[GitHub Releases](https://github.com/yangr8640-eng/reticle-studio/releases)。

**最新版 v0.3.0-independent.2 修复不透明度 100% 时细线仍偏淡的问题**，同时包含安装器修订 4。准星采用硬边像素绘制，斜线与圆环会有像素阶梯；[修复说明与验证](docs/opacity-rendering.md)。[安装包 SHA-256](https://github.com/yangr8640-eng/reticle-studio/releases/download/v0.3.0-independent.2/Reticle-Independent-v0.3.0-independent.2-win-x64.zip.sha256) 可用于核对下载文件。

独立版源码分支：[codex/independent-overlay](https://github.com/yangr8640-eng/reticle-studio/tree/codex/independent-overlay)。它的程序、配置和更新系列与旧版分开，不会要求先安装其他 Reticle 版本。

仓库默认分支 `codex/reticle-mvp` 维护稳定桌面版源码；需要构建独立覆盖版时，请切换到上面的独立分支。下面的独立版构建命令针对该分支。

## 独立覆盖版：从下载到游戏内开启

详细命令、更新、卸载和故障处理见 [独立覆盖版完整指南](docs/fullscreen-independent.md)。首次使用可按以下步骤操作。

### 1. 解压完整安装包

需要 **Windows 10/11 x64**、管理员安装权限。运行时已经包含在包中，使用者无需安装 .NET SDK，也无需先装稳定版、Game Bar 版或 Xbox Game Bar。

下载 `Reticle-Independent-v0.3.0-independent.2-win-x64.zip`，先在 ZIP 的“属性”中选择“解除锁定”（若有此选项），再解压到一个新的空目录。ZIP 自带一层同名根目录；进入同时包含以下内容的那一层：

```text
Setup-Independent.cmd
setup-independent.ps1
install-independent.ps1
Start-Reticle.cmd
package-files.json
INSTALL.txt
README.md
editor/
host/
ReticleStudio.Independent.Preview.cer
```

保留完整目录，不要只复制 EXE，也不要重复解压进已经存在的同名安装包目录。安装器会核对清单与文件哈希，额外混入一套包会被拒绝。

### 2. 选择安装选项

双击 **`Setup-Independent.cmd`**，阅读界面显示的签名与安装信息。

- **桌面快捷方式可选**：图形界面默认勾选，可以取消；选择后在所有用户桌面创建“Reticle Studio 独立覆盖版”。命令行仅在提供 `-CreateDesktopShortcut` 时创建。
- **首次预览证书信任需明确选择**：这会把本包显示的那一张公钥证书加入本机受信任根证书存储，使 Windows 能授予 UIAccess。签名主体、指纹和有效期可与 `INSTALL.txt`、Release 页面核对。
- 执行安装后，完成 Windows 管理员权限提示。组件与编辑器会安装到 Program Files 下各自的目录。

希望使用命令行时，在**安装包目录的 64 位管理员 PowerShell** 运行：

```powershell
# 首次安装，并创建桌面快捷方式
.\install-independent.ps1 -TrustPreviewCertificate -CreateDesktopShortcut

# 首次安装，不创建桌面快捷方式
.\install-independent.ps1 -TrustPreviewCertificate
```

`-TrustPreviewCertificate` 只用于本包的自签名预览证书。证书已经被 Windows 信任时可以省略。只读核对命令是 `.\install-independent.ps1 -VerifyOnly`。

### 3. 打开独立版并开启准星

1. 双击“**Reticle Studio 独立覆盖版**”桌面快捷方式；未创建快捷方式时，运行安装目录中的编辑器：

   ```text
   C:\Program Files\Reticle Studio\Independent\Editor\Reticle.exe
   ```

2. 确认显示方式为 **“全屏 · 独立覆盖（实验）”**，点击 **“开启独立准星”**，完成辅助组件的 Windows 权限提示。
3. 确认状态显示 **“独立准星已开启 · UIAccess 已确认”**，再切回游戏。Game Bar 可以保持关闭。

安装完成后，编辑器与组件从受保护安装目录运行，桌面快捷方式不会依赖下载或解压位置，可以删除安装包的解压目录。快捷方式的 `--independent` 参数用于选择独立显示方式，开启准星仍由用户控制。

未创建快捷方式时，也可以运行 `C:\Program Files\Reticle Studio\Independent\Start-Reticle.cmd`。若下载标记或执行策略阻止启动，见 [独立指南的故障处理](docs/fullscreen-independent.md#故障排查)；安装入口仅为当前 PowerShell 进程指定 RemoteSigned，不更改全局策略。

### 4. 设置 CS 与全屏优化

CS 视频设置选择 **全屏**。退出 CS 后，在实际 `cs2.exe` 的“属性 → 兼容性”页，**取消勾选“禁用全屏优化”**，应用并重启 CS。

这里的双重否定容易混淆：**“禁用全屏优化”不勾选 = 保持全屏优化开启**。工具不会自动更改这个选项、CS 文件、启动项或显卡设置。

准星显示后，可以在编辑器调整外观、显示器、水平/垂直偏移，修改会同步到独立组件。多屏时先选游戏所在显示器，再点“重新居中”。

独立覆盖目前为实验支持。Windows 权限确认与桌面绘制检查不能证明所有 CS、驱动、HDR 或 Vulkan/OpenGL 全屏组合兼容，也不代表固定的帧率或输入延迟；实际游戏效果需要在自己的配置中验证。

## 日常使用

| 操作 | 方式 |
| --- | --- |
| 显示 / 隐藏准星 | `Ctrl + Alt + H` 或主界面开关 |
| 打开设置 | `Ctrl + Alt + S`，或左键单击托盘图标 |
| 隐藏设置窗口继续使用 | 点击“收起到托盘” |
| 完全退出 | 关闭编辑器窗口的 ×，或托盘右键选择“退出”；独立准星也会关闭 |
| 调整位置 | 选择显示器，调整偏移，或点击“重新居中” |
| 保留 / 迁移外观 | 自动保存，或使用“导出配置 / 导入配置” |

快捷键被其他程序占用时界面会提示，仍可使用按钮和托盘。首次启动准星处于关闭状态。独立版配置保存在 `%LOCALAPPDATA%\ReticleStudio\Independent\settings.json`；卸载会保留个人配置。需要复用旧版外观时，可在旧版导出 JSON，再在独立版导入。独立组件与编辑器失联后会停止显示。

## 更新与卸载独立版

独立版“检查更新”识别 `independent` 系列。发现新版后打开对应发布页，下载完整 Independent ZIP；退出独立准星和编辑器，再按相同流程安装。程序不会自动替换正在运行的安装文件。快捷方式创建选项可在安装时选择。

卸载前先关闭独立版，用 64 位管理员 PowerShell 运行安装包或安装目录中的 `uninstall-independent.ps1`。完整路径命令见 [卸载指南](docs/fullscreen-independent.md#卸载)。

```powershell
# 卸载独立组件、已安装独立编辑器及它创建的快捷方式
.\uninstall-independent.ps1

# 同时删除本安装器原先添加的预览证书
.\uninstall-independent.ps1 -RemovePreviewCertificate
```

独立版卸载不会删除其他版本的便携目录或个人准星配置。证书仅在显式选择删除时处理。

## 其他显示方式

### 稳定桌面模式

下载 v0.2.1，解压运行 `Reticle.exe`。CS 设置为**全屏窗口化 / 无边框**或窗口化，在工具中选择显示器并开启桌面准星。桌面覆盖层会恢复置顶和位置；真正独占全屏不是普通桌面模式的支持目标。

### Game Bar 模式

安装 preview.4 编辑器与 preview.2 的 Game Bar 组件。在游戏中按 Win+G，打开“Reticle Studio 准星”小组件，固定并开启鼠标穿透；编辑器选择“全屏 · Game Bar（实验）”后开启。具体见 [Game Bar 安装与排障](docs/fullscreen-gamebar.md)。

## 外观功能

- 十字、45° 斜十字、圆形准星，可选中心点及黑色描边。
- 线宽、线长、中心间距、圆半径、HEX 颜色和不透明度。
- 可视化调色盘：色相条、饱和度 / 明度区域、精细滑块，与 HEX、预设双向同步。
- 四个外观预设、1× / 4× 预览、三种预览背景。
- 显示器选择、水平 / 垂直偏移、重新居中、JSON 导入导出。
- 托盘、快捷键、配置自动保存、损坏配置备份及手动检查更新。

尺寸使用物理像素。中心间距是中心到线条内端的径向距离，斜十字旋转后保持相同距离；圆形用半径控制中心留白。4× 预览不改变实际准星大小。

在“颜色与透明度”中展开“调色盘 · 自由选色”，拖动色相和颜色区域即可调色。颜色区域获得焦点后，方向键每次调整 1%，Shift＋方向键每次调整 10%。

![调色盘](docs/color-palette.png)

## 常见安装问题

| 现象 | 处理 |
| --- | --- |
| `CommandNotFoundException`，找不到 `.\install-independent.ps1` | 当前目录可能是 `C:\Windows\System32`。先进入包目录，或用 `& '完整脚本路径'`；详见独立指南 |
| 双击 CMD 后 `GetFullPath` 提示“路径的形式不合法” | 下载上方安装器修订 4，重新解压到新目录；旧包可按下方命令显式指定包目录启动 |
| `Unlisted files found in package` | 包被重复解压或混入其他文件。重新解压到新空目录，进入同时有脚本、`editor`、`host` 和清单的那一层 |
| 显示需要信任证书 | 核对本包公开证书信息；首次预览安装需明确选择信任或使用 `-TrustPreviewCertificate` |
| 找不到组件 / UIAccess 未授予 | 完整安装组件，保留正常签名、受保护安装目录及 UAC 设置；记录完整错误 |
| 桌面快捷方式不可见 | 安装时选择创建选项；该快捷方式在所有用户桌面，也可从已安装编辑器路径启动 |
| 显示已开启，游戏里不可见 | 核对独立模式、CS 全屏、全屏优化、显示器、透明度和偏移，再记录 Windows / 显卡 / 渲染接口供排查 |

旧包的临时启动方式：在包含 `setup-independent.ps1` 的目录中打开 PowerShell，执行：

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy RemoteSigned -File .\setup-independent.ps1 -PackageDirectory $PWD.Path
```

随后按图形安装界面选择选项并完成安装。修订 4 可直接双击 `Setup-Independent.cmd`。

## 开发与验证

源码开发需要 Windows x64 和 .NET 10 SDK。普通用户的完整安装包已经包含运行时。

```powershell
# 构建与核心 / 组件检查
.\scripts\build.ps1

# 桌面诊断：使用独立测试配置并短暂创建测试窗口
.\scripts\test-desktop.ps1

# 构建完整的独立版安装包
.\scripts\build-independent.ps1
```

独立版默认构建使用一次性的预览代码签名证书，只分发公钥；构建不会将证书加入根信任或安装程序。正式代码签名证书可通过构建参数指定，见 [独立版构建说明](docs/fullscreen-independent.md#从源码构建)。

```text
src/Reticle.Core/         参数、几何、配置与独立通信协议
src/Reticle.App/          中文 WPF 编辑器、桌面显示与独立组件桥接
src/Reticle.OverlayHost/  独立 UIAccess 覆盖进程
src/Reticle.GameBar/      UWP Game Bar 小组件
tests/                   核心与协议等检查
scripts/                 构建、安装、卸载和桌面诊断
docs/                    详细指南与验证记录
```

验证范围和未覆盖场景见 [测试记录](docs/testing.md)。独立覆盖不读取、修改或注入游戏进程。仅在用户主动检查更新时连接 GitHub 获取公开版本信息，不上传准星配置；检查更新不会自动安装程序。请按所玩游戏及赛事规则使用。

实现参考：[Windows 分层窗口](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)、[SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)、[UIAccess](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)、[DXGI 呈现机制](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model)。
