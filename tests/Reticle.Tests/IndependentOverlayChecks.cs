using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using Reticle.Core;

internal static class IndependentOverlayChecks
{
    private const string Session = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

    public static void Run(Action<string, Action> check)
    {
        check("Independent overlay snapshots cannot change after queued settings edits", () =>
        {
            var source = new CrosshairSettings { Shape = CrosshairShape.Diagonal, Color = "#123456", Opacity = 31, Monitor = "screen2", OffsetX = -87, OffsetY = 43, CenterDot = true };
            var snapshot = IndependentOverlayProtocol.Snapshot(source);
            source.Apply(new CrosshairSettings());
            Assert(!ReferenceEquals(source, snapshot) && snapshot.Shape == CrosshairShape.Diagonal && snapshot.Color == "#123456" && snapshot.Opacity == 31);
            Assert(snapshot.Monitor == "screen2" && snapshot.OffsetX == -87 && snapshot.OffsetY == 43 && snapshot.CenterDot);
        });
        check("Independent overlay reads frames split across arbitrary pipe read boundaries", () => Run(async () =>
        {
            using var wire = new MemoryStream();
            await IndependentOverlayProtocol.WriteAsync(wire, State(17, true));
            using var fragmented = new FragmentedStream(wire.ToArray(), 1);
            var state = await IndependentOverlayProtocol.ReadAsync(fragmented);
            Assert(state?.Kind == "state" && state.Sequence == 17 && state.Enabled && state.Settings?.Color == "#123456");
            Assert(await IndependentOverlayProtocol.ReadAsync(fragmented) is null);
        }));
        check("Independent overlay frame boundaries preserve consecutive visibility updates", () => Run(async () =>
        {
            using var wire = new MemoryStream();
            await IndependentOverlayProtocol.WriteAsync(wire, State(98, true));
            await IndependentOverlayProtocol.WriteAsync(wire, State(99, false));
            wire.Position = 0;
            var shown = await IndependentOverlayProtocol.ReadAsync(wire);
            var hidden = await IndependentOverlayProtocol.ReadAsync(wire);
            Assert(shown is { Sequence: 98, Enabled: true } && hidden is { Sequence: 99, Enabled: false });
            Assert(await IndependentOverlayProtocol.ReadAsync(wire) is null);
        }));
        check("Independent overlay rejects impossible frame lengths before reading payload", () => Run(async () =>
        {
            foreach (int length in new[] { -1, int.MinValue, 0, IndependentOverlayProtocol.MaxFrameBytes + 1, int.MaxValue })
            {
                byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, length);
                using var wire = new MemoryStream(header);
                await Throws<InvalidDataException>(async () => await IndependentOverlayProtocol.ReadAsync(wire));
                Assert(wire.Position == 4);
            }
        }));
        check("Independent overlay distinguishes clean disconnect from truncated frame", () => Run(async () =>
        {
            using var empty = new MemoryStream();
            Assert(await IndependentOverlayProtocol.ReadAsync(empty) is null);
            foreach (byte[] bytes in new[] { new byte[] { 1 }, new byte[] { 1, 0, 0 }, new byte[] { 5, 0, 0, 0, 123, 125 } })
            {
                using var wire = new MemoryStream(bytes);
                await Throws<EndOfStreamException>(async () => await IndependentOverlayProtocol.ReadAsync(wire));
            }
        }));
        check("Independent overlay rejects malformed and unsupported incoming documents", () => Run(async () =>
        {
            string[] malformed =
            [
                "null", "[]", "{", "{}",
                "{\"Version\":2,\"Kind\":\"hello\",\"Session\":\"" + Session + "\"}",
                "{\"Kind\":\"execute\",\"Session\":\"" + Session + "\"}",
                "{\"Kind\":\"hello\",\"Session\":null}",
                "{\"Kind\":\"hello\",\"Session\":\"short\"}",
                "{\"Kind\":\"hello\",\"Session\":\"zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz\"}",
                "{\"Kind\":\"hello\",\"Session\":\"" + Session + "\",\"Sequence\":-1}",
                "{\"Kind\":\"state\",\"Session\":\"" + Session + "\",\"Sequence\":1}",
                "{\"Kind\":\"state\",\"Session\":\"" + Session + "\",\"Sequence\":0,\"Settings\":{}}",
                "{\"Kind\":\"applied\",\"Session\":\"" + Session + "\",\"Sequence\":0}",
                "{\"Kind\":\"hello\",\"Session\":\"" + Session + "\",\"UiAccess\":\"true\"}"
            ];
            foreach (string json in malformed)
            {
                using var wire = Frame(json);
                await Throws<InvalidDataException>(async () => await IndependentOverlayProtocol.ReadAsync(wire));
            }
        }));
        check("Independent overlay bounds outgoing metadata without writing partial frames", () => Run(async () =>
        {
            using var wire = new MemoryStream();
            await Throws<InvalidDataException>(() => IndependentOverlayProtocol.WriteAsync(wire, new() { Kind = "error", Session = Session, Error = new string('x', 1025) }).AsTask());
            await Throws<InvalidDataException>(() => IndependentOverlayProtocol.WriteAsync(wire, State(1, true) with { Settings = new() { Monitor = new string('x', 257) } }).AsTask());
            await Throws<InvalidDataException>(() => IndependentOverlayProtocol.WriteAsync(wire, new() { Kind = "applied", Session = Session }).AsTask());
            Assert(wire.Length == 0);
        }));
        check("Independent overlay rejects excessively nested incoming JSON", () => Run(async () =>
        {
            string nested = "{\"Kind\":\"hello\",\"Session\":\"" + Session + "\",\"unused\":" + new string('[', 30) + "0" + new string(']', 30) + "}";
            using var wire = Frame(nested);
            await Throws<InvalidDataException>(async () => await IndependentOverlayProtocol.ReadAsync(wire));
        }));
        check("Independent overlay allows caller cancellation while waiting for a frame", () => Run(async () =>
        {
            // This stream waits without data, like a stalled editor connection.
            using var blocked = new CancellableReadStream();
            using var cancelled = new CancellationTokenSource();
            var pendingRead = IndependentOverlayProtocol.ReadAsync(blocked, cancelled.Token).AsTask();
            await blocked.Started.Task;
            cancelled.Cancel();
            await Throws<OperationCanceledException>(() => pendingRead);
            Assert(!blocked.Disposed);
        }));
        check("Independent overlay exchanges hello, state and applied over a real named pipe", () => Run(async () =>
        {
            string name = "Reticle.ProtocolTest." + Guid.NewGuid().ToString("N");
            using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            var incomingHello = IndependentOverlayProtocol.ReadAsync(server, timeout.Token).AsTask();
            await IndependentOverlayProtocol.WriteAsync(client, new() { Kind = "hello", Session = Session, ProcessId = Environment.ProcessId, UiAccess = true }, timeout.Token);
            var hello = await incomingHello;
            Assert(hello is { Kind: "hello", Sequence: 0, UiAccess: true } && hello.Session == Session && hello.ProcessId == Environment.ProcessId);
            var incomingState = IndependentOverlayProtocol.ReadAsync(client, timeout.Token).AsTask();
            await IndependentOverlayProtocol.WriteAsync(server, State(247, true), timeout.Token);
            var state = await incomingState;
            Assert(state?.Settings is { Color: "#123456", Shape: CrosshairShape.Circle } && state.Enabled);
            var incomingAck = IndependentOverlayProtocol.ReadAsync(server, timeout.Token).AsTask();
            await IndependentOverlayProtocol.WriteAsync(client, new() { Kind = "applied", Session = Session, Sequence = state!.Sequence, ProcessId = Environment.ProcessId, UiAccess = true, WindowHandle = 0x12345678 }, timeout.Token);
            var ack = await incomingAck;
            Assert(ack is { Kind: "applied", Sequence: 247, UiAccess: true, WindowHandle: 0x12345678 });
            client.Dispose();
            Assert(await IndependentOverlayProtocol.ReadAsync(server, timeout.Token) is null);
        }));
    }

    private static OverlayMessage State(long sequence, bool enabled) => new()
    {
        Kind = "state", Session = Session, Sequence = sequence, Enabled = enabled,
        Settings = new() { Shape = CrosshairShape.Circle, Color = "#123456", Radius = 29, OffsetX = -12 }
    };

    private static MemoryStream Frame(string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json), frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length); payload.CopyTo(frame, 4);
        return new MemoryStream(frame);
    }
    private static void Assert(bool value) { if (!value) throw new Exception("Independent overlay protocol assertion failed."); }
    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class FragmentedStream(byte[] data, int maxChunk) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, maxChunk)], cancellationToken);
    }
    private sealed class CancellableReadStream : Stream
    {
        public bool Disposed { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken); return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
