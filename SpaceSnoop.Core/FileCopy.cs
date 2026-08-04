using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core;

internal static class FileCopy
{
    private const uint ProgressContinue = 0;
    private const uint ProgressCancel = 1;
    private const uint CallbackChunkFinished = 0;
    private const int ErrorRequestAborted = 1235;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint CopyProgressRoutine(
        long totalFileSize,
        long totalBytesTransferred,
        long streamSize,
        long streamBytesTransferred,
        uint streamNumber,
        uint callbackReason,
        IntPtr sourceFile,
        IntPtr destinationFile,
        IntPtr data);

    public static void Copy(string source, string destination, Action<long>? onTransferred, CancellationToken cancel)
    {
        var abort = 0;

        CopyProgressRoutine routine = (_, transferred, _, _, _, reason, _, _, _) =>
        {
            if (reason == CallbackChunkFinished)
            {
                onTransferred?.Invoke(transferred);
            }

            return cancel.IsCancellationRequested ? ProgressCancel : ProgressContinue;
        };

        var copied = CopyFileEx(source, destination, routine, IntPtr.Zero, ref abort, 0);

        GC.KeepAlive(routine);

        if (copied)
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();

        if (error == ErrorRequestAborted)
        {
            throw new OperationCanceledException(cancel);
        }

        throw new IOException(new Win32Exception(error).Message, Marshal.GetHRForLastWin32Error());
    }

    [DllImport("kernel32.dll", EntryPoint = "CopyFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CopyFileEx(
        string existingFileName,
        string newFileName,
        CopyProgressRoutine? progressRoutine,
        IntPtr data,
        ref int cancel,
        uint copyFlags);
}
