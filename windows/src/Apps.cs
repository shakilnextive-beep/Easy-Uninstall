// Installed programs (from the Windows "Uninstall" registry list), running their uninstallers,
// and finding the files they leave behind.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace EasyUninstall
{
    class InstalledApp
    {
        public string Id, Name, Version = "", Publisher = "", Dir = "", UninstallString = "", IconPath = "", KeyName;
        public RegistryHive Hive; public RegistryView View;
        public bool IsMsi;
        public long Size = -1;          // bytes; -1 = not measured yet
        public DateTime? Installed;

        const string Root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        /// Parts of Windows itself and shared runtimes that other programs need.
        public bool IsProtected
        {
            get
            {
                string n = Name.ToLowerInvariant(), p = Publisher.ToLowerInvariant();
                bool hardware = p.Contains("intel") || p.Contains("realtek") || p.Contains("nvidia") || p.Contains("advanced micro devices") || p.Contains("amd") || p.Contains("synaptics") || p.Contains("qualcomm") || p.Contains("mediatek");
                return n.StartsWith("microsoft edge") || n.Contains("webview2")
                    || n.Contains("visual c++") || n.Contains("redistributable") || n.Contains("visual basic/c++ runtime")
                    || n.StartsWith("microsoft .net") || n.StartsWith("microsoft windows desktop runtime") || n.StartsWith("microsoft asp.net core")
                    || (p.Contains("microsoft") && (n.Contains(" runtime") || n.Contains("compatibility fix")))
                    || n.StartsWith("microsoft update health") || n.StartsWith("update for windows") || n.StartsWith("windows pc health")
                    || n.Contains(" driver") || (hardware && (n.Contains("chipset") || n.Contains("management engine") || n.Contains("graphics") || n.Contains("audio") || n.Contains("serial io") || n.Contains("rapid storage")));
            }
        }

        public bool IsAdobe { get { return Publisher.ToLowerInvariant().Contains("adobe") || Name.ToLowerInvariant().StartsWith("adobe "); } }

        /// False once the program's uninstaller has finished and removed it from the Windows list.
        public bool StillInstalled()
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(Hive, View))
                using (var k = b.OpenSubKey(Root + "\\" + KeyName)) return k != null;
            }
            catch { return true; }
        }

        public static List<InstalledApp> LoadAll()
        {
            var result = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            var sources = new[] {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry64) };
            foreach (var s in sources)
            {
                try
                {
                    using (var b = RegistryKey.OpenBaseKey(s.Item1, s.Item2))
                    using (var root = b.OpenSubKey(Root))
                    {
                        if (root == null) continue;
                        foreach (var name in root.GetSubKeyNames())
                        {
                            try
                            {
                                using (var k = root.OpenSubKey(name))
                                {
                                    var app = Make(k, name, s.Item1, s.Item2);
                                    if (app != null && !result.ContainsKey(app.Name + "|" + app.Version)) result[app.Name + "|" + app.Version] = app;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            var list = result.Values.ToList();
            // Some installers only record the vendor folder ("C:\Program Files\Adobe"): use the program's own folder inside it.
            foreach (var a in list.Where(x => x.Dir != ""))
            {
                var names = Leftovers.Names(a, false);
                if (names.Contains(Path.GetFileName(a.Dir))) continue;
                string child = FS.Dirs(a.Dir).FirstOrDefault(d => names.Contains(Path.GetFileName(d)));
                if (child != null) a.Dir = child;
            }
            // A folder that still contains other programs is not this program's own: never treat it as such.
            var shared = list.Where(a => a.Dir != "" && list.Any(o => o != a && o.Dir != "" && FS.IsUnder(o.Dir, a.Dir))).ToList();
            foreach (var a in shared) a.Dir = "";
            return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static string Str(RegistryKey k, string v) { return (k.GetValue(v) as string ?? "").Trim(); }

        static InstalledApp Make(RegistryKey k, string keyName, RegistryHive hive, RegistryView view)
        {
            if (k == null) return null;
            string name = Str(k, "DisplayName");
            if (name == "") return null;
            if (Convert.ToInt32(k.GetValue("SystemComponent", 0)) == 1) return null;
            if (Str(k, "ParentKeyName") != "") return null;                      // an update of another program
            string rel = Str(k, "ReleaseType").ToLowerInvariant();
            if (rel.Contains("update") || rel.Contains("hotfix")) return null;
            if (Regex.IsMatch(name, @"^(Security )?Update for |\(KB\d+\)$")) return null;

            var a = new InstalledApp { Id = hive + "|" + view + "|" + keyName, Name = name, KeyName = keyName, Hive = hive, View = view };
            a.Version = Str(k, "DisplayVersion");
            a.Publisher = Str(k, "Publisher");
            a.IsMsi = Convert.ToInt32(k.GetValue("WindowsInstaller", 0)) == 1;
            a.UninstallString = Str(k, "UninstallString");
            if (a.UninstallString == "" && !a.IsMsi) return null;                 // nothing we could run
            long kb = 0; try { kb = Convert.ToInt64(k.GetValue("EstimatedSize", 0)); } catch { }
            if (kb > 0) a.Size = kb * 1024;
            string d = Str(k, "InstallDate");
            DateTime dt;
            if (DateTime.TryParseExact(d, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out dt)) a.Installed = dt;

            string icon = Str(k, "DisplayIcon");
            int comma = icon.LastIndexOf(',');
            if (comma > 2) icon = icon.Substring(0, comma);
            icon = icon.Trim().Trim('"');
            if (File.Exists(icon)) a.IconPath = icon;

            // Where the program lives: InstallLocation, else the folder of its icon / uninstaller.
            string loc = FS.Trim(Str(k, "InstallLocation").Trim('"'));
            if (Safe.SpecificDir(loc) && Directory.Exists(loc)) a.Dir = loc;
            else
            {
                string exe, args;
                var guess = new List<string>();
                if (a.IconPath != "") guess.Add(a.IconPath);
                if (Uninstaller.Split(a.UninstallString, out exe, out args) && File.Exists(exe)) guess.Add(exe);
                foreach (var g in guess)
                {
                    string dir = Path.GetDirectoryName(g) ?? "";
                    string low = dir.ToLowerInvariant();
                    if (low.Contains("package cache") || low.Contains("installshield") || low.Contains("\\installer") || low.Contains("{")) continue;
                    if (!FS.IsUnder(dir, FS.PF) && !FS.IsUnder(dir, FS.PF86) && !FS.IsUnder(dir, Path.Combine(FS.Local, "Programs")) && !FS.IsUnder(dir, FS.Roaming) && !FS.IsUnder(dir, FS.Local)) continue;
                    if (Safe.SpecificDir(dir)) { a.Dir = dir; break; }
                }
            }
            return a;
        }
    }

    static class Uninstaller
    {
        /// Splits a registry command line into program + arguments.
        public static bool Split(string cmd, out string exe, out string args)
        {
            exe = ""; args = "";
            cmd = (cmd ?? "").Trim();
            if (cmd == "") return false;
            if (cmd.StartsWith("\""))
            {
                int e = cmd.IndexOf('"', 1);
                if (e > 0) { exe = cmd.Substring(1, e - 1); args = cmd.Substring(e + 1).Trim(); return true; }
            }
            if (File.Exists(cmd)) { exe = cmd; return true; }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                string cand = cmd.Substring(0, i + 4);
                if (File.Exists(cand) || !cand.Contains("\\")) { exe = cand; args = cmd.Substring(i + 4).Trim(); return true; }
                i = cmd.IndexOf(".exe", i + 4, StringComparison.OrdinalIgnoreCase);
            }
            int sp = cmd.IndexOf(' ');
            exe = sp < 0 ? cmd : cmd.Substring(0, sp);
            args = sp < 0 ? "" : cmd.Substring(sp + 1).Trim();
            return true;
        }

        /// Starts the program's own uninstaller (the same thing Windows Settings does). Returns an error text, or null when started.
        public static string Start(InstalledApp app)
        {
            string exe, args;
            if (app.IsMsi && Regex.IsMatch(app.KeyName, @"^\{[0-9A-Fa-f\-]{36}\}$")) { exe = "msiexec.exe"; args = "/x " + app.KeyName; }
            else
            {
                if (!Split(app.UninstallString, out exe, out args)) return L.T("এই software এর uninstaller পাওয়া যায়নি।", "This software has no uninstaller.");
                if (Path.GetFileNameWithoutExtension(exe).ToLowerInvariant() == "msiexec") args = Regex.Replace(args, @"/I\s*\{", "/X{", RegexOptions.IgnoreCase);
            }
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = true };
                string dir = Path.IsPathRooted(exe) ? Path.GetDirectoryName(exe) : null;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                Process.Start(psi);
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        /// Asks the program's windows to close (it may ask to save), like closing it by hand.
        public static void CloseIfRunning(InstalledApp app)
        {
            if (app.Dir == "") return;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = Procs.PathOf(p.Id);
                    if (path != null && FS.IsUnder(path, app.Dir) && p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
    }

    class Leftover
    {
        public string Path; public long Size; public bool Selected = true; public bool IsRegistry;
    }

    static class Leftovers
    {
        static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "microsoft", "windows", "packages", "temp", "programs", "common files", "package cache", "windows nt", "internet explorer",
            "windowsapps", "microsoft shared", "default", "public", "desktop", "documents", "data", "cache", "logs", "config", "settings",
            "google", "adobe", "mozilla", "intel", "nvidia", "amd", "apple", "user", "users", "system", "local", "roaming", "classes", "policies", "wow6432node" };

        /// Names a program's folders may carry: its display name, the name without version, its install folder.
        public static HashSet<string> Names(InstalledApp app, bool withFolder = true)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = s => { s = (s ?? "").Trim(); if (s.Length >= 4 && !Generic.Contains(s)) set.Add(s); };
            add(app.Name);
            string n = app.Name, prev;
            do
            {
                prev = n;
                n = Regex.Replace(n, @"\s*(\([^)]*\)|\bv?\d+(\.\d+)+\S*|\b(x64|x86|64-bit|32-bit|\d+)\b)\s*$", "", RegexOptions.IgnoreCase).Trim(' ', '-', '_');
            } while (n != prev && n.Length > 0);
            add(n);
            if (withFolder && app.Dir != "") add(Path.GetFileName(app.Dir));
            if (!app.KeyName.StartsWith("{") && !Regex.IsMatch(app.KeyName, @"_is1$")) add(app.KeyName);
            return set;
        }

        static string CleanPublisher(string p)
        {
            return Regex.Replace(p ?? "", @",?\s*(Inc\.?|LLC|Ltd\.?|Limited|Corporation|Corp\.?|GmbH|Co\.?|S\.A\.|Pty)\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        /// Finds files, folders and settings a program left behind. `others` = programs that are still installed.
        public static List<Leftover> Find(InstalledApp app, List<InstalledApp> others)
        {
            var names = Names(app);
            var found = new Dictionary<string, Leftover>(StringComparer.OrdinalIgnoreCase);
            var otherDirs = others.Where(o => o.Id != app.Id && o.Dir != "").Select(o => o.Dir).ToList();
            var otherNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in others.Where(o => o.Id != app.Id)) foreach (var n in Names(o)) otherNames.Add(n);

            Func<string, bool> usedByOther = p => otherDirs.Any(d => FS.Same(d, p) || FS.IsUnder(d, p) || FS.IsUnder(p, d));
            Action<string> addPath = p =>
            {
                if (found.ContainsKey(p) || !Safe.SpecificDir(p) || !FS.Exists(p) || usedByOther(p)) return;
                found[p] = new Leftover { Path = p };
            };

            // 1. The program folder itself, when the uninstaller left it.
            if (app.Dir != "") addPath(app.Dir);

            // 2. Data folders with exactly the program's name.
            var roots = new List<string> { FS.Roaming, FS.Local, Path.Combine(FS.Home, "AppData", "LocalLow"), FS.ProgramData,
                Path.Combine(FS.Local, "Programs"), FS.PF, FS.PF86,
                FS.Special(Environment.SpecialFolder.Programs), FS.Special(Environment.SpecialFolder.CommonPrograms) };
            string pub = CleanPublisher(app.Publisher);
            if (pub.Length >= 3 && !Generic.Contains(pub) || pub.ToLowerInvariant() == "adobe" || pub.ToLowerInvariant() == "google")
                foreach (var r in roots.ToArray()) { string pr = Path.Combine(r, pub); if (Directory.Exists(pr)) roots.Add(pr); }
            foreach (var r in roots)
            {
                if (r == "" || !Directory.Exists(r)) continue;
                foreach (var item in FS.Children(r))
                {
                    string leaf = Path.GetFileName(item);
                    string baseName = File.Exists(item) ? Path.GetFileNameWithoutExtension(item) : leaf;
                    if (!names.Contains(leaf) && !names.Contains(baseName)) continue;
                    if (otherNames.Contains(leaf) || otherNames.Contains(baseName)) continue;   // another installed program has the same name
                    addPath(item);
                }
            }

            // 3. Desktop shortcuts.
            foreach (var desk in new[] { FS.Special(Environment.SpecialFolder.DesktopDirectory), FS.Special(Environment.SpecialFolder.CommonDesktopDirectory) })
                foreach (var n in names) { string lnk = Path.Combine(desk, n + ".lnk"); if (File.Exists(lnk) && !otherNames.Contains(n)) found[lnk] = new Leftover { Path = lnk }; }

            // 4. The program's own settings in the registry (current user only).
            try
            {
                using (var soft = Registry.CurrentUser.OpenSubKey("Software"))
                {
                    if (soft != null)
                    {
                        var subs = soft.GetSubKeyNames();
                        foreach (var s in subs)
                        {
                            if (names.Contains(s) && !otherNames.Contains(s)) found[@"HKCU\Software\" + s] = new Leftover { Path = @"HKCU\Software\" + s, IsRegistry = true };
                            else if (pub.Length >= 3 && string.Equals(s, pub, StringComparison.OrdinalIgnoreCase))
                                using (var pk = soft.OpenSubKey(s))
                                    foreach (var s2 in pk.GetSubKeyNames())
                                        if (names.Contains(s2) && !otherNames.Contains(s2))
                                            found[@"HKCU\Software\" + s + "\\" + s2] = new Leftover { Path = @"HKCU\Software\" + s + "\\" + s2, IsRegistry = true };
                        }
                    }
                }
            }
            catch { }

            var list = found.Values.ToList();
            foreach (var l in list) if (!l.IsRegistry) l.Size = FS.Size(l.Path);
            return list.OrderBy(l => l.IsRegistry).ThenBy(l => l.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// Removes the chosen leftovers: files go to the Recycle Bin, registry keys are saved to a backup file first.
        /// Returns what could not be removed.
        public static List<string> Remove(IEnumerable<Leftover> items)
        {
            var failed = new List<string>();
            var files = new List<string>();
            foreach (var l in items)
            {
                if (!l.IsRegistry) { files.Add(l.Path); continue; }
                string sub = l.Path.Substring(@"HKCU\".Length);
                if (sub.Split('\\').Length < 2 || !sub.StartsWith("Software\\", StringComparison.OrdinalIgnoreCase)) { failed.Add(l.Path); continue; }
                try
                {
                    string dir = Path.Combine(FS.Local, "EasyUninstall", "Registry backup");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, Regex.Replace(sub, @"[\\/:*?""<>|]", "_") + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".reg");
                    var psi = new ProcessStartInfo("reg.exe", "export \"" + l.Path + "\" \"" + file + "\" /y") { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) p.WaitForExit();
                    if (!File.Exists(file)) { failed.Add(l.Path); continue; }       // no backup, no delete
                    Registry.CurrentUser.DeleteSubKeyTree(sub, false);
                }
                catch { failed.Add(l.Path); }
            }
            failed.AddRange(Recycle.Send(files, true));
            return failed;
        }
    }
}
