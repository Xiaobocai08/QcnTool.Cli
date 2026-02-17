using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace QcnTool.Cli;

internal static class DiagPortScanner
{
    private static readonly Regex VidRegex = new(@"VID_([0-9A-Fa-f]{4})", RegexOptions.CultureInvariant);
    private static readonly Regex MiRegex = new(@"MI_([0-9A-Fa-f]{2})", RegexOptions.CultureInvariant);
    private static readonly Regex ComRegex = new(@"\((COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryAutoSelectSingleDiagPort(bool verbose, out string? portName, out IReadOnlyList<DiagPortCandidate> candidates)
    {
        var all = FindQualcommPortsVid05C6(verbose);

        var mi00 = all.Where(static p => string.Equals(p.Mi, "00", StringComparison.OrdinalIgnoreCase)).ToList();
        var hasMsmMi00 = mi00.Any(static p => p.Name.Contains("MSM", StringComparison.OrdinalIgnoreCase));

        if (hasMsmMi00)
        {
            var mdm = all
                .Where(static p => string.Equals(p.Mi, "01", StringComparison.OrdinalIgnoreCase))
                .Where(static p => p.Name.Contains("MDM", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (mdm.Count == 1)
            {
                candidates = mdm;
                portName = mdm[0].PortName;
                return true;
            }

            if (mdm.Count > 1)
            {
                candidates = mdm;
                portName = null;
                return false;
            }
        }

        candidates = mi00;
        if (candidates.Count == 1)
        {
            portName = candidates[0].PortName;
            return true;
        }

        portName = null;
        return false;
    }

    public static IReadOnlyList<DiagPortCandidate> FindQualcommPortsVid05C6(bool verbose)
    {
        var results = new List<DiagPortCandidate>();

        if (!OperatingSystem.IsWindows())
        {
            return results;
        }

        try
        {
            results.AddRange(EnumeratePortsBySetupApi());
        }
        catch
        {
            return results;
        }

        results = results
            .Where(static p => string.Equals(p.Vid, "05C6", StringComparison.OrdinalIgnoreCase))
            .GroupBy(static x => x.PortName, StringComparer.OrdinalIgnoreCase)
            .Select(static g => g.First())
            .OrderBy(static x => x.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return results;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<DiagPortCandidate> EnumeratePortsBySetupApi()
    {
        var portsGuid = new Guid("4D36E978-E325-11CE-BFC1-08002BE10318");
        var devInfo = SetupDiGetClassDevsW(ref portsGuid, null, IntPtr.Zero, DIGCF_PRESENT);
        if (devInfo == IntPtr.Zero || devInfo == InvalidHandleValue)
        {
            yield break;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>()
                };

                if (!SetupDiEnumDeviceInfo(devInfo, index, ref data))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == ERROR_NO_MORE_ITEMS)
                    {
                        yield break;
                    }

                    continue;
                }

                var instanceId = TryGetDeviceInstanceIdOrNull(devInfo, ref data);
                if (string.IsNullOrWhiteSpace(instanceId))
                {
                    continue;
                }

                var hwIds = TryGetMultiSzProperty(devInfo, ref data, SPDRP_HARDWAREID);
                var vidMiSource = hwIds.FirstOrDefault(static s => !string.IsNullOrWhiteSpace(s)) ?? instanceId;
                if (!TryGetVidMi(vidMiSource, out var vid, out var mi))
                {
                    if (!TryGetVidMi(instanceId, out vid, out mi))
                    {
                        continue;
                    }
                }

                if (!string.Equals(vid, "05C6", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mi))
                {
                    continue;
                }

                var friendly = TryGetStringProperty(devInfo, ref data, SPDRP_FRIENDLYNAME);
                var desc = TryGetStringProperty(devInfo, ref data, SPDRP_DEVICEDESC);
                var name = (friendly ?? desc ?? instanceId).Trim();

                var port = TryExtractComPortName(name) ?? TryReadPortNameFromRegistry(instanceId);
                if (string.IsNullOrWhiteSpace(port))
                {
                    continue;
                }

                yield return new DiagPortCandidate(port.Trim(), instanceId.Trim(), name, vid.ToUpperInvariant(), mi.ToUpperInvariant());
            }
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(devInfo);
        }
    }

    private static string? TryExtractComPortName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var m = ComRegex.Match(name);
        return m.Success ? m.Groups[1].Value : null;
    }

