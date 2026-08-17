using System.Runtime.InteropServices;

namespace VL.Interpole;

// A tiny native "open file" dialog wrapper (Win32 comdlg32 GetOpenFileNameW), used when the user
// double-clicks an empty audio lane to pick a sound file. We call the Win32 common dialog directly
// rather than pulling in the whole WinForms stack (UseWindowsForms) — one P/Invoke, no extra
// dependency. The dialog is modal and must run on an STA thread, and the ImGui/Skia render thread
// is not guaranteed to be STA, so Open() spins up a dedicated STA thread, blocks on it, and returns
// the chosen path (or null if cancelled).
internal static class NativeFileDialog
{
    // Subset of OPENFILENAMEW we need. CharSet.Unicode → the "W" (wide) entry point.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string? lpstrFile;          // receives the full path (caller-allocated buffer)
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_NOCHANGEDIR = 0x00000008;   // don't mutate the process's CWD

    // Show a modal "open file" dialog and return the chosen absolute path, or null if cancelled.
    // `filter` is a Win32 double-null-terminated filter list, e.g.
    //   "Audio files\0*.wav;*.mp3;*.aif;*.aiff;*.flac\0All files\0*.*\0"
    // Blocks the calling thread until the dialog closes; runs the dialog on its own STA thread.
    public static string? Open(string title, string filter, string? initialDir = null)
    {
        string? result = null;

        var t = new Thread(() =>
        {
            var buffer = new string('\0', 4096);   // GetOpenFileNameW writes the path in-place here
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = buffer.Length,
                lpstrTitle = title,
                lpstrInitialDir = initialDir,
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_EXPLORER | OFN_NOCHANGEDIR,
            };

            if (GetOpenFileNameW(ref ofn))
            {
                // lpstrFile now holds the path, null-terminated inside the buffer.
                int end = ofn.lpstrFile!.IndexOf('\0');
                result = end >= 0 ? ofn.lpstrFile[..end] : ofn.lpstrFile;
                if (string.IsNullOrWhiteSpace(result)) result = null;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();

        return result;
    }

    // Audio-file filter for the open dialog (double-null-terminated, as Win32 expects).
    public const string AudioFilter =
        "Audio files\0*.wav;*.mp3;*.aif;*.aiff;*.flac\0WAV files\0*.wav\0All files\0*.*\0\0";
}
