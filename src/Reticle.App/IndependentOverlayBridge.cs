using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Reticle.Core;

namespace Reticle.App;

// The editor owns the pipe; the elevated host only accepts this editor's
// connection. A successful launch is not reported as an active overlay until
// the host has verified UIAccess and acknowledged a rendered state.
internal sealed class IndependentOverlayBridge : IDisposable
{
    internal static string HostPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Reticle Studio", "OverlayHost", "Reticle.OverlayHost.exe");

    private readonly SemaphoreSlim sending = new(1, 1);
    private NamedPipeServerStream? pipe;
    private CancellationTokenSource? lifetime;
    private Process? host;
    private TaskCompletionSource<OverlayMessage>? acknowledgement;
    private long pendingSequence, sequence;
    private string session = "";
    private bool connected, disposed;
    public event Action<string>? Disconnected;
    public bool Connected => connected;
    public long WindowHandle { get; private set; }

    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connected) return;
        Stop();
        if (!File.Exists(HostPath))
            throw new FileNotFoundException("请先运行独立覆盖版安装程序，再开启独立准星。", HostPath);
        if ((File.GetAttributes(HostPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("独立组件不能是文件链接，请重新安装。");
        // Loading the UIAccess executable from a writable/reparse destination
        // would invalidate the installation's trust assumptions.
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(HostPath)!); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("独立组件安装路径不能经过目录链接，请重新安装。");

        session = Guid.NewGuid().ToString("N");
        string pipeName = "ReticleStudio.Overlay." + session;
        var activeLifetime = lifetime = new CancellationTokenSource();
        var activePipe = pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var editor = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(HostPath)
        {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(HostPath)!
        };
        foreach (string argument in new[] { "--pipe", pipeName, "--session", session,
            "--editor-pid", editor.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--editor-start", editor.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);
        try
        {
            // ShellExecute may wait for UAC; keep the editor responsive while
            // the user decides. Cancellation never implies UAC acceptance.
            Process startedHost = await Task.Run(() => Process.Start(start)).ConfigureAwait(false)
                ?? throw new IOException("Windows 未能启动独立覆盖组件。");
            if (disposed || !ReferenceEquals(lifetime, activeLifetime) || activeLifetime.IsCancellationRequested)
            {
                startedHost.Dispose();
                throw new OperationCanceledException(activeLifetime.Token);
            }
            host = startedHost;
            activeLifetime.Token.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(IndependentOverlayProtocol.ConnectTimeoutSeconds));
            await activePipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(activePipe.SafePipeHandle, out uint clientPid) || clientPid != host.Id)
                throw new InvalidDataException("独立组件连接身份不匹配。");
            OverlayMessage? hello = await IndependentOverlayProtocol.ReadAsync(activePipe, timeout.Token).ConfigureAwait(false);
            if (hello is not { Kind: "hello", UiAccess: true } || hello.Session != session || hello.ProcessId != host.Id)
                throw new InvalidDataException("组件未取得 UIAccess 权限，或启动握手失败。请检查安装与签名。");
            connected = true;
            _ = ReadRepliesAsync(activePipe, activeLifetime, session, host.Id);
        }
        catch
        {
            if (ReferenceEquals(lifetime, activeLifetime)) Stop();
            throw;
        }
    }

    public async Task SendAsync(CrosshairSettings settings, bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var snapshot = IndependentOverlayProtocol.Snapshot(settings);
        var activeLifetime = lifetime ?? throw new IOException("独立覆盖组件尚未连接。");
        await sending.WaitAsync(activeLifetime.Token).ConfigureAwait(false);
        try
        {
            var activePipe = pipe;
            if (!connected || activePipe is null) throw new IOException("独立覆盖组件已断开。");
            var reply = new TaskCompletionSource<OverlayMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingSequence = ++sequence;
            acknowledgement = reply;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            await IndependentOverlayProtocol.WriteAsync(activePipe, new OverlayMessage
            {
                Kind = "state", Session = session, Sequence = pendingSequence, Enabled = enabled, Settings = snapshot
            }, timeout.Token).ConfigureAwait(false);
            OverlayMessage applied = await reply.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (!applied.UiAccess || applied.Enabled != enabled || (enabled && applied.WindowHandle == 0))
                throw new InvalidDataException("独立组件没有确认有效的覆盖窗口。");
            WindowHandle = applied.WindowHandle;
        }
        catch
        {
            if (ReferenceEquals(lifetime, activeLifetime)) Stop();
            throw;
        }
        finally { acknowledgement = null; sending.Release(); }
    }

    private async Task ReadRepliesAsync(NamedPipeServerStream activePipe, CancellationTokenSource activeLifetime, string activeSession, int hostPid)
    {
        string? failure = null;
        try
        {
            while (!activeLifetime.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(IndependentOverlayProtocol.SilenceTimeoutSeconds));
                OverlayMessage? reply = await IndependentOverlayProtocol.ReadAsync(activePipe, timeout.Token).ConfigureAwait(false);
                if (reply is null) throw new EndOfStreamException("独立覆盖组件已退出。");
                if (reply.Session != activeSession || reply.ProcessId != hostPid)
                    throw new InvalidDataException("独立组件响应身份不匹配。");
                if (reply.Kind == "error") throw new IOException(reply.Error ?? "独立覆盖组件发生错误。");
                if (reply.Kind != "applied" || reply.Sequence != pendingSequence || !reply.UiAccess)
                    throw new InvalidDataException("独立组件响应与当前状态不匹配。");
                acknowledgement?.TrySetResult(reply);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException or Win32Exception)
        {
            if (!activeLifetime.IsCancellationRequested) failure = error.Message;
        }
        finally
        {
            if (ReferenceEquals(lifetime, activeLifetime))
            {
                acknowledgement?.TrySetException(new IOException(failure ?? "独立覆盖组件已断开。"));
                Stop();
                if (failure is not null) Disconnected?.Invoke(failure);
            }
        }
    }

    public void Stop()
    {
        connected = false;
        WindowHandle = 0;
        var oldLifetime = lifetime;
        lifetime = null;
        oldLifetime?.Cancel();
        pipe?.Dispose(); pipe = null;
        host?.Dispose(); host = null;
        // The host exits when the pipe disconnects. Do not kill an unrelated
        // process if a PID was reused after it exited.
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);
}
