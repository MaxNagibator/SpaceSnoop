using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed record MachineProfile(
    string AppVersion,
    string OperatingSystem,
    bool Is64BitOperatingSystem,
    int ProcessorCount,
    long TotalMemoryBytes,
    long GcMemoryLimitBytes,
    string Runtime,
    bool ServerGarbageCollector,
    string GcLatencyMode,
    int RenderTier,
    string RenderMode,
    double DpiScale,
    bool RemoteSession,
    bool ClientAreaAnimation,
    bool Elevated,
    bool PortableStorage,
    string Culture)
{
    public static MachineProfile Capture(Visual? visual = null)
    {
        return new(
            AppInfo.Version,
            Environment.OSVersion.VersionString,
            Environment.Is64BitOperatingSystem,
            Environment.ProcessorCount,
            ReadPhysicalMemory(),
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            RuntimeInformation.FrameworkDescription,
            GCSettings.IsServerGC,
            GCSettings.LatencyMode.ToString(),
            RenderCapability.Tier >> 16,
            RenderOptions.ProcessRenderMode.ToString(),
            ReadDpiScale(visual),
            SystemParameters.IsRemoteSession,
            SystemParameters.ClientAreaAnimation,
            AdminElevation.IsElevated,
            !AppStorage.UseAppData,
            CultureInfo.CurrentCulture.Name);
    }

    public IEnumerable<string> Describe()
    {
        yield return $"Версия: {AppVersion}, {Runtime}, культура {Culture}";
        yield return $"Система: {OperatingSystem}, {(Is64BitOperatingSystem ? "64-разрядная" : "32-разрядная")}";
        yield return $"Процессор: {ProcessorCount} логических, память машины {SizeFormatter.Format(TotalMemoryBytes)}, лимит сборщика {SizeFormatter.Format(GcMemoryLimitBytes)}";
        yield return $"Сборщик мусора: {(ServerGarbageCollector ? "серверный" : "рабочий")}, режим {GcLatencyMode}";
        yield return $"Рендер: уровень {RenderTier}, режим {RenderMode}, масштаб {DpiScale:0.##}";
        yield return $"Сеанс: {(RemoteSession ? "удалённый рабочий стол" : "локальный")}, анимации {(ClientAreaAnimation ? "включены" : "выключены")}";
        yield return $"Права: {(Elevated ? "администратор" : "обычный режим")}, хранение {(PortableStorage ? "переносное" : "в профиле пользователя")}";
    }

    private static long ReadPhysicalMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };

        return GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0
            ? (long)status.TotalPhysical
            : GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    private static double ReadDpiScale(Visual? visual)
    {
        if (visual is null)
        {
            return 1;
        }

        try
        {
            return VisualTreeHelper.GetDpi(visual).DpiScaleX;
        }
        catch (InvalidOperationException)
        {
            return 1;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;

        public uint MemoryLoad;

        public ulong TotalPhysical;

        public ulong AvailablePhysical;

        public ulong TotalPageFile;

        public ulong AvailablePageFile;

        public ulong TotalVirtual;

        public ulong AvailableVirtual;

        public ulong AvailableExtendedVirtual;
    }
}
