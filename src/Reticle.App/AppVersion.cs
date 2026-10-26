using System.Reflection;
using Reticle.Core;

namespace Reticle.App;

internal static class AppVersion
{
    public static string Current { get; } = typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    public static ReleaseVersion Parsed { get; } = ReleaseVersion.TryParse(Current, out var version)
        ? version : throw new InvalidOperationException("应用版本号无效。");
}
