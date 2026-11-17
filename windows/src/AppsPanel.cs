// This PC (model, chip), the app picker (grid or list, pick several) and drag-and-drop to uninstall.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace EasyUninstall
{
    /// Small saved preferences (same registry key as the language).
    static class Settings
    {
        const string Key = @"Software\EasyUninstall";
        public static bool Get(string name, bool fallback)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) { if (k != null && k.GetValue(name) != null) return Convert.ToInt32(k.GetValue(name)) == 1; } } catch { }
            return fallback;
        }
        public static void Set(string name, bool value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord); } catch { }
        }
    }

    /// What this PC is: maker and model, processor, Windows version.
    static class DeviceInfo
    {
        public static readonly string Name, Chip, Os;
        public static readonly bool IsLaptop;

        static string Reg(string key, string value)
        {
            try { using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)) using (var k = b.OpenSubKey(key)) return k == null ? "" : (Convert.ToString(k.GetValue(value)) ?? "").Trim(); }
            catch { return ""; }
        }

        static DeviceInfo()
        {
            const string bios = @"HARDWARE\DESCRIPTION\System\BIOS";
            Func<string, string> real = s => Regex.IsMatch(s, @"^(to be filled|system (product|manufacturer|version)|default string|o\.?e\.?m\.?|not applicable|none|n/?a)", RegexOptions.IgnoreCase) ? "" : s;
            string maker = real(Reg(bios, "SystemManufacturer")), model = real(Reg(bios, "SystemProductName"));
            maker = Regex.Replace(maker, @",?\s*(Inc\.?|Corporation|Corp\.?|Co\.,? ?Ltd\.?|Ltd\.?|COMPUTER INC\.?|Technology|Electronics)\s*$", "", RegexOptions.IgnoreCase).Trim();
            if (maker.Equals("ASUSTeK", StringComparison.OrdinalIgnoreCase)) maker = "ASUS";
            if (maker.Equals("Hewlett-Packard", StringComparison.OrdinalIgnoreCase)) maker = "HP";
            if (maker.StartsWith("Micro-Star", StringComparison.OrdinalIgnoreCase)) maker = "MSI";
            Name = model == "" ? (maker == "" ? "Windows PC" : maker + " PC")
                 : model.StartsWith(maker, StringComparison.OrdinalIgnoreCase) || maker == "" ? model : maker + " " + model;

            Chip = Regex.Replace(Reg(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString"), @"\((R|TM)\)|\s+CPU|\s+@.*$|\s+\d+-Core Processor", "", RegexOptions.IgnoreCase);
            Chip = Regex.Replace(Chip, @"\s+", " ").Trim();

            const string cv = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            int build; int.TryParse(Reg(cv, "CurrentBuild"), out build);
            Os = (build >= 22000 ? "Windows 11" : "Windows 10") + (Reg(cv, "DisplayVersion") != "" ? " " + Reg(cv, "DisplayVersion") : "");

            try { var s = new Native.SYSTEM_POWER_STATUS(); IsLaptop = Native.GetSystemPowerStatus(ref s) && s.BatteryFlag != 128 && s.BatteryFlag != 255; } catch { }
        }
    }

    /// Finds which installed program a dropped file, shortcut or folder belongs to.
    static class Drop
    {
        public static InstalledApp Resolve(string path, List<InstalledApp> apps)
        {
            var names = new List<string>();
            string p = path;
            if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(Path.GetFileNameWithoutExtension(p));
                string target = StartupScanner.LinkTarget(p);
                if (!string.IsNullOrEmpty(target)) p = target;
            }
            p = FS.Trim(p);
            var withDir = apps.Where(a => a.Dir != "").ToList();

            // the program's own folder, or anything inside it
            var owner = withDir.Where(a => FS.Same(a.Dir, p) || FS.IsUnder(p, a.Dir)).OrderByDescending(a => a.Dir.Length).FirstOrDefault();
            if (owner != null) return owner;

            // a folder that holds the program, e.g. "C:\Program Files\Adobe\Adobe Photoshop 2026" inside a dropped parent
            if (Directory.Exists(p) && Safe.SpecificDir(p))
            {
                var inside = withDir.Where(a => FS.IsUnder(a.Dir, p)).ToList();
                string leaf = Path.GetFileName(p);
                var byName = inside.FirstOrDefault(a => Leftovers.Names(a).Contains(leaf));
                if (byName != null) return byName;
                if (inside.Count == 1) return inside[0];
            }

            // by name: the shortcut's name, the file's name, or the program name written inside the .exe
            names.Add(Directory.Exists(p) ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p));
            if (File.Exists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                try { var v = FileVersionInfo.GetVersionInfo(p); names.Add(v.ProductName ?? ""); names.Add(v.FileDescription ?? ""); } catch { }
            }
            foreach (var n in names.Where(n => n.Trim().Length >= 3))
            {
                var hit = apps.FirstOrDefault(a => Leftovers.Names(a).Contains(n.Trim())) ?? apps.FirstOrDefault(a => a.Name.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            return null;
        }

        /// Dropped on the window (or on the .exe icon): tick the programs and open the uninstall window.
        public static void Handle(IEnumerable<string> paths)
        {
            var all = App.Store.Apps.Count > 0 ? App.Store.Apps : InstalledApp.LoadAll();
            var apps = new List<InstalledApp>(); var notApps = new List<string>(); var blocked = new List<string>();
            foreach (var path in paths)
            {
                var app = Resolve(path, all);
                if (app == null) { notApps.Add(Path.GetFileName(path.TrimEnd('\\'))); continue; }
                if (app.IsProtected) { blocked.Add(app.Name); continue; }
                if (!apps.Any(a => a.Id == app.Id)) apps.Add(app);
            }
            if (blocked.Count > 0)
                Dialogs.Info(L.T("এগুলো মুছা যাবে না", "These can't be removed"), L.T("Windows এর অংশ বা অন্য software এর দরকারি অংশ: ", "Part of Windows or something other software needs: ") + string.Join(", ", blocked));
            if (apps.Count == 0)
            {
                if (notApps.Count > 0)
                    Dialogs.Info(L.T("এটা কোনো installed software না", "That's not an installed program"),
                        L.T("Desktop এর shortcut, program এর .exe ফাইল বা program এর ফোল্ডার টেনে এনে ছাড়ুন।\n\n", "Drop a desktop shortcut, a program's .exe file or a program's folder.\n\n") + string.Join(", ", notApps));
                return;
            }
            foreach (var a in apps) App.Store.Picked.Add(a.Id);
            App.Go(Section.Uninstall);
            App.Uninstall(apps);
        }

        /// Dashed hint shown next to the app list.
        public static FrameworkElement Hint()
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            g.Children.Add(new System.Windows.Shapes.Rectangle { Stroke = Theme.Faint, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, RadiusX = 8, RadiusY = 8 });
            g.Children.Add(UI.H(UI.Icon(Glyph.Download, 12), UI.Text(L.T("Shortcut বা program এর ফোল্ডার টেনে এনে এখানে ছাড়ুন", "Drag a shortcut or program folder here"), 12, Theme.Sub, false, false).M(6, 0, 0, 0)).M(10, 4, 10, 5));
            g.ToolTip = L.T("Easy Uninstall.exe এর icon এর উপরে ছাড়লেও হবে", "Dropping on the Easy Uninstall.exe icon works too");
            return g;
        }
    }

    /// The app picker: search, grid or list, tick several apps and uninstall them together.
    class AppsPanel
    {
        public readonly FrameworkElement Root;
        readonly bool home;                 // on the home page a click opens the Uninstall page
        readonly ContentControl title = Views.Holder(), view = Views.Holder(), body = Views.Holder(), footer = Views.Holder();
        readonly ScrollViewer scroll;
        readonly Border search;
        string shown;

        public AppsPanel(bool onHome)
        {
            home = onHome;
            scroll = UI.Scroll(null);
            search = UI.Search(L.T("App খুঁজুন…", "Find an app…"), q => { if (App.Store.Query == q) return; App.Store.Query = q; Refresh(); });
            search.Width = 210;
            ((TextBox)search.Tag).Text = App.Store.Query;
            var head = UI.Dock(new Border(), new FrameworkElement[] { title },
                new FrameworkElement[] { search, view, home ? null : UI.IconButton(Glyph.Refresh, () => App.Store.Reload(), L.T("আবার খুঁজুন", "Search again")).M(6, 0, 0, 0) });
            view.Margin = new Thickness(8, 0, 0, 0);
            body.Margin = new Thickness(0, 10, 0, 10);
            Root = UI.Rows(body, head, body, footer);
        }

        static FrameworkElement PickMark(bool on, bool locked)
        {
            if (locked) return new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Theme.Hover, Child = UI.Icon(Glyph.Lock, 9, Theme.Sub) };
            if (on) return new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Theme.B(Theme.Coral), Child = UI.Icon(Glyph.Check, 10, Theme.White) };
            return new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderBrush = Theme.Faint, BorderThickness = new Thickness(1.5), Background = Theme.Window };
        }

        void Tap(InstalledApp a)
        {
            var s = App.Store;
            if (home) { s.Picked.Add(a.Id); App.Go(Section.Uninstall); return; }
            if (!s.Picked.Remove(a.Id)) s.Picked.Add(a.Id);
            Refresh();
        }

        FrameworkElement Cell(InstalledApp a, bool on)
        {
            var name = UI.Text(a.Name, 12, null, false, true); name.TextAlignment = TextAlignment.Center; name.Height = 32; name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Top;
            var size = UI.Text(a.Size >= 0 ? FS.Pretty(a.Size) : " ", 11, Theme.Sub, false, false); size.TextAlignment = TextAlignment.Center;
            var icon = UI.FileIcon(a.IconPath, 44); icon.HorizontalAlignment = HorizontalAlignment.Center;
            var g = new Grid();
            g.Children.Add(UI.V(icon, name.M(0, 6, 0, 2), size));
            var mark = PickMark(on, a.IsProtected); mark.HorizontalAlignment = HorizontalAlignment.Right; mark.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(mark);
            var b = UI.Clickable(g, () => { if (!a.IsProtected) Tap(a); }, on ? Theme.A(Theme.Coral, 0.16) : Theme.Tile, 12, 8);
            b.Width = home ? 100 : 108; b.Margin = new Thickness(4);
            b.BorderThickness = new Thickness(1.5); b.BorderBrush = on ? Theme.A(Theme.Coral, 0.75) : Theme.Clear;
            b.ToolTip = a.IsProtected ? L.T("Windows এর অংশ বা দরকারি runtime — মুছা বন্ধ", "Part of Windows or a needed runtime — can't be removed") : a.Name;
            if (a.IsProtected) { b.Opacity = 0.5; b.Cursor = System.Windows.Input.Cursors.Arrow; }
            return b;
        }

        FrameworkElement Line(InstalledApp a, bool on)
        {
            string sub = string.Join("  ·  ", new[] { a.Publisher, a.Version == "" ? "" : "Version " + a.Version }.Where(x => x != ""));
            var row = UI.Dock(UI.V(UI.Text(a.Name, 13, null, false, false), sub == "" ? null : UI.Text(sub, 11, Theme.Sub, false, false)).M(10, 0, 10, 0),
                new FrameworkElement[] { PickMark(on, a.IsProtected).M(0, 0, 10, 0), UI.FileIcon(a.IconPath, 28) },
                new FrameworkElement[] { a.IsProtected ? UI.Badge("System", Theme.Gray).M(0, 0, 10, 0) : null, UI.Text(a.Size >= 0 ? FS.Pretty(a.Size) : "", 12, Theme.Sub, false, false) });
            var b = UI.Clickable(row, () => { if (!a.IsProtected) Tap(a); }, on ? Theme.A(Theme.Coral, 0.16) : Theme.Clear, 8, 0);
            b.Padding = new Thickness(8, 5, 10, 5); b.Margin = new Thickness(0, 1, 0, 1);
            if (a.IsProtected) { b.Opacity = 0.5; b.Cursor = System.Windows.Input.Cursors.Arrow; }
            return b;
        }

        public void Refresh()
        {
            var s = App.Store;
            var list = s.Filtered;
            var picked = s.Apps.Where(a => s.Picked.Contains(a.Id)).ToList();
            // redraw only when something this panel shows has changed (the home page refreshes every few seconds for RAM)
            string sig = s.Apps.GetHashCode() + "|" + s.Loading + "|" + s.Query + "|" + s.Grid + "|" + L.English + "|" + string.Join(",", picked.Select(a => a.Id)) + "|" + s.Apps.Sum(a => a.Size);
            if (sig == shown) return;
            shown = sig;

            title.Content = UI.H(UI.Icon(Glyph.Trash, 14), UI.Text(L.T("Uninstall — " + s.Apps.Count + " টি app", "Uninstall — " + s.Apps.Count + " apps"), 13, Theme.Sub, true, false).M(7, 0, 0, 0));
            view.Content = UI.Segmented(new[] { "Grid", "List" }, s.Grid ? 0 : 1, i => { s.Grid = i == 0; Settings.Set("AppsGrid", s.Grid); Refresh(); });

            if (s.Loading && s.Apps.Count == 0) body.Content = UI.Busy(L.T("Apps খোঁজা হচ্ছে…", "Looking for apps…"));
            else if (list.Count == 0) body.Content = UI.Center(UI.Text(L.T("কোন app পাওয়া যায়নি", "No apps found"), 13, Theme.Sub));
            else
            {
                Panel p = s.Grid ? (Panel)new WrapPanel() : new StackPanel();
                foreach (var a in list) p.Children.Add(s.Grid ? Cell(a, s.Picked.Contains(a.Id)) : Line(a, s.Picked.Contains(a.Id)));
                scroll.Content = p;
                if (body.Content != scroll) body.Content = scroll;
            }

            FrameworkElement left;
            if (picked.Count == 0)
                left = UI.H(UI.Text(home ? L.T("কোনো app এ click করলে Uninstall পেজ খুলবে", "Click an app to open the Uninstall page") : L.T("যেগুলো মুছতে চান সেগুলোতে click করে বেছে নিন", "Click the apps you want to remove"), 13, Theme.Sub, false, false), Drop.Hint());
            else
                left = UI.H(UI.Text(L.T(picked.Count + " টি বাছাই · " + FS.Pretty(picked.Sum(a => Math.Max(0, a.Size))), picked.Count + " picked · " + FS.Pretty(picked.Sum(a => Math.Max(0, a.Size)))), 13, null, true, false),
                    UI.Link(L.T("বাছাই মুছুন", "Clear"), () => { s.Picked.Clear(); Refresh(); }).M(8, 0, 0, 0));
            var go = UI.Button(picked.Count == 0 ? "Uninstall" : "Uninstall (" + picked.Count + ")", () => { if (home) App.Go(Section.Uninstall); else App.Uninstall(picked); }, Theme.Coral, Glyph.Trash);
            go.IsEnabled = picked.Count > 0;
            footer.Content = UI.Dock(new Border(), new[] { left }, new FrameworkElement[] { go });
        }
    }
}
