// RAM: how much is in use, which app uses how much, and quitting apps politely.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace EasyUninstall
{
    struct MemInfo
    {
        public long Total, Used, SwapTotal, SwapUsed;
        public int Pressure;              // 1 normal, 2 warning, 4 critical

        public static MemInfo Read()
        {
            var m = new MemInfo { Pressure = 1 };
            var s = new Native.MEMORYSTATUSEX(); s.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            try
            {
                if (!Native.GlobalMemoryStatusEx(ref s)) return m;
                m.Total = (long)s.ullTotalPhys;
                m.Used = (long)(s.ullTotalPhys - s.ullAvailPhys);
                // page file = commit limit beyond the physical RAM
                m.SwapTotal = Math.Max(0, (long)s.ullTotalPageFile - (long)s.ullTotalPhys);
                long committed = (long)(s.ullTotalPageFile - s.ullAvailPageFile);
                m.SwapUsed = Math.Min(m.SwapTotal, Math.Max(0, committed - m.Total));
                m.Pressure = s.dwMemoryLoad >= 90 ? 4 : s.dwMemoryLoad >= 80 ? 2 : 1;
            }
            catch { }
            return m;
        }
    }

    class RamApp
    {
        public string Key, Name, ExePath, Note, Duplicate;
        public long Memory; public bool Caution;
        public List<int> Pids = new List<int>();
    }

    static class RamScanner
    {
        static readonly string[] WorkApps = { "photoshop", "premiere", "afterfx", "illustrator", "indesign", "lightroom", "audition", "media encoder", "animate",
            "winword", "excel", "powerpnt", "onenote", "devenv", "code", "blender", "resolve", "figma", "notepad", "wordpad", "obs64", "capcut", "filmora", "vegas",
            "autocad", "acad", "sketchup", "3dsmax", "maya", "unity", "idea64", "pycharm", "studio64", "cmd", "powershell", "windowsterminal" };
        static readonly string[] Browsers = { "chrome", "opera", "firefox", "msedge", "brave", "vivaldi", "arc" };
        static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "explorer", "ApplicationFrameHost", "TextInputHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "LockApp", "dwm", "SystemSettings", "Taskmgr", "EasyUninstall", "Easy Uninstall", "msedgewebview2", "Widgets", "WidgetService" };
        static readonly Regex Year = new Regex(@"\s+(19|20)\d{2}$");

        static string Title(string exe)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(exe);
                string d = (v.FileDescription ?? "").Trim(), p = (v.ProductName ?? "").Trim();
                // "WhatsApp.Root" style descriptions are file names, not titles
                bool fileLike = d.Contains(".") && !d.Contains(" ");
                if (d != "" && !fileLike) return d;
                if (p != "") return p;
                if (fileLike) return d.Substring(0, d.IndexOf('.'));
            }
            catch { }
            return Path.GetFileNameWithoutExtension(exe);
        }

        public static List<RamApp> Scan()
        {
            var snap = Procs.Snapshot(true);
            int me = Process.GetCurrentProcess().Id;
            // one app = every process started from the same folder (browsers and Electron apps run many helpers)
            Func<Procs.P, string> keyOf = p => FS.IsUnder(p.Path, FS.WinDir) ? p.Path : (Path.GetDirectoryName(p.Path) ?? p.Path);
            var groups = snap.All.Where(p => p.Path != null && p.Pid != me).GroupBy(keyOf, StringComparer.OrdinalIgnoreCase);
            var apps = new List<RamApp>();
            foreach (var g in groups)
            {
                var windows = g.Where(p => p.HasWindow && !Hidden.Contains(p.Name)).ToList();
                if (windows.Count == 0) continue;
                var main = windows.OrderByDescending(p => p.Memory).First();
                apps.Add(new RamApp { Key = g.Key, ExePath = main.Path, Name = Title(main.Path), Memory = g.Sum(p => p.Memory), Pids = windows.Select(p => p.Pid).ToList() });
            }
            foreach (var a in apps)
            {
                string exe = Path.GetFileNameWithoutExtension(a.ExePath).ToLowerInvariant(), low = a.Name.ToLowerInvariant();
                var twin = apps.FirstOrDefault(o => o != a && Year.IsMatch(o.Name) && Year.IsMatch(a.Name) && Year.Replace(o.Name, "") == Year.Replace(a.Name, ""));
                a.Note = L.T("বন্ধ করলে কোন সমস্যা নেই — দরকার হলে আবার খুলুন।", "Safe to quit — reopen it when needed.");
                if (exe.Contains("claude") || low.Contains("claude"))
                { a.Caution = true; a.Note = L.T("Claude বন্ধ করলে চলমান কথোপকথন ও কাজ থেমে যাবে।", "Quitting Claude stops any conversation and running work."); }
                else if (WorkApps.Any(w => exe == w || exe.StartsWith(w) || low.Contains(w + " ")) || low.StartsWith("adobe "))
                { a.Caution = true; a.Note = L.T("কাজের app — বন্ধ করার আগে project / file save করুন।", "Work app — save your project / files before quitting."); }
                else if (Browsers.Any(b => exe == b))
                    a.Note = L.T("Browser — প্রতিটা tab আলাদা RAM নেয়। অপ্রয়োজনীয় tab বন্ধ করুন, আবার খুললে tab ফিরে আসে।", "Browser — every tab uses its own RAM. Close tabs you don't need; they come back when you reopen.");
                if (twin != null)
                {
                    a.Duplicate = twin.Name;
                    a.Note = L.T("⚠️ " + twin.Name + " ও চলছে — একই app দুইবার খোলা, একটা বন্ধ করুন। ", "⚠️ " + twin.Name + " is also running — same app open twice, quit one. ") + a.Note;
                }
            }
            return apps.OrderByDescending(a => a.Memory).ToList();
        }

        /// Normal close: the app asks to save unsaved work.
        public static void Quit(RamApp a)
        {
            foreach (int pid in a.Pids)
            {
                try { using (var p = Process.GetProcessById(pid)) p.CloseMainWindow(); } catch { }
            }
        }
    }

    class RamModel
    {
        public MemInfo Info = MemInfo.Read();
        public List<RamApp> Apps = new List<RamApp>();
        public bool Loaded;
        DispatcherTimer timer; bool busy;

        public void Start()
        {
            Refresh();
            if (timer != null) return;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, e) => Refresh();
            timer.Start();
        }
        public void Stop() { if (timer != null) { timer.Stop(); timer = null; } }

        public void Refresh()
        {
            if (busy) return;
            busy = true;
            Bg.Run(() => Tuple.Create(MemInfo.Read(), RamScanner.Scan()),
                r => { busy = false; if (r == null) return; Info = r.Item1; Apps = r.Item2; Loaded = true; App.Invalidate(); });
        }

        public void Quit(RamApp a)
        {
            RamScanner.Quit(a);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (s, e) => { t.Stop(); Refresh(); };
            t.Start();
        }
    }
}
