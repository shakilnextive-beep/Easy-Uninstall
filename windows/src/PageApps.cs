// Apps pages: Uninstall (list + the uninstall window), Unused software / startup items, and RAM.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyUninstall
{
    class UninstallPage : Page
    {
        readonly ContentControl body = Views.Holder(), footer = Views.Holder();
        readonly ScrollList list = new ScrollList();
        string query = "";

        public UninstallPage()
        {
            var search = UI.Search(L.T("Software এর নাম লিখুন…", "Type a software name…"), q => { query = q; Refresh(); }, 17);
            var top = UI.Dock(search, null, new FrameworkElement[] { UI.IconButton(Glyph.Refresh, () => App.Store.Reload(), L.T("আবার খুঁজুন", "Search again")).M(8, 0, 0, 0) });
            var g = UI.Rows(body, top, body, footer);
            body.Margin = new Thickness(0, 10, 0, 8); g.Margin = new Thickness(16);
            Root = g;
        }

        public override void Refresh()
        {
            var store = App.Store;
            var shown = store.Apps.Where(a => Kinds.Matches(query, a.Name, a.Publisher)).ToList();
            if (store.Loading && store.Apps.Count == 0) body.Content = UI.Busy(L.T("Apps খোঁজা হচ্ছে…", "Looking for apps…"));
            else if (shown.Count == 0) body.Content = UI.Center(UI.Text(L.T("কোন software পাওয়া যায়নি", "No software found"), 13, Theme.Sub));
            else
            {
                list.Clear();
                foreach (var app in shown)
                {
                    var a = app;
                    string sub = string.Join("  ·  ", new[] { a.Publisher, a.Version }.Where(s => s != ""));
                    var row = UI.Dock(UI.V(UI.Text(a.Name, 14, null, true, false), sub == "" ? null : UI.Text(sub, 12, Theme.Sub, false, false)).M(12, 0, 10, 0),
                        new FrameworkElement[] { UI.FileIcon(a.IconPath, 32) },
                        new FrameworkElement[] { a.IsProtected ? UI.Badge("System", Theme.Gray).M(0, 0, 10, 0) : null,
                            a.Size >= 0 ? UI.Text(FS.Pretty(a.Size), 12, Theme.Sub, false, false) : null, UI.Icon(Glyph.Right, 10, Theme.Faint).M(10, 0, 0, 0) });
                    var b = UI.Clickable(row, () => App.Uninstall(a), Theme.Clear, 8, 0); b.Padding = new Thickness(10, 7, 10, 7);
                    list.Add(b);
                }
                body.Content = list.Take();
            }
            var hint = UI.Text(L.T("মুছে ফেলা ফাইল Recycle Bin এ যায় — দরকার হলে ফেরত আনতে পারবেন", "Removed files go to the Recycle Bin — you can get them back if needed"), 12, Theme.Sub, false, false);
            hint.TextAlignment = TextAlignment.Right; hint.Margin = new Thickness(16, 0, 4, 0);
            footer.Content = UI.Dock(hint, new FrameworkElement[] { UI.Text(L.T(shown.Count + " টি software", shown.Count + " apps"), 12, Theme.Sub, false, false) });
        }
    }

    /// The window that walks through uninstalling one program.
    class UninstallDialog : Window
    {
        readonly InstalledApp app;
        readonly ContentControl body = new ContentControl { Focusable = false, Margin = new Thickness(0, 16, 0, 0) };
        List<Leftover> leftovers = new List<Leftover>();
        DispatcherTimer poll;
        bool removed, entryBroken;

        public UninstallDialog(InstalledApp a)
        {
            app = a;
            Title = "Easy Uninstall"; Width = 620; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Background = Theme.Window; FontFamily = UI.Font; UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = App.Window;
            App.DarkTitleBar(this);
            string sub = string.Join("  ·  ", new[] { a.Version == "" ? "" : "Version " + a.Version, a.Dir }.Where(s => s != ""));
            var head = UI.Dock(UI.V(UI.Text(a.Name, 19, null, true), UI.Text(sub, 12, Theme.Sub, false, false)).M(14, 0, 0, 0), new FrameworkElement[] { UI.FileIcon(a.IconPath, 48) });
            Content = new Border { Padding = new Thickness(24), Child = UI.V(head, body) };
            Closed += (s, e) => { if (poll != null) poll.Stop(); };
            Choose();
        }

        /// For UI checks only: shows what Full Uninstall would list. Deletes nothing.
        public void PreviewLeftovers() { Scan(); }

        /// Shows the window; true when the program was removed.
        public bool Run() { ShowDialog(); return removed || !app.StillInstalled(); }

        FrameworkElement Buttons(params Button[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            foreach (var b in items) { b.Margin = new Thickness(8, 0, 0, 0); p.Children.Add(b); }
            return p;
        }
        Button CancelButton(string text = null) { var b = UI.Button(text ?? L.T("বাতিল", "Cancel"), () => Close()); b.IsCancel = true; return b; }

        FrameworkElement Option(string glyph, Color color, string title, string subtitle, Action act)
        {
            var row = UI.Dock(UI.V(UI.Text(title, 14, null, true), UI.Text(subtitle, 12, Theme.Sub)).M(14, 0, 0, 0), new FrameworkElement[] { UI.IconBox(glyph, Theme.B(color), 44) });
            return UI.Clickable(row, act, Theme.Tile, 12, 12).M(0, 0, 0, 10);
        }

        void Choose()
        {
            if (app.IsProtected)
            {
                body.Content = UI.V(UI.H(UI.Icon(Glyph.Lock, 16, Theme.B(Theme.Amber)), UI.Text(L.T("এটি Windows এর অংশ বা অন্য software এর দরকারি অংশ — নিরাপত্তার জন্য মুছা বন্ধ রাখা হয়েছে।", "This is part of Windows or something other software needs — deleting is blocked for safety."), 13, Theme.B(Theme.Amber)).M(8, 0, 0, 0).W(520)),
                    Buttons(CancelButton(L.T("বন্ধ করুন", "Close"))));
                return;
            }
            body.Content = UI.V(
                Option(Glyph.Trash, Theme.Blue, L.T("শুধু Uninstall", "Only uninstall"), L.T("Software টির নিজের uninstaller চালাবে। Settings ও data থেকে যেতে পারে।", "Runs the software's own uninstaller. Settings and data may stay."), () => Start(false)),
                Option(Glyph.Bolt, Theme.Teal, "Full Uninstall", L.T("Uninstaller চালানোর পর ফেলে যাওয়া ফোল্ডার, AppData, shortcut ও registry settings খুঁজে তালিকা দেখাবে — তারপর সব মুছে দিবে।", "After the uninstaller finishes, finds the folders, AppData, shortcuts and registry settings it left behind, shows you the list — then deletes them all."), () => Start(true)),
                Buttons(CancelButton()));
        }

        void Start(bool full)
        {
            Bg.Run(() => { Uninstaller.CloseIfRunning(app); return Uninstaller.Start(app); }, err =>
            {
                if (err != null)
                {
                    entryBroken = true;
                    body.Content = UI.V(UI.Text(L.T("Uninstaller চালানো যায়নি: ", "Couldn't start the uninstaller: ") + err, 13, Theme.Risk),
                        UI.Text(L.T("Software টি হয়তো আগেই হাতে মুছে ফেলা হয়েছে। চাইলে এর ফেলে যাওয়া ফাইল খুঁজে মুছতে পারেন, আর Windows এর তালিকা থেকেও নামটা সরিয়ে দিতে পারেন।", "The software may already have been deleted by hand. You can find and remove the files it left behind, and take its name off the Windows list too."), 13, Theme.Sub).M(0, 8, 0, 0),
                        Buttons(CancelButton(), UI.Button(L.T("ফেলে যাওয়া ফাইল খুঁজুন", "Find leftover files"), () => Scan(), Theme.Teal)));
                    return;
                }
                body.Content = UI.V(UI.Busy(L.T("Uninstaller খোলা হয়েছে — সেখানে ধাপগুলো শেষ করুন।\nশেষ হলে এই window নিজে থেকে এগিয়ে যাবে।", "The uninstaller is open — finish its steps there.\nThis window moves on by itself when it's done.")).M(0, 10, 0, 0),
                    Buttons(CancelButton(L.T("বন্ধ করুন", "Close"))));
                int gone = 0;
                poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                poll.Tick += (s, e) =>
                {
                    if (app.StillInstalled()) { gone = 0; return; }
                    if (++gone < 3) return;                 // let the uninstaller finish deleting its files
                    poll.Stop(); removed = true;
                    if (full) Scan();
                    else Finish(L.T("✓ " + app.Name + " uninstall হয়ে গেছে।", "✓ " + app.Name + " was uninstalled."));
                };
                poll.Start();
            });
        }

        void Scan()
        {
            body.Content = UI.Busy(L.T("সব related ফাইল খোঁজা হচ্ছে…", "Finding all related files…")).M(0, 10, 0, 10);
            Bg.Run(() => Leftovers.Find(app, InstalledApp.LoadAll()), found => { leftovers = found ?? new List<Leftover>(); Review(); });
        }

        void Review()
        {
            var total = UI.Text("", 14, null, true, false);
            Action update = () => total.Text = L.T("মোট খালি হবে: ", "Total to free: ") + FS.Pretty(leftovers.Where(l => l.Selected).Sum(l => l.Size));
            update();
            var panel = new StackPanel();
            panel.Children.Add(UI.Text(leftovers.Count == 0
                ? L.T("কোন ফেলে যাওয়া ফাইল পাওয়া যায়নি — সব পরিষ্কার।", "No leftover files found — all clean.")
                : L.T("এই " + leftovers.Count + " টি related ফাইল/ফোল্ডার পাওয়া গেছে। যেগুলো রাখতে চান সেগুলোর টিক তুলে দিন:", "Found " + leftovers.Count + " related files/folders. Untick the ones you want to keep:"), 13));
            if (leftovers.Count > 0)
            {
                var rows = new StackPanel();
                foreach (var item in leftovers)
                {
                    var l = item;
                    var path = UI.Text(l.Path, 12, null, false, false); path.FontFamily = UI.Mono; path.ToolTip = l.Path;
                    rows.Children.Add(UI.Dock(path.M(8, 0, 10, 0), new FrameworkElement[] { UI.Check(l.Selected, on => { l.Selected = on; update(); }) },
                        new FrameworkElement[] { UI.Text(l.IsRegistry ? "registry" : FS.Pretty(l.Size), 12, Theme.Sub, false, false) }).M(8, 5, 8, 5));
                }
                panel.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Height = 240, Margin = new Thickness(0, 10, 0, 0), Child = UI.Scroll(rows) });
                panel.Children.Add(UI.Text(L.T("ফাইল Recycle Bin এ যাবে। Registry settings মুছার আগে backup রাখা হবে (AppData\\Local\\EasyUninstall)।", "Files go to the Recycle Bin. Registry settings are backed up before deleting (AppData\\Local\\EasyUninstall)."), 12, Theme.Sub).M(0, 8, 0, 0));
            }
            Button go = leftovers.Count == 0 && !entryBroken
                ? UI.Button(L.T("ঠিক আছে", "OK"), () => Close(), Theme.Purple)
                : UI.Button(leftovers.Count == 0 ? L.T("তালিকা থেকে সরান", "Remove from list") : L.T("সব মুছে ফেলুন", "Delete everything"), () => RemoveAll(), Theme.Red);
            go.IsDefault = true;
            panel.Children.Add(UI.Dock(new Border(), new FrameworkElement[] { leftovers.Count > 0 ? total : null }, new FrameworkElement[] { Buttons(CancelButton(), go) }));
            body.Content = panel;
        }

        void RemoveAll()
        {
            body.Content = UI.Busy(L.T("মুছে ফেলা হচ্ছে… (Windows অনুমতি চাইলে Yes দিন)", "Removing… (click Yes if Windows asks for permission)")).M(0, 10, 0, 10);
            var chosen = leftovers.Where(l => l.Selected).ToList();
            Bg.Run(() =>
            {
                var failed = Leftovers.Remove(chosen);
                if (entryBroken && app.StillInstalled()) RemoveEntry();
                return failed;
            }, failed =>
            {
                removed = !app.StillInstalled();
                if (failed == null || failed.Count == 0)
                    Finish(L.T("✓ " + app.Name + " এবং এর সব ফাইল মুছে Recycle Bin এ পাঠানো হয়েছে। Recycle Bin খালি করলে স্থায়ীভাবে মুছে যাবে।", "✓ " + app.Name + " and all its files were removed and sent to the Recycle Bin. Empty the Recycle Bin to delete them for good."));
                else Finish(L.T("⚠ কিছু ফাইল মুছা যায়নি (অনুমতি দেননি বা ফাইল ব্যবহার হচ্ছে):\n", "⚠ Some files couldn't be deleted (permission refused or file in use):\n") + string.Join("\n", failed));
            });
        }

        /// Takes a dead entry off the Windows "installed apps" list (its uninstaller no longer exists).
        void RemoveEntry()
        {
            string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + app.KeyName;
            try
            {
                if (app.Hive == Microsoft.Win32.RegistryHive.CurrentUser) Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false);
                else Shell.RunElevated("reg.exe", "delete \"HKLM\\" + key + "\" /f " + (app.View == Microsoft.Win32.RegistryView.Registry32 ? "/reg:32" : "/reg:64"));
            }
            catch { }
        }

        void Finish(string msg)
        {
            var ok = UI.Button(L.T("ঠিক আছে", "OK"), () => Close(), Theme.Purple); ok.IsDefault = true; ok.IsCancel = true;
            body.Content = UI.V(UI.Text(msg, 13), Buttons(ok));
        }
    }

    class BloatPage : Page
    {
        readonly ContentControl tabs = Views.Holder(), body = Views.Holder();
        readonly ScrollList list = new ScrollList();
        readonly FilterBar filter;

        public BloatPage()
        {
            filter = new FilterBar(() => Refresh());
            var g = UI.Rows(body, tabs, filter.Root, body);
            filter.Root.Margin = new Thickness(0, 12, 0, 0); body.Margin = new Thickness(0, 10, 0, 0); g.Margin = new Thickness(16);
            Root = g;
        }

        public override void Shown()
        {
            var m = App.Bloat;
            if (!m.UsageLoaded && !m.LoadingUsage) m.LoadUsage();
            if (!m.ItemsLoaded && !m.LoadingItems) m.LoadItems();
        }

        public override void Refresh()
        {
            var m = App.Bloat;
            tabs.Content = UI.Segmented(new[] { L.T("ব্যবহার হয় না এমন app", "Unused apps"), L.T("Background এ চলে", "Runs in background"), L.T("Windows built-in", "Windows built-in") }, m.Tab, i => { m.Tab = i; Refresh(); });
            filter.Root.Visibility = m.Tab == 2 || (m.Tab == 0 && m.LoadingUsage) || (m.Tab == 1 && m.LoadingItems) ? Visibility.Collapsed : Visibility.Visible;
            if (m.Tab == 0) Unused(m); else if (m.Tab == 1) Background(m); else BuiltIn();
        }

        void Unused(BloatModel m)
        {
            if (m.LoadingUsage) { body.Content = UI.Busy(L.T("কোন app কবে ব্যবহার করেছেন দেখা হচ্ছে…", "Checking when you last used each app…")); return; }
            var all = m.Unused.Where(u => Kinds.Matches(filter.Query, u.App.Name, u.App.Publisher)).ToList();
            Func<AppUsage, bool> isSafe = u => !u.App.IsAdobe;
            filter.SetCounts(all.Count(isSafe), all.Count(u => !isSafe(u)));
            var shown = all.Where(u => filter.Allows(isSafe(u))).ToList();

            int[] days = { 30, 90, 180, 365 };
            var pick = UI.Segmented(new[] { L.T("১ মাস", "1 month"), L.T("৩ মাস", "3 months"), L.T("৬ মাস", "6 months"), L.T("১ বছর", "1 year") }, Array.IndexOf(days, m.MinDays), i => { m.MinDays = days[i]; Refresh(); });
            var head = UI.Dock(new Border(), new FrameworkElement[] { UI.Text(L.T("যেগুলো", "Apps not opened in"), 13, Theme.Sub, false, false), pick.M(8, 0, 8, 0),
                UI.Text(L.T("বা তার বেশি সময় খোলেননি: " + shown.Count + " টি · মোট " + FS.Pretty(shown.Sum(u => u.Size)), "or more: " + shown.Count + " · total " + FS.Pretty(shown.Sum(u => u.Size))), 13, null, true, false) },
                new FrameworkElement[] { UI.IconButton(Glyph.Refresh, () => m.LoadUsage(), L.T("আবার দেখুন", "Check again")) });

            list.Clear();
            foreach (var usage in shown)
            {
                var u = usage; bool safe = isSafe(u);
                string when;
                if (!u.Days.HasValue) when = L.T("খোলার কোন রেকর্ড পাওয়া যায়নি", "No record of being opened");
                else
                {
                    int d = u.Days.Value;
                    string t = d >= 365 ? L.T((d / 365) + " বছর", (d / 365) + " yr") : d >= 30 ? L.T((d / 30) + " মাস", (d / 30) + " mo") : L.T(d + " দিন", d + " days");
                    when = L.T("শেষ ব্যবহার: " + t + " আগে", "Last used: " + t + " ago") + (u.Estimated ? L.T(" (আনুমানিক)", " (estimate)") : "");
                }
                if (!safe) when += L.T(" · অন্য Adobe app এটা ভিতরে ব্যবহার করতে পারে (যেমন Premiere export এ Media Encoder)", " · Other Adobe apps may use it internally (e.g. Media Encoder for Premiere exports)");
                list.Add(ScrollList.Row(UI.Dock(UI.V(UI.Text(u.App.Name, 14, null, true, false), UI.Text(when, 12, safe ? Theme.Safe : Theme.Risk)).M(12, 0, 10, 0),
                    new FrameworkElement[] { UI.FileIcon(u.App.IconPath, 32) },
                    new FrameworkElement[] { UI.Text(FS.Pretty(u.Size), 13, null, true, false).M(0, 0, 12, 0), UI.Button("Uninstall", () => App.Uninstall(u.App), safe ? Theme.Teal : Theme.Red) })));
            }
            if (shown.Count == 0) list.Add(UI.Center(UI.Text(m.Unused.Count == 0 ? L.T("এমন কোন app নেই — সব app ই ব্যবহার করছেন।", "Nothing here — you use all your apps.") : L.T("এই filter এ কিছু পাওয়া যায়নি", "Nothing matches this filter"), 13, Theme.Sub)).M(0, 30, 0, 0));
            var view = list.Take();
            body.Content = UI.Rows(view, UI.RiskLegend(), head.M(0, 8, 0, 6), view,
                UI.Text(L.T("\"রেকর্ড পাওয়া যায়নি\" মানে Windows এর কাছে খোলার কোন হিসাব নেই (অন্য app এর ভিতর থেকে চালু হলে হিসাব থাকে না) — মুছার আগে নিজে একবার ভেবে নিন।", "\"No record\" means Windows has no history of it being opened (none is kept when another app starts it) — think it over before deleting."), 12, Theme.Sub).M(0, 6, 0, 0));
        }

        void Background(BloatModel m)
        {
            if (m.LoadingItems) { body.Content = UI.Busy(""); return; }
            var all = m.Items.Where(i => Kinds.Matches(filter.Query, i.Owner, i.Name, i.Note)).ToList();
            filter.SetCounts(all.Count(i => !i.Caution), all.Count(i => i.Caution));
            var head = UI.Dock(UI.Text(L.T("এগুলো PC চালু হলেই নিজে থেকে background এ চলে — RAM, battery ও internet খরচ করে, PC চালু হতেও দেরি করায়। বন্ধ করলে app গুলো ঠিকই চলবে। যেকোন সময় আবার চালু করতে পারবেন।", "These start on their own whenever the PC starts — using RAM, battery and internet, and slowing startup. The apps still work if you turn these off, and you can turn them back on any time."), 12, Theme.Sub).M(0, 0, 10, 0),
                null, new FrameworkElement[] { UI.Button(L.T("Startup apps খুলুন", "Open Startup apps"), () => Shell.Open("ms-settings:startupapps")), UI.IconButton(Glyph.Refresh, () => m.LoadItems(), L.T("আবার দেখুন", "Check again")).M(6, 0, 0, 0) });

            list.Clear();
            foreach (var item in all.Where(i => filter.Allows(!i.Caution)))
            {
                var i = item;
                var color = i.Disabled && !i.Orphan ? Theme.Gray : i.Caution ? Theme.Red : Theme.Teal;
                var title = UI.H(UI.Text(i.Owner, 14, null, true, false).M(0, 0, 8, 0),
                    i.Orphan ? UI.Badge(L.T("অবশিষ্ট — app টি আর নেই", "Leftover — app is gone"), Theme.Teal) : i.Disabled ? UI.Badge(L.T("বন্ধ", "Off"), Theme.Gray) : i.Running ? UI.Badge(L.T("এখন চলছে", "Running"), Theme.Blue) : null,
                    i.NeedsAdmin ? UI.Badge(L.T("অনুমতি লাগবে", "Needs permission"), Theme.Gray).M(6, 0, 0, 0) : null);
                var cmd = UI.Text(i.Name, 11, Theme.Faint, false, false); cmd.FontFamily = UI.Mono; cmd.ToolTip = i.Command;
                Button act;
                if (i.Orphan) act = UI.Button(L.T("মুছে ফেলুন", "Delete"), () =>
                {
                    if (!Dialogs.Confirm(L.T("অবশিষ্ট startup item মুছবেন?", "Delete leftover startup item?"), L.T(i.Name + "\n\nএটা যে program চালাত সেটা আর এই PC তে নেই, তাই এটা কোন কাজে লাগে না।", i.Name + "\n\nThe program it started is no longer on this PC, so it does nothing."), L.T("মুছে ফেলুন", "Delete"))) return;
                    StartupScanner.Remove(i); m.LoadItems();
                }, Theme.Teal);
                else if (i.Disabled) act = UI.Button(L.T("চালু করুন", "Turn on"), () => { StartupScanner.SetEnabled(i, true); m.LoadItems(); });
                else act = UI.Button(L.T("বন্ধ করুন", "Turn off"), () =>
                {
                    if (i.Caution && !Dialogs.Confirm(L.T(i.Owner + " এর এই অংশ বন্ধ করবেন?", "Turn off this part of " + i.Owner + "?"), i.Note, L.T("বন্ধ করুন", "Turn off"), true)) return;
                    StartupScanner.SetEnabled(i, false); m.LoadItems();
                }, i.Caution ? Theme.Red : Theme.Teal);
                string note = i.Orphan ? L.T("এই item যে program চালাত সেটা মুছে ফেলা হয়েছে। এটা এখন শুধু শুধু পড়ে আছে — মুছে ফেলা নিরাপদ।", "The program this item started was deleted. It's just sitting there now — safe to delete.") : i.Note;
                list.Add(ScrollList.Row(UI.Dock(UI.V(title, UI.Text(note, 12, i.Caution ? Theme.Risk : Theme.Sub).M(0, 3, 0, 2), cmd).M(12, 0, 12, 0),
                    new FrameworkElement[] { UI.IconBox(i.Orphan ? Glyph.Trash : i.Caution ? Glyph.Warn : Glyph.Gear, Theme.B(color), 32) }, new FrameworkElement[] { act })));
            }
            if (list.Count == 0) list.Add(UI.Center(UI.Text(L.T("কিছু পাওয়া যায়নি", "Nothing found"), 13, Theme.Sub)).M(0, 30, 0, 0));
            var view = list.Take();
            body.Content = UI.Rows(view, head, UI.RiskLegend().M(0, 8, 0, 6), view);
        }

        void BuiltIn()
        {
            var card = UI.Card(UI.V(
                UI.H(UI.Icon(Glyph.Lock, 16, Theme.B(Theme.Blue)), UI.Text(L.T("Windows এর সাথে আসা app গুলো", "Apps that come with Windows"), 15, null, true).M(8, 0, 0, 0)),
                UI.Text(L.T("Xbox, Clipchamp, Solitaire, News, Weather এর মতো Store app গুলো এই app এর তালিকায় আসে না — ওগুলো Windows এর Settings → Apps → Installed apps থেকে এক click এ uninstall করা যায়।\n\nEdge, Microsoft Store, Photos, Windows Security এর মতো কিছু app Windows এর নিজের অংশ। এগুলো মুছা যায় না, বা জোর করে মুছলে পরের Windows update এ আবার ফিরে আসে — আর জায়গাও তেমন খালি হয় না।\n\nভালো খবর: এগুলো না খুললে প্রায় কোন RAM বা battery খরচ করে না। না চাইলে শুধু Start menu থেকে unpin করে দিন (icon এ right-click → Unpin from Start)।",
                    "Store apps like Xbox, Clipchamp, Solitaire, News and Weather don't appear in this app's list — you can uninstall them in one click from Windows Settings → Apps → Installed apps.\n\nSome apps, like Edge, Microsoft Store, Photos and Windows Security, are part of Windows itself. They can't be removed, or if you force it they come back with the next Windows update — and it frees very little space.\n\nGood news: unopened, they use almost no RAM or battery. If you don't want to see them, just unpin them from the Start menu (right-click the icon → Unpin from Start)."), 13, Theme.Sub).M(0, 10, 0, 12),
                UI.Button(L.T("Installed apps খুলুন", "Open Installed apps"), () => Shell.Open("ms-settings:appsfeatures"), Theme.Blue, Glyph.Open)), Theme.A(Theme.Blue, 0.09), 12, 16);
            ((Button)((StackPanel)card.Child).Children[2]).HorizontalAlignment = HorizontalAlignment.Left;
            card.VerticalAlignment = VerticalAlignment.Top;
            body.Content = card;
        }
    }

    class RamPage : Page
    {
        readonly ContentControl summary = Views.Holder(), body = Views.Holder();
        readonly ScrollList list = new ScrollList();
        readonly FilterBar filter;

        public RamPage()
        {
            filter = new FilterBar(() => Refresh());
            var g = UI.Rows(body, summary, filter.Root, body);
            filter.Root.Margin = new Thickness(0, 12, 0, 0); body.Margin = new Thickness(0, 10, 0, 0); g.Margin = new Thickness(16);
            Root = g;
        }
        public override void Shown() { App.Ram.Start(); }
        public override void Hidden() { App.Ram.Stop(); }

        FrameworkElement Summary(MemInfo i)
        {
            double frac = i.Total > 0 ? Math.Min(1, (double)i.Used / i.Total) : 0;
            var color = i.Pressure >= 4 ? Theme.Red : i.Pressure >= 2 ? Theme.Amber : Theme.Teal;
            string state = i.Pressure >= 4 ? L.T("RAM এর উপর অনেক চাপ — PC ধীর হবে", "RAM under heavy pressure — your PC will slow down") : i.Pressure >= 2 ? L.T("RAM এর উপর চাপ বাড়ছে", "RAM pressure rising") : L.T("RAM ঠিক আছে", "RAM is fine");
            var head = UI.Dock(new Border(), new FrameworkElement[] { UI.Text("RAM", 14, null, true, false), UI.Dot(Theme.B(color)).M(10, 0, 6, 0), UI.Text(state, 13, Theme.B(color), false, false) },
                new FrameworkElement[] { UI.Text(L.T("ব্যবহার " + FS.Pretty(i.Used), FS.Pretty(i.Used) + " used"), 13, null, true, false), UI.Text(L.T("/ মোট " + FS.Pretty(i.Total), "of " + FS.Pretty(i.Total)), 13, Theme.Sub, false, false).M(6, 0, 0, 0) });
            var v = UI.V(head, UI.Bar(frac, Theme.B(color), 12).M(0, 8, 0, 0));
            if (i.SwapTotal > 0)
            {
                v.Children.Add(UI.Text(L.T("Page file (disk এ রাখা RAM): ", "Page file (RAM kept on disk): ") + FS.Pretty(i.SwapUsed) + " / " + FS.Pretty(i.SwapTotal), 12, (double)i.SwapUsed / i.SwapTotal > 0.5 ? Theme.Risk : Theme.Sub).M(0, 8, 0, 0));
                if (i.SwapUsed > 4L * 1073741824)
                    v.Children.Add(UI.Text(L.T("Page file অনেক বেশি ব্যবহার হচ্ছে — PC ধীর লাগবে। ভারী app বন্ধ করুন, আর সম্ভব হলে PC একবার Restart দিন।", "The page file is in heavy use — the PC will feel slow. Quit heavy apps, and restart the PC if you can."), 12, Theme.B(Theme.Amber)).M(0, 4, 0, 0));
            }
            return UI.Card(v);
        }

        public override void Refresh()
        {
            var m = App.Ram;
            summary.Content = Summary(m.Info);
            if (!m.Loaded) { body.Content = UI.Busy(""); filter.Root.Visibility = Visibility.Collapsed; return; }
            filter.Root.Visibility = Visibility.Visible;
            var found = m.Apps.Where(a => Kinds.Matches(filter.Query, a.Name)).ToList();
            var green = found.Where(a => !a.Caution).ToList();
            filter.SetCounts(green.Count, found.Count - green.Count);

            list.Clear();
            foreach (var ramApp in found.Where(a => filter.Allows(!a.Caution)))
            {
                var a = ramApp;
                var tint = a.Caution ? Theme.Risk : Theme.Safe;
                var quit = UI.Button(L.T("বন্ধ করুন", "Quit"), () =>
                {
                    if (!a.Caution || Dialogs.Confirm(L.T(a.Name + " বন্ধ করবেন?", "Quit " + a.Name + "?"), a.Note, L.T("বন্ধ করুন", "Quit"), true)) m.Quit(a);
                });
                list.Add(ScrollList.Row(UI.Dock(UI.V(UI.Text(a.Name, 13, null, false, false), UI.Text(a.Note, 12, a.Duplicate != null ? Theme.B(Theme.Amber) : tint)).M(10, 0, 10, 0),
                    new FrameworkElement[] { UI.Dot(tint).M(0, 0, 10, 0), UI.FileIcon(a.ExePath, 28) },
                    new FrameworkElement[] { UI.Text(FS.Pretty(a.Memory), 14, null, true, false).M(0, 0, 12, 0), quit }), Theme.A(a.Caution ? Theme.RiskC : Theme.SafeC, 0.07)));
            }
            Button all = null;
            if (green.Count > 0)
                all = UI.Button(L.T("সব সবুজ app বন্ধ করুন", "Quit all green apps"), () =>
                {
                    if (Dialogs.Confirm(L.T(green.Count + " টি সবুজ app বন্ধ করবেন?", "Quit " + green.Count + " green apps?"), string.Join(", ", green.Select(x => x.Name)), L.T("সব বন্ধ করুন", "Quit all")))
                        foreach (var app in green) m.Quit(app);
                }, null, Glyph.Cancel);
            var view = list.Take();
            body.Content = UI.Rows(view, UI.RiskLegend().M(0, 0, 0, 8), view,
                UI.Dock(UI.Text(L.T("বন্ধ করুন চাপলে app টা স্বাভাবিকভাবে বন্ধ হয় — save না করা কাজ থাকলে app নিজেই জিজ্ঞেস করবে।", "Quit closes the app normally — if there's unsaved work, the app will ask first."), 12, Theme.Sub).M(0, 0, 10, 0), null, new FrameworkElement[] { all }).M(0, 8, 0, 0));
        }
    }
}
