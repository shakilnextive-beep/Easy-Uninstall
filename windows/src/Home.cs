// Home page: this PC with disk and RAM, quick tiles on the right, and the app picker below.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace EasyUninstall
{
    class HomePage : Page
    {
        readonly ContentControl header = Views.Holder(), tiles = Views.Holder();
        readonly AppsPanel apps = new AppsPanel(true);
        readonly Popup ramPopup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 6 };
        const double Gap = 12;

        public HomePage()
        {
            var appsTile = new Border { Background = Theme.Tile, CornerRadius = new CornerRadius(18), BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(16), Height = 430, Margin = new Thickness(0, Gap, 0, 0), Child = apps.Root };
            tiles.Height = 340;
            var stack = UI.V(header, tiles, appsTile);
            stack.Margin = new Thickness(24, 20, 24, 24);
            Root = UI.Scroll(stack);
            ramPopup.Closed += (s, e) => Refresh();
        }

        public override void Shown()
        {
            var c = App.Cleanup;
            c.RefreshDisk();
            Breakdown.Load();
            if (!c.SuggestScanned && !c.Scanning) c.ScanSuggestions();
            if (!App.Bloat.UsageLoaded && !App.Bloat.LoadingUsage) App.Bloat.LoadUsage();
            App.Ram.Start();
        }
        public override void Hidden() { App.Ram.Stop(); ramPopup.IsOpen = false; }

        static Button Tile(Brush bg, UIElement content, Section go, double pad = 16)
        {
            var b = UI.Clickable(content, () => App.Go(go), bg, 18, pad);
            b.BorderThickness = new Thickness(1);
            return b;
        }

        static FrameworkElement Label(string glyph, string text, Brush color)
        {
            return UI.H(UI.Icon(glyph, 14, color), UI.Text(text, 13, color, true, false).M(7, 0, 0, 0));
        }

        static Color RamColor(MemInfo m) { return m.Pressure >= 4 ? Theme.Red : m.Pressure >= 2 ? Theme.Amber : Theme.Teal; }

        /// RAM pill in the disk tile: click to see apps, one button to clean.
        FrameworkElement RamChip()
        {
            var ram = App.Ram; var m = ram.Info;
            double frac = m.Total > 0 ? Math.Min(1, (double)m.Used / m.Total) : 0;
            var color = RamColor(m); var lilac = Theme.B(Theme.Lilac);
            var info = UI.H(UI.Icon(Glyph.Chip, 16, Theme.B(color)).M(0, 0, 10, 0),
                UI.V(UI.H(UI.Text("RAM", 12, Theme.White, true, false), UI.Text((m.Used / 1073741824.0).ToString("0.0") + " / " + FS.Pretty(m.Total), 12, lilac, false, false).M(5, 0, 0, 0)),
                     UI.Bar(frac, Theme.B(color), 5, Theme.B("#26FFFFFF")).W(124).M(0, 4, 0, 0)),
                UI.Icon(Glyph.Down, 9, lilac).M(10, 0, 0, 0));
            var open = UI.Clickable(info, () => { ramPopup.IsOpen = !ramPopup.IsOpen; if (ramPopup.IsOpen) FillPopup(); }, Theme.Clear, 16, 0);
            open.Padding = new Thickness(12, 6, 8, 6); open.ToolTip = L.T("কোন app কত RAM নিচ্ছে দেখুন", "See which app uses how much RAM");
            var clean = UI.Clickable(UI.H(UI.Icon(Glyph.Broom, 12, Theme.White), UI.Text("Clean", 12, Theme.White, true, false).M(6, 0, 0, 0)), () => ram.CleanSafe(), Theme.B(color), 13, 0);
            clean.Padding = new Thickness(11, 5, 12, 6); clean.Margin = new Thickness(2, 0, 5, 0); clean.VerticalAlignment = VerticalAlignment.Center;
            clean.ToolTip = L.T("নিরাপদ (সবুজ) app গুলো বন্ধ করে RAM খালি করুন", "Free RAM by quitting safe (green) apps");
            var pill = new Border { Background = Theme.B("#1AFFFFFF"), BorderBrush = Theme.B("#1AFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Child = UI.H(open, clean),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            ramPopup.PlacementTarget = pill;
            return pill;
        }

        void FillPopup()
        {
            var ram = App.Ram;
            var p = new StackPanel { Width = 380 };
            p.Children.Add(UI.Dock(new Border(), new FrameworkElement[] { UI.Text(L.T("RAM কে কতটুকু নিচ্ছে", "What's using RAM"), 14, null, true, false) },
                new FrameworkElement[] { UI.Text(FS.Pretty(ram.Info.Used) + " / " + FS.Pretty(ram.Info.Total), 12, Theme.Sub, false, false) }).M(0, 0, 0, 8));
            if (!ram.Loaded) p.Children.Add(UI.Busy("").M(0, 10, 0, 10));
            foreach (var app in ram.Apps.Take(8))
            {
                var a = app;
                var quit = UI.Button(L.T("বন্ধ", "Quit"), () => ram.AskQuit(a)); quit.Padding = new Thickness(9, 3, 9, 4);
                var row = UI.Dock(UI.Text(a.Name, 13, null, false, false).M(8, 0, 8, 0),
                    new FrameworkElement[] { UI.Dot(a.Caution ? Theme.Risk : Theme.Safe).M(0, 0, 8, 0), UI.FileIcon(a.ExePath, 22) },
                    new FrameworkElement[] { UI.Text(FS.Pretty(a.Memory), 13, null, true, false).M(0, 0, 10, 0), quit }).M(0, 3, 0, 3);
                row.ToolTip = a.Note;
                p.Children.Add(row);
            }
            p.Children.Add(UI.Line().M(0, 8, 0, 10));
            p.Children.Add(UI.Dock(new Border(), new FrameworkElement[] { UI.Button(L.T("সবুজ app বন্ধ করে Clean", "Clean: quit green apps"), () => ram.CleanSafe(), Theme.Teal, Glyph.Broom) },
                new FrameworkElement[] { UI.Link(L.T("সব দেখুন →", "See all →"), () => { ramPopup.IsOpen = false; App.Go(Section.Ram); }) }));
            p.Children.Add(UI.H(UI.Dot(Theme.Safe), UI.Text(L.T("নিরাপদে বন্ধ করা যায়", "safe to quit"), 12, Theme.Sub, false, false).M(6, 0, 14, 0),
                UI.Dot(Theme.Risk), UI.Text(L.T("কাজের app, আগে save করুন", "work app, save first"), 12, Theme.Sub, false, false).M(6, 0, 0, 0)).M(0, 10, 0, 0));
            ramPopup.Child = new Border { Background = Theme.B(Theme.Dark ? "#2B2A28" : "#FFFFFF"), BorderBrush = Theme.B(Theme.Dark ? "#4A4844" : "#D9D9DE"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Child = p };
        }

        FrameworkElement DeviceBadge()
        {
            var lilac = Theme.B(Theme.Lilac);
            var pic = new Border { Width = 96, Height = 80, CornerRadius = new CornerRadius(14), Background = Theme.B("#14FFFFFF"), Child = UI.Icon(DeviceInfo.IsLaptop ? Glyph.Laptop : Glyph.Pc, 46, lilac) };
            return UI.H(pic, UI.V(UI.Big(DeviceInfo.Name, 18, Theme.White), UI.Text(DeviceInfo.Chip, 13, lilac, false, false).M(0, 2, 0, 1),
                UI.Text(FS.Pretty(App.Ram.Info.Total) + " RAM · " + DeviceInfo.Os, 12, Theme.A(Theme.Lilac, 0.8), false, false)).M(16, 0, 0, 0));
        }

        FrameworkElement DiskTile()
        {
            var d = App.Cleanup.Disk;
            double frac = d.Total > 0 ? (double)d.Used / d.Total : 0;
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

            var top = new Grid();
            top.Children.Add(Label(Glyph.Drive, d.Label ?? "", lilac).Top());
            top.Children.Add(RamChip());
            var spacer = new Border();
            return UI.Rows(spacer, top, DeviceBadge().M(0, 4, 0, 0), spacer,
                UI.H(UI.Big(FS.Pretty(d.Free), 46, Theme.White), UI.Text(L.T("খালি", "free"), 18, lilac).M(8, 12, 0, 0)),
                UI.Text(L.T(FS.Pretty(d.Total) + " এর " + (int)(frac * 100) + "% ভরা", (int)(frac * 100) + "% of " + FS.Pretty(d.Total) + " used"), 14, lilac).M(0, 0, 0, 14),
                bar, legend);
        }

        FrameworkElement SmartTile()
        {
            var smart = App.Cleanup.Smart;
            string value = smart.State == SmartModel.Stage.Ready ? FS.Pretty(smart.SelectedSize) : smart.State == SmartModel.Stage.Done ? "✓" : "—";
            string sub = smart.State == SmartModel.Stage.Ready ? L.T("নিরাপদে মুছা যাবে", "safe to delete")
                : smart.State == SmartModel.Stage.Done ? L.T("পরিষ্কার হয়ে গেছে", "cleaned")
                : smart.State == SmartModel.Stage.Scanning ? L.T("পরীক্ষা হচ্ছে…", "Scanning…") : L.T("এখনো পরীক্ষা হয়নি", "not scanned yet");
            var go = UI.Button(L.T("এক click এ Clean", "One-click clean"), () => { if (smart.State != SmartModel.Stage.Ready) smart.Scan(); App.Go(Section.Smart); }, Theme.Teal, smart.State == SmartModel.Stage.Scanning ? Glyph.Sync : Glyph.Broom);
            go.HorizontalAlignment = HorizontalAlignment.Stretch; go.VerticalAlignment = VerticalAlignment.Bottom;
            var spacer = new Border();
            var body = UI.Rows(spacer, UI.Dock(new Border(), new FrameworkElement[] { Label(Glyph.Broom, "Smart Clean", Theme.Safe) }, new FrameworkElement[] { UI.Big(value, 18, Theme.Safe) }),
                UI.Text(sub, 12, Theme.Sub, false, false).M(0, 2, 0, 0), spacer, go);
            return Tile(Theme.A(Theme.Teal, 0.16), body, Section.Smart, 12);
        }

        FrameworkElement TopTile()
        {
            var c = App.Cleanup;
            var top = new StackPanel();
            top.Children.Add(Label(Glyph.Bolt, L.T("সবচেয়ে বেশি জায়গা নিচ্ছে", "Taking the most space"), Theme.Sub).M(0, 0, 0, 8));
            if (c.Scanning) top.Children.Add(UI.Busy("").M(0, 22, 0, 0));
            else foreach (var s in c.Suggestions.Where(s => s.Size > 0).OrderByDescending(s => s.Size).Take(3))
            {
                var tint = s.Safe ? Theme.Teal : Theme.Coral;
                string title = s.Title.Replace("পুরনো version: ", "পুরনো ").Replace("Old version: ", "Old ");
                var row = UI.Dock(UI.Text(title, 13, null, false, false).M(8, 0, 6, 0),
                    new FrameworkElement[] { UI.IconBox(s.Icon, Theme.A(tint, 0.16), 22, Theme.B(tint)) },
                    new FrameworkElement[] { UI.Text(FS.Pretty(s.Size), 13, s.Safe ? Theme.Safe : Theme.B(Theme.Dark ? Theme.Peach : Theme.Coral), true, false) }).M(0, 0, 0, 7);
                row.ToolTip = s.Safe ? L.T("নিরাপদ", "Safe") : L.T("সাবধান", "Careful");
                top.Children.Add(row);
            }
            return Tile(Theme.Tile, top, Section.Suggestions, 12);
        }

        static FrameworkElement Small(string glyph, string label, string value, string tip, Section go)
        {
            var spacer = new Border();
            var b = Tile(Theme.Tile, UI.Rows(spacer, Label(glyph, label, Theme.Sub), spacer, UI.Big(value, 20)), go, 12);
            b.ToolTip = tip;
            return b;
        }

        static void Put(Grid g, FrameworkElement el, int row, int col, Thickness margin)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); el.Margin = margin; g.Children.Add(el);
        }

        public override void Refresh()
        {
            apps.Refresh();
            if (ramPopup.IsOpen) { FillPopup(); return; }   // keep the page still while the RAM list is open

            var c = App.Cleanup; var d = c.Disk;
            double frac = d.Total > 0 ? (double)d.Used / d.Total : 0;
            header.Content = UI.V(UI.Big(L.T("আপনার PC", "Your PC"), 26),
                UI.Text(frac > 0.85 ? L.T("Disk প্রায় ভরা — নিচে দেখুন কোথায় জায়গা খালি করা যায়", "Disk is almost full — see below where to free space") : L.T("সব ঠিকঠাক চলছে", "Everything looks good"), 13, Theme.Sub).M(0, 2, 0, 0)).M(0, 0, 0, 12);

            var g = new Grid();
            for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
            var disk = Tile(Theme.B(Theme.Ink), DiskTile(), Section.Explorer);
            Grid.SetColumnSpan(disk, 2); Put(g, disk, 0, 0, new Thickness(0, 0, Gap / 2, 0));

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(104) });
            right.RowDefinitions.Add(new RowDefinition());
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68 + Gap) });
            Put(right, SmartTile(), 0, 0, new Thickness(0));
            Put(right, TopTile(), 1, 0, new Thickness(0, Gap, 0, 0));
            var small = new Grid(); small.ColumnDefinitions.Add(new ColumnDefinition()); small.ColumnDefinitions.Add(new ColumnDefinition());
            var bloat = App.Bloat;
            Put(small, Small(Glyph.Box, L.T("অপ্রয়োজনীয়", "Unused"), bloat.LoadingUsage || !bloat.UsageLoaded ? "…" : bloat.Usage.Count(u => (u.Days ?? 9999) >= 90).ToString(), L.T("৩ মাস খোলা হয়নি", "not opened in 3 months"), Section.Unused), 0, 0, new Thickness(0, 0, Gap / 2, 0));
            Put(small, Small(Glyph.Copy, L.T("বড় ফাইল", "Big files"), c.BigScanned ? c.BigFiles.Count.ToString() : "—", L.T("500 MB এর বেশি", "over 500 MB"), Section.BigFiles), 0, 1, new Thickness(Gap / 2, 0, 0, 0));
            Put(right, small, 2, 0, new Thickness(0, Gap, 0, 0));
            Put(g, right, 0, 2, new Thickness(Gap / 2, 0, 0, 0));
            tiles.Content = g;
        }
    }
}
