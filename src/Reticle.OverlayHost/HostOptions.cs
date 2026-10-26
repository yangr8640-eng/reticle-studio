using System.Globalization;
using System.IO;

namespace Reticle.OverlayHost;

internal sealed record HostOptions(string? Pipe, string? Session, int EditorPid, long EditorStartTicks, string? DiagnosticPath)
{
    internal static HostOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length || !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("启动参数不完整或重复。");
        }
        if (values.Keys.Any(k => k is not ("--pipe" or "--session" or "--editor-pid" or "--editor-start" or "--diagnose")))
            throw new ArgumentException("启动参数包含不支持的选项。");
        if (values.TryGetValue("--diagnose", out string? output))
        {
            if (values.Count != 1 || !Path.IsPathFullyQualified(output)) throw new ArgumentException("诊断需要单独指定完整的报告路径。");
            return new(null, null, 0, 0, Path.GetFullPath(output));
        }
        if (values.Count != 4 || !values.TryGetValue("--pipe", out string? pipe) ||
            !values.TryGetValue("--session", out string? session) || !Guid.TryParseExact(session, "N", out _) ||
            !values.TryGetValue("--editor-pid", out string? pidText) || !int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0 ||
            !values.TryGetValue("--editor-start", out string? startText) || !long.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out long start) || start <= 0 ||
            !string.Equals(pipe, "ReticleStudio.Overlay." + session, StringComparison.Ordinal))
            throw new ArgumentException("请从 Reticle Studio 编辑器开启独立全屏模式。");
        return new(pipe, session, pid, start, null);
    }
}
