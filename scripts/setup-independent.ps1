[CmdletBinding()]
param(
    [string]$PackageDirectory = '',
    [switch]$InspectOnly,
    [switch]$InstallWorker,
    [string]$ResultPath = '',
    [string]$ExpectedCertificateThumbprint = '',
    [switch]$CreateDesktopShortcut,
    [switch]$TrustPreviewCertificate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Windows PowerShell 5.1 -File binds parameter defaults before PSScriptRoot is set.
# Resolve the omitted directory in the script body, where its value is available.
if (-not $PSBoundParameters.ContainsKey('PackageDirectory')) { $PackageDirectory = $PSScriptRoot }

function ConvertTo-ProcessArgument([string]$Value) {
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function New-SetupWorkerArguments([string]$SetupScript, [string]$PackageRoot, [string]$OutputPath, [bool]$Shortcut, [bool]$Trust, [string]$Thumbprint) {
    if ($Thumbprint -notmatch '\A[a-fA-F0-9]{40}\z') { throw '证书指纹必须是 40 位十六进制值。' }
    $arguments = @('-NoLogo','-NoProfile','-STA','-ExecutionPolicy','RemoteSigned','-File',$SetupScript,
        '-PackageDirectory',$PackageRoot,'-InstallWorker','-ResultPath',$OutputPath,
        '-ExpectedCertificateThumbprint',$Thumbprint)
    if ($Shortcut) { $arguments += '-CreateDesktopShortcut' }
    if ($Trust) { $arguments += '-TrustPreviewCertificate' }
    return $arguments
}

function Get-SetupPackage([string]$PackageRoot) {
    $installer = Join-Path $PackageRoot 'install-independent.ps1'
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
        throw '没有找到完整安装包。请先将 ZIP 解压到一个新目录，再打开同时包含安装脚本、editor 和 host 的那一层目录。'
    }
    & $installer -PackageDirectory $PackageRoot -VerifyOnly
    $manifest = Get-Content -LiteralPath (Join-Path $PackageRoot 'package-files.json') -Raw | ConvertFrom-Json
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $PackageRoot $manifest.certificate))
    try {
        if ($certificate.Thumbprint -ne $manifest.certificateThumbprint) { throw '安装包证书与清单不一致。' }
        $alreadyTrusted = Test-Path -LiteralPath ('Cert:\LocalMachine\Root\' + $certificate.Thumbprint)
        return [pscustomobject]@{
            Version = [string]$manifest.version
            CertificateSubject = $certificate.Subject
            CertificateThumbprint = $certificate.Thumbprint
            CertificateExpires = $certificate.NotAfter.ToString('yyyy-MM-dd')
            PreviewCertificate = [bool]$manifest.previewCertificate
            CertificateTrusted = $alreadyTrusted
            TrustRequired = ([bool]$manifest.previewCertificate -and -not $alreadyTrusted)
            PackageRoot = $PackageRoot
            EditorPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'Reticle Studio\Independent\Editor\Reticle.exe'
            HostPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'Reticle Studio\OverlayHost\Reticle.OverlayHost.exe'
        }
    } finally { $certificate.Dispose() }
}

