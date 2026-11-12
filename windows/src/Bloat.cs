// Unused software (when was each program last opened) and startup items that run in the background.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace EasyUninstall
{
    class AppUsage
    {
        public InstalledApp App; public DateTime? LastUsed; public bool Estimated; public long Size;
        public int? Days { get { return LastUsed.HasValue ? (int?)Math.Max(0, (int)(DateTime.Now - LastUsed.Value).TotalDays) : null; } }
    }

    static class UsageScanner
    {
        static string Rot13(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= 'a' && c <= 'z') sb.Append((char)((c - 'a' + 13) % 26 + 'a'));
                else if (c >= 'A' && c <= 'Z') sb.Append((char)((c - 'A' + 13) % 26 + 'A'));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// Windows' own record of when each program was last started from Explorer / Start (path -> time).
        public static Dictionary<string, DateTime> LastRuns()
        {
            var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                { "{6D809377-6AF0-444B-8957-A3773F02200E}", FS.PF }, { "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", FS.PF86 },
                { "{F38BF404-1D43-42F2-9305-67DE0B28FC23}", FS.WinDir }, { "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", Path.Combine(FS.WinDir, "System32") },
                { "{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}", Path.Combine(FS.WinDir, "SysWOW64") },
                { "{0139D44E-6AFE-49F2-8690-3DAFCAE6FFB8}", FS.Special(Environment.SpecialFolder.CommonPrograms) },
                { "{A77F5D77-2E2B-44C3-A6A2-ABA601054A51}", FS.Special(Environment.SpecialFolder.Programs) },
                { "{9E3995AB-1F9C-4F13-B827-48B24B6C7174}", Path.Combine(FS.Roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned") } };
            Action<string, DateTime> note = (path, t) =>
            {
                DateTime old;
                if (!map.TryGetValue(path, out old) || t > old) map[path] = t;
            };

            // 1. UserAssist: programs and shortcuts started from Explorer, Start and the taskbar
            foreach (var guid in new[] { "{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}", "{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}" })
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\" + guid + @"\Count"))
                    {
                        if (k == null) continue;
                        foreach (var name in k.GetValueNames())
                        {
                            var data = k.GetValue(name) as byte[];
                            if (data == null || data.Length < 68) continue;
                            long ft = BitConverter.ToInt64(data, 60);
                            if (ft <= 0) continue;
                            string path = Rot13(name);
                            if (path.StartsWith("{") && path.Length > 38)
                            {
                                string root;
                                if (!folders.TryGetValue(path.Substring(0, 38), out root)) continue;
                                path = root + path.Substring(38);
                            }
                            if (!path.Contains("\\")) continue;
                            DateTime t; try { t = DateTime.FromFileTime(ft); } catch { continue; }
                            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!File.Exists(path)) continue;
                                path = StartupScanner.LinkTarget(path);
                                if (string.IsNullOrEmpty(path)) continue;
                            }
                            note(path, t);
                        }
                    }
                }
                catch { }
            }

            // 2. Windows' background-activity record: every program run in the last few days, however it was started
            try
            {
                var drives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in Environment.GetLogicalDrives())
                {
                    var sb = new StringBuilder(260);
                    if (Native.QueryDosDevice(d.TrimEnd('\\'), sb, sb.Capacity) > 0) drives[sb.ToString()] = d.TrimEnd('\\');
                }
                string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                foreach (var svc in new[] { "bam", "dam" })
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + svc + @"\State\UserSettings\" + sid))
                    {
                        if (k == null) continue;
                        foreach (var name in k.GetValueNames())
                        {
                            var data = k.GetValue(name) as byte[];
                            if (data == null || data.Length < 8 || !name.StartsWith(@"\Device\")) continue;
                            int cut = name.IndexOf('\\', 8);
                            string drive;
                            if (cut < 0 || !drives.TryGetValue(name.Substring(0, cut), out drive)) continue;
                            try { note(drive + name.Substring(cut), DateTime.FromFileTime(BitConverter.ToInt64(data, 0))); } catch { }
                        }
                    }
            }
            catch { }
            return map;
        }

        public static List<AppUsage> Scan(List<InstalledApp> apps)
        {
            var runs = LastRuns();
            var running = Procs.Snapshot(false);
            var outp = new List<AppUsage>();
            foreach (var a in apps.Where(a => !a.IsProtected && a.Name != "Easy Uninstall"))
            {
                var u = new AppUsage { App = a, Size = a.Size >= 0 ? a.Size : (a.Dir != "" ? FS.Size(a.Dir) : 0) };
                if (a.Dir != "")
                {
                    if (running.RunsFrom(a.Dir)) u.LastUsed = DateTime.Now;
                    else
                    {
                        var hits = runs.Where(r => FS.IsUnder(r.Key, a.Dir)).Select(r => r.Value).ToList();
                        if (hits.Count > 0) u.LastUsed = hits.Max();
                    }
                }
                if (!u.LastUsed.HasValue)
                {
                    // fallback: when did the program last write its settings / data?
                    var dates = new List<DateTime>();
                    foreach (var n in Leftovers.Names(a))
                        foreach (var root in new[] { FS.Roaming, FS.Local })
                        {
                            string d = Path.Combine(root, n);
                            try { if (Directory.Exists(d)) dates.Add(Directory.GetLastWriteTime(d)); } catch { }
                        }
                    if (dates.Count > 0) { u.LastUsed = dates.Max(); u.Estimated = true; }
                }
                outp.Add(u);
            }
            // never used first, then oldest
            return outp.OrderBy(u => u.LastUsed.HasValue).ThenBy(u => u.LastUsed ?? DateTime.MinValue).ThenByDescending(u => u.Size).ToList();
        }
    }

    enum StartupSource { RunUser, RunMachine, RunMachine32, FolderUser, FolderCommon }

    class StartupItem
    {
        public string Name, Command = "", Exe = "", Owner, Note, File;   // File: the shortcut, for Startup-folder items
        public StartupSource Source;
        public bool Caution, Orphan, Disabled, Running;
        public bool NeedsAdmin { get { return Source == StartupSource.RunMachine || Source == StartupSource.RunMachine32 || Source == StartupSource.FolderCommon; } }
    }

    static class StartupScanner
    {
        const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string Approved = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        static string ApprovedName(StartupSource s)
        {
            return s == StartupSource.RunMachine32 ? "Run32" : (s == StartupSource.FolderUser || s == StartupSource.FolderCommon) ? "StartupFolder" : "Run";
        }
        static RegistryKey Base(StartupSource s, bool run)
        {
            bool user = s == StartupSource.RunUser || s == StartupSource.FolderUser;
            var view = (run && s == StartupSource.RunMachine32) ? RegistryView.Registry32 : RegistryView.Registry64;
            return RegistryKey.OpenBaseKey(user ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view);
        }

        static bool IsDisabled(StartupSource s, string valueName)
        {
            try
            {
                using (var b = Base(s, false))
                using (var k = b.OpenSubKey(Approved + ApprovedName(s)))
                {
                    var d = k == null ? null : k.GetValue(valueName) as byte[];
                    return d != null && d.Length > 0 && (d[0] & 1) == 1;
                }
            }
            catch { return false; }
        }

        public static List<StartupItem> Scan()
        {
            var running = Procs.Snapshot(false);
            var outp = new List<StartupItem>();
            foreach (var src in new[] { StartupSource.RunUser, StartupSource.RunMachine, StartupSource.RunMachine32 })
            {
                try
                {
                    using (var b = Base(src, true))
                    using (var k = b.OpenSubKey(RunKey))
                    {
                        if (k == null) continue;
                        foreach (var name in k.GetValueNames())
                        {
                            string cmd = (k.GetValue(name) as string ?? "").Trim();
                            if (name == "" || cmd == "") continue;
                            string exe, args; Uninstaller.Split(Environment.ExpandEnvironmentVariables(cmd), out exe, out args);
                            outp.Add(Make(name, cmd, exe, src, null, running));
                        }
                    }
                }
                catch { }
            }
            var folders = new[] { Tuple.Create(FS.Special(Environment.SpecialFolder.Startup), StartupSource.FolderUser),
                                  Tuple.Create(FS.Special(Environment.SpecialFolder.CommonStartup), StartupSource.FolderCommon) };
            foreach (var f in folders)
            {
                if (f.Item1 == "" || !Directory.Exists(f.Item1)) continue;
                foreach (var file in FS.Children(f.Item1))
                {
                    if (Path.GetFileName(file).ToLowerInvariant() == "desktop.ini") continue;
                    string target = file.ToLowerInvariant().EndsWith(".lnk") ? LinkTarget(file) : file;
                    outp.Add(Make(Path.GetFileName(file), file, target ?? "", f.Item2, file, running));
                }
            }
            return outp.OrderBy(i => i.Orphan ? 0 : 1).ThenBy(i => i.Owner, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string LinkTarget(string lnk)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                string target = sh.CreateShortcut(lnk).TargetPath;
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh);
                return target;
            }
            catch { return null; }
        }

        static StartupItem Make(string name, string cmd, string exe, StartupSource src, string file, Procs running)
        {
            var i = new StartupItem { Name = name, Command = cmd, Exe = exe ?? "", Source = src, File = file };
            i.Disabled = IsDisabled(src, name);
            bool rooted = i.Exe.Length > 3 && Path.IsPathRooted(i.Exe);
            i.Orphan = rooted && !System.IO.File.Exists(i.Exe);
            i.Owner = Path.GetFileNameWithoutExtension(name);
            if (rooted && !i.Orphan)
            {
                try
                {
                    var v = FileVersionInfo.GetVersionInfo(i.Exe);
                    string n = !string.IsNullOrWhiteSpace(v.FileDescription) ? v.FileDescription : v.ProductName;
                    if (!string.IsNullOrWhiteSpace(n)) i.Owner = n.Trim();
                }
                catch { }
                i.Running = running.All.Any(p => p.Path != null && FS.Same(p.Path, i.Exe));
            }
            Describe(i);
            return i;
        }

        /// Plain-language explanation and whether turning it off may break something.
        static void Describe(StartupItem i)
        {
            string l = (i.Name + " " + i.Owner + " " + Path.GetFileName(i.Exe)).ToLowerInvariant();
            Func<string[], bool> has = list => list.Any(s => l.Contains(s));
            if (has(new[] { "securityhealth", "windows security", "defender" }))
            { i.Caution = true; i.Note = L.T("Windows Security এর icon ও সতর্কবার্তা। বন্ধ না করাই ভালো।", "The Windows Security icon and alerts. Best left on."); }
            else if (has(new[] { "realtek", "rtkaud", "rthdv", "nvidia", "igfx", "intel", "radeon", "amd ", "synaptics", "elan", "waves", "dolby", "touchpad", "audio", "bluetooth" }))
            { i.Caution = true; i.Note = L.T("Sound, graphics বা touchpad এর driver helper। বন্ধ করলে কিছু feature (যেমন volume key, gesture) কাজ নাও করতে পারে।", "Driver helper for sound, graphics or touchpad. Turning it off may stop some features (like volume keys or gestures)."); }
            else if (has(new[] { "agsservice", "adobegc", "genuine" }))
            { i.Caution = true; i.Note = L.T("Adobe Genuine Service — license যাচাই করে। বন্ধ করা যায়, তবে Adobe পরে আবার চালু করে দিতে পারে।", "Adobe Genuine Service — checks your license. You can turn it off, but Adobe may turn it back on later."); }
            else if (has(new[] { "ccxprocess" }))
                i.Note = L.T("Creative Cloud এর tutorial / stock / extra feature। বন্ধ করলে কোন সমস্যা নেই।", "Creative Cloud tutorials / stock / extra features. Safe to turn off.");
            else if (has(new[] { "creative cloud" }))
                i.Note = L.T("PC চালু হলেই Creative Cloud app চালু করে। বন্ধ করলে দরকার হলে নিজে খুলে নেবেন।", "Starts the Creative Cloud app when the PC starts. Turn it off and open it yourself when needed.");
            else if (has(new[] { "onedrive" }))
                i.Note = L.T("OneDrive এর file sync। বন্ধ করলে OneDrive নিজে না খোলা পর্যন্ত ফাইল sync হবে না।", "OneDrive file sync. If you turn it off, files won't sync until you open OneDrive yourself.");
            else if (has(new[] { "update", "updater" }))
                i.Note = L.T("Auto-update করার জন্য সারাক্ষণ চলে। বন্ধ করলে app ঠিকই চলবে, শুধু নিজে থেকে update হবে না।", "Runs all the time to auto-update. Turning it off is fine — the app still works, it just won't update by itself.");
            else
                i.Note = L.T("PC চালু হলেই এটা নিজে থেকে চালু হয়। বন্ধ করলে app টি খুললে ঠিকই কাজ করবে, শুধু PC চালু হলে নিজে থেকে চলবে না।", "Starts by itself when the PC starts. Turn it off and the app still works when you open it — it just won't run at startup.");
        }

        /// Turns the item off or on (reversible) — the same switch Task Manager's "Startup apps" uses.
        public static bool SetEnabled(StartupItem i, bool on)
        {
            var data = new byte[12];
            data[0] = (byte)(on ? 2 : 3);
            if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
            string sub = Approved + ApprovedName(i.Source);
            if (!i.NeedsAdmin)
            {
                try { using (var k = Registry.CurrentUser.CreateSubKey(sub)) k.SetValue(i.Name, data, RegistryValueKind.Binary); return true; }
                catch { return false; }
            }
            string hex = BitConverter.ToString(data).Replace("-", "");
            return Shell.RunElevated("reg.exe", "add \"HKLM\\" + sub + "\" /v \"" + i.Name.Replace("\"", "\\\"") + "\" /t REG_BINARY /d " + hex + " /f /reg:64");
        }

        /// Removes a leftover item whose program no longer exists.
        public static bool Remove(StartupItem i)
        {
            if (!i.Orphan) return false;
            if (i.File != null) return Recycle.Send(new[] { i.File }, true).Count == 0;
            if (i.Source == StartupSource.RunUser)
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k.DeleteValue(i.Name, false); return true; }
                catch { return false; }
            }
            return Shell.RunElevated("reg.exe", "delete \"HKLM\\" + RunKey + "\" /v \"" + i.Name.Replace("\"", "\\\"") + "\" /f " + (i.Source == StartupSource.RunMachine32 ? "/reg:32" : "/reg:64"));
        }
    }

    class BloatModel
    {
        public int Tab;                 // 0 unused, 1 background, 2 built-in
        public List<AppUsage> Usage = new List<AppUsage>();
        public List<StartupItem> Items = new List<StartupItem>();
        public bool LoadingUsage, LoadingItems, UsageLoaded, ItemsLoaded;
        public int MinDays = 90;

        public void LoadUsage()
        {
            if (LoadingUsage) return;
            LoadingUsage = true; App.Invalidate();
            var apps = App.Store.Apps.ToList();
            Bg.Run(() => UsageScanner.Scan(apps.Count > 0 ? apps : InstalledApp.LoadAll()),
                u => { Usage = u ?? new List<AppUsage>(); LoadingUsage = false; UsageLoaded = true; App.Invalidate(); });
        }

        public void LoadItems()
        {
            if (LoadingItems) return;
            LoadingItems = true; App.Invalidate();
            Bg.Run(() => StartupScanner.Scan(), i => { Items = i ?? new List<StartupItem>(); LoadingItems = false; ItemsLoaded = true; App.Invalidate(); });
        }

        public List<AppUsage> Unused { get { return Usage.Where(u => (u.Days ?? int.MaxValue) >= MinDays).ToList(); } }
    }
}
