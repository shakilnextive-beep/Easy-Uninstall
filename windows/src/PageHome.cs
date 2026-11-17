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
