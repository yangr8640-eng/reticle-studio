using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Reticle.Core;
using Reticle.OverlayHost;

internal static class Program
{
    private static int passed;

    [STAThread]
    private static int Main(string[] args)
    {
        try { RunAsync(args).GetAwaiter().GetResult(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task RunAsync(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: Checks <absolute dotnet.exe> <absolute host.dll>");
        string session = Guid.NewGuid().ToString("N");
        string pipe = "ReticleStudio.Overlay." + session;
        var values = new[] { "--pipe", pipe, "--session", session, "--editor-pid", Environment.ProcessId.ToString(), "--editor-start", Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString() };
        Check("valid editor launch", HostOptions.Parse(values).Pipe == pipe);
        Reject("pipe must match nonce", () => HostOptions.Parse(Replace(values, "--pipe", pipe + "extra")));
        Reject("pipe cannot contain a path", () => HostOptions.Parse(Replace(values, "--pipe", @"\\.\pipe\" + pipe)));
        Reject("nonce must have fixed format", () => HostOptions.Parse(Replace(values, "--session", "wrong")));
        Reject("duplicate arguments", () => HostOptions.Parse(values.Concat(new[] { "--pipe", pipe }).ToArray()));
        Reject("diagnostics require absolute output", () => HostOptions.Parse(["--diagnose", "relative.json"]));
        HostSession.ValidateCommand(new OverlayMessage { Kind = "state", Session = session, Sequence = 2 }, session, 1);
        Check("ordered state accepted", true);
        Reject("foreign nonce", () => HostSession.ValidateCommand(new OverlayMessage { Kind = "state", Session = Guid.NewGuid().ToString("N"), Sequence = 2 }, session, 1));
        Reject("replayed sequence", () => HostSession.ValidateCommand(new OverlayMessage { Kind = "state", Session = session, Sequence = 1 }, session, 1));
        Reject("unexpected reply as command", () => HostSession.ValidateCommand(new OverlayMessage { Kind = "applied", Session = session, Sequence = 2 }, session, 1));

        using (var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        using (var child = Start(args[0], args[1], values))
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            try
            {
                await server.WaitForConnectionAsync(deadline.Token);
                var hello = await IndependentOverlayProtocol.ReadAsync(server, deadline.Token);
                Check("real child hello echoes nonce and PID", hello is { Kind: "hello" } && hello.Session == session && hello.ProcessId == child.Id);
                Check("unsigned runtime reports UIAccess false", hello is { UiAccess: false, WindowHandle: 0 });
                var error = await IndependentOverlayProtocol.ReadAsync(server, deadline.Token);
                Check("no permission emits explicit error", error is { Kind: "error", WindowHandle: 0 } && error.Session == session);
                Check("child closes connection", await IndependentOverlayProtocol.ReadAsync(server, deadline.Token) is null);
                await child.WaitForExitAsync(deadline.Token);
                Check("permission refusal exit code", child.ExitCode == 2);
            }
            finally { if (!child.HasExited) child.Kill(); }
        }

        using (var child = Start(args[0], args[1], Replace(values, "--editor-start", "1")))
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            try { await child.WaitForExitAsync(deadline.Token); Check("wrong editor birth identity exits", child.ExitCode == 3); }
            finally { if (!child.HasExited) child.Kill(); }
        }
        Console.WriteLine($"Passed {passed} helper boundary and process checks. Actual UIAccess rendering remains a signed-install test.");
    }

    private static Process Start(string dotnet, string host, string[] values)
    {
        var info = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(host);
        foreach (var value in values) info.ArgumentList.Add(value);
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start host process.");
    }

    private static string[] Replace(string[] values, string key, string replacement)
    {
        var copy = (string[])values.Clone();
        copy[Array.IndexOf(copy, key) + 1] = replacement;
        return copy;
    }

    private static void Reject(string name, Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { Check(name, true); return; }
        throw new InvalidOperationException("Expected rejection: " + name);
    }

    private static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + name);
        passed++; Console.WriteLine("PASS " + name);
    }
}
