// App shell: window, sidebar, navigation, and the command-line dry runs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EasyUninstall
{
    enum Section { Home, Smart, Suggestions, Explorer, BigFiles, Uninstall, Unused, Ram, About }

    abstract class Page
    {
        public FrameworkElement Root;
        public abstract void Refresh();
        public virtual void Shown() { }
        public virtual void Hidden() { }
    }

    class Store
    {
        public List<InstalledApp> Apps = new List<InstalledApp>();
        public bool Loading;

        public void Reload()
        {
            if (Loading) return;
            Loading = true; App.Invalidate();
            Bg.Run(() => InstalledApp.LoadAll(), list =>
            {
                Apps = list ?? new List<InstalledApp>(); Loading = false; App.Invalidate();
                // Windows doesn't know the size of every program: measure the rest in the background
                var todo = Apps.Where(a => a.Size < 0 && a.Dir != "").ToList();
                Bg.Run(() => { foreach (var a in todo) a.Size = FS.Size(a.Dir); }, () => App.Invalidate());
            });
        }
    }

    static class App
    {
        public static Store Store = new Store();
        public static CleanupModel Cleanup = new CleanupModel();
        public static BloatModel Bloat = new BloatModel();
        public static RamModel Ram = new RamModel();
        public static MainWindow Window;
        static bool pending;

        /// Something changed: redraw the visible page (many calls in a row become one redraw).
        public static void Invalidate()
        {
            if (Window == null || pending) return;
            pending = true;
            Bg.Ui.BeginInvoke(DispatcherPriority.Background, new Action(() => { pending = false; Window.RefreshPage(); }));
        }

        public static void Go(Section s) { if (Window != null) Window.Navigate(s); }

        public static void Uninstall(InstalledApp app)
        {
            bool removed = new UninstallDialog(app).Run();
            if (!removed) return;
            Store.Reload(); Cleanup.RefreshDisk(); Cleanup.Suggestions.Clear(); Cleanup.SuggestScanned = false;
            Bloat.Usage.Clear(); Bloat.UsageLoaded = false; Bloat.ItemsLoaded = false;
            Invalidate();
        }

        public static void DarkTitleBar(Window w)
        {
            if (!Theme.Dark) return;
            w.SourceInitialized += (s, e) =>
            {
                try { int on = 1; Native.DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(w).Handle, 20, ref on, 4); } catch { }
            };
        }
    }

    class MainWindow : Window
    {
        Section section = Section.Home;
        Page page;
        readonly ContentControl host = new ContentControl { Focusable = false };
        readonly ContentControl side = new ContentControl { Focusable = false };

        public MainWindow()
        {
            Title = "Easy Uninstall"; Width = 1120; Height = 800; MinWidth = 920; MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.Window; FontFamily = UI.Font; UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            try { Icon = BitmapFrame.Create(typeof(MainWindow).Assembly.GetManifestResourceStream("app.ico")); } catch { }
            App.DarkTitleBar(this);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var sideBorder = new Border { Background = Theme.Side, Child = side };
            g.Children.Add(sideBorder); Grid.SetColumn(host, 1); g.Children.Add(host);
            Content = g;

            L.Changed += () =>
            {
                // notes are written in the old language: scan again
                App.Cleanup.Suggestions.Clear(); App.Cleanup.SuggestScanned = false;
                if (App.Cleanup.Smart.State == SmartModel.Stage.Ready) App.Cleanup.Smart.Scan();
                App.Bloat.ItemsLoaded = false; App.Ram.Loaded = false;
                Navigate(section);
            };
            Loaded += (s, e) => { App.Store.Reload(); Updater.Check(true); };
            Navigate(Section.Home);
        }

        public static string TitleOf(Section s)
        {
            switch (s)
            {
                case Section.Home: return L.T("হোম", "Home");
                case Section.Smart: return "Smart Clean";
                case Section.Suggestions: return L.T("পরামর্শ", "Suggestions");
                case Section.Explorer: return L.T("জায়গা কোথায় গেল", "Where's my space");
                case Section.BigFiles: return L.T("বড় ফাইল", "Big files");
                case Section.Uninstall: return "Uninstall";
                case Section.Unused: return L.T("অপ্রয়োজনীয় Software", "Unused software");
                case Section.Ram: return "RAM";
                default: return L.T("পরিচিতি ও Update", "About and updates");
            }
        }
        static string GlyphOf(Section s)
        {
            switch (s)
            {
                case Section.Home: return Glyph.Home; case Section.Smart: return Glyph.Broom; case Section.Suggestions: return Glyph.Bulb;
                case Section.Explorer: return Glyph.Folder; case Section.BigFiles: return Glyph.Copy; case Section.Uninstall: return Glyph.Trash;
                case Section.Unused: return Glyph.Box; case Section.Ram: return Glyph.Chip; default: return Glyph.Info;
            }
        }
        static Color TintOf(Section s)
        {
            switch (s)
            {
                case Section.Smart: case Section.Suggestions: return Theme.Teal;
                case Section.Explorer: case Section.BigFiles: return Theme.Coral;
                case Section.Ram: return Theme.Amber;
                case Section.About: return Theme.Stone;
                default: return Theme.Purple;
            }
        }

        public void Navigate(Section s)
        {
            if (page != null) page.Hidden();
            section = s;
            switch (s)
            {
                case Section.Home: page = new HomePage(); break;
                case Section.Smart: page = new SmartPage(); break;
                case Section.Suggestions: page = new SuggestPage(); break;
                case Section.Explorer: page = new ExplorerPage(); break;
                case Section.BigFiles: page = new BigFilesPage(); break;
                case Section.Uninstall: page = new UninstallPage(); break;
                case Section.Unused: page = new BloatPage(); break;
                case Section.Ram: page = new RamPage(); break;
                default: page = new AboutPage(); break;
            }
            host.Content = page.Root;
            page.Shown();
            page.Refresh();
            BuildSidebar();
        }

        UpdateState shownUpdate = (UpdateState)(-1);

        public void RefreshPage()
        {
            if (page != null) page.Refresh();
            if (shownUpdate != Updater.State) BuildSidebar();
        }

        void BuildSidebar()
        {
            shownUpdate = Updater.State;
            var top = new StackPanel { Margin = new Thickness(10, 14, 10, 0) };
            top.Children.Add(UI.H(new Image { Source = Icons.Logo, Width = 30, Height = 30 }, UI.Text("Easy Uninstall", 14, null, true, false).M(10, 0, 0, 0)).M(8, 0, 0, 12));
            Action<Section> item = s =>
            {
                bool on = s == section;
                var row = UI.H(UI.IconBox(GlyphOf(s), Theme.B(TintOf(s)), 22), UI.Text(TitleOf(s), 13, null, on, false).M(9, 0, 0, 0));
                var b = UI.Clickable(row, () => Navigate(s), on ? Theme.Hover : Theme.Clear, 7, 0);
                b.Padding = new Thickness(8, 5, 8, 5); b.Margin = new Thickness(0, 1, 0, 1);
                top.Children.Add(b);
            };
            Action<string> head = t => top.Children.Add(UI.Text(t, 11.5, Theme.Sub, true, false).M(8, 14, 0, 4));
            item(Section.Home);
            head(L.T("পরিষ্কার", "Clean")); item(Section.Smart); item(Section.Suggestions); item(Section.Explorer); item(Section.BigFiles);
            head("Apps"); item(Section.Uninstall); item(Section.Unused);
            head(L.T("সিস্টেম", "System")); item(Section.Ram);
            head(L.T("অন্যান্য", "More")); item(Section.About);

            var bottom = new StackPanel { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Bottom };
            if (Updater.State == UpdateState.Available || Updater.State == UpdateState.Downloading)
            {
                var btn = UI.Button(Updater.State == UpdateState.Downloading ? L.T("Download হচ্ছে…", "Downloading…") : L.T("এক click এ Update", "Update in one click"), () => Updater.Install(), Theme.Teal);
                btn.IsEnabled = Updater.State != UpdateState.Downloading; btn.HorizontalAlignment = HorizontalAlignment.Stretch;
                bottom.Children.Add(UI.Card(UI.V(UI.H(UI.Icon(Glyph.Download, 14, Theme.Safe), UI.Text(L.T("নতুন version ", "New version ") + Updater.Latest, 13, Theme.Safe, true, false).M(6, 0, 0, 0)), btn.M(0, 8, 0, 0)), Theme.A(Theme.Teal, 0.12), 10, 10).M(0, 0, 0, 10));
            }
            var credit = UI.V(UI.Text("Creator: " + AppInfo.Creator, 12, null, true, false), UI.Text(AppInfo.Email, 11, Theme.Sub, false, false));
            foreach (TextBlock t in credit.Children) t.HorizontalAlignment = HorizontalAlignment.Center;
            var cb = UI.Clickable(credit, () => Navigate(Section.About), Theme.Clear, 7, 4);
            bottom.Children.Add(cb);
            var lang = UI.Segmented(new[] { "বাংলা", "English" }, L.English ? 1 : 0, i => L.English = i == 1);
            lang.HorizontalAlignment = HorizontalAlignment.Center; lang.Margin = new Thickness(0, 8, 0, 0); lang.ToolTip = "ভাষা / Language";
            bottom.Children.Add(lang);

            var g = new Grid(); g.Children.Add(UI.Scroll(top)); g.Children.Add(bottom);
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(bottom, 1);
            side.Content = g;
        }

        /// Saves a picture of one page (used to check the UI):
        ///   --shot <section> <file.png> [en|bn] [seconds]      or      --shot Dialog|Review <file.png> en 8 "app name"
        public void Shot(string what, string file, int waitSeconds, string appName)
        {
            Loaded += (a, b) =>
            {
                Window target = this;
                Section s;
                if (Enum.TryParse(what, true, out s)) Navigate(s);
                else
                {
                    // the uninstall window for one app — opened for a picture only, nothing is uninstalled
                    var app = InstalledApp.LoadAll().FirstOrDefault(x => x.Name.ToLowerInvariant().Contains((appName ?? "").ToLowerInvariant()));
                    if (app == null) { Application.Current.Shutdown(); return; }
                    var d = new UninstallDialog(app); target = d; d.Show();
                    if (what.ToLowerInvariant() == "review") d.PreviewLeftovers();
                }
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(waitSeconds) };
                t.Tick += (x, y) =>
                {
                    t.Stop();
                    var root = (FrameworkElement)target.Content;
                    var dpi = VisualTreeHelper.GetDpi(target);
                    var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen()) { dc.DrawRectangle(Theme.Window, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); }
                    bmp.Render(dv);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var f = File.Create(file)) enc.Save(f);
                    Application.Current.Shutdown();
                };
                t.Start();
            };
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--recycle-list") { Recycle.RunElevatedList(args[1]); return 0; }
            if (args.Length >= 1 && args[0].StartsWith("--") && args[0] != "--shot") return DryRun(args);

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Bg.Ui = app.Dispatcher;
            app.Resources = UI.Styles();
            app.DispatcherUnhandledException += (s, e) =>
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "EasyUninstall-error.log"), DateTime.Now + "\r\n" + e.Exception + "\r\n\r\n"); } catch { }
                e.Handled = true;
            };
            bool shot = args.Length >= 3 && args[0] == "--shot";
            if (shot && args.Length >= 4) L.UseOnce(args[3] == "en");
            var w = new MainWindow();
            App.Window = w;
            if (shot)
            {
                if (args[1].ToLowerInvariant() == "smart") App.Cleanup.Smart.Scan();
                if (args[1].ToLowerInvariant() == "bigfiles") App.Cleanup.ScanBigFiles();
                if (args[1].ToLowerInvariant() == "unused1") { App.Bloat.Tab = 1; args[1] = "Unused"; }
                w.Shot(args[1], args[2], args.Length >= 5 ? int.Parse(args[4]) : 12, args.Length >= 6 ? args[5] : null);
            }
            app.Run(w);
            return 0;
        }

        /// Dry runs from a terminal — nothing is deleted:  "Easy Uninstall.exe" --smart | more
        static int DryRun(string[] a)
        {
            if (a.Length > 1 && a[a.Length - 1] == "en") L.UseOnce(true);
            var o = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            switch (a[0])
            {
                case "--apps":
                    foreach (var x in InstalledApp.LoadAll()) o.WriteLine((x.IsProtected ? "[system] " : "") + x.Name + "  " + x.Version + "  | " + x.Publisher + " | " + (x.Dir == "" ? "(no folder)" : x.Dir) + " | " + (x.Size >= 0 ? FS.Pretty(x.Size) : "?"));
                    break;
                case "--scan":      // --scan "App Name": what Full Uninstall would look for
                    var all = InstalledApp.LoadAll();
                    foreach (var x in all.Where(x => a.Length > 1 && x.Name.ToLowerInvariant().Contains(a[1].ToLowerInvariant())))
                    {
                        o.WriteLine("== " + x.Name + " [" + x.KeyName + "] " + x.Dir + "\r\n   uninstall: " + x.UninstallString + "\r\n   names: " + string.Join(" | ", Leftovers.Names(x)));
                        foreach (var l in Leftovers.Find(x, all)) o.WriteLine("   " + l.Path + "  " + (l.IsRegistry ? "(registry)" : FS.Pretty(l.Size)));
                    }
                    break;
                case "--smart":
                    foreach (var i in SmartScanner.Scan()) o.WriteLine((i.SkipReason == null ? "[clean] " : "[skip]  ") + FS.Pretty(i.Size) + "  " + i.Title + "  " + (i.SkipReason ?? "") + "\r\n          " + string.Join("\r\n          ", i.Paths.Take(6)));
                    break;
                case "--suggest":
                    foreach (var s in SuggestionScanner.Scan(InstalledApp.LoadAll())) o.WriteLine((s.Safe ? "[SAFE]   " : "[REVIEW] ") + s.Title + "  " + FS.Pretty(s.Size) + "  " + s.Kind);
                    break;
                case "--big":
                    foreach (var e in CleanupModel.FindBig(500L * 1024 * 1024)) { var v = BigFileVerdict.Of(e.Path); o.WriteLine((v.Safe ? "[safe]    " : "[careful] ") + FS.Pretty(e.Size) + "  " + e.Path + "\r\n     " + v.Note); }
                    break;
                case "--bloat":
                    foreach (var u in UsageScanner.Scan(InstalledApp.LoadAll())) o.WriteLine((u.Days.HasValue ? u.Days + "d" : "never") + (u.Estimated ? "~" : "") + "  " + FS.Pretty(u.Size) + "  " + u.App.Name);
                    o.WriteLine("---");
                    foreach (var i in StartupScanner.Scan()) o.WriteLine((i.Orphan ? "ORPHAN" : i.Disabled ? "off" : "on") + (i.Running ? "*" : "") + "  " + i.Owner + "  [" + i.Name + "] " + i.Source + (i.Caution ? " (caution)" : "") + "  " + i.Exe);
                    break;
                case "--ram":
                    var m = MemInfo.Read();
                    o.WriteLine("used " + FS.Pretty(m.Used) + " / " + FS.Pretty(m.Total) + "  pagefile " + FS.Pretty(m.SwapUsed) + "/" + FS.Pretty(m.SwapTotal) + "  pressure " + m.Pressure);
                    foreach (var r in RamScanner.Scan().Take(15)) o.WriteLine((r.Caution ? "[careful] " : "[safe]    ") + FS.Pretty(r.Memory) + "  " + r.Name + "  (" + Path.GetFileName(r.ExePath) + ")");
                    break;
                case "--version": o.WriteLine(AppInfo.Version); break;
                default: o.WriteLine("Unknown option. Try --apps, --scan \"name\", --smart, --suggest, --big, --bloat, --ram"); return 1;
            }
            return 0;
        }
    }
}
