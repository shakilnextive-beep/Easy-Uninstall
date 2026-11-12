// Look and feel: colours (light + dark), icons and the small building blocks every page uses.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace EasyUninstall
{
    /// Icon glyphs from the Segoe icon font that ships with Windows 10/11.
    static class Glyph
    {
        public const string Home = "", Trash = "", Search = "", Refresh = "", Folder = "", Info = "",
            Bulb = "", Doc = "", Right = "", Left = "", Cancel = "", Check = "", Warn = "",
            Gear = "", Download = "", Box = "", Grid = "", Globe = "", Video = "", Code = "",
            Layers = "", Sync = "", Phone = "", Desktop = "", Power = "", Lock = "", Person = "",
            Mail = "", Chat = "", Drive = "", Chip = "", Broom = "", Bolt = "", Apps = "",
            Copy = "", Pause = "", Open = "";
    }

    static class Theme
    {
        public static readonly bool Dark = ReadDark();

        static bool ReadDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { return false; }
        }

        public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush B(string hex) { return B(C(hex)); }
        public static SolidColorBrush A(Color c, double opacity) { return B(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B)); }

        // Brand palette (same as the Mac app)
        public static readonly Color Ink = C("#26215C"), Purple = C("#7F77DD"), Lilac = C("#CECBF6"), Teal = C("#1D9E75"), Mint = C("#5DCAA5"),
            Coral = C("#D85A30"), Peach = C("#F0997B"), Amber = C("#EF9F27"), Stone = C("#B4B2A9"), Red = C("#E5484D"), Blue = C("#3B82F6"), Gray = C("#8E8E93");

        public static readonly SolidColorBrush Window = B(Dark ? "#1F1E1D" : "#FFFFFF"), Side = B(Dark ? "#292826" : "#F4F3F8"),
            Text = B(Dark ? "#F3F2EF" : "#1D1D1F"), Sub = B(Dark ? "#A6A49E" : "#6E6E73"), Faint = B(Dark ? "#6F6D68" : "#A3A3A8"),
            Tile = B(Dark ? "#12FFFFFF" : "#0C000000"), Line = B(Dark ? "#1FFFFFFF" : "#17000000"), Hover = B(Dark ? "#1CFFFFFF" : "#12000000"),
            White = B("#FFFFFF"), Clear = B("#00FFFFFF");

        public static SolidColorBrush Safe { get { return B(Dark ? Mint : Teal); } }
        public static SolidColorBrush Risk { get { return B(Dark ? C("#FF7A70") : Red); } }
        public static Color SafeC { get { return Teal; } }
        public static Color RiskC { get { return Red; } }
    }

    static class UI
    {
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI, Nirmala UI");
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI, Nirmala UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        static readonly Dictionary<int, ControlTemplate> templates = new Dictionary<int, ControlTemplate>();

        /// App-wide styles: a slim scrollbar that suits both light and dark.
        public static ResourceDictionary Styles()
        {
            return (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/><Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Margin='3,0,1,0' Background='#77888888'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
</ResourceDictionary>");
        }

        static ControlTemplate ButtonTemplate(int radius)
        {
            ControlTemplate t;
            if (templates.TryGetValue(radius, out t)) return t;
            string xaml = @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Grid>
    <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='R' SnapsToDevicePixels='True'/>
    <Border x:Name='h' Background='#000000' Opacity='0' CornerRadius='R'/>
    <Border x:Name='f' BorderBrush='#7F77DD' BorderThickness='2' CornerRadius='R' Visibility='Collapsed'/>
    <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='h' Property='Opacity' Value='0.08'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='h' Property='Opacity' Value='0.16'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='f' Property='Visibility' Value='Visible'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>".Replace("'R'", "'" + radius + "'");
            if (Theme.Dark) xaml = xaml.Replace("Background='#000000'", "Background='#FFFFFF'");
            t = (ControlTemplate)XamlReader.Parse(xaml);
            templates[radius] = t;
            return t;
        }

        public static TextBlock Text(string s, double size = 13, Brush color = null, bool bold = false, bool wrap = true)
        {
            var t = new TextBlock { Text = s ?? "", FontSize = size, Foreground = color ?? Theme.Text, FontFamily = Font, VerticalAlignment = VerticalAlignment.Center };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            if (wrap) t.TextWrapping = TextWrapping.Wrap; else t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        public static TextBlock Big(string s, double size, Brush color = null)
        {
            return new TextBlock { Text = s, FontSize = size, FontWeight = FontWeights.Bold, FontFamily = Display, Foreground = color ?? Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        public static TextBlock Icon(string glyph, double size = 14, Brush color = null)
        {
            return new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = color ?? Theme.Sub, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }

        /// White glyph on a coloured rounded square.
        public static Border IconBox(string glyph, Brush bg, double box = 24, Brush fg = null)
        {
            return new Border { Width = box, Height = box, CornerRadius = new CornerRadius(box * 0.27), Background = bg, Child = Icon(glyph, box * 0.5, fg ?? Theme.White), VerticalAlignment = VerticalAlignment.Top };
        }

        public static T M<T>(this T el, double l, double t, double r, double b) where T : FrameworkElement { el.Margin = new Thickness(l, t, r, b); return el; }
        public static T W<T>(this T el, double w) where T : FrameworkElement { el.Width = w; return el; }
        public static T Tip<T>(this T el, string tip) where T : FrameworkElement { el.ToolTip = tip; return el; }

        /// Text button. With `bg` it is a filled (primary) button, otherwise a quiet one.
        public static Button Button(string text, Action click, Color? bg = null, string glyph = null)
        {
            var fg = bg.HasValue ? Theme.White : Theme.Text;
            UIElement content = Text(text, 13, fg, bg.HasValue, false);
            if (glyph != null) content = H(Icon(glyph, 13, fg).M(0, 0, 7, 0), (FrameworkElement)content);
            var b = new Button { Content = content, Template = ButtonTemplate(7), Padding = new Thickness(13, 6, 13, 7), Cursor = Cursors.Hand,
                Background = bg.HasValue ? Theme.B(bg.Value) : Theme.Tile, BorderBrush = bg.HasValue ? Theme.Clear : Theme.Line, BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FocusVisualStyle = null };
            b.Click += (s, e) => click();
            return b;
        }

        public static Button IconButton(string glyph, Action click, string tip, Brush color = null)
        {
            var b = new Button { Content = Icon(glyph, 14, color ?? Theme.Sub), Template = ButtonTemplate(6), Width = 30, Height = 28, Cursor = Cursors.Hand, Background = Theme.Clear,
                BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip, FocusVisualStyle = null, VerticalAlignment = VerticalAlignment.Center };
            b.Click += (s, e) => click();
            return b;
        }

        /// Any content as a clickable surface (tiles, rows).
        public static Button Clickable(UIElement content, Action click, Brush bg, int radius = 12, double pad = 0)
        {
            var b = new Button { Content = content, Template = ButtonTemplate(radius), Padding = new Thickness(pad), Cursor = Cursors.Hand, Background = bg ?? Theme.Clear, BorderBrush = Theme.Line,
                BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, FocusVisualStyle = null };
            b.Click += (s, e) => click();
            return b;
        }

        public static Button Link(string text, Action click)
        {
            var b = new Button { Content = Text(text, 13, Theme.B(Theme.Purple), false, false), Template = ButtonTemplate(5), Padding = new Thickness(5, 2, 5, 3), Cursor = Cursors.Hand,
                Background = Theme.Clear, BorderThickness = new Thickness(0), FocusVisualStyle = null, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220 };
            b.Click += (s, e) => click();
            return b;
        }

        public static Border Card(UIElement child, Brush bg = null, double radius = 12, double pad = 14)
        {
            return new Border { Child = child, Background = bg ?? Theme.Tile, CornerRadius = new CornerRadius(radius), Padding = new Thickness(pad) };
        }

        public static StackPanel H(params FrameworkElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var i in items) if (i != null) p.Children.Add(i);
            return p;
        }
        public static StackPanel V(params FrameworkElement[] items)
        {
            var p = new StackPanel();
            foreach (var i in items) if (i != null) p.Children.Add(i);
            return p;
        }

        /// left… | fill (takes the rest) | …right
        public static DockPanel Dock(FrameworkElement fill, FrameworkElement[] left = null, FrameworkElement[] right = null)
        {
            var d = new DockPanel { LastChildFill = true };
            foreach (var l in left ?? new FrameworkElement[0]) if (l != null) { DockPanel.SetDock(l, System.Windows.Controls.Dock.Left); d.Children.Add(l); }
            foreach (var r in (right ?? new FrameworkElement[0]).Reverse()) if (r != null) { DockPanel.SetDock(r, System.Windows.Controls.Dock.Right); d.Children.Add(r); }
            d.Children.Add(fill ?? new Border());
            return d;
        }

        /// Stacks rows top to bottom; the row marked as `fill` takes the remaining height.
        public static Grid Rows(FrameworkElement fill, params FrameworkElement[] rows)
        {
            var g = new Grid();
            int n = 0;
            foreach (var r in rows)
            {
                if (r == null) continue;
                g.RowDefinitions.Add(new RowDefinition { Height = r == fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
                Grid.SetRow(r, n++); g.Children.Add(r);
            }
            return g;
        }

        public static FrameworkElement Bar(double frac, Brush color, double height = 8, Brush track = null)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(frac, 0.0001), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - frac, 0.0001), GridUnitType.Star) });
            if (frac > 0) g.Children.Add(new Border { Background = color, CornerRadius = new CornerRadius(height / 2), MinWidth = height });
            return new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = track ?? Theme.Hover, Child = g };
        }

        public static Border Badge(string text, Color c)
        {
            return new Border { Background = Theme.A(c, 0.18), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, FontFamily = Font, Foreground = Theme.B(Theme.Dark ? Lighten(c) : c) } };
        }
        static Color Lighten(Color c) { return Color.FromRgb((byte)(c.R + (255 - c.R) * 0.35), (byte)(c.G + (255 - c.G) * 0.35), (byte)(c.B + (255 - c.B) * 0.35)); }

        public static Border Pill(bool safe) { return Badge(safe ? L.T("নিরাপদ", "Safe") : L.T("সাবধান", "Careful"), safe ? Theme.Teal : Theme.Coral); }

        public static FrameworkElement Dot(Brush color) { return new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = color, VerticalAlignment = VerticalAlignment.Center }; }

        /// Centered "busy" message.
        public static FrameworkElement Busy(string text)
        {
            var bar = new ProgressBar { IsIndeterminate = true, Width = 180, Height = 4, Foreground = Theme.B(Theme.Purple), Background = Theme.Hover, BorderThickness = new Thickness(0) };
            var p = V(bar, Text(text, 13, Theme.Sub).M(0, 12, 0, 0));
            ((TextBlock)p.Children[1]).TextAlignment = TextAlignment.Center;
            p.VerticalAlignment = VerticalAlignment.Center; p.HorizontalAlignment = HorizontalAlignment.Center;
            return p;
        }

        public static FrameworkElement Center(params FrameworkElement[] items)
        {
            var p = V(items);
            foreach (FrameworkElement c in p.Children) { c.HorizontalAlignment = HorizontalAlignment.Center; var t = c as TextBlock; if (t != null) t.TextAlignment = TextAlignment.Center; }
            p.VerticalAlignment = VerticalAlignment.Center; p.HorizontalAlignment = HorizontalAlignment.Center; p.MaxWidth = 560;
            return p;
        }

        public static ScrollViewer Scroll(UIElement content)
        {
            return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }

        /// Rounded search box. Calls `changed` on every keystroke.
        public static Border Search(string placeholder, Action<string> changed, double fontSize = 13)
        {
            var box = new TextBox { BorderThickness = new Thickness(0), Background = Theme.Clear, Foreground = Theme.Text, CaretBrush = Theme.Text, FontSize = fontSize, FontFamily = Font, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 1, 0, 1) };
            var hint = Text(placeholder, fontSize, Theme.Faint, false, false); hint.IsHitTestVisible = false; hint.Margin = new Thickness(3, 0, 0, 0);
            var clear = IconButton(Glyph.Cancel, () => { box.Text = ""; box.Focus(); }, null); clear.Visibility = Visibility.Collapsed; clear.Width = 24; clear.Height = 22;
            box.TextChanged += (s, e) => { hint.Visibility = box.Text == "" ? Visibility.Visible : Visibility.Collapsed; clear.Visibility = box.Text == "" ? Visibility.Collapsed : Visibility.Visible; changed(box.Text); };
            var g = new Grid(); g.Children.Add(hint); g.Children.Add(box);
            return new Border { Tag = box, Background = Theme.Tile, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, fontSize > 14 ? 8 : 4, 4, fontSize > 14 ? 8 : 4), VerticalAlignment = VerticalAlignment.Center,
                Child = Dock(g, new FrameworkElement[] { Icon(Glyph.Search, fontSize).M(0, 0, 7, 0) }, new FrameworkElement[] { clear }) };
        }

        /// Row of choices where one is picked.
        public static Border Segmented(string[] labels, int selected, Action<int> pick)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < labels.Length; i++)
            {
                int n = i; bool on = i == selected;
                var b = new Button { Content = Text(labels[i], 12.5, on ? Theme.White : Theme.Text, on, false), Template = ButtonTemplate(6), Padding = new Thickness(11, 4, 11, 5), Cursor = Cursors.Hand,
                    Background = on ? Theme.B(Theme.Purple) : Theme.Clear, BorderThickness = new Thickness(0), FocusVisualStyle = null };
                b.Click += (s, e) => pick(n);
                p.Children.Add(b);
            }
            return new Border { Background = Theme.Tile, CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = p, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        }

        public static CheckBox Check(bool on, Action<bool> changed)
        {
            var c = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            c.Checked += (s, e) => changed(true); c.Unchecked += (s, e) => changed(false);
            return c;
        }

        public static FrameworkElement Line() { return new Border { Height = 1, Background = Theme.Line }; }

        /// Small key: green = safe to remove, red = may cause problems.
        public static FrameworkElement RiskLegend()
        {
            return H(Dot(Theme.Safe), Text(L.T("সবুজ = মুছলে / বন্ধ করলে কোন সমস্যা নেই", "Green = safe to delete / turn off"), 12, Theme.Sub).M(6, 0, 16, 0),
                     Dot(Theme.Risk), Text(L.T("লাল = সাবধান, সমস্যা হতে পারে", "Red = careful, may cause problems"), 12, Theme.Sub).M(6, 0, 0, 0));
        }

        public static Image FileIcon(string path, double size)
        {
            return new Image { Source = Icons.For(path), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        }
    }

    /// A scrolling list that survives redraws, so the scroll position stays where the user left it.
    class ScrollList
    {
        public readonly StackPanel Items = new StackPanel();
        readonly ScrollViewer view;
        public ScrollList() { view = UI.Scroll(Items); }
        public void Clear() { Items.Children.Clear(); }
        public void Add(FrameworkElement el) { if (el != null) Items.Children.Add(el); }
        public int Count { get { return Items.Children.Count; } }
        /// The list, removed from wherever it was shown before.
        public ScrollViewer Take()
        {
            var p = view.Parent as Panel;
            if (p != null) p.Children.Remove(view);
            var b = view.Parent as Decorator; if (b != null) b.Child = null;
            var c = view.Parent as ContentControl; if (c != null) c.Content = null;
            return view;
        }

        /// One list row: content with a hairline under it, optionally tinted.
        public static Border Row(FrameworkElement content, Brush tint = null)
        {
            return new Border { Child = content, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(8),
                Background = tint ?? Theme.Clear, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, tint == null ? 1 : 0) };
        }
    }

    /// Search box + "all / green / red" switch with counts.
    class FilterBar
    {
        public string Query = ""; public RiskFilter Risk = RiskFilter.All;
        public FrameworkElement Root;
        ContentControl seg = new ContentControl { Focusable = false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Action changed;

        public FilterBar(Action onChange, string placeholder = null)
        {
            changed = onChange;
            Root = UI.Dock(UI.Search(placeholder ?? L.T("নাম দিয়ে খুঁজুন…", "Search by name…"), q => { Query = q; changed(); }), null, new FrameworkElement[] { seg });
        }

        public void SetCounts(int safe, int risky)
        {
            seg.Content = UI.Segmented(new[] { L.T("সব (", "All (") + (safe + risky) + ")", L.T("নিরাপদ (", "Safe (") + safe + ")", L.T("সাবধান (", "Careful (") + risky + ")" },   // WPF draws emoji in one colour, so no 🟢/🔴 here
                (int)Risk, i => { Risk = (RiskFilter)i; changed(); });
        }
        public bool Allows(bool safe) { return Kinds.Allows(Risk, safe); }
    }

    /// File and program icons from Windows, cached.
    static class Icons
    {
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> own = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".msi", "" };

        public static ImageSource For(string path)
        {
            if (string.IsNullOrEmpty(path)) path = "x.exe";
            bool exists = FS.Exists(path), dir = exists && Directory.Exists(path);
            string ext = dir ? "" : Path.GetExtension(path);
            string key = dir ? (FS.IsUnder(path, FS.Home) || path.Length < 4 ? path : "<dir>") : (own.Contains(ext) && exists ? path : "*" + ext);
            ImageSource img;
            if (cache.TryGetValue(key, out img)) return img;
            var info = new Native.SHFILEINFO();
            uint flags = 0x100 /* ICON */ | (exists ? 0u : 0x10u /* USEFILEATTRIBUTES */);
            try
            {
                Native.SHGetFileInfo(path, dir ? 0x10u : 0x80u, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SHFILEINFO)), flags);
                if (info.hIcon != IntPtr.Zero)
                {
                    img = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    img.Freeze();
                    Native.DestroyIcon(info.hIcon);
                }
            }
            catch { }
            cache[key] = img;
            return img;
        }

        static ImageSource logo;
        public static ImageSource Logo
        {
            get
            {
                if (logo != null) return logo;
                try
                {
                    var s = typeof(Icons).Assembly.GetManifestResourceStream("logo.png");
                    var b = new BitmapImage(); b.BeginInit(); b.StreamSource = s; b.CacheOption = BitmapCacheOption.OnLoad; b.DecodePixelWidth = 256; b.EndInit(); b.Freeze();
                    logo = b;
                }
                catch { }
                return logo;
            }
        }
    }

    static class Dialogs
    {
        static Window Make(string title, string text, out StackPanel buttons)
        {
            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var body = UI.V(UI.Text(title, 16, null, true), UI.Text(text, 13, Theme.Sub).M(0, 8, 0, 0), buttons);
            var w = new Window { Title = "Easy Uninstall", Width = 470, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Theme.Window, Content = new Border { Padding = new Thickness(22), Child = body }, FontFamily = UI.Font };
            var owner = Application.Current != null ? Application.Current.MainWindow : null;
            if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            App.DarkTitleBar(w);
            return w;
        }

        public static bool Confirm(string title, string text, string ok, bool danger = false)
        {
            StackPanel buttons; bool result = false;
            var w = Make(title, text, out buttons);
            var cancel = UI.Button(L.T("বাতিল", "Cancel"), () => w.Close()); cancel.IsCancel = true;
            var yes = UI.Button(ok, () => { result = true; w.Close(); }, danger ? Theme.Red : Theme.Teal); yes.IsDefault = true; yes.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(cancel); buttons.Children.Add(yes);
            w.ShowDialog();
            return result;
        }

        public static void Info(string title, string text)
        {
            StackPanel buttons;
            var w = Make(title, text, out buttons);
            var ok = UI.Button(L.T("ঠিক আছে", "OK"), () => w.Close(), Theme.Purple); ok.IsDefault = true; ok.IsCancel = true;
            buttons.Children.Add(ok);
            w.ShowDialog();
        }
    }
}