    [SupportedOSPlatform("windows")]
    private static string? TryReadPortNameFromRegistry(string deviceInstanceId)
    {
        try
        {
            var sub = $@"SYSTEM\CurrentControlSet\Enum\{deviceInstanceId}\Device Parameters";
            using var key = Registry.LocalMachine.OpenSubKey(sub);
            var value = key?.GetValue("PortName") as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim();
            return value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ? value.ToUpperInvariant() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetDeviceInstanceIdOrNull(IntPtr devInfo, ref SP_DEVINFO_DATA data)
    {
        var sb = new StringBuilder(512);
        if (SetupDiGetDeviceInstanceIdW(devInfo, ref data, sb, sb.Capacity, out var required))
        {
            var value = sb.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        if (required > sb.Capacity)
        {
            sb = new StringBuilder(required + 2);
            if (SetupDiGetDeviceInstanceIdW(devInfo, ref data, sb, sb.Capacity, out _))
            {
                var value = sb.ToString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    private static string? TryGetStringProperty(IntPtr devInfo, ref SP_DEVINFO_DATA data, uint property)
    {
        var bytes = TryGetDeviceRegistryPropertyBytes(devInfo, ref data, property);
        if (bytes.Length == 0)
        {
            return null;
        }

        var s = Encoding.Unicode.GetString(bytes);
        return s.TrimEnd('\0').Trim();
    }

    private static string[] TryGetMultiSzProperty(IntPtr devInfo, ref SP_DEVINFO_DATA data, uint property)
    {
        var bytes = TryGetDeviceRegistryPropertyBytes(devInfo, ref data, property);
        if (bytes.Length == 0)
        {
            return Array.Empty<string>();
        }

        var s = Encoding.Unicode.GetString(bytes);
        return s.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static byte[] TryGetDeviceRegistryPropertyBytes(IntPtr devInfo, ref SP_DEVINFO_DATA data, uint property)
    {
        var buf = new byte[1024];
        if (SetupDiGetDeviceRegistryPropertyW(devInfo, ref data, property, out _, buf, (uint)buf.Length, out var required))
        {
            return buf.AsSpan(0, (int)Math.Min(required, (uint)buf.Length)).ToArray();
        }

        var err = Marshal.GetLastWin32Error();
        if (err != ERROR_INSUFFICIENT_BUFFER || required <= 0)
        {
            return Array.Empty<byte>();
        }

        buf = new byte[required];
        if (SetupDiGetDeviceRegistryPropertyW(devInfo, ref data, property, out _, buf, (uint)buf.Length, out required))
        {
            return buf.AsSpan(0, (int)Math.Min(required, (uint)buf.Length)).ToArray();
        }

        return Array.Empty<byte>();
    }

    private static bool TryGetVidMi(string pnpDeviceId, out string vid, out string mi)
    {
        vid = string.Empty;
        mi = string.Empty;
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return false;
        }

        var mVid = VidRegex.Match(pnpDeviceId);
        if (!mVid.Success)
        {
            return false;
        }

        var mMi = MiRegex.Match(pnpDeviceId);
        if (!mMi.Success)
        {
            return false;
        }

        vid = mVid.Groups[1].Value;
        mi = mMi.Groups[1].Value;
        return vid.Length == 4 && mi.Length == 2;
    }

    public sealed record DiagPortCandidate(string PortName, string PnpDeviceId, string Name, string Vid, string Mi);

    private static readonly IntPtr InvalidHandleValue = new(-1);

    private const uint DIGCF_PRESENT = 0x00000002;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    private const uint SPDRP_DEVICEDESC = 0x00000000;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        ref Guid ClassGuid,
        string? Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr DeviceInfoSet,
        uint MemberIndex,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        out uint PropertyRegDataType,
        [Out] byte[] PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        StringBuilder DeviceInstanceId,
        int DeviceInstanceIdSize,
        out int RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
}
