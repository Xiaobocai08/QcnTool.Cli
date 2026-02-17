using System.Globalization;
using System.Text;

namespace QcnTool.Cli;

internal sealed class QcnToolClient : IDisposable
{
    private const string DefaultImei = "000000000000000";

    private readonly int _portNumber;
    private readonly Localizer _l10n;
    private readonly NativeMethods.NvToolCallback _progressCallback;
    private readonly object _progressLock = new();
    private IntPtr _handle;
    private string _progressOperation = string.Empty;
    private int _lastProgress = -1;
    private DeviceInfo _deviceInfo = DeviceInfo.Empty;

    public QcnToolClient(int portNumber, Localizer localizer)
    {
        _portNumber = portNumber;
        _l10n = localizer;
        _progressCallback = OnNvProgressChanged;
    }

    public bool ConnectDevice(bool configureSimMode = true)
    {
        Console.WriteLine(_l10n.T("正在设置 QPST 模式...", "Setting QPST mode..."));
        NativeMethods.QLIB_SetLibraryMode(0);

        Console.WriteLine(_l10n.T($"正在连接手机端口 COM{_portNumber}...", $"Connecting to phone on COM{_portNumber}..."));
        _handle = NativeMethods.QLIB_ConnectServer((uint)_portNumber);
        if (_handle == IntPtr.Zero || NativeMethods.QLIB_IsPhoneConnected(_handle) == 0)
        {
            Console.Error.WriteLine(_l10n.T("连接失败，请检查 DIAG 端口和驱动。", "Failed to connect. Check DIAG port and driver."));
            return false;
        }

        Console.WriteLine(_l10n.T("连接成功。", "Connected."));
        var buildInfo = ReadBuildInfo();
        if (buildInfo.HasValue)
        {
            Console.WriteLine(_l10n.T($"MSM-HW 版本: {buildInfo.Value.MsmHwVersion}", $"MSM-HW version: {buildInfo.Value.MsmHwVersion}"));
            Console.WriteLine(_l10n.T($"设备型号码: {buildInfo.Value.MobileModel}", $"Mobile model : {buildInfo.Value.MobileModel}"));
        }

        if (!SendSpc("000000"))
        {
            return false;
        }

        var imei1 = ReadImei(0);
        var imei2 = ReadImei(1);
        Console.WriteLine($"IMEI1: {imei1}");
        Console.WriteLine($"IMEI2: {imei2}");

        bool isDualSim = !string.Equals(imei1, imei2, StringComparison.Ordinal);
        if (buildInfo.HasValue)
        {
            _deviceInfo = new DeviceInfo(
                PortNumber: _portNumber,
                MsmHwVersion: buildInfo.Value.MsmHwVersion,
                MobileModel: buildInfo.Value.MobileModel,
                SoftwareRevision: buildInfo.Value.SoftwareRevision,
                ModelString: buildInfo.Value.ModelString,
                Imei1: imei1,
                Imei2: imei2,
                IsDualSim: isDualSim);
        }
        else
        {
            _deviceInfo = new DeviceInfo(
                PortNumber: _portNumber,
                MsmHwVersion: null,
                MobileModel: null,
                SoftwareRevision: string.Empty,
                ModelString: string.Empty,
                Imei1: imei1,
                Imei2: imei2,
                IsDualSim: isDualSim);
        }

        if (!configureSimMode)
        {
            Console.WriteLine(_l10n.T(
                $"SIM 模式推断: {(isDualSim ? "双卡" : "单卡")}（信息采集模式，不写入）",
                $"SIM mode infer: {(isDualSim ? "dual" : "single")} (info-only, no write)"));
            return true;
        }

        Console.WriteLine(_l10n.T($"正在配置 SIM 模式: {(isDualSim ? "双卡" : "单卡")}...", $"Configuring SIM mode: {(isDualSim ? "dual" : "single")}..."));
        if (NativeMethods.QLIB_NV_SetTargetSupportMultiSIM(_handle, isDualSim) == 0)
        {
            Console.Error.WriteLine(_l10n.T("SIM 模式配置失败。", "Failed to configure SIM mode."));
            return false;
        }

        Console.WriteLine(_l10n.T("SIM 模式配置成功。", "SIM mode configured."));
        return true;
    }