function New-SetupWindow($Package) {
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
    [xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Title="Reticle Studio · 独立覆盖版安装" Width="780" Height="740" MinWidth="720" MinHeight="700" WindowStartupLocation="CenterScreen" Background="#10151E" Foreground="#ECF1F8" FontFamily="Microsoft YaHei UI, Segoe UI" FontSize="13">
  <Grid Margin="28">
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <StackPanel Margin="0,0,0,18"><TextBlock Text="安装独立覆盖版" FontSize="25" FontWeight="Bold"/><TextBlock Text="无需 Game Bar。安装编辑器、准星组件及自带运行时。" Foreground="#AAB8CC" Margin="0,8,0,0"/></StackPanel>
    <Border Grid.Row="1" Background="#19222F" CornerRadius="8" Padding="16" Margin="0,0,0,14"><StackPanel>
      <TextBlock x:Name="VersionText" FontWeight="SemiBold"/>
      <TextBlock Text="编辑器安装位置" Foreground="#AAB8CC" Margin="0,10,0,4"/>
      <TextBox x:Name="EditorPath" IsReadOnly="True" BorderThickness="0" Background="Transparent" Foreground="#ECF1F8" Padding="0" FontSize="12"/>
      <TextBlock Text="完整安装后可删除解压目录；个人准星配置保存在用户目录。" Foreground="#AAB8CC" Margin="0,8,0,0" TextWrapping="Wrap"/>
    </StackPanel></Border>
    <Border Grid.Row="2" Background="#19222F" CornerRadius="8" Padding="16" Margin="0,0,0,14"><StackPanel>
      <TextBlock Text="组件签名证书" FontWeight="SemiBold"/>
      <TextBlock x:Name="SubjectText" Margin="0,8,0,0" TextWrapping="Wrap"/>
      <TextBlock x:Name="ThumbprintText" FontFamily="Consolas" FontSize="12" Margin="0,6,0,0" TextWrapping="Wrap"/>
      <TextBlock x:Name="ExpiryText" Foreground="#AAB8CC" Margin="0,6,0,0"/>
    </StackPanel></Border>
    <Border Grid.Row="3" BorderBrush="#344052" BorderThickness="1" CornerRadius="8" Padding="16" Margin="0,0,0,14"><StackPanel>
      <CheckBox x:Name="ShortcutOption" IsChecked="True" Foreground="#ECF1F8" Content="创建桌面快捷方式（所有用户桌面）" Margin="0,0,0,12"/>
      <CheckBox x:Name="TrustOption" IsChecked="False" Foreground="#ECF1F8" Content="我信任上方证书，并允许将其加入本机根信任存储"/>
      <TextBlock x:Name="TrustDescription" Foreground="#AAB8CC" Margin="0,8,0,0" TextWrapping="Wrap"/>
    </StackPanel></Border>
    <ScrollViewer Grid.Row="4" VerticalScrollBarVisibility="Auto" Margin="0,0,0,16"><TextBlock x:Name="StatusText" Foreground="#B9C8DA" TextWrapping="Wrap" Text="安装包文件及签名校验已通过。选择安装选项后点击安装；Windows 将请求管理员权限。"/></ScrollViewer>
    <Grid Grid.Row="5"><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="12"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <Button x:Name="CloseButton" Grid.Column="1" Content="关闭" Width="100" Height="38"/>
      <Button x:Name="InstallButton" Grid.Column="3" Content="安装" Width="140" Height="38" Background="#67E8B0" Foreground="#10261D" FontWeight="Bold"/>
    </Grid>
  </Grid>
</Window>
'@
    $reader = [Xml.XmlNodeReader]::new($xaml)
    try { $window = [Windows.Markup.XamlReader]::Load($reader) } finally { $reader.Dispose() }
    $window.FindName('VersionText').Text = 'Reticle Studio 独立覆盖版  v' + $Package.Version
    $window.FindName('EditorPath').Text = $Package.EditorPath
    $window.FindName('SubjectText').Text = $Package.CertificateSubject
    $window.FindName('ThumbprintText').Text = '指纹：' + $Package.CertificateThumbprint
    $window.FindName('ExpiryText').Text = '有效期至 ' + $Package.CertificateExpires
    $trust = $window.FindName('TrustOption')
    if ($Package.TrustRequired) {
        $window.FindName('TrustDescription').Text = '首次安装此预览签名需要明确勾选此项。仅添加上方这一张公钥证书，以满足 UIAccess 的签名链要求；私钥不随安装包分发。'
    } else {
        $trust.IsEnabled = $false
        $window.FindName('TrustDescription').Text = 'Windows 已认可此签名的信任条件，无需再次添加预览根证书。'
    }
    return $window
}

function Invoke-SetupWorker([string]$PackageRoot, [string]$OutputPath, [bool]$Shortcut, [bool]$Trust, [string]$Thumbprint) {
    if ($Thumbprint -notmatch '\A[a-fA-F0-9]{40}\z') { throw '缺少有效的安装授权证书指纹。' }
    if (-not [IO.Path]::IsPathRooted($OutputPath) -or [IO.Path]::GetFullPath($OutputPath) -ne $OutputPath) { throw '安装结果路径必须为完整路径。' }
    $outputName = [IO.Path]::GetFileName($OutputPath)
    if ($outputName -notmatch '\Aresult-[a-f0-9]{32}\.json\z') { throw '无效的安装结果文件名。' }
    if (Test-Path -LiteralPath $OutputPath) { throw '安装结果文件已经存在。' }
    $result = $null
    $exitCode = 1
    try {
        # Keep the certificate displayed before UAC bound to the certificate
        # checked by the privileged installer. Never replace this value with
        # a newly read value from the mutable extracted package.
        $parameters = @{ PackageDirectory=$PackageRoot; ExpectedCertificateThumbprint=$Thumbprint }
        if ($Shortcut) { $parameters.CreateDesktopShortcut = $true }
        if ($Trust) { $parameters.TrustPreviewCertificate = $true }
        $null = & (Join-Path $PackageRoot 'install-independent.ps1') @parameters
        $result = [ordered]@{ success=$true; message='安装完成。请关闭安装窗口，双击桌面快捷方式或安装目录中的 Reticle.exe，以普通账户启动编辑器。' }
        $exitCode = 0
    } catch {
        $description = $_.Exception.Message
        if ($description.Length -gt 8000) { $description = $description.Substring(0,8000) }
        $result = [ordered]@{ success=$false; message=$description }
    }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($result | ConvertTo-Json -Depth 3))
    $stream = [IO.FileStream]::new($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
    return $exitCode
}

if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) { throw '请使用 Windows 10/11 的 64 位 PowerShell。' }
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
if ($InstallWorker) {
    if ($InspectOnly -or -not $ResultPath) { throw '安装工作进程参数不完整。' }
    exit (Invoke-SetupWorker $packageRoot $ResultPath ([bool]$CreateDesktopShortcut) ([bool]$TrustPreviewCertificate) $ExpectedCertificateThumbprint)
}
if ($CreateDesktopShortcut -or $TrustPreviewCertificate -or $ResultPath -or $ExpectedCertificateThumbprint) { throw '请在安装窗口内选择选项，或直接运行 install-independent.ps1。' }

