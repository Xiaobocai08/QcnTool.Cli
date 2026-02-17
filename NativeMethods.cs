using System.Runtime.InteropServices;

namespace QcnTool.Cli;

internal static class NativeMethods
{
    internal const ushort NV_UE_IMEI_I = 550;
    private const string QmslDll = "QMSL_MSVC10R.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void NvToolCallback(
        IntPtr qmslContext,
        ushort subscriptionId,
        ushort nvId,
        ushort sourceFunc,
        ushort eventId,
        ushort progress);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void QLIB_SetLibraryMode(byte useQpstMode);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr QLIB_ConnectServer(uint comPortNumber);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte QLIB_IsPhoneConnected(IntPtr resourceContext);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte QLIB_DIAG_EXT_BUILD_ID_F(
        IntPtr resourceContext,
        ref uint msmHwVersion,
        ref uint mobileModel,
        [Out] byte[] mobileSoftwareRevision,
        [Out] byte[] modelString);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte QLIB_NV_SetTargetSupportMultiSIM(
        IntPtr resourceContext,
        [MarshalAs(UnmanagedType.I1)] bool targetSupportsMultiSim);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void QLIB_NV_ConfigureCallBack(
        IntPtr resourceContext,
        NvToolCallback? callback);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte QLIB_DIAG_NV_READ_EXT_F(
        IntPtr resourceContext,
        ushort itemId,
        [Out] byte[] itemData,
        ushort contextId,
        int length,
        ref ushort status);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte QLIB_DIAG_SPC_F(
        IntPtr resourceContext,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 6)] byte[] spc,
        ref int spcResult);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern byte QLIB_BackupNVFromMobileToQCN(
        IntPtr resourceContext,
        string qcnPath,
        ref int resultCode);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern byte QLIB_NV_LoadNVsFromQCN(
        IntPtr resourceContext,
        string qcnPath,
        ref int loadedItemCount,
        ref int resultCode);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte QLIB_NV_WriteNVsToMobile(
        IntPtr resourceContext,
        ref int resultCode);

    [DllImport(QmslDll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void QLIB_DisconnectServer(IntPtr resourceContext);
}