    public void PrintDeviceInfo()
    {
        Console.WriteLine(_l10n.T("----- 设备信息 -----", "----- Device Info -----"));
        Console.WriteLine($"{_l10n.T("端口", "Port"),-16}: COM{_deviceInfo.PortNumber}");
        Console.WriteLine($"{_l10n.T("MSM-HW 版本", "MSM-HW version"),-16}: {_deviceInfo.MsmHwVersion?.ToString(CultureInfo.InvariantCulture) ?? _l10n.T("无", "N/A")}");
        Console.WriteLine($"{_l10n.T("设备型号码", "Mobile model"),-16}: {_deviceInfo.MobileModel?.ToString(CultureInfo.InvariantCulture) ?? _l10n.T("无", "N/A")}");
        Console.WriteLine($"{_l10n.T("软件版本", "Software rev"),-16}: {FormatOrUnknown(_deviceInfo.SoftwareRevision)}");
        Console.WriteLine($"{_l10n.T("设备字符串", "Model string"),-16}: {FormatOrUnknown(_deviceInfo.ModelString)}");
        Console.WriteLine($"IMEI1           : {_deviceInfo.Imei1}");
        Console.WriteLine($"IMEI2           : {_deviceInfo.Imei2}");
        Console.WriteLine($"{_l10n.T("SIM 模式推断", "SIM mode infer"),-16}: {_l10n.T(_deviceInfo.IsDualSim ? "双卡" : "单卡", _deviceInfo.IsDualSim ? "dual" : "single")}");
    }

    public bool WriteQcn(string qcnPath)
    {
        int loadedItems = -1;
        int loadResultCode = -1;

        using (NativeConsoleSilencer.Begin())
        {
            if (NativeMethods.QLIB_NV_LoadNVsFromQCN(_handle, qcnPath, ref loadedItems, ref loadResultCode) == 0)
            {
                Console.Error.WriteLine(_l10n.T($"加载 QCN 失败。resultCode={loadResultCode}", $"Failed to load QCN. resultCode={loadResultCode}"));
                return false;
            }
        }
        _ = loadedItems;

        Console.WriteLine(_l10n.T("正在写入 NV 到手机...", "Writing NV items to mobile..."));
        StartProgress(_l10n.T("写入", "Write"));
        try
        {
            int writeResultCode = -1;
            if (NativeMethods.QLIB_NV_WriteNVsToMobile(_handle, ref writeResultCode) == 0)
            {
                Console.Error.WriteLine(_l10n.T($"写入失败。resultCode={writeResultCode}", $"Write failed. resultCode={writeResultCode}"));
                return false;
            }

            Console.WriteLine(_l10n.T($"写入完成。resultCode={writeResultCode}", $"Write completed. resultCode={writeResultCode}"));
            return true;
        }
        finally
        {
            StopProgress();
        }
    }

