using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ServerPilot.Agent.Backups;

internal static class BackupFileSafety
{
    internal static void RejectHardLinks(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!GetFileInformationByHandle(handle, out FileInformation information) || information.NumberOfLinks != 1)
            throw new IOException("Backup source must be an ordinary, unlinked local file.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
