// The Uninstall page and the window that walks through uninstalling one program or several.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyUninstall
{
    class UninstallPage : Page
    {
        readonly AppsPanel panel = new AppsPanel(false);

        public UninstallPage()
        {
            var note = UI.Text(L.T("মুছে ফেলা ফাইল Recycle Bin এ যায় — দরকার হলে ফেরত আনতে পারবেন", "Removed files go to the Recycle Bin — you can get them back if needed"), 12, Theme.Sub, false, false);
            var g = UI.Rows(panel.Root, panel.Root, note.M(0, 8, 0, 0));
            g.Margin = new Thickness(20);
            Root = g;
        }
        public override void Refresh() { panel.Refresh(); }
    }

    /// Uninstalls one program, or several one after another (Windows runs only one uninstaller at a time).
    class UninstallDialog : Window
    {
        readonly List<InstalledApp> apps;
        readonly ContentControl body = new ContentControl { Focusable = false, Margin = new Thickness(0, 16, 0, 0) };
        readonly List<InstalledApp> gone = new List<InstalledApp>();      // really uninstalled
        readonly List<string> problems = new List<string>();              // skipped or failed, for the summary
        List<Leftover> leftovers = new List<Leftover>();
        DispatcherTimer poll;
        bool entryBroken, full;
        int index;

        bool Many { get { return apps.Count > 1; } }
        InstalledApp First { get { return apps[0]; } }

        public UninstallDialog(List<InstalledApp> list)
        {
            apps = list;
            Title = "Easy Uninstall"; Width = Many ? 660 : 620; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Background = Theme.Window; FontFamily = UI.Font; UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = App.Window;
            App.DarkTitleBar(this);
            FrameworkElement head;
            if (!Many)
            {
                var a = First;
                string sub = string.Join("  ·  ", new[] { a.Version == "" ? "" : "Version " + a.Version, a.Dir }.Where(s => s != ""));
                head = UI.Dock(UI.V(UI.Text(a.Name, 19, null, true), UI.Text(sub, 12, Theme.Sub, false, false)).M(14, 0, 0, 0), new FrameworkElement[] { UI.FileIcon(a.IconPath, 48) });
            }
            else
            {
                var icons = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var a in apps.Take(8)) icons.Children.Add(UI.FileIcon(a.IconPath, 40).M(0, 0, 6, 0));
                var names = UI.Text(string.Join(", ", apps.Select(a => a.Name)), 12, Theme.Sub); names.MaxHeight = 52; names.TextTrimming = TextTrimming.CharacterEllipsis;
                head = UI.V(icons, UI.Text(L.T(apps.Count + " টি app Uninstall", "Uninstall " + apps.Count + " apps"), 19, null, true).M(0, 10, 0, 2), names);
            }
            Content = new Border { Padding = new Thickness(24), Child = UI.V(head, body) };
            Closed += (s, e) => { if (poll != null) poll.Stop(); };
            Choose();
        }

        /// For UI checks only: shows what Full Uninstall would list. Deletes nothing.
        public void PreviewLeftovers() { gone.AddRange(apps); Scan(); }

        /// Shows the window; true when at least one program was removed.
        public bool Run() { ShowDialog(); return gone.Count > 0 || apps.Any(a => !a.StillInstalled()); }

        FrameworkElement Buttons(params Button[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            foreach (var b in items) { if (b == null) continue; b.Margin = new Thickness(8, 0, 0, 0); p.Children.Add(b); }
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
            if (!Many && First.IsProtected)
            {
                body.Content = UI.V(UI.H(UI.Icon(Glyph.Lock, 16, Theme.B(Theme.Amber)), UI.Text(L.T("এটি Windows এর অংশ বা অন্য software এর দরকারি অংশ — নিরাপত্তার জন্য মুছা বন্ধ রাখা হয়েছে।", "This is part of Windows or something other software needs — deleting is blocked for safety."), 13, Theme.B(Theme.Amber)).M(8, 0, 0, 0).W(520)),
                    Buttons(CancelButton(L.T("বন্ধ করুন", "Close"))));
                return;
            }
            body.Content = UI.V(
                Option(Glyph.Trash, Theme.Blue, L.T("শুধু Uninstall", "Only uninstall"),
                    Many ? L.T("প্রতিটা software এর নিজের uninstaller এক এক করে চালাবে। Settings ও data থেকে যেতে পারে।", "Runs each program's own uninstaller, one after another. Settings and data may stay.")
                         : L.T("Software টির নিজের uninstaller চালাবে। Settings ও data থেকে যেতে পারে।", "Runs the software's own uninstaller. Settings and data may stay."), () => Start(false)),
                Option(Glyph.Bolt, Theme.Teal, "Full Uninstall", L.T("Uninstaller চালানোর পর ফেলে যাওয়া ফোল্ডার, AppData, shortcut ও registry settings খুঁজে তালিকা দেখাবে — তারপর সব মুছে দিবে।", "After the uninstaller finishes, finds the folders, AppData, shortcuts and registry settings it left behind, shows you the list — then deletes them all."), () => Start(true)),
                Buttons(CancelButton()));
        }

        void Start(bool fullUninstall) { full = fullUninstall; index = 0; Next(); }

        /// Runs the uninstaller of the next program in the list and waits until Windows no longer lists it.
        void Next()
        {
            if (poll != null) poll.Stop();
            if (index >= apps.Count) { AfterAll(); return; }
            var app = apps[index];
            string step = Many ? "(" + (index + 1) + " / " + apps.Count + ")  " + app.Name + "\n" : "";
            Bg.Run(() => { Uninstaller.CloseIfRunning(app); return Uninstaller.Start(app); }, err =>
            {
                if (err != null && Many) { problems.Add(app.Name + L.T(" — uninstaller চালানো যায়নি", " — couldn't start its uninstaller")); index++; Next(); return; }
                if (err != null)
                {
                    entryBroken = true;
                    body.Content = UI.V(UI.Text(L.T("Uninstaller চালানো যায়নি: ", "Couldn't start the uninstaller: ") + err, 13, Theme.Risk),
                        UI.Text(L.T("Software টি হয়তো আগেই হাতে মুছে ফেলা হয়েছে। চাইলে এর ফেলে যাওয়া ফাইল খুঁজে মুছতে পারেন, আর Windows এর তালিকা থেকেও নামটা সরিয়ে দিতে পারেন।", "The software may already have been deleted by hand. You can find and remove the files it left behind, and take its name off the Windows list too."), 13, Theme.Sub).M(0, 8, 0, 0),
                        Buttons(CancelButton(), UI.Button(L.T("ফেলে যাওয়া ফাইল খুঁজুন", "Find leftover files"), () => { gone.Add(app); Scan(); }, Theme.Teal)));
                    return;
                }
                Button skip = Many ? UI.Button(L.T("এটা বাদ দিন", "Skip this one"), () => { problems.Add(app.Name + L.T(" — বাদ দেওয়া হয়েছে", " — skipped")); index++; Next(); }) : null;
                body.Content = UI.V(UI.Busy(step + L.T("Uninstaller খোলা হয়েছে — সেখানে ধাপগুলো শেষ করুন।\nশেষ হলে এই window নিজে থেকে এগিয়ে যাবে।", "The uninstaller is open — finish its steps there.\nThis window moves on by itself when it's done.")).M(0, 10, 0, 0),
                    Buttons(skip, CancelButton(L.T("বন্ধ করুন", "Close"))));
                int quiet = 0;
                poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                poll.Tick += (s, e) =>
                {
                    if (app.StillInstalled()) { quiet = 0; return; }
                    if (++quiet < 3) return;                 // let the uninstaller finish deleting its files
                    poll.Stop(); gone.Add(app); index++; Next();
                };
                poll.Start();
            });
        }

        string ProblemText { get { return problems.Count == 0 ? "" : L.T("\n\nযেগুলো uninstall হয়নি:\n", "\n\nNot uninstalled:\n") + string.Join("\n", problems); } }

        void AfterAll()
        {
            if (gone.Count == 0) { Finish(L.T("কোন software uninstall হয়নি।", "Nothing was uninstalled.") + ProblemText); return; }
            if (full) { Scan(); return; }
            Finish((Many ? L.T("✓ " + gone.Count + " টি software uninstall হয়ে গেছে।", "✓ " + gone.Count + " programs were uninstalled.")
                         : L.T("✓ " + First.Name + " uninstall হয়ে গেছে।", "✓ " + First.Name + " was uninstalled.")) + ProblemText);
        }

        void Scan()
        {
            body.Content = UI.Busy(L.T("সব related ফাইল খোঁজা হচ্ছে…", "Finding all related files…")).M(0, 10, 0, 10);
            var targets = gone.ToList();
            Bg.Run(() =>
            {
                var all = InstalledApp.LoadAll();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var found = new List<Leftover>();
                foreach (var a in targets)
                    foreach (var l in Leftovers.Find(a, all))
                        if (seen.Add(l.Path)) { l.Owner = a.Name; found.Add(l); }
                return found;
            }, found => { leftovers = found ?? new List<Leftover>(); Review(); });
        }

        void Review()
        {
            var total = UI.Text("", 14, null, true, false);
            Action update = () => total.Text = L.T("মোট খালি হবে: ", "Total to free: ") + FS.Pretty(leftovers.Where(l => l.Selected).Sum(l => l.Size));
            update();
            var panel = new StackPanel();
            panel.Children.Add(UI.Text(leftovers.Count == 0
                ? L.T("কোন ফেলে যাওয়া ফাইল পাওয়া যায়নি — সব পরিষ্কার।", "No leftover files found — all clean.") + ProblemText
                : L.T("এই " + leftovers.Count + " টি related ফাইল/ফোল্ডার পাওয়া গেছে। যেগুলো রাখতে চান সেগুলোর টিক তুলে দিন:", "Found " + leftovers.Count + " related files/folders. Untick the ones you want to keep:"), 13));
            if (leftovers.Count > 0)
            {
                var rows = new StackPanel();
                foreach (var item in leftovers)
                {
                    var l = item;
                    var path = UI.Text(l.Path, 12, null, false, false); path.FontFamily = UI.Mono; path.ToolTip = l.Path;
                    var left = new List<FrameworkElement> { UI.Check(l.Selected, on => { l.Selected = on; update(); }) };
                    if (Many) left.Add(UI.Text(l.Owner, 12, null, true, false).W(130).M(8, 0, 0, 0));
                    rows.Children.Add(UI.Dock(path.M(8, 0, 10, 0), left.ToArray(),
                        new FrameworkElement[] { UI.Text(l.IsRegistry ? "registry" : FS.Pretty(l.Size), 12, Theme.Sub, false, false) }).M(8, 5, 8, 5));
                }
                panel.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Height = Many ? 260 : 240, Margin = new Thickness(0, 10, 0, 0), Child = UI.Scroll(rows) });
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
                if (entryBroken && First.StillInstalled()) RemoveEntry(First);
                return failed;
            }, failed =>
            {
                string what = Many ? L.T(gone.Count + " টি software", gone.Count + " programs") : First.Name;
                if (failed == null || failed.Count == 0)
                    Finish(L.T("✓ " + what + " এবং এর সব ফাইল মুছে Recycle Bin এ পাঠানো হয়েছে। Recycle Bin খালি করলে স্থায়ীভাবে মুছে যাবে।", "✓ " + what + " and all leftover files were removed and sent to the Recycle Bin. Empty the Recycle Bin to delete them for good.") + ProblemText);
                else Finish(L.T("⚠ কিছু ফাইল মুছা যায়নি (অনুমতি দেননি বা ফাইল ব্যবহার হচ্ছে):\n", "⚠ Some files couldn't be deleted (permission refused or file in use):\n") + string.Join("\n", failed) + ProblemText);
            });
        }

        /// Takes a dead entry off the Windows "installed apps" list (its uninstaller no longer exists).
        static void RemoveEntry(InstalledApp app)
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
}
