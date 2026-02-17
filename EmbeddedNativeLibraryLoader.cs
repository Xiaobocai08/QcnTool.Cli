using System.Reflection;
using System.Runtime.InteropServices;

namespace QcnTool.Cli;

internal static class EmbeddedNativeLibraryLoader
{
    private const string QmslDllFileName = "QMSL_MSVC10R.dll";
    private const string QmslResourceName = "QcnTool.Cli.native.QMSL_MSVC10R.dll";

    private static readonly object SyncRoot = new();
    private static bool _initialized;
    private static IntPtr _qmslHandle;

    public static bool EnsureQmslLoaded(Localizer l10n, out string errorMessage)
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                errorMessage = string.Empty;
                return true;
            }

            if (NativeLibrary.TryLoad(QmslDllFileName, out _qmslHandle))
            {
                _initialized = true;
                errorMessage = string.Empty;
                return true;
            }

            var assembly = typeof(EmbeddedNativeLibraryLoader).Assembly;
            using var resourceStream = assembly.GetManifestResourceStream(QmslResourceName);
            if (resourceStream is null)
            {
                errorMessage = l10n.T(
                    $"未找到内嵌资源: {QmslResourceName}",
                    $"Embedded resource not found: {QmslResourceName}");
                return false;
            }

            string extractDirectory;
            try
            {
                extractDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QcnTool.Cli",
                    "native");
                Directory.CreateDirectory(extractDirectory);
            }
            catch (Exception ex)
            {
                errorMessage = l10n.T(
                    $"创建 DLL 解压目录失败: {ex.Message}",
                    $"Failed to create extraction directory: {ex.Message}");
                return false;
            }

            var targetPath = Path.Combine(extractDirectory, QmslDllFileName);
            try
            {
                using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                resourceStream.CopyTo(fileStream);
            }
            catch (Exception ex)
            {
                errorMessage = l10n.T(
                    $"释放内嵌 DLL 失败: {ex.Message}",
                    $"Failed to extract embedded DLL: {ex.Message}");
                return false;
            }

            if (!NativeLibrary.TryLoad(targetPath, out _qmslHandle))
            {
                errorMessage = l10n.T(
                    $"加载 QMSL DLL 失败: {targetPath}",
                    $"Failed to load QMSL DLL: {targetPath}");
                return false;
            }

            _initialized = true;
            errorMessage = string.Empty;
            return true;
        }
    }

    public static string GetExtractionPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QcnTool.Cli",
            "native",
            QmslDllFileName);
    }
}
