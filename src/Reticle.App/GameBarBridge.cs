using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Reticle.Core;

namespace Reticle.App;

internal sealed class GameBarBridge
{
    private GameBarStateStore? store;

    internal static string PackageFamilyName
    {
        get
        {
            var id = new PackageId { Name = "ReticleStudio.GameBar", Publisher = "CN=ReticleStudio", Architecture = 9 };
            uint length = 0;
            int error = PackageFamilyNameFromId(ref id, ref length, null);
            if (error != 122) throw new Win32Exception(error);
            var value = new StringBuilder((int)length);
            error = PackageFamilyNameFromId(ref id, ref length, value);
            if (error != 0) throw new Win32Exception(error);
            return value.ToString();
        }
    }

    public bool TryConnect(out string message)
    {
        store = null;
        try
        {
            if (!IsInstalled("Microsoft.XboxGamingOverlay_8wekyb3d8bbwe"))
            {
                message = "未找到 Xbox Game Bar，请先从 Microsoft Store 安装。";
                return false;
            }
            string family = PackageFamilyName;
            if (!IsInstalled(family))
            {
                message = "请先安装 Reticle Game Bar 全屏组件，再重新开启。";
                return false;
            }
            store = new GameBarStateStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", family, "LocalState", "overlay-state.json"));
            message = "已发送到全屏组件；请在 Game Bar 中固定并开启鼠标穿透。";
            return true;
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException)
        {
            message = "无法连接全屏组件：" + e.Message;
            return false;
        }
    }

    private static bool IsInstalled(string family)
    {
        uint count = 0, length = 0;
        int error = GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
        if (error != 0 && error != 122) throw new Win32Exception(error);
        return count != 0;
    }

    public bool TrySend(CrosshairSettings settings, bool enabled, out string? error)
    {
        error = null;
        if (store is null) { error = "全屏组件尚未连接。"; return false; }
        try { store.Save(settings, enabled); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { error = "全屏组件同步失败：" + e.Message; return false; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PackageId
    {
        public uint Reserved, Architecture;
        public ulong Version;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Publisher;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ResourceId;
        [MarshalAs(UnmanagedType.LPWStr)] public string? PublisherId;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int PackageFamilyNameFromId(ref PackageId id, ref uint length, StringBuilder? family);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
}
