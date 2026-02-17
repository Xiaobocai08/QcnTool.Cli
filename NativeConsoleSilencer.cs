using System.Runtime.InteropServices;

namespace QcnTool.Cli;

internal sealed class NativeConsoleSilencer : IDisposable
{
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    private const int OWrOnly = 0x0001;
    private const int StdOutFd = 1;
    private const int StdErrFd = 2;

    private readonly bool _isActive;
    private int _nulFd = -1;
    private int _savedStdOutFd = -1;
    private int _savedStdErrFd = -1;

    private NativeConsoleSilencer()
    {
        _isActive = TryRedirectMsvcr100StdStreams();
    }

    public static NativeConsoleSilencer Begin()
    {
        return new NativeConsoleSilencer();
    }

    public void Dispose()
    {
        if (!_isActive)
        {
            return;
        }

        _ = fflush(IntPtr.Zero);

        if (_savedStdOutFd >= 0)
        {
            _ = _dup2(_savedStdOutFd, StdOutFd);
            _ = _close(_savedStdOutFd);
            _savedStdOutFd = -1;
        }

        if (_savedStdErrFd >= 0)
        {
            _ = _dup2(_savedStdErrFd, StdErrFd);
            _ = _close(_savedStdErrFd);
            _savedStdErrFd = -1;
        }

        if (_nulFd >= 0)
        {
            _ = _close(_nulFd);
            _nulFd = -1;
        }
    }

    private bool TryRedirectMsvcr100StdStreams()
    {
        IntPtr nulHandle = CreateFileW(
            "NUL",
            GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        if (nulHandle == IntPtr.Zero || nulHandle == InvalidHandleValue)
        {
            return false;
        }

        try
        {
            _nulFd = _open_osfhandle(nulHandle, OWrOnly);
            if (_nulFd < 0)
            {
                _ = CloseHandle(nulHandle);
                return false;
            }

            _savedStdOutFd = _dup(StdOutFd);
            _savedStdErrFd = _dup(StdErrFd);
            if (_savedStdOutFd < 0 || _savedStdErrFd < 0)
            {
                CleanupPartialState();
                return false;
            }

            _ = fflush(IntPtr.Zero);
            if (_dup2(_nulFd, StdOutFd) != 0 || _dup2(_nulFd, StdErrFd) != 0)
            {
                CleanupPartialState();
                return false;
            }

            return true;
        }
        catch (DllNotFoundException)
        {
            _ = CloseHandle(nulHandle);
            CleanupPartialState();
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            _ = CloseHandle(nulHandle);
            CleanupPartialState();
            return false;
        }
    }

    private void CleanupPartialState()
    {
        if (_savedStdOutFd >= 0)
        {
            _ = _close(_savedStdOutFd);
            _savedStdOutFd = -1;
        }

        if (_savedStdErrFd >= 0)
        {
            _ = _close(_savedStdErrFd);
            _savedStdErrFd = -1;
        }

        if (_nulFd >= 0)
        {
            _ = _close(_nulFd);
            _nulFd = -1;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("msvcr100.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int _open_osfhandle(IntPtr osfhandle, int flags);

    [DllImport("msvcr100.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int _dup(int fd);

    [DllImport("msvcr100.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int _dup2(int sourceFd, int targetFd);

    [DllImport("msvcr100.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int _close(int fd);

    [DllImport("msvcr100.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int fflush(IntPtr stream);
}