try {
    $package = Get-SetupPackage $packageRoot
    if ($InspectOnly) { $package; return }
    if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) { throw '请通过 Setup-Independent.cmd 打开安装窗口。' }
    $window = New-SetupWindow $package
    $install = $window.FindName('InstallButton')
    $close = $window.FindName('CloseButton')
    $status = $window.FindName('StatusText')
    $shortcut = $window.FindName('ShortcutOption')
    $trust = $window.FindName('TrustOption')
    $script:worker = $null
    $script:resultFile = $null
    $script:finished = $false
    $timer = [Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds(300)
    $timer.Add_Tick({
        try {
            if ($null -eq $script:worker -or -not $script:worker.HasExited) { return }
            $timer.Stop()
            $workerExit = $script:worker.ExitCode
            $script:worker.Dispose(); $script:worker = $null
            if (-not (Test-Path -LiteralPath $script:resultFile) -or (Get-Item -LiteralPath $script:resultFile).Length -gt 32768) { throw ('安装进程已退出（代码 ' + $workerExit + '），但没有生成有效结果。可使用 README 中的管理员 PowerShell 命令查看详细错误。') }
            $outcome = Get-Content -LiteralPath $script:resultFile -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($workerExit -ne 0 -or $outcome.success -ne $true) { throw ([string]$outcome.message) }
            $script:finished = $true
            $status.Text = [string]$outcome.message + "`n`n编辑器位置：" + $package.EditorPath
            $status.Foreground = [Windows.Media.BrushConverter]::new().ConvertFromString('#67E8B0')
            $install.Content = '已安装'
            $close.IsEnabled = $true
        } catch {
            $timer.Stop()
            if ($script:worker) { $script:worker.Dispose(); $script:worker = $null }
            $status.Text = '安装未完成：' + $_.Exception.Message
            $status.Foreground = [Windows.Media.BrushConverter]::new().ConvertFromString('#FFACAC')
            $install.IsEnabled = $true; $close.IsEnabled = $true
            $shortcut.IsEnabled = $true; $trust.IsEnabled = $package.TrustRequired
        }
    })
    $install.Add_Click({
        if ($package.TrustRequired -and $trust.IsChecked -ne $true) {
            $status.Text = '此预览证书尚未受本机信任。确认上方证书信息后，明确勾选信任选项才能继续安装。'
            return
        }
        try {
            $install.IsEnabled = $false; $close.IsEnabled = $false
            $shortcut.IsEnabled = $false; $trust.IsEnabled = $false
            $status.Text = '正在请求管理员权限。请在 Windows 权限提示中核对程序并作出选择；安装完成前请保留此窗口。'
            $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::Render)
            $workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Reticle-Independent-Setup-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $workDirectory | Out-Null
            $script:resultFile = Join-Path $workDirectory ('result-' + [Guid]::NewGuid().ToString('N') + '.json')
            $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            $arguments = New-SetupWorkerArguments $PSCommandPath $packageRoot $script:resultFile ($shortcut.IsChecked -eq $true) ($trust.IsChecked -eq $true) $package.CertificateThumbprint
            $start = [Diagnostics.ProcessStartInfo]::new($powershell)
            $start.UseShellExecute = $true; $start.Verb = 'runas'; $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            $start.Arguments = ($arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' '
            $script:worker = [Diagnostics.Process]::Start($start)
            if ($null -eq $script:worker) { throw 'Windows 未能启动安装进程。' }
            $status.Text = '正在安装，请稍候…'
            $timer.Start()
        } catch {
            $failure = $_.Exception.GetBaseException()
            $status.Text = if ($failure -is [ComponentModel.Win32Exception] -and $failure.NativeErrorCode -eq 1223) { '已取消 Windows 权限提示，安装尚未开始。' } else { '无法开始安装：' + $failure.Message }
            $install.IsEnabled = $true; $close.IsEnabled = $true
            $shortcut.IsEnabled = $true; $trust.IsEnabled = $package.TrustRequired
        }
    })
    $close.Add_Click({ $window.Close() })
    $window.Add_Closing({ param($sender,$eventArgs) if ($script:worker) { $eventArgs.Cancel = $true; $status.Text = '安装进程仍在运行，请等待完成后关闭。' } })
    $null = $window.ShowDialog()
} catch {
    if ($InspectOnly) { throw }
    Add-Type -AssemblyName PresentationFramework
    [Windows.MessageBox]::Show($_.Exception.Message,'Reticle Studio · 无法安装','OK','Warning') | Out-Null
    exit 1
}
