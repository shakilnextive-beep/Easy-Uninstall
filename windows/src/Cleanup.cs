// Disk info, suggestions, the "where did my space go" explorer and the big-file finder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EasyUninstall
{
    struct DiskInfo
    {
        public long Total, Free; public string Label;
        public long Used { get { return Total - Free; } }

        public static DiskInfo Read()
        {
            try
            {
                var d = new DriveInfo(FS.SysDrive);
                string name = string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;
                return new DiskInfo { Total = d.TotalSize, Free = d.AvailableFreeSpace, Label = name + " (" + FS.SysDrive.TrimEnd('\\') + ")" };
            }
            catch { return new DiskInfo { Label = "Local Disk" }; }
        }
    }

    enum SuggestKind { DeleteContents, EmptyBin, Explore, RecycleFolder, OldVersion, OpenSettings, Info }

    class Suggestion
    {
        public string Title, Detail, Icon, Extra;     // Extra: settings link or help text
        public bool Safe;
        public List<string> Paths = new List<string>();
        public long Size;
        public SuggestKind Kind;
        public InstalledApp App;
    }

    static class SuggestionScanner
    {
        static readonly Regex Year = new Regex(@"\s+(19|20)\d{2}$");

        public static List<Suggestion> Scan(List<InstalledApp> apps)
        {
            var outp = new List<Suggestion>();
            Action<string, string, string, IEnumerable<string>> addSafe = (title, detail, icon, paths) =>
            {
                var ex = paths.Where(FS.Exists).ToList();
                long size = ex.Sum(x => FS.Size(x));
                if (size > 10000000) outp.Add(new Suggestion { Title = title, Detail = detail, Icon = icon, Safe = true, Paths = ex, Size = size, Kind = SuggestKind.DeleteContents });
            };

            string adobe = Path.Combine(FS.Roaming, @"Adobe\Common");
            addSafe("Adobe Media Cache",
                L.T("Premiere Pro / After Effects video preview দ্রুত দেখানোর জন্য এই ফাইল বানায়। মুছলে project খোলার সময় আবার নিজে বানিয়ে নেবে। Video editor দের disk ভরার এটা সবচেয়ে বড় কারণ।", "Premiere Pro / After Effects create these files to show video previews fast. Delete them and they're rebuilt when you open a project. This is the #1 reason video editors' disks fill up."),
                Glyph.Video, new[] { "Media Cache Files", "Media Cache", "Peak Files", "Team Projects Cache", "PTX" }.Select(n => Path.Combine(adobe, n)));
            addSafe(L.T("Browser cache", "Browser cache"),
                L.T("Browser গুলো website দ্রুত খোলার জন্য অস্থায়ী কপি জমায়। মুছলে আবার নিজে নামিয়ে নেবে — password, bookmark, history থেকে যাবে। মুছার আগে browser বন্ধ রাখা ভালো।", "Browsers store temporary copies of websites to open them faster. Delete them and they download again — passwords, bookmarks and history stay. Best to close the browser first."),
                Glyph.Globe, SmartScanner.BrowserCaches().Select(b => b[2]));
            addSafe(L.T("Temp ফাইল", "Temp files"),
                L.T("App গুলোর ফেলে রাখা অস্থায়ী ফাইল। ১ দিনের বেশি পুরোনো গুলোই শুধু মুছা হবে, চালু app এর ফাইল থেকে যাবে।", "Temporary files apps left behind. Only ones older than 1 day are deleted; files of running apps stay."),
                Glyph.Doc, SmartScanner.OldTemp());
            addSafe(L.T("Crash report ও dump", "Crash reports and dumps"),
                L.T("App crash করলে Windows এর রাখা রেকর্ড। সাধারণ ব্যবহারকারীর কোন কাজে লাগে না।", "Records Windows keeps when an app crashes. No use for everyday users."),
                Glyph.Warn, SmartScanner.CrashDumps());
            addSafe("Developer / Python / Node cache",
                L.T("Programming tool গুলোর download cache। দরকার হলে আবার download হবে।", "Download cache from programming tools. Re-downloaded if needed."),
                Glyph.Code, SmartScanner.DevCaches().Concat(new[] { Path.Combine(FS.Home, ".cache") }));

            // Recycle Bin
            outp.Add(new Suggestion { Title = "Recycle Bin", Icon = Glyph.Trash, Safe = true, Size = Recycle.BinSize(), Kind = SuggestKind.EmptyBin,
                Detail = L.T("Recycle Bin এ রাখা ফাইলও disk এর জায়গা নেয়! খালি না করলে জায়গা ফেরত পাবেন না।", "Files in the Recycle Bin still take disk space! You won't get the space back until you empty it.") });

            // Old versions: same name with a different year ("Premiere Pro 2026" / "2027")
            foreach (var g in apps.Where(a => !a.IsProtected && Year.IsMatch(a.Name)).GroupBy(a => Year.Replace(a.Name, ""), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                var sorted = g.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var newest = sorted.Last();
                foreach (var old in sorted.Take(sorted.Count - 1))
                    outp.Add(new Suggestion { Title = L.T("পুরনো version: " + old.Name, "Old version: " + old.Name), Icon = Glyph.Layers, Safe = true, App = old, Kind = SuggestKind.OldVersion,
                        Paths = old.Dir != "" ? new List<string> { old.Dir } : new List<string>(), Size = old.Dir != "" ? FS.Size(old.Dir) : Math.Max(0, old.Size),
                        Detail = L.T(newest.Name + " ও ইনস্টল আছে। দুটো version রাখার দরকার না থাকলে পুরনোটা uninstall করুন।", newest.Name + " is also installed. If you don't need both versions, uninstall the old one.")
                            + (old.IsAdobe ? L.T(" Adobe app হলে Creative Cloud app থেকে uninstall করা সবচেয়ে ভালো।", " For Adobe apps, uninstalling from the Creative Cloud app works best.") : "") });
            }

            // Old Windows installation left after an upgrade
            string winOld = Path.Combine(FS.SysDrive, "Windows.old");
            if (Directory.Exists(winOld))
                outp.Add(new Suggestion { Title = L.T("আগের Windows (Windows.old)", "Previous Windows (Windows.old)"), Icon = Glyph.Sync, Safe = true, Paths = new List<string> { winOld }, Size = FS.Size(winOld),
                    Kind = SuggestKind.OpenSettings, Extra = "ms-settings:storagesense",
                    Detail = L.T("Windows upgrade এর পর পুরোনো Windows এর কপি থেকে যায়। আগের version এ ফিরতে না চাইলে Settings → Storage → Temporary files থেকে \"Previous Windows installation\" মুছুন।", "After a Windows upgrade a copy of the old Windows stays behind. If you don't plan to go back, remove \"Previous Windows installation\" in Settings → Storage → Temporary files.") });

            // Folders in Program Files left behind by programs that were already removed
            var appDirs = apps.Where(a => a.Dir != "").Select(a => a.Dir).ToList();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Common Files", "Internet Explorer", "Microsoft", "Microsoft Office", "Microsoft Update Health Tools", "ModifiableWindowsApps",
                "Reference Assemblies", "Uninstall Information", "Windows Defender", "Windows Defender Advanced Threat Protection", "Windows Mail", "Windows Media Player", "Windows NT",
                "Windows Photo Viewer", "Windows Portable Devices", "Windows Security", "Windows Sidebar", "WindowsApps", "WindowsPowerShell", "MSBuild", "dotnet", "Microsoft.NET", "PackageManagement", "Windows Kits", "InstallShield Installation Information", "desktop.ini",
                "Intel", "AMD", "NVIDIA Corporation", "NVIDIA", "Realtek", "Dell", "HP", "Lenovo", "ASUS", "Acer", "MSI" };   // driver / PC-maker folders
            foreach (var pf in new[] { FS.PF, FS.PF86 }.Where(p => p != ""))
                foreach (var d in FS.Dirs(pf))
                {
                    string leaf = Path.GetFileName(d);
                    if (keep.Contains(leaf) || leaf.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) || leaf.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                    try { if ((File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue; } catch { continue; }
                    if (appDirs.Any(a => FS.Same(a, d) || FS.IsUnder(a, d))) continue;
                    if (HasPrograms(d)) continue;
                    outp.Add(new Suggestion { Title = L.T("অবশিষ্ট ফোল্ডার: " + leaf, "Leftover folder: " + leaf), Icon = Glyph.Folder, Safe = false, Paths = new List<string> { d }, Size = FS.Size(d), Kind = SuggestKind.RecycleFolder,
                        Detail = L.T("এই ফোল্ডারে কোন program নেই — সম্ভবত আগেই uninstall করা software এর ফেলে যাওয়া ফাইল। ভিতরে একবার দেখে নিয়ে মুছুন।", "There is no program in this folder — probably files left by software you already uninstalled. Take a look inside before deleting.") });
                }

            // Review folders
            var review = new[] {
                Tuple.Create(FS.Downloads, "Downloads", L.T("Download করা ফাইল — installer (.exe, .msi, .zip) ইনস্টল হয়ে গেলে আর লাগে না।", "Downloaded files — installers (.exe, .msi, .zip) aren't needed once installed."), Glyph.Download),
                Tuple.Create(Path.Combine(FS.Special(Environment.SpecialFolder.MyDocuments), "Adobe"), "Adobe", L.T("Premiere / After Effects এর project, Auto-Save ও preview ফাইল। পুরনো project এর Auto-Save গুলো সাধারণত মুছা যায়।", "Premiere / After Effects projects, Auto-Saves and previews. Auto-Saves of old projects can usually go."), Glyph.Folder),
                Tuple.Create(Path.Combine(FS.Roaming, @"Apple Computer\MobileSync\Backup"), "iPhone backup", L.T("iPhone / iPad এর backup। পুরনো device এর backup থাকলে মুছতে পারেন।", "iPhone / iPad backups. Backups of old devices can be deleted."), Glyph.Phone),
                Tuple.Create(Path.Combine(FS.Home, @"Apple\MobileSync\Backup"), "iPhone backup", L.T("iPhone / iPad এর backup। পুরনো device এর backup থাকলে মুছতে পারেন।", "iPhone / iPad backups. Backups of old devices can be deleted."), Glyph.Phone),
                Tuple.Create(FS.Special(Environment.SpecialFolder.MyVideos), "Videos", L.T("Video ফাইল।", "Video files."), Glyph.Video),
                Tuple.Create(FS.Special(Environment.SpecialFolder.DesktopDirectory), "Desktop", L.T("Desktop এর ফাইল।", "Files on your Desktop."), Glyph.Desktop) };
            foreach (var r in review)
            {
                if (r.Item1 == "" || !Directory.Exists(r.Item1)) continue;
                long s = FS.Size(r.Item1);
                if (s > 500L * 1024 * 1024) outp.Add(new Suggestion { Title = r.Item2, Detail = r.Item3, Icon = r.Item4, Safe = false, Paths = new List<string> { r.Item1 }, Size = s, Kind = SuggestKind.Explore });
            }

            // Hibernation file
            try
            {
                var hib = new FileInfo(Path.Combine(FS.SysDrive, "hiberfil.sys"));
                if (hib.Exists && hib.Length > 2L * 1024 * 1024 * 1024)
                    outp.Add(new Suggestion { Title = L.T("Hibernate ফাইল (hiberfil.sys)", "Hibernation file (hiberfil.sys)"), Icon = Glyph.Power, Safe = false, Size = hib.Length, Kind = SuggestKind.Info,
                        Detail = L.T("Hibernate ও Fast Startup এর জন্য Windows এই ফাইল রাখে। দুটোই ব্যবহার না করলে বন্ধ করে জায়গা ফেরত পাওয়া যায়।", "Windows keeps this file for Hibernate and Fast Startup. If you use neither, turning it off gives the space back."),
                        Extra = L.T("বন্ধ করতে: Start এ \"cmd\" লিখে Run as administrator দিন, তারপর লিখুন:\n\npowercfg /hibernate off\n\nএতে Hibernate ও Fast Startup বন্ধ হবে (Sleep ঠিকই কাজ করবে)। আবার চালু করতে: powercfg /hibernate on", "To turn it off: type \"cmd\" in Start, choose Run as administrator, then type:\n\npowercfg /hibernate off\n\nThis turns off Hibernate and Fast Startup (Sleep still works). To turn it back on: powercfg /hibernate on") });
            }
            catch { }

            return outp.OrderByDescending(s => s.Safe).ThenByDescending(s => s.Size).ToList();
        }

        /// True if the folder (2 levels deep) still holds programs.
        static bool HasPrograms(string dir)
        {
            try
            {
                int seen = 0;
                var stack = new Stack<string>(); stack.Push(dir);
                while (stack.Count > 0)
                {
                    string d = stack.Pop();
                    foreach (var f in Directory.EnumerateFiles(d))
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        if (e == ".exe" || e == ".dll" || e == ".sys" || e == ".msi" || e == ".jar") return true;
                        if (++seen > 20000) return true;   // too big to judge: leave it alone
                    }
                    foreach (var s in Directory.EnumerateDirectories(d)) stack.Push(s);
                }
                return false;
            }
            catch { return true; }   // can't read it: leave it alone
        }

        public static void Clean(Suggestion s)
        {
            foreach (var p in s.Paths)
            {
                if (FS.IsUnder(p, FS.Temp)) FS.Nuke(p);                         // an old temp item
                else foreach (var c in FS.Children(p)) FS.Nuke(c);             // contents of a cache folder
            }
        }
    }

    class Entry
    {
        public string Path; public long Size; public bool IsFolder;
        public string Name { get { string n = System.IO.Path.GetFileName(Path); return string.IsNullOrEmpty(n) ? Path : n; } }
    }

    enum RiskFilter { All, Safe, Risky }
    enum FileKind { All, Video, Installer, Audio, Other }

    static class Kinds
    {
        public static bool Allows(RiskFilter f, bool safe) { return f == RiskFilter.All || (f == RiskFilter.Safe) == safe; }

        public static FileKind Of(string path)
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant().TrimStart('.'))
            {
                case "mp4": case "mov": case "mxf": case "mkv": case "avi": case "m4v": case "mts": case "m2ts": case "webm": case "braw": case "r3d": case "wmv": case "cfa": case "pek": case "mcdb": return FileKind.Video;
                case "msi": case "zip": case "iso": case "rar": case "7z": case "tar": case "gz": case "cab": case "msix": case "msixbundle": case "appx": return FileKind.Installer;
                case "exe": return path.ToLowerInvariant().Contains("\\downloads\\") ? FileKind.Installer : FileKind.Other;
                case "wav": case "mp3": case "aif": case "aiff": case "m4a": case "flac": case "wma": return FileKind.Audio;
                default: return FileKind.Other;
            }
        }

        public static bool Matches(string q, params string[] fields)
        {
            string t = (q ?? "").Trim();
            return t == "" || fields.Any(f => f != null && f.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }
    }

    /// Explains one big file in plain words: green = safe to delete, red = keep unless sure.
    class BigFileVerdict
    {
        public bool Safe; public string Note;
        static BigFileVerdict V(bool safe, string bn, string en) { return new BigFileVerdict { Safe = safe, Note = L.T(bn, en) }; }

        public static BigFileVerdict Of(string path)
        {
            string p = path.ToLowerInvariant().Replace('\\', '/');
            string ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
            var kind = Kinds.Of(path);
            Func<string[], bool> has = list => list.Any(s => p.Contains(s));
            Func<string[], bool> isExt = list => list.Contains(ext);
            string owner = AppName(path) ?? L.T("একটি app", "an app");

            // App internals that must stay (checked first so a cache inside them does not mislead)
            if (isExt(new[] { "vhd", "vhdx", "vdi", "vmdk", "qcow2", "hdd", "avhdx" }) || has(new[] { "/virtualbox vms/", "/virtual machines/" }))
                return V(false, "Virtual machine / WSL / Docker এর ডিস্ক — হাতে মুছলে ভিতরের সব কিছু হারাবেন। app এর নিজের settings থেকে মুছুন বা রিসেট করুন।", "Virtual machine / WSL / Docker disk — deleting it by hand loses everything inside. Remove or reset it from the app's own settings.");
            if (has(new[] { "/mobilesync/backup" }))
                return V(false, "iPhone/iPad এর backup — মুছলে backup চলে যাবে। iCloud এ backup থাকলে মুছতে পারেন।", "iPhone/iPad backup — deleting removes the backup. Fine to delete if it's backed up to iCloud.");
            if (isExt(new[] { "pst", "ost" }))
                return V(false, "Outlook এর email ফাইল — মুছলে email হারাতে পারেন।", "Outlook mail file — deleting it can lose your email.");

            // Safe: caches, updates, logs, installers
            if (has(new[] { "crx_cache", "/softwareupdate/", "/updates/", "squirrel", "/package cache/", "/downloaded installations/" }))
                return V(true, "পুরোনো update ফাইল — app ইতিমধ্যে আপডেট হয়ে গেছে, মুছলে কোন সমস্যা নেই।", "Old update file — the app is already updated, safe to delete.");
            if (has(new[] { "media cache", "peak files" }) || isExt(new[] { "cfa", "pek", "mcdb", "ims" }))
                return V(true, "Adobe Media Cache — Premiere/After Effects দরকার হলে আবার বানিয়ে নেবে।", "Adobe Media Cache — Premiere/After Effects rebuild it when needed.");
            if (has(new[] { "/caches/", "/.cache/", "/cache/", "/code cache/", "/tmp/", "/temp/" }))
                return V(true, "Cache ফাইল — app দরকার হলে আবার বানিয়ে নেবে।", "Cache file — the app rebuilds it when needed.");
            if (isExt(new[] { "log", "dmp", "mdmp" }))
                return V(true, "Log / crash dump — শুধু সমস্যা খোঁজার জন্য, মুছলে কোন সমস্যা নেই।", "Log / crash dump — only for troubleshooting, safe to delete.");
            if (isExt(new[] { "msi", "iso", "msix", "msixbundle", "appx" }) || (ext == "exe" && has(new[] { "/downloads/" })))
                return V(true, "Installer ফাইল — software ইনস্টল হয়ে গেলে আর দরকার নেই।", "Installer — not needed once the software is installed.");
            if (kind == FileKind.Installer && has(new[] { "/downloads/" }))
                return V(true, "Download করা zip — ভিতরের জিনিস বের করে নিলে আর দরকার নেই।", "Downloaded zip — not needed once you've extracted it.");

            // Red: user's own work and app data
            if (isExt(new[] { "prproj", "aep", "aepx", "psd", "psb", "ai", "indd", "drp", "veg", "blend" }))
                return V(false, "আপনার project ফাইল — মুছলে কাজ হারাবেন।", "Your project file — deleting loses your work.");
            if (kind == FileKind.Video)
                return V(false, "আপনার ভিডিও — project এ লাগলে রাখুন। অন্য ড্রাইভে কপি করা থাকলে মুছতে পারেন।", "Your video — keep it if a project uses it. Fine to delete if it's copied to another drive.");
            if (kind == FileKind.Audio)
                return V(false, "আপনার অডিও ফাইল — দরকার না হলে বা backup থাকলে মুছতে পারেন।", "Your audio file — delete if not needed or backed up.");
            if (kind == FileKind.Installer)
                return V(false, "Zip/archive — ভিতরে কি আছে দেখে নিন, অন্য কোথাও কপি না থাকলে রাখুন।", "Zip/archive — check what's inside; keep it if there's no other copy.");
            if (has(new[] { "/appdata/" }))
                return V(false, owner + " এর ভিতরের ফাইল — হাতে মুছলে app ঠিকমতো চলবে না। দরকার না হলে app টাই Uninstall করুন।", "Internal file of " + owner + " — deleting it by hand breaks the app. If you don't need it, uninstall the app instead.");
            return V(false, "কি ফাইল নিশ্চিত না — খুলে দেখে নিন, না বুঝলে রাখুন।", "Unknown file — open it to check; keep it if unsure.");
        }

        /// Folder name right after AppData\Roaming, Local or LocalLow, e.g. "Claude".
        public static string AppName(string path)
        {
            var c = path.Split('\\');
            for (int i = 0; i + 2 < c.Length; i++)
                if (string.Equals(c[i], "AppData", StringComparison.OrdinalIgnoreCase))
                {
                    string n = c[i + 2];
                    if (string.Equals(n, "Packages", StringComparison.OrdinalIgnoreCase) && i + 3 < c.Length) n = c[i + 3].Split('_')[0];
                    return n;
                }
            return null;
        }
    }

    class CleanupModel
    {
        public SmartModel Smart = new SmartModel();
        public DiskInfo Disk = DiskInfo.Read();

        public List<Suggestion> Suggestions = new List<Suggestion>();
        public bool Scanning, SuggestScanned;

        public List<string> Stack = new List<string>();     // explorer path; empty = overview
        public List<Entry> Entries = new List<Entry>();
        public bool Exploring;
        Dictionary<string, long> sizeCache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        int loadToken;

        public List<Entry> BigFiles = new List<Entry>();
        public bool BigScanning, BigScanned;

        public void RefreshDisk() { Disk = DiskInfo.Read(); }

        public void ScanSuggestions()
        {
            if (Scanning) return;
            Scanning = true; App.Invalidate();
            var apps = App.Store.Apps.ToList();
            Bg.Run(() => SuggestionScanner.Scan(apps.Count > 0 ? apps : InstalledApp.LoadAll()),
                s => { Suggestions = s ?? new List<Suggestion>(); Scanning = false; SuggestScanned = true; RefreshDisk(); App.Invalidate(); });
        }

        public string[] Roots
        {
            get { return new[] { FS.Home, FS.PF, FS.PF86, FS.ProgramData, FS.WinDir }.Where(p => p != "" && Directory.Exists(p)).ToArray(); }
        }

        public void Open(string path) { if (path != null) Stack.Add(path); Load(); }
        public void Back() { if (Stack.Count > 0) { Stack.RemoveAt(Stack.Count - 1); Load(); } }
        public void Jump(int depth) { Stack = Stack.Take(depth).ToList(); Load(); }

        public void Load()
        {
            int token = ++loadToken;
            string cur = Stack.LastOrDefault();
            var list = new List<Entry>();
            if (cur == null) foreach (var r in Roots) list.Add(new Entry { Path = r, IsFolder = true });
            else
            {
                try
                {
                    foreach (var i in new DirectoryInfo(cur).EnumerateFileSystemInfos())
                    {
                        bool dir = (i.Attributes & FileAttributes.Directory) != 0;
                        if (dir && (i.Attributes & FileAttributes.ReparsePoint) != 0) continue;   // a link to somewhere else
                        list.Add(new Entry { Path = i.FullName, IsFolder = dir, Size = dir ? -1 : FS.Size(i.FullName) });
                    }
                }
                catch { }
            }
            foreach (var e in list.Where(e => e.IsFolder)) { long s; e.Size = sizeCache.TryGetValue(e.Path, out s) ? s : -1; }
            Entries = list.OrderByDescending(e => e.Size).ToList();
            var todo = Entries.Where(e => e.Size < 0).ToList();
            Exploring = todo.Count > 0;
            App.Invalidate();
            if (todo.Count == 0) return;

            int remaining = todo.Count;
            var queue = new System.Collections.Concurrent.ConcurrentQueue<Entry>(todo);
            for (int w = 0; w < Math.Min(4, todo.Count); w++)
                System.Threading.Tasks.Task.Factory.StartNew(() =>
                {
                    Entry e;
                    while (token == loadToken && queue.TryDequeue(out e))
                    {
                        long s = FS.Size(e.Path); var done = e;
                        Bg.Ui.BeginInvoke(new Action(() =>
                        {
                            sizeCache[done.Path] = s;
                            if (token != loadToken) return;
                            done.Size = s;
                            // keep rows still while measuring so they don't jump under the mouse
                            if (--remaining == 0) { Entries = Entries.OrderByDescending(x => x.Size).ToList(); Exploring = false; }
                            App.Invalidate();
                        }));
                    }
                }, System.Threading.Tasks.TaskCreationOptions.LongRunning);
        }

        public void Forget(string path)
        {
            // drop cached sizes of the item and its parents so totals refresh
            foreach (var k in sizeCache.Keys.Where(k => FS.Same(k, path) || FS.IsUnder(path, k) || FS.IsUnder(k, path)).ToList()) sizeCache.Remove(k);
        }

        public void ScanBigFiles()
        {
            if (BigScanning) return;
            BigScanning = true; App.Invalidate();
            Bg.Run(() => FindBig(500L * 1024 * 1024), found => { BigFiles = found ?? new List<Entry>(); BigScanning = false; BigScanned = true; App.Invalidate(); });
        }

        public static List<Entry> FindBig(long minBytes)
        {
            var found = new List<Entry>();
            var stack = new Stack<string>(); stack.Push(FS.Home);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                try
                {
                    foreach (var i in new DirectoryInfo(d).EnumerateFileSystemInfos())
                    {
                        if ((i.Attributes & FileAttributes.Directory) != 0)
                        {
                            if ((i.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(i.FullName);
                        }
                        else
                        {
                            long len = ((FileInfo)i).Length;
                            if (len >= minBytes) { long real = FS.Size(i.FullName); if (real >= minBytes) found.Add(new Entry { Path = i.FullName, Size = real }); }
                        }
                    }
                }
                catch { }
            }
            return found.OrderByDescending(e => e.Size).ToList();
        }
    }
}
