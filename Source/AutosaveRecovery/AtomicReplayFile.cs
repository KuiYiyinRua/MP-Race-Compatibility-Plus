using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AutosaveRecovery.Tests")]

namespace Meow.AutosaveRecovery
{
    public static class AtomicReplayFile
    {
        // Only local file names use this GUID; no simulation or Verse Rand state changes.
        public static string UniqueSuffix() => ".tmp-" + Guid.NewGuid().ToString("N") + ".zip";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existing, string destination, uint flags);

        public static FileInfo Commit(FileInfo source, string destination, string backup)
            => CommitCore(source, destination, backup, (s, d, b) => s.Replace(d, b));

        internal static FileInfo CommitCore(FileInfo source, string destination, string backup,
            Func<FileInfo, string, string, FileInfo> replace)
        {
            // Match the original API for any future caller that requests a backup.
            if (backup != null) return replace(source, destination, backup);
            Exception lastError = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { return replace(source, destination, null); }
                catch (IOException e) { lastError = e; }
                catch (UnauthorizedAccessException e) { lastError = e; }

                // ReplaceFile may fail with ERROR_UNABLE_TO_REMOVE_REPLACED.
                // Same-directory MoveFileEx avoids that interface's metadata merge.
                // REPLACE_EXISTING | WRITE_THROUGH; never delete the old save first.
                if (Environment.OSVersion.Platform == PlatformID.Win32NT &&
                    MoveFileEx(source.FullName, destination, 1u | 8u))
                    return new FileInfo(destination);
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    lastError = new Win32Exception(Marshal.GetLastWin32Error());
                if (attempt < 2) Thread.Sleep(100);
            }
            throw new IOException("Replay commit failed; previous save was not deliberately deleted. " +
                "New replay retained for recovery: " + source.FullName, lastError);
        }
    }
}
