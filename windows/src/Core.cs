// Easy Uninstall for Windows — shared helpers.
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4.8), so the app
// builds with no SDK install and runs on any Windows 10/11 PC with nothing extra.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace EasyUninstall
{
    static class AppInfo
    {
        public const string Repo = "shakilnextive-beep/Easy-Uninstall";
        public const string Creator = "Md. Shakil";
        public const string Email = "shakil.nextive@gmail.com";
        public const string RepoUrl = "https://github.com/" + Repo;
        public const string FeedbackUrl = RepoUrl + "/discussions";
        public const string IssuesUrl = RepoUrl + "/issues/new/choose";
        public const string ReleasesUrl = RepoUrl + "/releases/latest";

        public static string Version
        {
            get { var v = typeof(AppInfo).Assembly.GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }
        public static string ExePath { get { return typeof(AppInfo).Assembly.Location; } }
    }

    /// App language: Bangla (default) or English. Saved between launches.
    static class L
    {
        const string Key = @"Software\EasyUninstall";
        static bool english;
        public static event Action Changed;

        static L()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) { if (k != null) english = Convert.ToInt32(k.GetValue("English", 0)) == 1; } }
            catch { }
        }

        public static bool English
        {
            get { return english; }
            set
            {
                if (english == value) return;
                english = value;
                try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("English", value ? 1 : 0, RegistryValueKind.DWord); } catch { }
                if (Changed != null) Changed();
            }
        }

        /// Language for this run only (dry runs, screenshots) — the saved choice is left alone.
        public static void UseOnce(bool en) { english = en; }

        /// Picks the Bangla or English text for the current language.
        public static string T(string bn, string en) { return english ? en : bn; }
    }

    static class FS
    {
        public static readonly string Home = Trim(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        public static readonly string Roaming = Trim(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        public static readonly string Local = Trim(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        public static readonly string ProgramData = Trim(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        public static readonly string WinDir = Trim(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        public static readonly string SysDrive = Path.GetPathRoot(Environment.SystemDirectory);   // "C:\"
        public static readonly string PF = Trim(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        public static readonly string PF86 = Trim(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "");
        public static readonly string Temp = Trim(Path.GetTempPath());
        public static readonly string Downloads = FindDownloads();

        public static string Trim(string p) { return string.IsNullOrEmpty(p) ? "" : (p.Length > 3 ? p.TrimEnd('\\') : p); }
        public static string Special(Environment.SpecialFolder f) { return Trim(Environment.GetFolderPath(f)); }

        static string FindDownloads()
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                if (Native.SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out p) == 0) return Trim(Marshal.PtrToStringUni(p));
            }
            catch { }
            finally { if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }
            return Path.Combine(Home, "Downloads");
        }

        public static bool Exists(string p) { return !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)); }

        // Files that only live in the cloud (OneDrive "online-only") take no disk space.
        const int CloudOnly = 0x1000 | 0x40000 | 0x400000;   // OFFLINE | RECALL_ON_OPEN | RECALL_ON_DATA_ACCESS

        static long FileLen(FileInfo f)
        {
            try { return ((int)f.Attributes & CloudOnly) != 0 ? 0 : f.Length; } catch { return 0; }
        }

        /// Size on disk of a file or a whole folder. Never follows links / junctions.
        public static long Size(string path)
        {
            try
            {
                if (File.Exists(path)) return FileLen(new FileInfo(path));
                if (!Directory.Exists(path)) return 0;
            }
            catch { return 0; }
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                try
                {
                    foreach (var i in new DirectoryInfo(d).EnumerateFileSystemInfos())
                    {
                        var a = i.Attributes;
                        if ((a & FileAttributes.Directory) != 0)
                        {
                            if ((a & FileAttributes.ReparsePoint) == 0) stack.Push(i.FullName);
                        }
                        else total += FileLen((FileInfo)i);
                    }
                }
                catch { }   // no permission / path too long: skip that folder
            }
            return total;
        }

        public static string Pretty(long b)
        {
            if (b < 0) return "…";
            if (b < 1024) return b + " B";
            double kb = b / 1024.0;
            if (kb < 1024) return kb.ToString("0") + " KB";
            double mb = kb / 1024;
            if (mb < 1024) return mb.ToString(mb < 100 ? "0.#" : "0") + " MB";
            double gb = mb / 1024;
            return gb.ToString(gb < 100 ? "0.##" : "0.#") + " GB";
        }

        /// Permanently deletes a file or folder (used only for caches / temp files).
        public static void Nuke(string path)
        {
            try
            {
                var a = File.GetAttributes(path);
                if ((a & FileAttributes.Directory) != 0)
                {
                    if ((a & FileAttributes.ReparsePoint) == 0)
                        foreach (var c in Directory.GetFileSystemEntries(path)) Nuke(c);
                    Directory.Delete(path, false);
                }
                else
                {
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch { }   // in use by a running app: leave it
        }

        public static string[] Children(string dir)
        {
            try { return Directory.GetFileSystemEntries(dir); } catch { return new string[0]; }
        }
        public static string[] Dirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
        }

        public static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return false;
            return path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        public static bool Same(string a, string b)
        {
            return string.Equals(Trim(a ?? ""), Trim(b ?? ""), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// Guards for anything that deletes. A path must be specific (deep enough) and not a system place.
    static class Safe
    {
        static string[] Never()
        {
            return new[] { FS.Home, FS.Roaming, FS.Local, Path.Combine(FS.Home, "AppData"), Path.Combine(FS.Home, "AppData", "LocalLow"),
                FS.ProgramData, FS.PF, FS.PF86, FS.WinDir, Path.Combine(FS.SysDrive, "Users"),
                Path.Combine(FS.PF, "Common Files"), Path.Combine(FS.PF86, "Common Files"), Path.Combine(FS.PF, "WindowsApps"),
                Path.Combine(FS.Local, "Microsoft"), Path.Combine(FS.Roaming, "Microsoft"), Path.Combine(FS.Local, "Packages"),
                Path.Combine(FS.Local, "Programs"), Path.Combine(FS.ProgramData, "Microsoft"), FS.Temp,
                FS.Special(Environment.SpecialFolder.MyDocuments), FS.Special(Environment.SpecialFolder.DesktopDirectory),
                FS.Special(Environment.SpecialFolder.MyPictures), FS.Special(Environment.SpecialFolder.MyMusic),
                FS.Special(Environment.SpecialFolder.MyVideos), FS.Downloads };
        }

        /// True when the folder clearly belongs to one program (never a drive root, Program Files itself, Windows…).
        public static bool SpecificDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p;
            try { p = FS.Trim(Path.GetFullPath(path)); } catch { return false; }
            if (p.Length < 4 || !Path.IsPathRooted(p)) return false;
            if (p.Split('\\').Length < 3) return false;                       // C:\Something is too broad
            if (FS.Same(p, FS.WinDir) || FS.IsUnder(p, FS.WinDir)) return false;
            foreach (var n in Never()) if (FS.Same(p, n)) return false;
            return true;
        }

        /// What the user may send to the Recycle Bin from the explorer / big file lists: their own content only.
        public static bool CanRecycle(string path)
        {
            if (!SpecificDir(path)) return false;
            if (!FS.IsUnder(path, FS.Home)) return false;
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (FS.Same(Path.GetDirectoryName(path), FS.Home) && (name.StartsWith("ntuser") || name == "onedrive" || name == "appdata")) return false;
            if (FS.IsUnder(path, Path.Combine(FS.Local, "Microsoft")) || FS.IsUnder(path, Path.Combine(FS.Roaming, "Microsoft"))) return false;
            return true;
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd; public uint wFunc; public IntPtr pFrom; public IntPtr pTo; public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted; public IntPtr hNameMappings; public IntPtr lpszProgressTitle;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        [DllImport("shell32.dll")] public static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern uint QueryDosDevice(string device, StringBuilder target, int max);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    /// The Recycle Bin: everything the user removes goes here first, so it can be restored.
    static class Recycle
    {
        const ushort Flags = 0x0004 | 0x0010 | 0x0040 | 0x0400 | 0x4000;   // SILENT | NOCONFIRMATION | ALLOWUNDO | NOERRORUI | WANTNUKEWARNING

        static void One(string path)
        {
            IntPtr from = Marshal.StringToHGlobalUni(path + "\0");   // list must end with two nulls
            try
            {
                var op = new Native.SHFILEOPSTRUCT { wFunc = 3 /* FO_DELETE */, pFrom = from, fFlags = Flags };
                Native.SHFileOperation(ref op);
            }
            catch { }
            finally { Marshal.FreeHGlobal(from); }
        }

        /// Moves items to the Recycle Bin. Items that need admin rights are moved with one Windows (UAC) prompt.
        /// Returns the paths that could not be removed.
        public static List<string> Send(IEnumerable<string> paths, bool allowElevate)
        {
            var list = paths.Where(FS.Exists).ToList();
            foreach (var p in list) One(p);
            var left = list.Where(FS.Exists).ToList();
            if (left.Count == 0 || !allowElevate) return left;

            string file = Path.Combine(Path.GetTempPath(), "EasyUninstall-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllLines(file, left.ToArray(), Encoding.UTF8);
                var psi = new ProcessStartInfo(AppInfo.ExePath, "--recycle-list \"" + file + "\"") { UseShellExecute = true, Verb = "runas" };
                using (var p = Process.Start(psi)) { if (p != null) p.WaitForExit(); }
            }
            catch { }   // user said No to the Windows prompt
            finally { try { File.Delete(file); } catch { } }
            return left.Where(FS.Exists).ToList();
        }

        /// Runs inside the elevated copy of the app.
        public static void RunElevatedList(string file)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file, Encoding.UTF8); } catch { return; }
            foreach (var p in lines) if (Safe.SpecificDir(p) || (File.Exists(p) && Safe.SpecificDir(Path.GetDirectoryName(p)))) One(p);
        }

        public static long BinSize()
        {
            var info = new Native.SHQUERYRBINFO(); info.cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO));
            try { return Native.SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0; } catch { return 0; }
        }

        public static void Empty()
        {
            try { Native.SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4); } catch { }   // no confirm, no progress, no sound
        }
    }

    /// Runs work off the UI thread and hands the result back to it.
    static class Bg
    {
        public static System.Windows.Threading.Dispatcher Ui;

        public static void Run<T>(Func<T> work, Action<T> done)
        {
            Task.Factory.StartNew(() =>
            {
                T r = default(T);
                try { r = work(); } catch (Exception e) { Debug.WriteLine(e); }
                Ui.BeginInvoke(new Action(() => done(r)));
            }, TaskCreationOptions.LongRunning);
        }
        public static void Run(Action work, Action done)
        {
            Run<bool>(() => { work(); return true; }, _ => done());
        }
    }

    static class Shell
    {
        public static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }
        /// Opens File Explorer with the item selected.
        public static void Reveal(string path)
        {
            try
            {
                if (FS.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch { }
        }
        /// Runs a command as administrator (one Windows prompt). Returns false if it was refused or failed.
        public static bool RunElevated(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi)) { if (p == null) return false; p.WaitForExit(); return p.ExitCode == 0; }
            }
            catch { return false; }
        }
    }

    /// Snapshot of running programs.
    class Procs
    {
        public class P { public int Pid; public string Name; public string Path; public long Memory; public bool HasWindow; }
        public List<P> All = new List<P>();

        public static string PathOf(int pid)
        {
            IntPtr h = Native.OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int n = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(h); }
        }

        public static Procs Snapshot(bool withWindows)
        {
            var s = new Procs();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var x = new P { Pid = p.Id, Name = p.ProcessName, Memory = p.PrivateMemorySize64 };
                    if (p.Id > 4) x.Path = PathOf(p.Id);
                    if (withWindows) x.HasWindow = p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle);
                    s.All.Add(x);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return s;
        }

        /// Name of a running program that owns the given folder name (e.g. "discord"), or null.
        public string OwnerOf(string folderName)
        {
            string f = folderName.ToLowerInvariant();
            foreach (var p in All)
            {
                string n = (p.Name ?? "").ToLowerInvariant();
                if (n.Length < 3) continue;
                if (n == f) return p.Name;
                if (n.Length >= 4 && f.Length >= 4 && (f.Contains(n) || n.Contains(f))) return p.Name;
            }
            return null;
        }
        public bool IsRunning(params string[] names)
        {
            return All.Any(p => names.Any(n => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)));
        }
        public bool RunsFrom(string dir)
        {
            return !string.IsNullOrEmpty(dir) && All.Any(p => p.Path != null && FS.IsUnder(p.Path, dir));
        }
    }
}
