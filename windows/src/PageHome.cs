// Home dashboard (bento tiles) and the About page.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyUninstall
{
    /// What fills the disk, for the big tile. Measured once in the background.
    static class Breakdown
    {
        public class Part { public string Name; public long Size; public Color Color; }
        static long[] sizes;
        public static bool Loading;

        public static List<Part> Parts
        {
            get
            {
                var list = new List<Part>();
                if (sizes == null) return list;
                list.Add(new Part { Name = "Apps", Size = sizes[0], Color = Theme.Purple });
                list.Add(new Part { Name = L.T("আপনার ফাইল", "Your files"), Size = sizes[1], Color = Theme.Mint });
                list.Add(new Part { Name = L.T("App data ও cache", "App data and cache"), Size = sizes[2], Color = Theme.Peach });
                list.Add(new Part { Name = "Windows", Size = Math.Max(0, App.Cleanup.Disk.Used - sizes.Sum()), Color = Theme.Stone });
                return list;
            }
        }

        public static void Load()
        {
            if (Loading || sizes != null) return;
            Loading = true;
            Bg.Run(() =>
            {
                long apps = FS.Size(FS.PF) + (FS.PF86 != "" ? FS.Size(FS.PF86) : 0) + FS.Size(Path.Combine(FS.Local, "Programs"));
                long files = new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyVideos,
                                     Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyPictures }.Sum(f => FS.Size(FS.Special(f))) + FS.Size(FS.Downloads);
                long data = Math.Max(0, FS.Size(Path.Combine(FS.Home, "AppData")) - FS.Size(Path.Combine(FS.Local, "Programs")));
                return new[] { apps, files, data };
            }, r =>
            {
                Loading = false; sizes = r;
                App.Invalidate();
            });
        }
    }

    class HomePage : Page
    {
        readonly Grid grid = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        const double Gap = 12;

        public HomePage() { Root = UI.Scroll(grid); }

        public override void Shown()
        {
            var c = App.Cleanup;
            c.RefreshDisk();
            Breakdown.Load();
            if (!c.SuggestScanned && !c.Scanning) c.ScanSuggestions();
            if (!App.Bloat.UsageLoaded && !App.Bloat.LoadingUsage) App.Bloat.LoadUsage();
            App.Ram.Start();
        }
        public override void Hidden() { App.Ram.Stop(); }

        Button Tile(Brush bg, UIElement content, Section go, Action extra = null)
        {
            var b = UI.Clickable(content, () => { if (extra != null) extra(); App.Go(go); }, bg, 18, 16);
            b.BorderThickness = new Thickness(1);
            return b;
        }

        static FrameworkElement Label(string glyph, string text, Brush color)
        {
            return UI.H(UI.Icon(glyph, 14, color), UI.Text(text, 13, color, true, false).M(7, 0, 0, 0));
        }

        static void Place(Grid g, FrameworkElement el, int row, int col, int colSpan = 1)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); Grid.SetColumnSpan(el, colSpan);
            el.Margin = new Thickness(col == 0 ? 0 : Gap / 2, row == 0 ? 0 : Gap, col + colSpan == 3 ? 0 : Gap / 2, 0);
            g.Children.Add(el);
        }

        public override void Refresh()
        {
            var c = App.Cleanup; var d = c.Disk;
            grid.Children.Clear(); grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Clear();
            for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(262) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(182) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(132) });

            double frac = d.Total > 0 ? (double)d.Used / d.Total : 0;
            var header = UI.V(UI.Big(L.T("আপনার PC", "Your PC"), 26),
                UI.Text(frac > 0.85 ? L.T("Disk প্রায় ভরা — নিচে দেখুন কোথায় জায়গা খালি করা যায়", "Disk is almost full — see below where to free space") : L.T("সব ঠিকঠাক চলছে", "Everything looks good"), 13, Theme.Sub).M(0, 2, 0, 0));
            Place(grid, header, 0, 0, 3); header.Margin = new Thickness(0, 0, 0, 4);

            // Disk
            var lilac = Theme.B(Theme.Lilac);
            var bar = new Grid { Height = 12 };
            long shown = 0; int col = 0;
            var parts = Breakdown.Parts;
            foreach (var p in parts.Where(p => p.Size > 0))
            {
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, p.Size), GridUnitType.Star) });
                var seg = new Border { Background = Theme.B(p.Color), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 3, 0), MinWidth = 4 };
                Grid.SetColumn(seg, col++); bar.Children.Add(seg); shown += p.Size;
            }
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, d.Total - shown), GridUnitType.Star) });
            var rest = new Border { Background = Theme.B("#26FFFFFF"), CornerRadius = new CornerRadius(6) };
            Grid.SetColumn(rest, col); bar.Children.Add(rest);

            var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (parts.Count == 0) legend.Children.Add(UI.Text(L.T("কিসে কত জায়গা — মাপা হচ্ছে…", "Measuring what uses space…"), 12, lilac));
            foreach (var p in parts)
                legend.Children.Add(UI.H(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Theme.B(p.Color) }, UI.Text(p.Name + " " + FS.Pretty(p.Size), 12, lilac, false, false).M(5, 0, 14, 0)));

            var diskBody = UI.Rows(null,
                Label(Glyph.Drive, d.Label ?? "", lilac),
                UI.H(UI.Big(FS.Pretty(d.Free), 46, Theme.White), UI.Text(L.T("খালি", "free"), 18, lilac).M(8, 12, 0, 0)).M(0, 0, 0, 0),
                UI.Text(L.T(FS.Pretty(d.Total) + " এর " + (int)(frac * 100) + "% ভরা", (int)(frac * 100) + "% of " + FS.Pretty(d.Total) + " used"), 14, lilac).M(0, 0, 0, 14),
                bar, legend);
            diskBody.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            ((FrameworkElement)diskBody.Children[0]).VerticalAlignment = VerticalAlignment.Top;
            Place(grid, Tile(Theme.B(Theme.Ink), diskBody, Section.Explorer), 1, 0, 2);

            // Smart Clean + one-click
            var smart = c.Smart;
            bool ready = smart.State == SmartModel.Stage.Ready || smart.State == SmartModel.Stage.Done;
            string value = smart.State == SmartModel.Stage.Ready ? FS.Pretty(smart.SelectedSize) : smart.State == SmartModel.Stage.Done ? "✓" : "—";
            var smartBody = UI.Dock(Bottom(UI.V(UI.Big(value, 26, Theme.Safe), UI.Text(ready ? L.T("নিরাপদে মুছা যাবে", "safe to delete") : L.T("এখনো পরীক্ষা হয়নি", "not scanned yet"), 12, Theme.Sub, false, false))),
                null, null);
            var smartGrid = new Grid(); smartGrid.Children.Add(Label(Glyph.Broom, "Smart Clean", Theme.Safe).Top()); smartGrid.Children.Add(smartBody);
            var cleanBody = UI.Center(UI.Icon(smart.State == SmartModel.Stage.Scanning ? Glyph.Sync : Glyph.Broom, 26, Theme.White), UI.Text(L.T("এক click এ Clean", "One-click clean"), 15, Theme.White, true).M(0, 8, 0, 0));
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition());
            var t1 = Tile(Theme.A(Theme.Teal, 0.16), smartGrid, Section.Smart); t1.Margin = new Thickness(0, 0, 0, Gap / 2);
            var t2 = Tile(Theme.B(Theme.Teal), cleanBody, Section.Smart, () => { if (smart.State != SmartModel.Stage.Ready) smart.Scan(); }); t2.Margin = new Thickness(0, Gap / 2, 0, 0);
            Grid.SetRow(t2, 1); right.Children.Add(t1); right.Children.Add(t2);
            Place(grid, right, 1, 2);

            // RAM
            var m = App.Ram.Info;
            double rf = m.Total > 0 ? Math.Min(1, (double)m.Used / m.Total) : 0;
            var rc = Theme.B(m.Pressure >= 4 ? Theme.Red : m.Pressure >= 2 ? Theme.Amber : Theme.Teal);
            var ramBottom = UI.V(UI.H(UI.Big((m.Used / 1073741824.0).ToString("0.0"), 30), UI.Text("/ " + FS.Pretty(m.Total), 13, Theme.Sub).M(6, 8, 0, 0)), UI.Bar(rf, rc).M(0, 6, 0, 0),
                m.SwapUsed > 4L * 1073741824 ? UI.Text(L.T("Page file " + FS.Pretty(m.SwapUsed) + " — Restart দিলে ভালো", "Page file " + FS.Pretty(m.SwapUsed) + " — a restart would help"), 12, Theme.B(Theme.Amber), false, false).M(0, 6, 0, 0) : null);
            var ramGrid = new Grid(); ramGrid.Children.Add(Label(Glyph.Chip, "RAM", Theme.Sub).Top()); ramGrid.Children.Add(Bottom(ramBottom));
            Place(grid, Tile(Theme.Tile, ramGrid, Section.Ram), 2, 0);

            // Taking the most space
            var top = new StackPanel();
            top.Children.Add(Label(Glyph.Bolt, L.T("সবচেয়ে বেশি জায়গা নিচ্ছে", "Taking the most space"), Theme.Sub).M(0, 0, 0, 10));
            if (c.Scanning) top.Children.Add(UI.Busy("").M(0, 30, 0, 0));
            else foreach (var s in c.Suggestions.Where(s => s.Size > 0).OrderByDescending(s => s.Size).Take(3))
            {
                var tint = s.Safe ? Theme.Teal : Theme.Coral;
                var size = UI.Text(FS.Pretty(s.Size), 14, null, true, false).W(84); size.TextAlignment = TextAlignment.Right;
                top.Children.Add(UI.Dock(UI.Text(s.Title, 14, null, false, false).M(10, 0, 10, 0),
                    new FrameworkElement[] { UI.IconBox(s.Icon, Theme.A(tint, 0.16), 28, Theme.B(tint)) },
                    new FrameworkElement[] { UI.Pill(s.Safe), size }).M(0, 0, 0, 9));
            }
            Place(grid, Tile(Theme.Tile, top, Section.Suggestions), 2, 1, 2);

            // Small tiles
            var bloat = App.Bloat;
            Place(grid, Small(Glyph.Box, L.T("অপ্রয়োজনীয়", "Unused"), bloat.LoadingUsage || !bloat.UsageLoaded ? "…" : bloat.Usage.Count(u => (u.Days ?? 9999) >= 90).ToString(), L.T("৩ মাস খোলা হয়নি", "not opened in 3 months"), Section.Unused), 3, 0);
            Place(grid, Small(Glyph.Apps, "Apps", App.Store.Loading ? "…" : App.Store.Apps.Count.ToString(), L.T("Uninstall করতে click করুন", "click to uninstall"), Section.Uninstall), 3, 1);
            Place(grid, Small(Glyph.Copy, L.T("বড় ফাইল", "Big files"), c.BigScanned ? c.BigFiles.Count.ToString() : "—", L.T("500 MB এর বেশি", "over 500 MB"), Section.BigFiles), 3, 2);
        }

        static FrameworkElement Bottom(FrameworkElement el) { el.VerticalAlignment = VerticalAlignment.Bottom; return el; }

        Button Small(string glyph, string label, string value, string sub, Section go)
        {
            var g = new Grid();
            g.Children.Add(Label(glyph, label, Theme.Sub).Top());
            g.Children.Add(Bottom(UI.V(UI.Big(value, 28), UI.Text(sub, 12, Theme.Sub, false, false))));
            return Tile(Theme.Tile, g, go);
        }
    }

    static class HomeExt
    {
        public static T Top<T>(this T el) where T : FrameworkElement { el.VerticalAlignment = VerticalAlignment.Top; return el; }
    }

    class AboutPage : Page
    {
        readonly StackPanel body = new StackPanel { MaxWidth = 620, Margin = new Thickness(32) };
        public AboutPage() { Root = UI.Scroll(body); }

        static FrameworkElement Row(string glyph, string title, string value, FrameworkElement button)
        {
            var v = new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Theme.Clear, Foreground = Theme.Text, FontSize = 13, FontFamily = UI.Font, VerticalAlignment = VerticalAlignment.Center };
            return UI.Dock(v, new FrameworkElement[] { UI.Icon(glyph, 15).W(24), UI.Text(title, 13, Theme.Sub, false, false).W(70).M(8, 0, 0, 0) }, new[] { button }).M(0, 4, 0, 4);
        }

        public override void Refresh()
        {
            body.Children.Clear();
            var logo = new Image { Source = Icons.Logo, Width = 110, Height = 110, HorizontalAlignment = HorizontalAlignment.Center };
            var name = UI.Big("Easy Uninstall", 28); name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(0, 14, 0, 2);
            var ver = UI.Text("Version " + AppInfo.Version + " · Windows", 13, Theme.Sub); ver.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(logo); body.Children.Add(name); body.Children.Add(ver);

            body.Children.Add(UI.Card(UI.V(
                Row(Glyph.Person, "Creator", AppInfo.Creator, null), UI.Line().M(0, 4, 0, 4),
                Row(Glyph.Mail, "Email", AppInfo.Email, UI.Button(L.T("Email করুন", "Send email"), () => Shell.Open("mailto:" + AppInfo.Email + "?subject=Easy%20Uninstall"))), UI.Line().M(0, 4, 0, 4),
                Row(Glyph.Code, "GitHub", AppInfo.Repo, UI.Button(L.T("খুলুন", "Open"), () => Shell.Open(AppInfo.RepoUrl)))), null, 16, 16).M(0, 18, 0, 0));

            string status;
            switch (Updater.State)
            {
                case UpdateState.Checking: status = L.T("খোঁজা হচ্ছে…", "Checking…"); break;
                case UpdateState.UpToDate: status = L.T("আপনার কাছে সবচেয়ে নতুন version আছে ✓", "You're up to date ✓"); break;
                case UpdateState.Available: status = L.T("নতুন version " + Updater.Latest + " পাওয়া গেছে!", "Version " + Updater.Latest + " is available!"); break;
                case UpdateState.Downloading: status = L.T("Download হচ্ছে… শেষ হলে app নিজে আবার খুলবে", "Downloading… the app will reopen by itself"); break;
                case UpdateState.Failed: status = Updater.Error; break;
                default: status = L.T("এখন version " + AppInfo.Version, "You have version " + AppInfo.Version); break;
            }
            Button ub = Updater.State == UpdateState.Available
                ? UI.Button(L.T("এক click এ Update", "Update in one click"), () => Updater.Install(), Theme.Teal)
                : UI.Button(L.T("Update খুঁজুন", "Check for updates"), () => Updater.Check(false));
            ub.IsEnabled = Updater.State != UpdateState.Checking && Updater.State != UpdateState.Downloading;
            var upd = UI.V(UI.Dock(UI.V(UI.Text(L.T("Update", "Updates"), 14, null, true), UI.Text(status, 13, Theme.Sub)).M(12, 0, 12, 0), new FrameworkElement[] { UI.Icon(Glyph.Sync, 20, Theme.B(Theme.Purple)) }, new FrameworkElement[] { ub }),
                Updater.State == UpdateState.Available && Updater.Notes != "" ? UI.Text(Updater.Notes, 12, Theme.Sub).M(0, 10, 0, 0) : null);
            body.Children.Add(UI.Card(upd, null, 16, 16).M(0, 14, 0, 0));

            body.Children.Add(UI.Card(UI.Dock(
                UI.V(UI.Text(L.T("মতামত ও পরামর্শ", "Review and suggestions"), 14, null, true), UI.Text(L.T("আপনার মতামত, সমস্যা বা নতুন idea আমাকে জানান।", "Tell me what you think, report a problem or share an idea."), 13, Theme.Sub)).M(12, 0, 12, 0),
                new FrameworkElement[] { UI.Icon(Glyph.Chat, 20, Theme.Safe) },
                new FrameworkElement[] { UI.Button(L.T("মতামত দিন", "Give feedback"), () => Shell.Open(AppInfo.FeedbackUrl)), UI.Button(L.T("সমস্যা জানান", "Report a bug"), () => Shell.Open(AppInfo.IssuesUrl)).M(8, 0, 0, 0) }), null, 16, 16).M(0, 14, 0, 0));

            var made = UI.Text("Made with ♥ by " + AppInfo.Creator, 12, Theme.Sub); made.HorizontalAlignment = HorizontalAlignment.Center; made.Margin = new Thickness(0, 18, 0, 0);
            body.Children.Add(made);
        }
    }
}