    public bool ReadQcn(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Console.WriteLine(_l10n.T($"正在从手机读取 QCN 到: {fullPath}", $"Reading QCN from mobile to: {fullPath}"));
        StartProgress(_l10n.T("读取", "Read"));
        try
        {
            int readResultCode = -1;
            if (NativeMethods.QLIB_BackupNVFromMobileToQCN(_handle, fullPath, ref readResultCode) == 0)
            {
                Console.Error.WriteLine(_l10n.T($"读取失败。resultCode={readResultCode}", $"Read failed. resultCode={readResultCode}"));
                return false;
            }

            Console.WriteLine(_l10n.T($"读取完成。resultCode={readResultCode}", $"Read completed. resultCode={readResultCode}"));
            return true;
        }
        finally
        {
            StopProgress();
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.QLIB_DisconnectServer(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private bool SendSpc(string spc)
    {
        if (spc.Length != 6)
        {
            Console.Error.WriteLine(_l10n.T("SPC 必须是 6 位数字。", "SPC must be 6 digits."));
            return false;
        }

        var spcBytes = Encoding.ASCII.GetBytes(spc);
        int spcResult = -1;
        Console.WriteLine(_l10n.T("正在发送 SPC...", "Sending SPC..."));
        if (NativeMethods.QLIB_DIAG_SPC_F(_handle, spcBytes, ref spcResult) == 0)
        {
            Console.Error.WriteLine(_l10n.T($"SPC 发送失败。result={spcResult}", $"SPC failed. result={spcResult}"));
            return false;
        }

        Console.WriteLine(_l10n.T("SPC 验证通过。", "SPC accepted."));
        return true;
    }

    private BuildInfo? ReadBuildInfo()
    {
        var softwareRevision = new byte[512];
        var modelString = new byte[512];
        uint msmHwVersion = 0;
        uint mobileModel = 0;
        if (NativeMethods.QLIB_DIAG_EXT_BUILD_ID_F(
                _handle,
                ref msmHwVersion,
                ref mobileModel,
                softwareRevision,
                modelString) == 0)
        {
            return null;
        }

        return new BuildInfo(
            msmHwVersion,
            mobileModel,
            ReadNullTerminatedAnsi(softwareRevision),
            ReadNullTerminatedAnsi(modelString));
    }

    private string ReadImei(int index)
    {
        var rawData = new byte[128];
        ushort status = 4;
        if (NativeMethods.QLIB_DIAG_NV_READ_EXT_F(
                _handle,
                NativeMethods.NV_UE_IMEI_I,
                rawData,
                (ushort)index,
                rawData.Length,
                ref status) == 0)
        {
            return DefaultImei;
        }

        var digits = new int[15];
        int digitPosition = 0;
        for (int i = 1; i <= 8; i++)
        {
            if (i != 8)
            {
                digits[digitPosition] = (rawData[i] & 0xF0) >> 4;
                digits[digitPosition + 1] = rawData[i + 1] & 0x0F;
            }
            else
            {
                digits[digitPosition] = (rawData[i] & 0xF0) >> 4;
            }

            digitPosition += 2;
        }

        var imeiBuilder = new StringBuilder(15);
        for (int i = 0; i < digits.Length; i++)
        {
            imeiBuilder.Append(digits[i].ToString(CultureInfo.InvariantCulture));
        }

        var imei = imeiBuilder.ToString();
        return imei.Length == 15 ? imei : DefaultImei;
    }

    private void StartProgress(string operation)
    {
        lock (_progressLock)
        {
            _progressOperation = operation;
            _lastProgress = -1;
        }

        NativeMethods.QLIB_NV_ConfigureCallBack(_handle, _progressCallback);
    }

    private void StopProgress()
    {
        NativeMethods.QLIB_NV_ConfigureCallBack(_handle, null);

        lock (_progressLock)
        {
            if (_lastProgress >= 0)
            {
                Console.WriteLine();
            }

            _progressOperation = string.Empty;
            _lastProgress = -1;
        }
    }

    private void OnNvProgressChanged(
        IntPtr qmslContext,
        ushort subscriptionId,
        ushort nvId,
        ushort sourceFunc,
        ushort eventId,
        ushort progress)
    {
        lock (_progressLock)
        {
            if (string.IsNullOrEmpty(_progressOperation))
            {
                return;
            }

            if (progress == _lastProgress)
            {
                return;
            }

            _lastProgress = progress;
            Console.Write($"\r{_progressOperation}{_l10n.T("进度", " progress")}: {FormatProgressValue(progress),-8}");
        }
    }

    private static string FormatProgressValue(int progress)
    {
        return progress <= 100
            ? $"{progress}%"
            : progress.ToString(CultureInfo.InvariantCulture);
    }

    private string FormatOrUnknown(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? _l10n.T("无", "N/A") : value;
    }

    private static string ReadNullTerminatedAnsi(byte[] rawBuffer)
    {
        int zeroIndex = Array.IndexOf(rawBuffer, (byte)0);
        int length = zeroIndex >= 0 ? zeroIndex : rawBuffer.Length;
        return length == 0 ? string.Empty : Encoding.ASCII.GetString(rawBuffer, 0, length).Trim();
    }

    private readonly record struct BuildInfo(
        uint MsmHwVersion,
        uint MobileModel,
        string SoftwareRevision,
        string ModelString);

    private readonly record struct DeviceInfo(
        int PortNumber,
        uint? MsmHwVersion,
        uint? MobileModel,
        string SoftwareRevision,
        string ModelString,
        string Imei1,
        string Imei2,
        bool IsDualSim)
    {
        public static DeviceInfo Empty => new(
            PortNumber: 0,
            MsmHwVersion: null,
            MobileModel: null,
            SoftwareRevision: string.Empty,
            ModelString: string.Empty,
            Imei1: DefaultImei,
            Imei2: DefaultImei,
            IsDualSim: false);
    }
}
