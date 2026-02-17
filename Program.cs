using System.Globalization;

namespace QcnTool.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        var parserLanguage = ResolveLanguageFromArgsOrDefault(args);
        if (!TryParseArguments(args, parserLanguage, out var options, out var errorMessage))
        {
            var localizerForError = new Localizer(options.Language);
            Console.Error.WriteLine(errorMessage);
            PrintUsage(localizerForError);
            return 1;
        }

        var l10n = new Localizer(options.Language);
        if (options.Mode == OperationMode.Help)
        {
            PrintUsage(l10n);
            return 0;
        }

        if (options.Mode == OperationMode.Version)
        {
            PrintBanner();
            return 0;
        }

        PrintBanner();

        if (!EmbeddedNativeLibraryLoader.EnsureQmslLoaded(l10n, out var loadError))
        {
            Console.Error.WriteLine(loadError);
            return 1;
        }

        if (!TryResolvePortNumber(options, l10n, out var portNumber))
        {
            return 1;
        }

        using var client = new QcnToolClient(portNumber, l10n);
        if (!client.ConnectDevice(configureSimMode: options.Mode != OperationMode.Info))
        {
            return 1;
        }

        if (options.Mode == OperationMode.Info)
        {
            client.PrintDeviceInfo();
            return 0;
        }

        if (options.Mode == OperationMode.Write)
        {
            var qcnPath = Path.GetFullPath(options.FilePath!);
            if (!File.Exists(qcnPath))
            {
                Console.Error.WriteLine(l10n.T($"输入 QCN 文件不存在: {qcnPath}", $"Input QCN file not found: {qcnPath}"));
                return 1;
            }

            return client.WriteQcn(qcnPath) ? 0 : 1;
        }

        var outputPath = string.IsNullOrWhiteSpace(options.FilePath)
            ? Path.Combine(Environment.CurrentDirectory, $"QCN_{DateTime.Now:yyyyMMdd_HHmmss}.qcn")
            : Path.GetFullPath(options.FilePath);
        return client.ReadQcn(outputPath) ? 0 : 1;
    }

    private static bool TryParseArguments(
        string[] args,
        AppLanguage parserLanguage,
        out CliOptions options,
        out string errorMessage)
    {
        options = new CliOptions(OperationMode.Help, null, null, parserLanguage);
        errorMessage = string.Empty;

        var language = parserLanguage;
        bool writeMode = false;
        bool readMode = false;
        bool infoMode = false;
        bool helpMode = false;
        bool versionMode = false;
        int? portNumber = null;
        string? filePath = null;

        string T(string zh, string en)
        {
            return language == AppLanguage.Zh ? zh : en;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-w":
                case "--write":
                    writeMode = true;
                    break;
                case "-r":
                case "--read":
                    readMode = true;
                    break;
                case "-i":
                case "--info":
                    infoMode = true;
                    break;
                case "-p":
                case "--port":
                    {
                        if (i + 1 >= args.Length)
                        {
                            errorMessage = T("参数 --port 缺少值。", "Missing value after --port.");
                            return false;
                        }

                        if (!TryParsePort(args[++i], out var parsedPort))
                        {
                            errorMessage = T($"端口值无效: {args[i]}", $"Invalid port value: {args[i]}");
                            return false;
                        }

                        portNumber = parsedPort;
                        break;
                    }
                case "-f":
                case "--file":
                    {
                        if (i + 1 >= args.Length)
                        {
                            errorMessage = T("参数 --file 缺少值。", "Missing value after --file.");
                            return false;
                        }

                        filePath = args[++i];
                        break;
                    }
                case "-l":
                case "--lang":
                    {
                        if (i + 1 >= args.Length)
                        {
                            errorMessage = T("参数 --lang 缺少值。", "Missing value after --lang.");
                            return false;
                        }

                        var rawLanguage = args[++i];
                        if (!Localizer.TryParse(rawLanguage, out language))
                        {
                            errorMessage = T(
                                $"语言参数无效: {rawLanguage}，可用值: zh/en/auto。",
                                $"Invalid language value: {rawLanguage}. Valid values: zh/en/auto.");
                            return false;
                        }

                        break;
                    }
                case "-h":
                case "--help":
                    helpMode = true;
                    break;
                case "-v":
                case "--version":
                    versionMode = true;
                    break;
                default:
                    errorMessage = T($"未知参数: {arg}", $"Unknown argument: {arg}");
                    return false;
            }
        }

        if (language == AppLanguage.Auto)
        {
            language = Localizer.DetectSystemLanguage();
        }

        if (helpMode)
        {
            options = new CliOptions(OperationMode.Help, portNumber, filePath, language);
            return true;
        }

        if (versionMode)
        {
            options = new CliOptions(OperationMode.Version, portNumber, filePath, language);
            return true;
        }

        int selectedModeCount = (writeMode ? 1 : 0) + (readMode ? 1 : 0) + (infoMode ? 1 : 0);
        if (selectedModeCount != 1)
        {
            errorMessage = language == AppLanguage.Zh
                ? "请且仅请选择一种模式: --write 或 --read 或 --info。"
                : "Specify exactly one mode: --write or --read or --info.";
            return false;
        }

        if (writeMode && string.IsNullOrWhiteSpace(filePath))
        {
            errorMessage = language == AppLanguage.Zh
                ? "写入模式必须提供 --file <input.qcn>。"
                : "Write mode requires --file <input.qcn>.";
            return false;
        }

        if (infoMode && !string.IsNullOrWhiteSpace(filePath))
        {
            errorMessage = language == AppLanguage.Zh
                ? "信息采集模式不需要 --file 参数。"
                : "Info mode does not use --file.";
            return false;
        }

        var mode = writeMode ? OperationMode.Write : (readMode ? OperationMode.Read : OperationMode.Info);
        options = new CliOptions(mode, portNumber, filePath, language);
        return true;
    }

    private static AppLanguage ResolveLanguageFromArgsOrDefault(string[] args)
    {
        var language = Localizer.DetectSystemLanguage();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "-l" || arg == "--lang") && i + 1 < args.Length)
            {
                if (Localizer.TryParse(args[i + 1], out var parsed))
                {
                    language = parsed == AppLanguage.Auto ? Localizer.DetectSystemLanguage() : parsed;
                }

                break;
            }
        }

        return language;
    }

    private static bool TryResolvePortNumber(CliOptions options, Localizer l10n, out int portNumber)
    {
        if (options.PortNumber is { } explicitPort)
        {
            portNumber = explicitPort;
            return true;
        }

        Console.WriteLine(l10n.T("未指定端口，正在自动识别 Qualcomm DIAG 端口...", "No port specified. Auto-detecting Qualcomm DIAG port..."));
        if (DiagPortScanner.TryAutoSelectSingleDiagPort(verbose: false, out var selectedPort, out var candidates)
            && !string.IsNullOrWhiteSpace(selectedPort)
            && TryParsePort(selectedPort, out portNumber))
        {
            Console.WriteLine(l10n.T($"自动识别成功: {selectedPort}", $"Auto-selected port: {selectedPort}"));
            return true;
        }

        portNumber = 0;
        if (candidates.Count == 0)
        {
            Console.Error.WriteLine(l10n.T("未找到符合规则的 Qualcomm 端口 (VID_05C6)。", "No matching Qualcomm port found (VID_05C6)."));
            return false;
        }

        Console.Error.WriteLine(l10n.T("自动识别失败，候选端口如下，请手动通过 --port 指定:", "Auto-select failed. Candidates below, please specify --port manually:"));
        foreach (var candidate in candidates)
        {
            Console.Error.WriteLine($"  {candidate.PortName}  {candidate.Name}  (VID_{candidate.Vid}, MI_{candidate.Mi})");
        }

        return false;
    }

    private static bool TryParsePort(string rawPort, out int portNumber)
    {
        portNumber = 0;
        if (string.IsNullOrWhiteSpace(rawPort))
        {
            return false;
        }

        var normalized = rawPort.Trim();
        if (normalized.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out portNumber)
               && portNumber > 0;
    }

    private static void PrintBanner()
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        Console.WriteLine($"QcnTool.Cli v{version}");
    }

    private static void PrintUsage(Localizer l10n)
    {
        PrintBanner();
        Console.WriteLine(l10n.T("用法:", "Usage:"));
        Console.WriteLine("  --write --file <input.qcn> [--port COM4] [--lang zh|en|auto]");
        Console.WriteLine("  --read  [--file output.qcn] [--port COM4] [--lang zh|en|auto]");
        Console.WriteLine("  --info  [--port COM4] [--lang zh|en|auto]");
        Console.WriteLine("  --help");
        Console.WriteLine("  --version");
        Console.WriteLine(l10n.T("提示: 未指定 --port 时会按 Qualcomm VID/MI 规则自动识别。", "Tip: if --port is not specified, Qualcomm VID/MI rules are used for auto-detection."));
    }
}

internal readonly record struct CliOptions(OperationMode Mode, int? PortNumber, string? FilePath, AppLanguage Language);

internal enum OperationMode
{
    Write,
    Read,
    Info,
    Help,
    Version
}
