# Reticle Studio 独立覆盖版 · 完整安装与使用指南

版本：**v0.3.0-independent.2（2026-10-29），包含安装器修订 4**。本系列通过单独的 Windows UIAccess 覆盖进程显示准星，目标是在 **CS 保持全屏、Windows 全屏优化开启**时使用，无需 Game Bar。

这是一个可以单独下载安装的完整发行包，包含中文编辑器、独立覆盖组件、.NET 运行时、安装/卸载工具和公开签名证书。使用者无需安装 .NET SDK、旧版 Reticle 或 Xbox Game Bar。

独立系列使用 `independent` 版本号、[独立源码分支](https://github.com/yangr8640-eng/reticle-studio/tree/codex/independent-overlay)、[独立 Release 页面](https://github.com/yangr8640-eng/reticle-studio/releases/tag/v0.3.0-independent.2)、配置目录和更新系列。实际游戏兼容性取决于 Windows、显卡驱动及呈现设置，不能仅凭权限与桌面检查判定 CS 已经兼容。

## 一、下载正确附件

打开 [v0.3.0-independent.2 Release](https://github.com/yangr8640-eng/reticle-studio/releases/tag/v0.3.0-independent.2)，在 **Assets** 中下载：

```text
Reticle-Independent-v0.3.0-independent.2-win-x64.zip
```

直接下载：[完整 Windows x64 安装包 · 安装器修订 4](https://github.com/yangr8640-eng/reticle-studio/releases/download/v0.3.0-independent.2/Reticle-Independent-v0.3.0-independent.2-win-x64.zip)。[对应的 `.sha256` 文件](https://github.com/yangr8640-eng/reticle-studio/releases/download/v0.3.0-independent.2/Reticle-Independent-v0.3.0-independent.2-win-x64.zip.sha256)用于核对 ZIP 摘要。

本版修复不透明度 100% 时细线仍偏淡的问题：准星采用硬边像素绘制，斜线和圆环会有像素阶梯，小中心点保证可见。仍包含安装器修订 4，修复 Windows PowerShell 5.1 下双击 CMD 后的 `GetFullPath` 路径格式错误，无需另行下载旧版的 installer-fix 包。

**不要将 GitHub 自动生成的 Source code (zip/tar.gz) 当作程序安装包。** 源码压缩包需要开发环境编译，没有可直接供普通用户安装的完整运行时与签名组件。

系统要求：Windows 10/11 x64、足够的解压和安装空间，以及管理员安装权限。正常编辑准星不要求把整个编辑器以管理员运行；开启独立组件时可能出现 Windows 权限提示。

## 二、只解压一次，进入正确目录

下载完成后，右键 ZIP →“属性”，若有**“解除锁定”**选项，勾选并应用，再将 ZIP 解压到一个**新的空目录**。这样可以避免下载来源标记继续附着到解压后的脚本。

ZIP 自带 `Reticle-Independent-v0.3.0-independent.2-win-x64` 根目录，进入同时包含以下文件的那一层：

```text
Reticle-Independent-v0.3.0-independent.2-win-x64/
├─ Setup-Independent.cmd          图形安装入口
├─ setup-independent.ps1          图形安装脚本
├─ install-independent.ps1        命令行安装器
├─ uninstall-independent.ps1      卸载器
├─ Start-Reticle.cmd              启动入口
├─ README.md                     本指南
├─ INSTALL.txt                   本包签名身份、指纹和有效期
├─ package-files.json            完整文件与 SHA-256 清单
├─ ReticleStudio.Independent.Preview.cer
├─ editor/                       编辑器及所需内容
└─ host/                         独立覆盖组件和自带运行时
```

不要只拿走某个 EXE，也不要把 ZIP 再解压进已存在的同名包目录。若看到“同名目录里面又有同名目录”，应进入实际包含脚本、`editor`、`host` 和清单的那一层，或重新解压到新空目录。安装器会拒绝清单外文件和校验不一致的文件。

## 三、使用图形入口安装

1. 关闭独立准星，从托盘退出正在运行的独立版编辑器。
2. 双击包内 **`Setup-Independent.cmd`**。
3. 查看安装界面显示的目录和证书信息。公开证书的主体、指纹及有效期可以与包内 `INSTALL.txt` 和 Release 页面核对。
4. 按需选择**创建桌面快捷方式**。图形界面默认勾选，可以取消；选择后会在**所有用户桌面**创建“Reticle Studio 独立覆盖版”。命令行仅在提供 `-CreateDesktopShortcut` 时创建。
5. 首次安装自签名预览包时，明确选择信任本包预览证书。若该签名证书已被 Windows 信任，可以按界面提示直接安装。
6. 执行安装，完成 Windows 管理员权限提示，等待安装成功信息。

预览证书信任选项会把**这一个安装包所显示的公钥证书**加入 `LocalMachine\Root`（本机受信任的根证书颁发机构），以满足 UIAccess 的签名链要求。默认安装不会自动添加信任；证书私钥不会随包发布。无需关闭签名验证、UAC、系统防护或更改全局安全策略。

## 四、使用 PowerShell 安装

### 先进入安装包目录

打开**64 位管理员 Windows PowerShell**。Windows 10/11 的内置 Windows PowerShell 5.1 与 PowerShell 7 均可使用。管理员窗口通常从 `C:\Windows\System32` 打开，必须先进入解压目录。

例如，把下面路径替换为自己电脑上实际的解压位置：

```powershell
Set-Location -LiteralPath 'D:\Downloads\Reticle-Independent-v0.3.0-independent.2-win-x64'
```

也可以在资源管理器中复制脚本完整路径，使用 PowerShell 调用运算符 `&`：

```powershell
& 'D:\Downloads\Reticle-Independent-v0.3.0-independent.2-win-x64\install-independent.ps1' -TrustPreviewCertificate -CreateDesktopShortcut
```

上面 `D:\Downloads\...` 是示例，不是程序要求的固定目录。路径有空格或中文时，保留引号。

### 可先只读校验

```powershell
.\install-independent.ps1 -VerifyOnly
```

该命令核对文件清单、SHA-256、辅助程序签名和公钥证书身份，显示签名主体、证书指纹和有效期。它不会安装、导入证书、创建快捷方式或更改目录权限。

### 首次安装并创建快捷方式

```powershell
.\install-independent.ps1 -TrustPreviewCertificate -CreateDesktopShortcut
```

### 首次安装但不创建快捷方式

```powershell
.\install-independent.ps1 -TrustPreviewCertificate
```

### 证书已经受信任时安装

```powershell
.\install-independent.ps1 -CreateDesktopShortcut
```

此时也可以省略 `-CreateDesktopShortcut`。`-TrustPreviewCertificate` 是对**本包自签名预览证书**的显式信任操作，不会接受任意证书或跳过签名校验。

`Setup-Independent.cmd` 只为其启动的当前 PowerShell 进程指定 **RemoteSigned**，不修改用户或计算机的全局执行策略。RemoteSigned 仍会遵守 Internet 下载来源标记（Mark of the Web），因此应优先在**解压前解除 ZIP 的锁定**。

如果已解压且脚本被下载标记阻止，先确认 ZIP 来自本项目 Release、核对摘要，然后重新对原 ZIP 解除锁定并解压到新空目录；也可以通过文件属性“解除锁定”或 `Unblock-File` 处理已核对的相应文件。不要为了安装而永久放宽整个系统的执行策略；保留完整错误以便排查。

## 五、安装后文件在哪里

默认安装到系统 Program Files 下：

```text
C:\Program Files\Reticle Studio\Independent\Editor\Reticle.exe
C:\Program Files\Reticle Studio\Independent\Start-Reticle.cmd
C:\Program Files\Reticle Studio\OverlayHost\Reticle.OverlayHost.exe
```

如果 Windows 的 Program Files 位于其他盘，安装器会使用系统实际目录。安装目录仅允许管理员和 SYSTEM 写入，普通用户具有读取与执行权限。

选择创建的桌面快捷方式指向**已安装的独立编辑器**，工作目录也在安装位置，并以 `--independent` 参数选择独立显示方式。它不依赖解压目录。安装完成后可以删除下载 ZIP 和解压目录。

独立版安装不会替换旧版便携编辑器或 Game Bar 小组件，配置也分开保存：

```text
%LOCALAPPDATA%\ReticleStudio\Independent\settings.json
```

需要沿用旧版外观时，在旧版“导出配置”，再在独立版“导入配置”。

## 六、启动并确认独立覆盖

1. 双击桌面上的 **Reticle Studio 独立覆盖版**。未创建快捷方式时，可从上面的已安装编辑器路径启动。
2. 确认左侧显示方式为 **“全屏 · 独立覆盖（实验）”**。
3. 点击 **“开启独立准星”**，完成独立组件的 Windows 权限提示。
4. 查看状态是否显示 **“独立准星已开启 · UIAccess 已确认”**。
5. 选游戏所在的显示器，调整颜色、尺寸或偏移，再切回游戏。

桌面快捷方式参数只选择显示方式，不会绕过开启按钮。正常首次启动准星处于关闭状态。独立模式不需要打开 Win+G、固定小组件或运行 Game Bar。

“UIAccess 已确认”表示组件取得了所需权限并确认状态应用，不能替代观察游戏内准星是否实际可见。

## 七、CS 与全屏优化设置

1. 在 CS 视频设置中选择**全屏**并应用。
2. 退出 CS，在实际运行的 `cs2.exe` 上右键“属性 → 兼容性”。
3. **取消勾选“禁用全屏优化”**，点击“应用”。如果曾修改“更改所有用户的设置”，也检查其中对应选项。
4. 重启 CS，开启独立准星并观察游戏内显示。

这里应理解为：

| 兼容性复选框 | 含义 |
| --- | --- |
| “禁用全屏优化”**未勾选** | 保持 Windows 全屏优化开启，本独立模式的目标设置 |
| “禁用全屏优化”已勾选 | 请求禁用全屏优化，需要另外验证该配置 |

本工具不会自动改动该选项、游戏文件、启动项或显卡设置。全屏优化开启不等于始终使用同一呈现路径，也不能保证零开销或固定延迟。Vulkan/OpenGL 全屏、HDR、多显卡、可变刷新率等组合仍需实际验证。

## 八、调整与日常操作

| 操作 | 方法 |
| --- | --- |
| 改准星 | 选择十字 / 斜十字 / 圆形，调整尺寸、颜色、不透明度、中心点与描边；自动同步 |
| 多屏选择 | 选择游戏所在显示器，再点“重新居中” |
| 微调位置 | 调整水平、垂直偏移；尺寸以物理像素表达 |
| 显示 / 隐藏 | `Ctrl + Alt + H`，或主界面开关 |
| 打开设置 | `Ctrl + Alt + S`，或单击托盘图标 |
| 收起设置继续玩 | 点击“收起到托盘” |
| 完全退出 | 编辑器 × 或托盘右键“退出”，独立组件也会关闭 |
| 迁移外观 | “导出配置”保存 JSON，另一台电脑“导入配置” |

快捷键占用会在编辑器提示，仍可使用按钮和托盘。编辑器意外退出、连接断开或心跳超时后，独立组件会停止显示。

## 九、更新独立版

1. 在独立版中“检查更新”，或从对应 **Independent Release** 下载完整的新 ZIP。
2. 退出独立准星与独立编辑器。
3. 解压到新的空目录，再运行新包的图形入口或安装命令。
4. 核对新包证书。预览版本如果更换签名证书，可能再次要求显式信任，按本包指纹确认。
5. 安装成功后从桌面快捷方式或已安装目录启动。

独立版更新只识别 `independent` 系列，不会将稳定桌面版或 Game Bar 预览版当作独立版更新。安装器检查已有独立安装并更新自己的程序文件，保留个人准星配置。快捷方式创建选项由安装时选择；发现运行中的独立进程时会提示先退出，不会强制结束其他程序。

## 卸载

先关闭独立准星与独立编辑器。打开 64 位管理员 PowerShell，运行包内或已安装位置的卸载脚本：

```powershell
# 在解压包目录运行
.\uninstall-independent.ps1

# 同时删除本安装器原先添加的预览证书
.\uninstall-independent.ps1 -RemovePreviewCertificate
```

安装包已经删除时，可以从独立版安装目录运行：

```powershell
& 'C:\Program Files\Reticle Studio\Independent\uninstall-independent.ps1'
```

组件目录也保留同一卸载入口：

```powershell
& 'C:\Program Files\Reticle Studio\OverlayHost\uninstall-independent.ps1'
```

需要删除安装器原先添加的证书时，在该完整命令末尾添加 `-RemovePreviewCertificate`。可先运行 `-VerifyOnly` 查看将处理的安装身份、位置与证书归属。

卸载处理本独立版的编辑器、覆盖组件及其创建且目标匹配的桌面快捷方式。个人准星配置、旧版便携目录和 Game Bar 小组件会保留。证书只有显式选择且属于安装器记录的预览证书时才删除。

## 故障排查

| 问题 / 提示 | 原因与处理 |
| --- | --- |
| `.\install-independent.ps1` 不是命令或文件 | 当前目录不对。先 `Set-Location` 到包目录，或用 `& '完整路径\install-independent.ps1'`；管理员窗口默认的 System32 不是包目录 |
| 双击 CMD 后 `GetFullPath` 提示“路径的形式不合法” | 旧安装脚本在 Windows PowerShell 5.1 的 `-File` 入口未取得默认包目录；安装脚本修订 4 已修复。旧包可使用下方命令显式传入目录 |
| 脚本来自 Internet / RemoteSigned 阻止执行 | 核对来源和摘要，对原 ZIP 属性“解除锁定”后重新解压到空目录；图形入口不会忽略 Internet 下载标记，也不更改全局执行策略 |
| `Unlisted files found in package` | 重复解压或多出文件。解压到新空目录，进入实际包含清单、脚本、`editor`、`host` 的目录，保持包完整 |
| SHA256 不一致 / 文件缺失 | 重新从官方 Release 下载完整 ZIP 并核对摘要，再解压到空目录 |
| 证书未被信任 | 核对 Release 与 `INSTALL.txt` 指纹；自签名预览包首次安装需要明确的信任选项 |
| 安装目录账户无法映射 | 使用本独立系列完整包；安装器以原生 SID 检查权限，已包含中文 Windows 名称转换修复 |
| 安装目录权限异常 | 保留完整错误，检查是否曾修改该目录权限或使用链接目录；受保护目录应保持管理员可写、普通用户可读执行 |
| 组件 / 编辑器正在运行 | 关闭独立准星，从托盘退出独立编辑器后再安装或卸载 |
| “请先安装独立组件” | 完整运行安装器；只运行解压目录中的编辑器不等于组件已经安装 |
| Windows 权限提示被取消 | 再点“开启独立准星”，确认对应组件名称后完成权限提示 |
| UIAccess 未取得 / 握手失败 | 核对安装成功、签名受信任及受保护目录；重新安装完整同版本包，并记录完整错误 |
| 快捷方式没有出现 | 安装时选择创建或带 `-CreateDesktopShortcut`；共享桌面可能被 Windows“显示桌面图标”设置隐藏 |
| 已开启但游戏中不可见 | 核对显示方式、CS 全屏、全屏优化、显示器、透明度和偏移；记录 Windows 版本、显卡驱动、分辨率、HDR、渲染接口和状态文字 |

旧包遇到上述 `GetFullPath` 错误时，在实际包含 `setup-independent.ps1` 的目录中打开 PowerShell，运行：

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy RemoteSigned -File .\setup-independent.ps1 -PackageDirectory $PWD.Path
```

该命令显式指定当前包目录并打开图形安装器，之后按界面选择安装选项。中文目录和同名嵌套目录本身不会引起这次错误；无需修改包内脚本或清单。

安装或权限错误不应通过关闭签名验证、禁用防护或绕过反作弊处理。请将错误文字与环境信息作为排查依据。

## 从源码构建

需要 Windows x64 与 .NET 10 SDK：

```powershell
git clone --branch codex/independent-overlay https://github.com/yangr8640-eng/reticle-studio.git
Set-Location reticle-studio
.\scripts\build.ps1
.\scripts\test-desktop.ps1
.\scripts\build-independent.ps1
```

`build-independent.ps1` 生成完整 ZIP、SHA-256、签名组件、编辑器、运行时、图形/脚本安装入口、公开证书和文件清单。默认生成一次性、不可导出的预览签名私钥，构建结束后删除该私钥，不会自动安装或添加根信任。

使用正式代码签名证书时，通过 `-SigningCertificateThumbprint` 指定当前用户 `My` 证书存储中的证书。证书需要代码签名用途、可用私钥，以及安装机器认可的签名链。

微软将 UIAccess 定位为辅助技术能力，明确不建议仅为覆盖其他应用而使用。本系列用于技术与兼容性验证，正式发布适用性需要单独评估。参考：[微软 UIAccess 说明](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)。
