using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Threading;
using Reticle.App;
using Reticle.Core;

namespace Reticle.OverlayHost;

internal sealed class HostSession : IDisposable
{
    private readonly HostOptions options;
    private readonly CrosshairSettings settings = new();
    private readonly OverlayWindow overlay;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer watchdog = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Stopwatch silence = Stopwatch.StartNew();
    private Process? editor;
    private NamedPipeClientStream? pipe;
    private bool connected, disposed;
    private long lastSequence;

    internal HostSession(HostOptions options)
    {
        this.options = options;
        overlay = new OverlayWindow(settings);
        watchdog.Tick += (_, _) =>
        {
            bool lostEditor;
            try { lostEditor = editor is null || editor.HasExited; }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { lostEditor = true; }
            if (lostEditor || silence.Elapsed.TotalSeconds >
                (connected ? IndependentOverlayProtocol.SilenceTimeoutSeconds : IndependentOverlayProtocol.ConnectTimeoutSeconds))
            {
                overlay.SetVisible(false);
                lifetime.Cancel();
                pipe?.Dispose();
            }
        };
    }

    internal async Task<int> RunAsync()
    {
        try
        {
            bool uiAccess = HostSecurity.GetUiAccess();
            editor = HostSecurity.OpenEditor(options);
            pipe = new NamedPipeClientStream(".", options.Pipe!, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            watchdog.Start();
            await pipe.ConnectAsync(IndependentOverlayProtocol.ConnectTimeoutSeconds * 1000, lifetime.Token);
            HostSecurity.CheckPipeServer(pipe, options.EditorPid);
            connected = true;
            silence.Restart();
            await SendAsync(new OverlayMessage { Kind = "hello", UiAccess = uiAccess });
            if (!uiAccess)
            {
                await SendAsync(new OverlayMessage { Kind = "error", Error = "独立覆盖进程未获得 UIAccess 权限，请使用已签名并安装的组件。" });
                return 2;
            }

            while (!lifetime.IsCancellationRequested)
            {
                var message = await IndependentOverlayProtocol.ReadAsync(pipe, lifetime.Token);
                if (message is null) return 0;
                ValidateCommand(message, options.Session!, lastSequence);
                silence.Restart();
                lastSequence = message.Sequence;
                if (message.Kind == "stop") return 0;
                if (message.Settings is null) throw new InvalidDataException("覆盖命令缺少准星参数。");
                settings.Apply(message.Settings);
                overlay.SetVisible(message.Enabled);
                if (message.Enabled) overlay.Position();
                // Finish local layout/render work before acknowledging the state.
                // This does not establish that Windows physically displayed the
                // pixels above a particular game's presentation path.
                if (message.Enabled)
                {
                    overlay.UpdateLayout();
                    await overlay.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                }
                await SendAsync(new OverlayMessage
                {
                    Kind = "applied", Sequence = message.Sequence, UiAccess = true,
                    Enabled = message.Enabled, WindowHandle = overlay.Handle.ToInt64()
                });
            }
            return 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return 0; }
        catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            overlay.SetVisible(false);
            if (pipe?.IsConnected == true && !lifetime.IsCancellationRequested)
            {
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    string description = "独立覆盖已停止：" + error.Message;
                    if (description.Length > 1024) description = description[..1024];
                    await IndependentOverlayProtocol.WriteAsync(pipe, new OverlayMessage
                    {
                        Kind = "error", Session = options.Session!, ProcessId = Environment.ProcessId,
                        Sequence = lastSequence, Error = description
                    }, deadline.Token);
                }
                catch (Exception sendError) when (sendError is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException) { }
            }
            return 3;
        }
        finally { Dispose(); }
    }

    private ValueTask SendAsync(OverlayMessage message) => IndependentOverlayProtocol.WriteAsync(pipe!, message with
    {
        Session = options.Session!, ProcessId = Environment.ProcessId
    }, lifetime.Token);

    internal static void ValidateCommand(OverlayMessage message, string expectedSession, long lastSequence)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(message.Session), Encoding.ASCII.GetBytes(expectedSession)))
            throw new InvalidDataException("覆盖会话标识不匹配。");
        if (message.Kind is not ("state" or "stop") || message.Sequence <= lastSequence)
            throw new InvalidDataException("覆盖命令类型或顺序无效。");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        watchdog.Stop(); lifetime.Cancel();
        overlay.Close(); pipe?.Dispose(); editor?.Dispose(); lifetime.Dispose();
    }
}
