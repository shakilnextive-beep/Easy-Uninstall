// Smart Clean: finds only things that are safe to delete (caches, temp files, old installers,
// Recycle Bin), shows them once, then deletes everything selected in one click.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyUninstall
{
    enum SmartMode { Contents, Items, RecycleItems, Bin }   // empty these folders / delete these / send to Recycle Bin / empty the Bin

    class SmartItem
    {
        public string Title, Note, Icon, SkipReason;
        public List<string> Paths = new List<string>();
        public SmartMode Mode;
        public long Size;
        public bool On = true;
    }

    static class SmartScanner
    {
        static readonly string[] CacheDirs = { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "CachedData", "ShaderCache", "GrShaderCache" };
        static readonly HashSet<string> SkipTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Microsoft", "Packages", "Temp", "Programs", "Google", "Mozilla", "BraveSoftware", "Adobe", "Opera Software", "EasyUninstall" };

        /// Items in the Temp folder that nothing has touched for a day (running programs keep fresh files there).
        public static List<string> OldTemp()
        {
            var cutoff = DateTime.Now.AddDays(-1);
            var list = new List<string>();
            foreach (var c in FS.Children(FS.Temp))
            {
                try { if (File.GetLastWriteTime(c) < cutoff && File.GetCreationTime(c) < cutoff) list.Add(c); } catch { }
            }
            return list;
        }

        public static List<string[]> BrowserCaches()   // { browser name, process name, cache folder }
        {
            var outp = new List<string[]>();
            var chromium = new[] {
                new[] { "Google Chrome", "chrome", Path.Combine(FS.Local, @"Google\Chrome\User Data") },
                new[] { "Microsoft Edge", "msedge", Path.Combine(FS.Local, @"Microsoft\Edge\User Data") },
                new[] { "Brave", "brave", Path.Combine(FS.Local, @"BraveSoftware\Brave-Browser\User Data") },
                new[] { "Vivaldi", "vivaldi", Path.Combine(FS.Local, @"Vivaldi\User Data") } };
            foreach (var b in chromium)
                foreach (var prof in FS.Dirs(b[2]))
                {
                    string leaf = Path.GetFileName(prof);
                    if (leaf != "Default" && !leaf.StartsWith("Profile ")) continue;
                    foreach (var c in new[] { "Cache", "Code Cache", "GPUCache" })
                    {
                        string d = Path.Combine(prof, c);
                        if (Directory.Exists(d)) outp.Add(new[] { b[0], b[1], d });
                    }
                }
            foreach (var prof in FS.Dirs(Path.Combine(FS.Local, @"Mozilla\Firefox\Profiles")))
            {
                string d = Path.Combine(prof, "cache2");
                if (Directory.Exists(d)) outp.Add(new[] { "Firefox", "firefox", d });
            }
            string opera = Path.Combine(FS.Local, @"Opera Software\Opera Stable\Cache");
            if (Directory.Exists(opera)) outp.Add(new[] { "Opera", "opera", opera });
            return outp;
        }

        public static string[] AdobeMedia()
        {
            string common = Path.Combine(FS.Roaming, @"Adobe\Common");
            return new[] { "Media Cache Files", "Media Cache", "Peak Files", "PTX" }.Select(n => Path.Combine(common, n)).Where(Directory.Exists).ToArray();
        }

        public static string[] DevCaches()
        {
            return new[] { Path.Combine(FS.Local, @"npm-cache\_cacache"), Path.Combine(FS.Local, @"pip\Cache"),
                Path.Combine(FS.Local, @"Yarn\Cache"), Path.Combine(FS.Local, @"NuGet\v3-cache") }.Where(Directory.Exists).ToArray();
        }

        public static string[] CrashDumps()
        {
            return new[] { Path.Combine(FS.Local, "CrashDumps"), Path.Combine(FS.Local, @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(FS.Local, @"Microsoft\Windows\WER\ReportQueue"), Path.Combine(FS.Local, "D3DSCache") }.Where(Directory.Exists).ToArray();
        }

        public static List<string> DownloadedInstallers()
        {
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".msi", ".iso", ".msix", ".msixbundle", ".appx", ".appxbundle" };
            var list = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(FS.Downloads))
                {
                    string ext = Path.GetExtension(f), n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if ((File.GetAttributes(f) & FileAttributes.Hidden) != 0) continue;
                    if (exts.Contains(ext) || (ext.ToLowerInvariant() == ".exe" && (n.Contains("setup") || n.Contains("install")))) list.Add(f);
                }
            }
            catch { }
            return list;
        }

        public static List<SmartItem> Scan()
        {
            var running = Procs.Snapshot(false);
            var outp = new List<SmartItem>();

            // 1. App caches (Discord, VS Code, Slack, Spotify… keep theirs in AppData), one row per app
            var small = new List<string>(); long smallSize = 0; int smallApps = 0;
            foreach (var root in new[] { FS.Roaming, FS.Local })
                foreach (var d in FS.Dirs(root))
                {
                    string n = Path.GetFileName(d);
                    if (SkipTop.Contains(n) || n.StartsWith(".")) continue;
                    var dirs = CacheDirs.Select(c => Path.Combine(d, c)).Where(Directory.Exists).ToList();
                    if (dirs.Count == 0) continue;
                    long s = dirs.Sum(x => FS.Size(x));
                    if (s < 1000000) continue;
                    string owner = running.OwnerOf(n);
                    if (owner != null)
                    {
                        if (s >= 20000000)
                            outp.Add(new SmartItem { Title = n + " cache", Note = L.T("App ফিরে আসলে আবার বানিয়ে নেবে।", "Rebuilt when the app comes back."), Icon = Glyph.Box,
                                Paths = dirs, Mode = SmartMode.Contents, Size = s, On = false,
                                SkipReason = L.T(owner + " এখন চালু আছে — বন্ধ করে আবার পরীক্ষা করলে এটাও মুছা যাবে।", owner + " is running now — quit it and check again to clean this too.") });
                        continue;
                    }
                    if (s < 20000000) { small.AddRange(dirs); smallSize += s; smallApps++; continue; }
                    outp.Add(new SmartItem { Title = n + " cache", Note = L.T("অস্থায়ী ফাইল — app দরকার হলে আবার বানিয়ে নেবে।", "Temporary files — the app rebuilds them when needed."),
                        Icon = Glyph.Box, Paths = dirs, Mode = SmartMode.Contents, Size = s });
                }
            if (small.Count > 0)
                outp.Add(new SmartItem { Title = L.T("ছোট ছোট cache (" + smallApps + " টি app)", "Small caches (" + smallApps + " apps)"),
                    Note = L.T("অনেক app এর অল্প অল্প অস্থায়ী ফাইল একসাথে।", "Little bits of temporary files from many apps."), Icon = Glyph.Grid, Paths = small, Mode = SmartMode.Contents, Size = smallSize });

            // 2. Browser caches — only when that browser is closed
            foreach (var g in BrowserCaches().GroupBy(b => b[0]))
            {
                var dirs = g.Select(b => b[2]).ToList();
                long s = dirs.Sum(x => FS.Size(x));
                if (s < 1000000) continue;
                bool busy = running.IsRunning(g.First()[1]);
                outp.Add(new SmartItem { Title = g.Key + " cache", Note = L.T("Website এর অস্থায়ী কপি — browser আবার নিজে নামিয়ে নেবে। Password, bookmark, history থেকে যাবে।", "Temporary copies of websites — the browser downloads them again. Passwords, bookmarks and history stay."),
                    Icon = Glyph.Globe, Paths = dirs, Mode = SmartMode.Contents, Size = s, On = !busy,
                    SkipReason = busy ? L.T(g.Key + " চালু আছে — বন্ধ করে আবার পরীক্ষা করলে এটাও মুছা যাবে।", g.Key + " is running — quit it and check again to clean this too.") : null });
            }

            // 3. Adobe Media Cache — only when Premiere / After Effects are closed
            var media = AdobeMedia();
            long mediaSize = media.Sum(x => FS.Size(x));
            if (mediaSize > 1000000)
            {
                bool busy = running.IsRunning("Adobe Premiere Pro", "AfterFX", "Adobe Media Encoder", "Adobe Audition");
                outp.Add(new SmartItem { Title = "Adobe Media Cache", Note = L.T("Premiere / After Effects এর preview ফাইল — project খুললে আবার বানিয়ে নেবে।", "Premiere / After Effects preview files — rebuilt when you open a project."),
                    Icon = Glyph.Video, Paths = media.ToList(), Mode = SmartMode.Contents, Size = mediaSize, On = !busy,
                    SkipReason = busy ? L.T("Adobe app চালু আছে — বন্ধ করে আবার পরীক্ষা করলে এটাও মুছা যাবে।", "An Adobe app is running — quit it and check again to clean this too.") : null });
            }

            // 4. Temp files, crash reports, developer caches
            var temp = OldTemp();
            long tempSize = temp.Sum(x => FS.Size(x));
            if (tempSize > 1000000)
                outp.Add(new SmartItem { Title = L.T("Temp ফাইল", "Temp files"), Note = L.T("App গুলোর ফেলে রাখা অস্থায়ী ফাইল (১ দিনের বেশি পুরোনো)।", "Temporary files apps left behind (older than 1 day)."),
                    Icon = Glyph.Doc, Paths = temp, Mode = SmartMode.Items, Size = tempSize });
            Add(outp, L.T("Crash report ও dump", "Crash reports and dumps"), L.T("App crash করলে Windows এই রেকর্ড রাখে — সাধারণ কাজে লাগে না।", "Windows keeps these when an app crashes — not needed day to day."), Glyph.Warn, CrashDumps());
            Add(outp, "Developer cache", L.T("Programming tool এর download cache — দরকার হলে আবার download হবে।", "Programming tool download cache — re-downloaded if needed."), Glyph.Code, DevCaches());

            // 5. Installers sitting in Downloads
            var inst = DownloadedInstallers();
            long instSize = inst.Sum(x => FS.Size(x));
            if (instSize > 1000000)
                outp.Add(new SmartItem { Title = L.T("Downloads এর installer (" + inst.Count + " টি)", "Installers in Downloads (" + inst.Count + ")"),
                    Note = string.Join(", ", inst.Take(4).Select(Path.GetFileName)) + (inst.Count > 4 ? " …" : "") + L.T(" — software ইনস্টল হয়ে গেলে আর লাগে না।", " — not needed once the software is installed."),
                    Icon = Glyph.Download, Paths = inst, Mode = SmartMode.RecycleItems, Size = instSize });

            outp = outp.OrderByDescending(i => i.Size).ToList();

            // 6. Recycle Bin — always last
            outp.Add(new SmartItem { Title = "Recycle Bin", Note = L.T("Recycle Bin এর ফাইল খালি না করলে জায়গা ফেরত আসে না।", "Files in the Recycle Bin don't free space until it's emptied."),
                Icon = Glyph.Trash, Mode = SmartMode.Bin, Size = Recycle.BinSize() });
            return outp;
        }

        static void Add(List<SmartItem> outp, string title, string note, string icon, string[] dirs)
        {
            long s = dirs.Sum(x => FS.Size(x));
            if (s > 1000000) outp.Add(new SmartItem { Title = title, Note = note, Icon = icon, Paths = dirs.ToList(), Mode = SmartMode.Contents, Size = s });
        }

        public static void Clean(List<SmartItem> items)
        {
            // Recycle Bin goes last so installers sent there are emptied too.
            foreach (var it in items.Where(i => i.On && i.SkipReason == null).OrderBy(i => i.Mode == SmartMode.Bin))
            {
                switch (it.Mode)
                {
                    case SmartMode.Contents: foreach (var f in it.Paths) foreach (var c in FS.Children(f)) FS.Nuke(c); break;
                    case SmartMode.Items: foreach (var p in it.Paths) if (FS.IsUnder(p, FS.Temp)) FS.Nuke(p); break;
                    case SmartMode.RecycleItems: Recycle.Send(it.Paths, false); break;
                    case SmartMode.Bin: Recycle.Empty(); break;
                }
            }
        }
    }

    class SmartModel
    {
        public enum Stage { Idle, Scanning, Ready, Cleaning, Done }
        public Stage State = Stage.Idle;
        public List<SmartItem> Items = new List<SmartItem>();
        public long Freed;

        public long SelectedSize { get { return Items.Where(i => i.On && i.SkipReason == null).Sum(i => i.Size); } }

        public void Scan()
        {
            if (State == Stage.Scanning || State == Stage.Cleaning) return;
            State = Stage.Scanning; App.Invalidate();
            Bg.Run(() => SmartScanner.Scan(), found => { Items = found ?? new List<SmartItem>(); State = Stage.Ready; App.Invalidate(); });
        }

        public void Clean(Action onDone)
        {
            var chosen = Items; long before = DiskInfo.Read().Free;
            State = Stage.Cleaning; App.Invalidate();
            Bg.Run(() => { SmartScanner.Clean(chosen); System.Threading.Thread.Sleep(800); return DiskInfo.Read().Free; },
                after => { Freed = Math.Max(0, after - before); State = Stage.Done; onDone(); App.Invalidate(); });   // what the disk really got back
        }
    }
}
