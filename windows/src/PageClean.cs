// Clean pages: Smart Clean, Suggestions, "Where's my space" and Big files.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyUninstall
{
    static class Views
    {
        public static FrameworkElement DiskBar(DiskInfo d)
        {
            double frac = d.Total > 0 ? (double)d.Used / d.Total : 0;
            var color = Theme.B(frac > 0.9 ? Theme.Red : frac > 0.75 ? Theme.Amber : Theme.Blue);
            var head = UI.Dock(new Border(), new FrameworkElement[] { UI.Text(d.Label ?? "", 14, null, true, false) },
                new FrameworkElement[] { UI.Text(L.T("খালি আছে " + FS.Pretty(d.Free), FS.Pretty(d.Free) + " free"), 13, null, true, false),
                                         UI.Text(L.T("/ মোট " + FS.Pretty(d.Total), "of " + FS.Pretty(d.Total)), 13, Theme.Sub, false, false).M(6, 0, 0, 0) });
            return UI.Card(UI.V(head, UI.Bar(frac, color, 12).M(0, 8, 0, 0),
                frac > 0.85 ? UI.Text(L.T("⚠ Disk প্রায় ভরা! PC ধীর হয়ে যেতে পারে। পরামর্শগুলো দেখুন।", "⚠ Disk almost full! Your PC may slow down. See the suggestions."), 12, Theme.Risk).M(0, 8, 0, 0) : null));
        }

        /// Page skeleton: disk bar on top, then fixed rows, then the part that is redrawn.
        public static Grid Frame(ContentControl disk, ContentControl body, params FrameworkElement[] fixedRows)
        {
            var rows = new List<FrameworkElement> { disk };
            foreach (var r in fixedRows) { r.Margin = new Thickness(0, 12, 0, 0); rows.Add(r); }
            body.Margin = new Thickness(0, 12, 0, 0); rows.Add(body);
            var g = UI.Rows(body, rows.ToArray());
            g.Margin = new Thickness(16);
            return g;
        }

        public static ContentControl Holder() { return new ContentControl { Focusable = false }; }

        public static TextBlock Right(TextBlock t, double width) { t.Width = width; t.TextAlignment = TextAlignment.Right; return t; }
    }

    class SmartPage : Page
    {
        readonly ContentControl disk = Views.Holder(), body = Views.Holder();
        readonly ScrollList list = new ScrollList();
        TextBlock total; Button cleanBtn;

        public SmartPage() { Root = Views.Frame(disk, body); }
        public override void Shown() { App.Cleanup.RefreshDisk(); }

        void UpdateTotal()
        {
            var m = App.Cleanup.Smart;
            if (total != null) total.Text = L.T("মোট খালি হবে: ", "Total to free: ") + FS.Pretty(m.SelectedSize);
            if (cleanBtn != null) cleanBtn.IsEnabled = m.SelectedSize > 0 || m.Items.Any(i => i.Mode == SmartMode.Bin && i.On);
        }

        public override void Refresh()
        {
            var m = App.Cleanup.Smart;
            disk.Content = Views.DiskBar(App.Cleanup.Disk);
            switch (m.State)
            {
                case SmartModel.Stage.Idle:
                    body.Content = UI.Center(UI.Icon(Glyph.Broom, 44, Theme.Safe), UI.Big("Smart Clean", 22).M(0, 12, 0, 6),
                        UI.Text(L.T("শুধু সেই ফাইলগুলো খুঁজবে যেগুলো মুছলে কোন সমস্যা হয় না — cache, temp ফাইল, crash report, installer আর Recycle Bin।\nআগে আপনাকে তালিকা দেখাবে, তারপর এক click এ সব মুছে জায়গা খালি করবে।", "Finds only files that are safe to delete — caches, temp files, crash reports, installers and the Recycle Bin.\nShows you the list first, then deletes it all in one click."), 13, Theme.Sub),
                        UI.Button(L.T("পরীক্ষা করুন", "Scan"), () => m.Scan(), Theme.Teal, Glyph.Search).M(0, 16, 0, 0));
                    return;
                case SmartModel.Stage.Scanning: body.Content = UI.Busy(L.T("নিরাপদে মুছা যায় এমন ফাইল খোঁজা হচ্ছে…", "Looking for files that are safe to delete…")); return;
                case SmartModel.Stage.Cleaning: body.Content = UI.Busy(L.T("পরিষ্কার করা হচ্ছে…", "Cleaning…")); return;
                case SmartModel.Stage.Done:
                    body.Content = UI.Center(UI.Icon(Glyph.Check, 46, Theme.Safe), UI.Big(L.T("প্রায় " + FS.Pretty(m.Freed) + " জায়গা খালি হলো!", "About " + FS.Pretty(m.Freed) + " freed!"), 22).M(0, 12, 0, 6),
                        UI.Text(L.T("App গুলো প্রথমবার খুলতে একটু বেশি সময় নিতে পারে — ওরা নতুন করে cache বানাচ্ছে, এটা স্বাভাবিক। চালু থাকা app এর ব্যবহার করা ফাইলগুলো থেকে গেছে।", "Apps may take a bit longer to open the first time — they're rebuilding their cache. That's normal. Files in use by running apps were left in place."), 13, Theme.Sub),
                        UI.Button(L.T("আবার পরীক্ষা করুন", "Check again"), () => m.Scan(), null, Glyph.Refresh).M(0, 16, 0, 0));
                    return;
            }

            var head = UI.Dock(UI.V(UI.Text(L.T("এগুলো মুছলে কোন সমস্যা নেই", "Safe to delete"), 15, Theme.Safe, true),
                    UI.Text(L.T("যেটা রাখতে চান তার টিক তুলে দিন। বাকি সব এক click এ মুছে যাবে।", "Untick anything you want to keep. Everything else is deleted in one click."), 12, Theme.Sub)),
                null, new FrameworkElement[] { UI.IconButton(Glyph.Refresh, () => m.Scan(), L.T("আবার পরীক্ষা করুন", "Check again")) });

            list.Clear();
            foreach (var item in m.Items.Where(i => i.SkipReason == null))
            {
                var it = item;
                var row = UI.Dock(UI.V(UI.Text(it.Title, 14, null, false, false), UI.Text(it.Note, 12, Theme.Sub)).M(10, 0, 10, 0),
                    new FrameworkElement[] { UI.Check(it.On, on => { it.On = on; UpdateTotal(); }).M(0, 0, 10, 0), UI.IconBox(it.Icon, Theme.B(Theme.Teal), 28) },
                    new FrameworkElement[] { UI.Text(it.Size > 0 ? FS.Pretty(it.Size) : "—", 14, null, true, false) });
                list.Add(ScrollList.Row(row));
            }
            var skipped = m.Items.Where(i => i.SkipReason != null).ToList();
            if (skipped.Count > 0) list.Add(UI.Text(L.T("এখন বাদ রাখা হলো", "Skipped for now"), 12, Theme.Sub, true).M(10, 14, 0, 4));
            foreach (var it in skipped)
                list.Add(ScrollList.Row(UI.Dock(UI.V(UI.Text(it.Title, 14, Theme.Sub, false, false), UI.Text(it.SkipReason, 12, Theme.Sub)).M(10, 0, 10, 0),
                    new FrameworkElement[] { UI.Icon(Glyph.Pause, 14).W(28) }, new FrameworkElement[] { UI.Text(FS.Pretty(it.Size), 14, Theme.Sub, false, false) })));

            total = UI.Text("", 15, Theme.Safe, true, false);
            cleanBtn = UI.Button(L.T("Clean করুন", "Clean"), () =>
            {
                int n = m.Items.Count(i => i.On && i.SkipReason == null);
                if (Dialogs.Confirm(L.T(n + " টি জিনিস মুছে " + FS.Pretty(m.SelectedSize) + " খালি করবেন?", "Delete " + n + " items and free " + FS.Pretty(m.SelectedSize) + "?"),
                    L.T("এগুলো সরাসরি মুছে যাবে, Recycle Bin ও খালি হবে — ফেরত আনা যাবে না। তবে সব cache / অস্থায়ী ফাইল, তাই কোন সমস্যা হবে না।", "These are deleted directly and the Recycle Bin is emptied — they can't be recovered. They're all caches / temporary files, so nothing will break."),
                    L.T("Clean করুন", "Clean")))
                    m.Clean(() => { var c = App.Cleanup; c.RefreshDisk(); c.Suggestions.Clear(); c.SuggestScanned = false; c.BigScanned = false; c.BigFiles.Clear(); c.Entries.Clear(); });
            }, Theme.Teal, Glyph.Broom);
            cleanBtn.Padding = new Thickness(20, 9, 20, 10);
            UpdateTotal();
            var view = list.Take();
            body.Content = UI.Rows(view, head, view.M(0, 8, 0, 8), UI.Dock(new Border(), new FrameworkElement[] { total }, new FrameworkElement[] { cleanBtn }));
        }
    }

    class SuggestPage : Page
    {
        readonly ContentControl disk = Views.Holder(), body = Views.Holder();
        readonly ScrollList list = new ScrollList();
        readonly FilterBar filter;

        public SuggestPage() { filter = new FilterBar(() => Refresh()); Root = Views.Frame(disk, body, filter.Root); }

        public override void Shown()
        {
            var c = App.Cleanup; c.RefreshDisk();
            if (!c.SuggestScanned && !c.Scanning) c.ScanSuggestions();
        }

        FrameworkElement Header(string title, string sub, Brush color)
        {
            return UI.V(UI.Text(title, 15, color, true), sub == "" ? null : UI.Text(sub, 12, Theme.Sub)).M(2, 12, 0, 8);
        }

        FrameworkElement Card(Suggestion s)
        {
            var c = App.Cleanup;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            if (s.Paths.Count > 0) buttons.Children.Add(UI.Button(L.T("ফোল্ডারে দেখুন", "Show in folder"), () => Shell.Reveal(s.Paths[0])).M(0, 0, 8, 0));
            Action rescan = () => c.ScanSuggestions();
            switch (s.Kind)
            {
                case SuggestKind.DeleteContents:
                    buttons.Children.Add(UI.Button(L.T("পরিষ্কার করুন", "Clean up"), () =>
                    {
                        if (!Dialogs.Confirm(L.T(s.Title + " মুছবেন?", "Delete " + s.Title + "?"), L.T(FS.Pretty(s.Size) + " খালি হবে। এগুলো অস্থায়ী ফাইল, app গুলো দরকার হলে আবার বানিয়ে নেবে। (এগুলো সরাসরি মুছে যাবে, Recycle Bin এ যাবে না)", "Frees " + FS.Pretty(s.Size) + ". These are temporary files; apps rebuild them when needed. (Deleted directly, not moved to the Recycle Bin)"), L.T("মুছে ফেলুন", "Delete"))) return;
                        c.Scanning = true; App.Invalidate();
                        Bg.Run(() => SuggestionScanner.Clean(s), () => { c.Scanning = false; rescan(); });
                    }, Theme.Teal));
                    break;
                case SuggestKind.EmptyBin:
                    buttons.Children.Add(UI.Button(L.T("Recycle Bin খালি করুন", "Empty Recycle Bin"), () =>
                    {
                        if (!Dialogs.Confirm(L.T("Recycle Bin খালি করবেন?", "Empty the Recycle Bin?"), L.T("Recycle Bin এর সব ফাইল স্থায়ীভাবে মুছে যাবে, আর ফেরত আনা যাবে না।", "Everything in the Recycle Bin will be permanently deleted and can't be recovered."), L.T("খালি করুন", "Empty"))) return;
                        Recycle.Empty(); rescan();
                    }, Theme.Teal));
                    break;
                case SuggestKind.RecycleFolder:
                    buttons.Children.Add(UI.Button(L.T("Recycle Bin এ পাঠান", "Move to Recycle Bin"), () =>
                    {
                        if (!Dialogs.Confirm(L.T(s.Title + " মুছবেন?", "Delete " + s.Title + "?"), L.T("Recycle Bin এ যাবে — দরকার হলে ফেরত আনতে পারবেন। Windows অনুমতি চাইলে Yes দিন।", "Goes to the Recycle Bin — you can get it back if needed. Click Yes if Windows asks for permission."), L.T("Recycle Bin এ পাঠান", "Move to Recycle Bin"), true)) return;
                        var failed = Recycle.Send(s.Paths, true);
                        if (failed.Count > 0) Dialogs.Info(L.T("মুছা যায়নি", "Couldn't delete"), string.Join("\n", failed));
                        rescan();
                    }, Theme.Red));
                    break;
                case SuggestKind.Explore:
                    buttons.Children.Add(UI.Button(L.T("ভিতরে দেখুন", "Look inside"), () => { c.Stack.Clear(); c.Stack.Add(s.Paths[0]); c.Load(); App.Go(Section.Explorer); }, Theme.Red));
                    break;
                case SuggestKind.OldVersion:
                    buttons.Children.Add(UI.Button("Uninstall", () => App.Uninstall(s.App), Theme.Teal));
                    break;
                case SuggestKind.OpenSettings:
                    buttons.Children.Add(UI.Button(L.T("Storage settings খুলুন", "Open Storage settings"), () => Shell.Open(s.Extra), Theme.Teal));
                    break;
                case SuggestKind.Info:
                    buttons.Children.Add(UI.Button(L.T("কিভাবে করবেন", "How to do it"), () => Dialogs.Info(s.Title, s.Extra), Theme.Red));
                    break;
            }
            var title = UI.Dock(UI.Text(s.Title, 14, null, true, false), null, new FrameworkElement[] { s.Size > 0 ? UI.Text(FS.Pretty(s.Size), 14, null, true, false) : null });
            return UI.Card(UI.Dock(UI.V(title, UI.Text(s.Detail, 12, Theme.Sub).M(0, 3, 0, 0), buttons).M(12, 0, 0, 0),
                new FrameworkElement[] { UI.IconBox(s.Icon, Theme.B(s.Safe ? Theme.Teal : Theme.Red), 36) }), null, 10, 12).M(0, 0, 0, 10);
        }

        public override void Refresh()
        {
            var c = App.Cleanup;
            disk.Content = Views.DiskBar(c.Disk);
            filter.Root.Visibility = c.Scanning ? Visibility.Collapsed : Visibility.Visible;
            if (c.Scanning) { body.Content = UI.Busy(L.T("আপনার PC পরীক্ষা করা হচ্ছে…", "Checking your PC…")); return; }

            var found = c.Suggestions.Where(s => Kinds.Matches(filter.Query, s.Title, s.Detail)).ToList();
            filter.SetCounts(found.Count(s => s.Safe), found.Count(s => !s.Safe));
            var safe = filter.Allows(true) ? found.Where(s => s.Safe).ToList() : new List<Suggestion>();
            var review = filter.Allows(false) ? found.Where(s => !s.Safe).ToList() : new List<Suggestion>();

            list.Clear();
            list.Add(UI.RiskLegend());
            if (safe.Count == 0 && review.Count == 0) list.Add(UI.Center(UI.Text(L.T("এই filter এ কিছু পাওয়া যায়নি", "Nothing matches this filter"), 13, Theme.Sub)).M(0, 30, 0, 0));
            long safeTotal = safe.Sum(s => s.Size);
            if (safe.Count > 0)
            {
                list.Add(Header(L.T("✓ মুছলে কোন সমস্যা নেই", "✓ Safe to delete"), safeTotal > 0 ? L.T("প্রায় " + FS.Pretty(safeTotal) + " খালি হবে", "About " + FS.Pretty(safeTotal) + " will be freed") : "", Theme.Safe));
                foreach (var s in safe) list.Add(Card(s));
            }
            if (review.Count > 0)
            {
                list.Add(Header(L.T("⚠ সাবধান — মুছলে সমস্যা হতে পারে", "⚠ Careful — deleting may cause problems"), L.T("এগুলো আপনার নিজের ফাইল। সব একসাথে মুছবেন না — ভিতরে ঢুকে দেখে শুধু অপ্রয়োজনীয়গুলো মুছুন", "These are your own files. Don't delete them all at once — open them and remove only what you don't need"), Theme.Risk));
                foreach (var s in review) list.Add(Card(s));
            }
            list.Add(UI.Text(L.T("বাকি জায়গা Windows নিজে ব্যবহার করে (system file, page file)। এগুলো হাত দেওয়া ঠিক না।", "The rest is used by Windows itself (system files, page file). Best not to touch it."), 12, Theme.Sub).M(2, 4, 0, 8));

            var view = list.Take();
            var again = UI.Button(L.T("আবার পরীক্ষা করুন", "Check again"), () => c.ScanSuggestions(), null, Glyph.Refresh); again.HorizontalAlignment = HorizontalAlignment.Right;
            body.Content = UI.Rows(view, view, again.M(0, 8, 0, 0));
        }
    }

    class ExplorerPage : Page
    {
        readonly ContentControl disk = Views.Holder(), crumbs = Views.Holder(), body = Views.Holder();
        readonly ScrollList list = new ScrollList();
        readonly Border search;
        string query = "", shownFor;

        public ExplorerPage()
        {
            search = UI.Search(L.T("এই ফোল্ডারে নাম দিয়ে খুঁজুন…", "Search this folder by name…"), q => { query = q; Refresh(); });
            Root = Views.Frame(disk, body, crumbs, search);
        }

        public override void Shown()
        {
            App.Cleanup.RefreshDisk();
            if (App.Cleanup.Entries.Count == 0) App.Cleanup.Load();
        }

        public static string RootName(string path)
        {
            if (FS.Same(path, FS.Home)) return L.T("আপনার ফোল্ডার (Documents, Downloads, app data…)", "Your folder (Documents, Downloads, app data…)");
            if (FS.Same(path, FS.PF)) return L.T("Program Files — ইনস্টল করা software", "Program Files — installed software");
            if (FS.Same(path, FS.PF86)) return L.T("Program Files (x86) — পুরোনো ধরনের software", "Program Files (x86) — older-style software");
            if (FS.Same(path, FS.ProgramData)) return L.T("ProgramData — সব user এর app data", "ProgramData — app data for all users");
            if (FS.Same(path, FS.WinDir)) return L.T("Windows — system নিজে (হাত দেবেন না)", "Windows — the system itself (don't touch)");
            return path;
        }

        void Trash(Entry e)
        {
            var m = App.Cleanup;
            if (!Dialogs.Confirm(L.T("\"" + e.Name + "\" Recycle Bin এ পাঠাবেন?", "Move \"" + e.Name + "\" to the Recycle Bin?"),
                L.T(FS.Pretty(e.Size) + " — ভুল হলে Recycle Bin থেকে ফেরত আনতে পারবেন। জায়গা খালি করতে পরে Recycle Bin খালি করুন।", FS.Pretty(e.Size) + " — if it's a mistake you can restore it from the Recycle Bin. Empty the Recycle Bin later to free the space."),
                L.T("Recycle Bin এ পাঠান", "Move to Recycle Bin"), true)) return;
            var failed = Recycle.Send(new[] { e.Path }, false);
            if (failed.Count > 0) { Dialogs.Info(L.T("মুছা যায়নি", "Couldn't delete"), string.Join("\n", failed)); return; }
            m.Forget(e.Path); m.Entries.Remove(e); m.RefreshDisk(); m.Suggestions.Clear(); m.SuggestScanned = false;
            Refresh();
        }

        public override void Refresh()
        {
            var m = App.Cleanup;
            disk.Content = Views.DiskBar(m.Disk);
            string cur = m.Stack.LastOrDefault() ?? "";
            if (shownFor != null && shownFor != cur && query != "") { shownFor = cur; ((TextBox)search.Tag).Text = ""; return; }   // new folder: clear the search
            shownFor = cur;

            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            var back = UI.IconButton(Glyph.Left, () => m.Back(), L.T("পিছনে", "Back")); back.IsEnabled = m.Stack.Count > 0;
            bar.Children.Add(back);
            bar.Children.Add(UI.Link(L.T("সব", "All"), () => m.Jump(0)));
            for (int i = 0; i < m.Stack.Count; i++)
            {
                int depth = i + 1; string p = m.Stack[i];
                bar.Children.Add(UI.Icon(Glyph.Right, 9, Theme.Faint).M(2, 0, 2, 0));
                bar.Children.Add(UI.Link(FS.Same(p, FS.Home) ? "Home" : new Entry { Path = p }.Name, () => m.Jump(depth)));
            }
            crumbs.Content = UI.Dock(new Border(), new FrameworkElement[] { bar },
                new FrameworkElement[] { m.Exploring ? UI.Text(L.T("মাপা হচ্ছে…", "Measuring…"), 12, Theme.Sub, false, false) : null });

            long max = Math.Max(1, m.Entries.Count > 0 ? m.Entries.Max(e => e.Size) : 1);
            bool overview = m.Stack.Count == 0;
            var shown = m.Entries.Where(e => Kinds.Matches(query, e.Name, overview ? RootName(e.Path) : null)).ToList();
            list.Clear();
            foreach (var entry in shown.Take(400))
            {
                var e = entry;
                double frac = e.Size > 0 ? (double)e.Size / max : 0;
                var bar2 = UI.Bar(frac, Theme.A(Theme.Purple, 0.75), 5, Theme.Clear);
                var row = UI.Dock(UI.V(UI.Text(overview ? RootName(e.Path) : e.Name, 13, null, false, false), bar2.M(0, 4, 0, 0)).M(10, 0, 10, 0),
                    new FrameworkElement[] { UI.FileIcon(e.Path, 24) },
                    new FrameworkElement[] { Views.Right(UI.Text(e.Size < 0 ? "…" : FS.Pretty(e.Size), 13, null, true, false), 80),
                        UI.IconButton(Glyph.Open, () => Shell.Reveal(e.Path), L.T("ফোল্ডারে দেখুন", "Show in folder")).M(6, 0, 0, 0),
                        Safe.CanRecycle(e.Path) ? UI.IconButton(Glyph.Trash, () => Trash(e), L.T("Recycle Bin এ পাঠান", "Move to Recycle Bin"), Theme.Risk) : (FrameworkElement)new Border { Width = 30 },
                        UI.Icon(Glyph.Right, 10, e.IsFolder ? Theme.Sub : Theme.Clear).M(4, 0, 0, 0) });
                if (e.IsFolder) { var b = UI.Clickable(row, () => m.Open(e.Path), Theme.Clear, 8, 0); b.Padding = new Thickness(10, 6, 10, 6); list.Add(b); }
                else { var r = ScrollList.Row(row); r.BorderThickness = new Thickness(0); r.Padding = new Thickness(10, 6, 10, 6); r.Margin = new Thickness(0); list.Add(r); }
            }
            if (shown.Count > 400) list.Add(UI.Text(L.T("আরো " + (shown.Count - 400) + " টি ছোট জিনিস দেখানো হয়নি", (shown.Count - 400) + " more small items not shown"), 12, Theme.Sub).M(10, 8, 0, 8));
            if (shown.Count == 0 && !m.Exploring) list.Add(UI.Center(UI.Text(L.T("এখানে কিছু নেই", "Nothing here"), 13, Theme.Sub)).M(0, 30, 0, 0));

            FrameworkElement other = null;
            if (overview && !m.Exploring && m.Entries.Count > 0)
            {
                long rest = m.Disk.Used - m.Entries.Sum(e => Math.Max(0, e.Size));
                if (rest > 0) other = UI.Text(L.T("বাকি প্রায় " + FS.Pretty(rest) + " — page file, hibernate ফাইল, অন্য user ও লুকানো system ফাইল। এগুলো হাত দেওয়া ঠিক না।", "About " + FS.Pretty(rest) + " more — page file, hibernation file, other users and hidden system files. Best not to touch."), 12, Theme.Sub).M(0, 6, 0, 0);
            }
            var view = list.Take();
            body.Content = UI.Rows(view, view, other, UI.Text(L.T("ফোল্ডারে click করে ভিতরে ঢুকুন। সবচেয়ে বড়টা সবার উপরে থাকে।", "Click a folder to go inside. The biggest is always on top."), 12, Theme.Sub).M(0, 6, 0, 0));
        }
    }

    class BigFilesPage : Page
    {
        readonly ContentControl disk = Views.Holder(), kinds = Views.Holder(), body = Views.Holder();
        readonly ScrollList list = new ScrollList();
        readonly FilterBar filter;
        readonly FrameworkElement filterRow;
        FileKind kind = FileKind.All;

        public BigFilesPage()
        {
            filter = new FilterBar(() => Refresh());
            kinds.Margin = new Thickness(10, 0, 0, 0);
            filterRow = UI.Dock(filter.Root, null, new FrameworkElement[] { kinds });
            Root = Views.Frame(disk, body, filterRow);
        }
        public override void Shown() { App.Cleanup.RefreshDisk(); }

        void Trash(Entry e)
        {
            var m = App.Cleanup; var v = BigFileVerdict.Of(e.Path);
            string msg = (v.Safe ? "" : L.T("সাবধান: ", "Careful: ")) + v.Note + L.T("\n\n" + FS.Pretty(e.Size) + " — ভুল হলে Recycle Bin থেকে ফেরত আনতে পারবেন (খুব বড় ফাইল হলে Windows আগে জিজ্ঞেস করবে)।", "\n\n" + FS.Pretty(e.Size) + " — if it's a mistake you can restore it from the Recycle Bin (for very big files Windows asks first).");
            if (!Dialogs.Confirm(L.T("\"" + e.Name + "\" Recycle Bin এ পাঠাবেন?", "Move \"" + e.Name + "\" to the Recycle Bin?"), msg,
                v.Safe ? L.T("Recycle Bin এ পাঠান", "Move to Recycle Bin") : L.T("তবুও পাঠান", "Move anyway"), !v.Safe)) return;
            if (Recycle.Send(new[] { e.Path }, false).Count == 0) m.BigFiles.Remove(e);
            m.RefreshDisk(); Refresh();
        }

        public override void Refresh()
        {
            var m = App.Cleanup;
            disk.Content = Views.DiskBar(m.Disk);
            filterRow.Visibility = m.BigScanned && !m.BigScanning ? Visibility.Visible : Visibility.Collapsed;
            if (m.BigScanning) { body.Content = UI.Busy(L.T("500 MB এর বড় ফাইল খোঁজা হচ্ছে… (কয়েক মিনিট লাগতে পারে)", "Looking for files over 500 MB… (may take a few minutes)")); return; }
            if (!m.BigScanned)
            {
                body.Content = UI.Center(UI.Icon(Glyph.Copy, 40, Theme.B(Theme.Coral)),
                    UI.Text(L.T("আপনার user ফোল্ডারে 500 MB এর চেয়ে বড় সব ফাইল খুঁজে বের করবে।", "Finds every file over 500 MB in your user folder."), 13, Theme.Sub).M(0, 12, 0, 0),
                    UI.Button(L.T("খোঁজা শুরু করুন", "Start search"), () => m.ScanBigFiles(), Theme.Purple, Glyph.Search).M(0, 14, 0, 0));
                return;
            }

            kinds.Content = UI.Segmented(new[] { L.T("সব", "All"), L.T("ভিডিও", "Video"), "Installer / Zip", L.T("অডিও", "Audio"), L.T("অন্যান্য", "Other") }, (int)kind, i => { kind = (FileKind)i; Refresh(); });
            var byKind = m.BigFiles.Where(e => (kind == FileKind.All || Kinds.Of(e.Path) == kind) && Kinds.Matches(filter.Query, e.Path)).ToList();
            var verdicts = byKind.ToDictionary(e => e, e => BigFileVerdict.Of(e.Path));
            int safeN = verdicts.Values.Count(v => v.Safe);
            filter.SetCounts(safeN, byKind.Count - safeN);

            list.Clear();
            foreach (var entry in byKind.Where(e => filter.Allows(verdicts[e].Safe)))
            {
                var e = entry; var v = verdicts[e];
                var tint = v.Safe ? Theme.Safe : Theme.Risk;
                var row = UI.Dock(UI.V(UI.Text(e.Name, 13, null, false, false), UI.Text(v.Note, 12, tint), UI.Text(Path.GetDirectoryName(e.Path), 11, Theme.Sub, false, false)).M(10, 0, 10, 0),
                    new FrameworkElement[] { UI.Dot(tint).M(0, 0, 10, 0), UI.FileIcon(e.Path, 28) },
                    new FrameworkElement[] { UI.Text(FS.Pretty(e.Size), 14, null, true, false),
                        UI.IconButton(Glyph.Open, () => Shell.Reveal(e.Path), L.T("ফোল্ডারে দেখুন", "Show in folder")).M(6, 0, 0, 0),
                        Safe.CanRecycle(e.Path) ? UI.IconButton(Glyph.Trash, () => Trash(e), v.Safe ? L.T("Recycle Bin এ পাঠান — নিরাপদ", "Move to Recycle Bin — safe") : L.T("সাবধান — মুছার আগে নিশ্চিত হন", "Careful — make sure before deleting"), tint) : null });
                list.Add(ScrollList.Row(row, Theme.A(v.Safe ? Theme.SafeC : Theme.RiskC, 0.07)));
            }
            if (list.Count == 0) list.Add(UI.Center(UI.Text(m.BigFiles.Count == 0 ? L.T("500 MB এর বড় কোন ফাইল নেই", "No files over 500 MB") : L.T("এই filter এ কিছু পাওয়া যায়নি", "Nothing matches this filter"), 13, Theme.Sub)).M(0, 30, 0, 0));

            var head = UI.Dock(new Border(), new FrameworkElement[] { UI.Text(L.T(m.BigFiles.Count + " টি বড় ফাইল · মোট " + FS.Pretty(m.BigFiles.Sum(e => e.Size)), m.BigFiles.Count + " big files · total " + FS.Pretty(m.BigFiles.Sum(e => e.Size))), 13, null, false, false) },
                new FrameworkElement[] { UI.Button(L.T("আবার খুঁজুন", "Search again"), () => m.ScanBigFiles(), null, Glyph.Refresh) });
            var view = list.Take();
            body.Content = UI.Rows(view, head, UI.RiskLegend().M(0, 8, 0, 8), view);
        }
    }
}
