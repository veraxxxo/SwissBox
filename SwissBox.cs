// SwissBox — лаунчер открытых утилит в стиле macOS.
// Portable-программы качаются официальными ссылками (или из GitHub Releases) в tools\<id>\
// рядом с SwissBox.exe и запускаются оттуда — без Program Files. У кого нет portable-сборки,
// ставится через winget. MAS и WinUtil запускаются их собственными официальными командами.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class AppInfo
{
    public const string Version = "1.0.0";
    public const string GithubRepo = "veraxxxo/SwissBox";
}

internal class ToolEntry
{
    public string Name;
    public string Category;
    public string Desc;
    public string Action;      // "winget" | "portable" | "silentInstall" | "script"
    public string Payload;     // winget package id, или elevated-команда для "script"
    public string LaunchHint;  // кусок имени exe, по которому ищем уже скачанный/поставленный файл
    public bool Cli;           // консольная утилита — открывать через cmd
    public bool NoLaunch;      // после установки нечего запускать (рантаймы)

    public string PortableUrl;   // прямая стабильная ссылка на exe или zip
    public bool IsZip;
    public string FolderId;      // подпапка в tools\
    public string GithubRepo;    // "owner/repo" — ссылка берётся из последнего релиза
    public string ForgeApiBase;  // база API релизов, если не api.github.com
    public string AssetMatch;    // подстрока имени нужного файла релиза
    public string AssetExclude;  // подстрока, при которой файл релиза пропускаем

    public string SilentArgs;    // флаги тихой установки (silentInstall)
    public string InstalledHint; // имя exe уже поставленной программы (silentInstall)
    public string StartAppName;  // MSIX-программа: ищем в «Пуске» и запускаем через shell:AppsFolder
    public string IconUrl;       // официальный логотип (png/ico из репозитория проекта)
}

internal enum ToolState { Checking, NotInstalled, Installed, UpdateAvailable, Script }

internal class ToolStatus
{
    public bool Present;
    public bool Update;
    public bool Script;
    public string Path;
    public Image Icon;
}

internal class CategoryInfo
{
    public readonly string Name;
    public readonly string Glyph;
    public readonly Color Color;

    public CategoryInfo(string name, string glyph, int rgb)
    {
        Name = name;
        Glyph = glyph;
        Color = Color.FromArgb(255, Color.FromArgb(rgb));
    }
}

internal static class Glyphs
{
    public const string Search = "";
    public const string Refresh = "";
    public const string Log = "";
    public const string Info = "";
    public const string Folder = "";
    public const string Download = "";
    public const string Globe = "";
    public const string Palette = "";
    public const string Document = "";
    public const string Pulse = "";
    public const string Chip = "";
    public const string Mouse = "";
    public const string Usb = "";
    public const string Code = "";
    public const string Sliders = "";
    public const string Key = "";
    public const string Gear = "";
    public const string Bolt = "";
    public const string Shield = "";
    public const string Thermo = "";
    public const string Gears = "";
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.Init(null);
        AppSettings.Load();
        Application.Run(new MainForm());
    }
}

// ============================================================================================
//  Настройки (swissbox.ini рядом с exe)
// ============================================================================================

internal static class AppSettings
{
    private static readonly Dictionary<string, string> Values = new Dictionary<string, string>();

    private static string FilePath
    {
        get { return Path.Combine(Application.StartupPath, "swissbox.ini"); }
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) Values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }
        catch { }
    }

    public static bool GetBool(string key)
    {
        string v;
        return Values.TryGetValue(key, out v) && v == "1";
    }

    public static void SetBool(string key, bool value)
    {
        Values[key] = value ? "1" : "0";
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in Values) sb.AppendLine(kv.Key + "=" + kv.Value);
            File.WriteAllText(FilePath, sb.ToString());
        }
        catch { }
    }
}

// ============================================================================================
//  Тема: цвета и шрифты в духе macOS, светлая/тёмная — как в Windows
// ============================================================================================

internal static class Theme
{
    public static bool Dark;
    public static bool WindowActive = true;

    public static Color WindowBg, SidebarBg, SidebarBorder, GroupBg, GroupBorder, Separator;
    public static Color TextPrimary, TextSecondary, TextTertiary, Accent;
    public static Color PillBg, PillBgHover, PillBgDown, Selection, SelectionInactive, SidebarHover, SearchBg, LogBg;
    public static Color Success, Warning, Danger, ToggleOn, ToggleOff, IconHover, LightsInactive;

    public static Font HeaderFont, HeroFont, RowTitleFont, BodyFont, CaptionFont, BadgeFont;
    public static Font SidebarFont, PillFont, SectionFont, MonoFont;
    public static string IconFamily;

    public static void Init(bool? forceDark)
    {
        Dark = forceDark.HasValue ? forceDark.Value : SystemPrefersDark();

        if (Dark)
        {
            WindowBg = Rgb(0x1C1C1E); SidebarBg = Rgb(0x28282B); SidebarBorder = Rgb(0x0C0C0E);
            GroupBg = Rgb(0x2C2C2E); GroupBorder = Rgb(0x343437); Separator = Rgb(0x3A3A3D);
            TextPrimary = Rgb(0xF2F2F7); TextSecondary = Rgb(0xA1A1A6); TextTertiary = Rgb(0x6E6E73);
            Accent = Rgb(0x0A84FF);
            PillBg = Rgb(0x3A3A3C); PillBgHover = Rgb(0x464649); PillBgDown = Rgb(0x303032);
            Selection = Rgb(0x0A84FF); SelectionInactive = Rgb(0x48484B);
            SidebarHover = Rgb(0x333336); SearchBg = Rgb(0x38383B); LogBg = Rgb(0x222224);
            Success = Rgb(0x30D158); Warning = Rgb(0xFF9F0A); Danger = Rgb(0xFF453A);
            ToggleOn = Rgb(0x30D158); ToggleOff = Rgb(0x48484B);
            IconHover = Rgb(0x2C2C2F); LightsInactive = Rgb(0x4A4A4D);
        }
        else
        {
            WindowBg = Rgb(0xF4F4F7); SidebarBg = Rgb(0xE8E8ED); SidebarBorder = Rgb(0xD1D1D6);
            GroupBg = Rgb(0xFFFFFF); GroupBorder = Rgb(0xE1E1E6); Separator = Rgb(0xE6E6EB);
            TextPrimary = Rgb(0x1D1D1F); TextSecondary = Rgb(0x6E6E73); TextTertiary = Rgb(0xA1A1A6);
            Accent = Rgb(0x007AFF);
            PillBg = Rgb(0xEDEDF2); PillBgHover = Rgb(0xE1E1E7); PillBgDown = Rgb(0xD5D5DC);
            Selection = Rgb(0x007AFF); SelectionInactive = Rgb(0xD1D1D7);
            SidebarHover = Rgb(0xDDDDE3); SearchBg = Rgb(0xDADAE0); LogBg = Rgb(0xFFFFFF);
            Success = Rgb(0x248A3D); Warning = Rgb(0xC45A00); Danger = Rgb(0xD70015);
            ToggleOn = Rgb(0x34C759); ToggleOff = Rgb(0xDADADF);
            IconHover = Rgb(0xE6E6EB); LightsInactive = Rgb(0xCFCFD4);
        }

        IconFamily = HasFamily("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        HeaderFont = MakeFont(14f, "Segoe UI Variable Display Semib", "Segoe UI Semibold");
        HeroFont = MakeFont(21f, "Segoe UI Variable Display Semib", "Segoe UI Semibold");
        RowTitleFont = MakeFont(10.5f, "Segoe UI Variable Text Semibold", "Segoe UI Semibold");
        BodyFont = MakeFont(9.25f, "Segoe UI Variable Text", "Segoe UI");
        CaptionFont = MakeFont(8.5f, "Segoe UI Variable Small", "Segoe UI");
        BadgeFont = MakeFont(7.5f, "Segoe UI Variable Small Semibol", "Segoe UI Semibold");
        SidebarFont = MakeFont(10f, "Segoe UI Variable Text", "Segoe UI");
        PillFont = MakeFont(9.25f, "Segoe UI Variable Text Semibold", "Segoe UI Semibold");
        SectionFont = MakeFont(10f, "Segoe UI Variable Text Semibold", "Segoe UI Semibold");
        MonoFont = MakeFont(8.5f, "Cascadia Mono", "Consolas");
    }

    public static Color CurrentSelection
    {
        get { return WindowActive ? Selection : SelectionInactive; }
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                return v is int && (int)v == 0;
            }
        }
        catch { return false; }
    }

    private static bool HasFamily(string name)
    {
        try { using (new FontFamily(name)) { } return true; }
        catch { return false; }
    }

    private static Font MakeFont(float size, params string[] families)
    {
        foreach (var f in families)
            if (HasFamily(f)) return new Font(f, size, FontStyle.Regular, GraphicsUnit.Point);
        return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static Color Rgb(int rgb)
    {
        return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }
}

// ============================================================================================
//  Графика
// ============================================================================================

internal static class Gfx
{
    public static void HQ(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Суперэллипс — форма иконок Apple (непрерывная кривизна углов).
    public static GraphicsPath Squircle(RectangleF r, double n)
    {
        int steps = r.Width > 64 ? 360 : 120;
        var pts = new PointF[steps];
        double a = r.Width / 2.0, b = r.Height / 2.0, cx = r.X + a, cy = r.Y + b;
        for (int i = 0; i < steps; i++)
        {
            double t = 2 * Math.PI * i / steps;
            double c = Math.Cos(t), s = Math.Sin(t);
            pts[i] = new PointF(
                (float)(cx + a * Math.Sign(c) * Math.Pow(Math.Abs(c), 2.0 / n)),
                (float)(cy + b * Math.Sign(s) * Math.Pow(Math.Abs(s), 2.0 / n)));
        }
        var p = new GraphicsPath();
        p.AddPolygon(pts);
        return p;
    }

    public static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static Color Lighten(Color c, float t) { return Mix(c, Color.White, t); }
    public static Color Darken(Color c, float t) { return Mix(c, Color.Black, t); }

    public static void DrawGlyph(Graphics g, string glyph, float pixelSize, Color color, RectangleF rect)
    {
        var old = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var f = new Font(Theme.IconFamily, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var b = new SolidBrush(color))
        using (var sf = new StringFormat(StringFormat.GenericTypographic))
        {
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            g.DrawString(glyph, f, b, rect, sf);
        }
        g.TextRenderingHint = old;
    }

    // Цветная «плашка» со значком как в Системных настройках macOS.
    public static void DrawIconTile(Graphics g, Rectangle r, Color color, string glyph, float radius)
    {
        using (var path = RoundRect(r, radius))
        using (var lg = new LinearGradientBrush(r, Lighten(color, 0.16f), color, LinearGradientMode.Vertical))
            g.FillPath(lg, path);
        DrawGlyph(g, glyph, r.Height * 0.58f, Color.White, new RectangleF(r.X, r.Y + 0.5f, r.Width, r.Height));
    }
}

// ============================================================================================
//  Иконка приложения: красный сквиркл Big Sur со швейцарским крестом
// ============================================================================================

internal static class AppIconArt
{
    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            Gfx.HQ(g);
            g.Clear(Color.Transparent);
            bool small = size < 48;
            float pad = small ? size * 0.04f : size * 0.095f;
            float w = size - pad * 2;
            var body = new RectangleF(pad, pad - (small ? 0 : size * 0.012f), w, w);

            if (!small)
            {
                for (int i = 6; i >= 1; i--)
                {
                    float grow = size * 0.006f * i;
                    var sr = new RectangleF(body.X - grow, body.Y - grow + size * 0.022f, body.Width + grow * 2, body.Height + grow * 2);
                    using (var sp = Gfx.Squircle(sr, 5))
                    using (var sb = new SolidBrush(Color.FromArgb(8, 0, 0, 0)))
                        g.FillPath(sb, sp);
                }
            }

            using (var path = Gfx.Squircle(body, 5))
            {
                using (var lg = new LinearGradientBrush(body, Color.FromArgb(255, 110, 96), Color.FromArgb(212, 30, 26), LinearGradientMode.Vertical))
                    g.FillPath(lg, path);

                var state = g.Save();
                g.SetClip(path);
                var hl = new RectangleF(body.X - body.Width * 0.3f, body.Y - body.Height * 0.65f, body.Width * 1.6f, body.Height * 1.1f);
                using (var hp = new GraphicsPath())
                {
                    hp.AddEllipse(hl);
                    using (var pgb = new PathGradientBrush(hp))
                    {
                        pgb.CenterColor = Color.FromArgb(75, 255, 255, 255);
                        pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) };
                        g.FillPath(pgb, hp);
                    }
                }
                g.Restore(state);

                float cs = body.Width * (small ? 0.66f : 0.56f);
                float aw = cs * 0.3f;
                float cx = body.X + body.Width / 2, cy = body.Y + body.Height / 2;
                float rr = aw * 0.16f;
                if (!small)
                {
                    using (var sh = Cross(cx, cy + size * 0.016f, cs, aw, rr))
                    using (var sb = new SolidBrush(Color.FromArgb(80, 110, 0, 0)))
                        g.FillPath(sb, sh);
                }
                using (var cp = Cross(cx, cy, cs, aw, rr))
                using (var wb = new LinearGradientBrush(new RectangleF(cx - cs / 2, cy - cs / 2, cs, cs), Color.White, Color.FromArgb(236, 236, 240), LinearGradientMode.Vertical))
                    g.FillPath(wb, cp);

                if (!small)
                {
                    using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255), Math.Max(1f, size * 0.004f)))
                        g.DrawPath(pen, path);
                }
            }
        }
        return bmp;
    }

    private static GraphicsPath Cross(float cx, float cy, float cs, float aw, float rr)
    {
        var p = new GraphicsPath(FillMode.Winding);
        using (var h = Gfx.RoundRect(new RectangleF(cx - cs / 2, cy - aw / 2, cs, aw), rr))
        using (var v = Gfx.RoundRect(new RectangleF(cx - aw / 2, cy - cs / 2, aw, cs), rr))
        {
            p.AddPath(h, false);
            p.AddPath(v, false);
        }
        return p;
    }

    // .ico: маленькие размеры — DIB (читает всё подряд), большие — PNG.
    public static void WriteIco(string path, int[] sizes)
    {
        var blobs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (var bmp = Render(s))
            {
                if (s >= 128)
                {
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        blobs.Add(ms.ToArray());
                    }
                }
                else blobs.Add(ToDib(bmp));
            }
        }

        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0);
            bw.Write((short)1);
            bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(blobs[i].Length);
                bw.Write(offset);
                offset += blobs[i].Length;
            }
            foreach (var b in blobs) bw.Write(b);
        }
    }

    private static byte[] ToDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new byte[w * 4];
            for (int y = h - 1; y >= 0; y--)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                bw.Write(row);
            }
            bmp.UnlockBits(data);
            var mask = new byte[maskStride];
            for (int y = 0; y < h; y++) bw.Write(mask);
            return ms.ToArray();
        }
    }

    public static Icon LoadWindowIcon()
    {
        try
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SwissBox.ico"))
                if (s != null) return new Icon(s);
        }
        catch { }
        try
        {
            using (var bmp = Render(64)) return Icon.FromHandle(bmp.GetHicon());
        }
        catch { return null; }
    }
}

// Заглушка-иконка (буква на цветном сквиркле), пока у программы нет своей.
internal static class LetterIcon
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb(10, 132, 255), Color.FromArgb(94, 92, 230), Color.FromArgb(191, 90, 242),
        Color.FromArgb(255, 55, 95), Color.FromArgb(255, 69, 58), Color.FromArgb(255, 159, 10),
        Color.FromArgb(48, 209, 88), Color.FromArgb(64, 200, 224), Color.FromArgb(172, 142, 104),
        Color.FromArgb(50, 173, 230)
    };

    public static void Draw(Graphics g, Rectangle r, string name)
    {
        int h = 0;
        foreach (char ch in name) h = unchecked(h * 31 + ch);
        Color c = Palette[(h & 0x7FFFFFFF) % Palette.Length];

        using (var path = Gfx.Squircle(r, 5))
        using (var lg = new LinearGradientBrush(r, Gfx.Lighten(c, 0.2f), Gfx.Darken(c, 0.06f), LinearGradientMode.Vertical))
            g.FillPath(lg, path);

        string letter = "?";
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                letter = char.ToUpperInvariant(ch).ToString();
                break;
            }
        }

        var old = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var f = new Font(Theme.RowTitleFont.FontFamily, r.Height * 0.46f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(letter, f, Brushes.White, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), sf);
        g.TextRenderingHint = old;
    }
}

internal static class IconTone
{
    private static readonly Dictionary<Image, bool> Cache = new Dictionary<Image, bool>();

    public static bool IsDark(Image img)
    {
        bool dark;
        if (Cache.TryGetValue(img, out dark)) return dark;
        double sum = 0;
        int count = 0;
        try
        {
            using (var small = new Bitmap(img, 24, 24))
            {
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 24; x++)
                    {
                        var c = small.GetPixel(x, y);
                        if (c.A < 128) continue;
                        sum += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                        count++;
                    }
            }
        }
        catch { }
        dark = count > 0 && sum / count < 60;
        Cache[img] = dark;
        return dark;
    }
}

internal static class IconLoader
{
    // png/jpg/ico с диска; у .ico берётся самый крупный кадр, в том числе PNG-сжатый.
    public static Image LoadImageFile(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length > 6 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0) return LoadIco(data);
            using (var ms = new MemoryStream(data))
            using (var img = Image.FromStream(ms)) return new Bitmap(img);
        }
        catch { return null; }
    }

    private static Image LoadIco(byte[] data)
    {
        int count = BitConverter.ToUInt16(data, 4);
        int best = -1, bestSize = -1, bestBpp = -1;
        for (int i = 0; i < count; i++)
        {
            int e = 6 + i * 16;
            int w = data[e] == 0 ? 256 : data[e];
            int bpp = BitConverter.ToUInt16(data, e + 6);
            if (w > bestSize || (w == bestSize && bpp > bestBpp))
            {
                best = i;
                bestSize = w;
                bestBpp = bpp;
            }
        }
        if (best < 0) return null;
        int be = 6 + best * 16;
        int len = BitConverter.ToInt32(data, be + 8);
        int off = BitConverter.ToInt32(data, be + 12);
        var frame = new byte[len];
        Buffer.BlockCopy(data, off, frame, 0, len);

        if (len > 8 && frame[0] == 0x89 && frame[1] == 0x50 && frame[2] == 0x4E && frame[3] == 0x47)
        {
            using (var ms = new MemoryStream(frame))
            using (var img = Image.FromStream(ms)) return new Bitmap(img);
        }
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)1);
            bw.Write(data, be, 8);
            bw.Write(len);
            bw.Write(22);
            bw.Write(frame);
            ms.Position = 0;
            using (var ico = new Icon(ms)) return ico.ToBitmap();
        }
    }

    public static Image Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var handles = new IntPtr[1];
                var ids = new uint[1];
                uint n = Native.PrivateExtractIcons(path, 0, 64, 64, handles, ids, 1, 0);
                if (n == 0 || n == 0xFFFFFFFF || handles[0] == IntPtr.Zero) return null; // своей иконки нет
                try
                {
                    using (var ico = Icon.FromHandle(handles[0])) return ico.ToBitmap();
                }
                finally { Native.DestroyIcon(handles[0]); }
            }
            using (var ico = Icon.ExtractAssociatedIcon(path)) return ico == null ? null : ico.ToBitmap();
        }
        catch { return null; }
    }
}

internal static class Native
{
    public const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
    public const int HTCLIENT = 1, HTCAPTION = 2, HTTRANSPARENT = -1, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    public const int SM_CYFRAME = 33, SM_CXPADDEDBORDER = 92;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct NCCALCSIZE_PARAMS { public RECT rgrc0, rgrc1, rgrc2; public IntPtr lppos; }

    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

    public static void DarkScrollbars(Control c)
    {
        if (!Theme.Dark) return;
        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    }
}

// Один общий индекс exe и ярлыков из Program Files, AppData\Local и «Пуска» — строится один раз
// на всех, вместо того чтобы каждая программа заново обходила диск.
internal static class ExeIndex
{
    private static readonly object Gate = new object();
    private static Dictionary<string, List<string>> _byName;
    private static readonly string[] SkipInLocalAppData = { "packages", "temp", "microsoft", "crashdumps", "d3dscache" };

    public static void Invalidate()
    {
        lock (Gate) _byName = null;
    }

    public static string Find(string hint)
    {
        var map = Get();
        List<string> hits;
        if (map.TryGetValue(hint + ".exe", out hits)) return hits[0];

        string h = hint.ToLowerInvariant();
        string best = null;
        foreach (var kv in map)
        {
            string name = kv.Key.ToLowerInvariant();
            if (!name.EndsWith(".exe") || !name.StartsWith(h)) continue;
            if (name.Contains("64")) return kv.Value[0];
            if (best == null) best = kv.Value[0];
        }
        if (best != null) return best;

        foreach (var kv in map)
        {
            string name = kv.Key.ToLowerInvariant();
            if (name.EndsWith(".lnk") && name.Contains(h)) return kv.Value[0];
        }
        return null;
    }

    private static Dictionary<string, List<string>> Get()
    {
        lock (Gate)
        {
            if (_byName != null) return _byName;
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Scan(map, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 3, "*.exe", null);
            Scan(map, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 3, "*.exe", null);
            Scan(map, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 2, "*.exe", SkipInLocalAppData);
            Scan(map, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), 3, "*.lnk", null);
            Scan(map, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), 3, "*.lnk", null);
            _byName = map;
            return map;
        }
    }

    private static void Scan(Dictionary<string, List<string>> map, string dir, int depth, string pattern, string[] skip)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, pattern))
            {
                string name = Path.GetFileName(f);
                List<string> list;
                if (!map.TryGetValue(name, out list)) map[name] = list = new List<string>();
                list.Add(f);
            }
            if (depth <= 0) return;
            var subdirs = new List<string>(Directory.EnumerateDirectories(dir));
            subdirs.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            foreach (var d in subdirs)
            {
                try
                {
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                    if (skip != null && Array.IndexOf(skip, Path.GetFileName(d).ToLowerInvariant()) >= 0) continue;
                }
                catch { continue; }
                Scan(map, d, depth - 1, pattern, null);
            }
        }
        catch { }
    }

    private static int Rank(string dir)
    {
        string n = Path.GetFileName(dir).ToLowerInvariant();
        if (n.Contains("arm")) return 3;
        if (n.Contains("64")) return 0;
        if (n.Contains("32") || n.Contains("86")) return 2;
        return 1;
    }
}

// ============================================================================================
//  Контролы
// ============================================================================================

// Панель, через которую клики «проваливаются» в окно — так работает перетаскивание окна
// за пустые места сайдбара и заголовка.
internal class ChromePanel : Panel
{
    public Func<Point, bool> PassThrough;

    public ChromePanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_NCHITTEST && PassThrough != null)
        {
            long lp = m.LParam.ToInt64();
            var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            if (PassThrough(pt))
            {
                m.Result = (IntPtr)Native.HTTRANSPARENT;
                return;
            }
        }
        base.WndProc(ref m);
    }
}

internal abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
    }
}

internal class TrafficLights : PaintedControl
{
    private bool _hover;
    public event EventHandler CloseClicked, MinimizeClicked, ZoomClicked;

    public TrafficLights()
    {
        Size = new Size(54, 14);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        int i = e.X / 20;
        EventHandler h = i == 0 ? CloseClicked : i == 1 ? MinimizeClicked : ZoomClicked;
        if (h != null) h(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.HQ(g);
        Color[] fill = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
        bool lit = Theme.WindowActive || _hover;
        for (int i = 0; i < 3; i++)
        {
            var r = new RectangleF(i * 20 + 0.5f, 0.5f, 12, 12);
            Color c = lit ? fill[i] : Theme.LightsInactive;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            using (var p = new Pen(Gfx.Darken(c, 0.12f), 0.8f)) g.DrawEllipse(p, r);
            if (!_hover) continue;

            float cx = r.X + 6, cy = r.Y + 6;
            using (var pen = new Pen(Color.FromArgb(150, 60, 10, 10), 1.3f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (i == 0)
                {
                    g.DrawLine(pen, cx - 2.5f, cy - 2.5f, cx + 2.5f, cy + 2.5f);
                    g.DrawLine(pen, cx - 2.5f, cy + 2.5f, cx + 2.5f, cy - 2.5f);
                }
                else if (i == 1) g.DrawLine(pen, cx - 3f, cy, cx + 3f, cy);
                else
                {
                    g.DrawLine(pen, cx - 3f, cy, cx + 3f, cy);
                    g.DrawLine(pen, cx, cy - 3f, cx, cy + 3f);
                }
            }
        }
    }
}

internal class SidebarItem : PaintedControl
{
    public string Glyph;
    public Color IconColor;
    public bool Selected;
    public int Count;
    public bool Attention;
    private bool _hover;

    public SidebarItem()
    {
        Height = 28;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.SidebarBg);
        Gfx.HQ(g);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        bool strongSelection = Selected && Theme.WindowActive;
        if (Selected || _hover)
        {
            using (var p = Gfx.RoundRect(rect, 6))
            using (var b = new SolidBrush(Selected ? Theme.CurrentSelection : Theme.SidebarHover))
                g.FillPath(b, p);
        }

        var tile = new Rectangle(7, (Height - 20) / 2, 20, 20);
        Gfx.DrawIconTile(g, tile, IconColor, Glyph, 5);
        if (strongSelection)
        {
            using (var p = Gfx.RoundRect(new RectangleF(tile.X - 0.5f, tile.Y - 0.5f, tile.Width + 1, tile.Height + 1), 5.5f))
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f))
                g.DrawPath(pen, p);
        }

        Color textColor = strongSelection ? Color.White : Theme.TextPrimary;
        int right = Width - 8;
        if (Count > 0)
        {
            string cnt = Count.ToString();
            Size cs = TextRenderer.MeasureText(cnt, Theme.CaptionFont, Size.Empty, TextFormatFlags.NoPadding);
            Color cc = strongSelection ? Gfx.Mix(Theme.Selection, Color.White, 0.75f) : Theme.TextTertiary;
            TextRenderer.DrawText(g, cnt, Theme.CaptionFont, new Rectangle(right - cs.Width, 0, cs.Width, Height), cc,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            right -= cs.Width + 8;
        }
        if (Attention)
        {
            Color dot = strongSelection ? Color.White : Theme.Warning;
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, right - 7, Height / 2f - 3.5f, 7, 7);
            right -= 14;
        }

        TextRenderer.DrawText(g, Text, Theme.SidebarFont, new Rectangle(36, 0, right - 36, Height), textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

internal class SearchField : PaintedControl
{
    public readonly TextBox Box;

    public SearchField()
    {
        Height = 30;
        Cursor = Cursors.IBeam;
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SearchBg,
            ForeColor = Theme.TextPrimary,
            Font = Theme.SidebarFont
        };
        Box.HandleCreated += (s, e) => Native.SendMessage(Box.Handle, 0x1501, (IntPtr)1, "Поиск");
        Controls.Add(Box);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Box.Focus();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Box == null) return;
        Box.Location = new Point(30, (Height - Box.Height) / 2 + 1);
        Box.Width = Width - 38;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.SidebarBg);
        Gfx.HQ(g);
        using (var p = Gfx.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 8))
        using (var b = new SolidBrush(Theme.SearchBg))
            g.FillPath(b, p);
        Gfx.DrawGlyph(g, Glyphs.Search, 13, Theme.TextTertiary, new RectangleF(6, 0, 20, Height));
    }
}

internal class PillButton : PaintedControl
{
    public bool Filled;
    public int MinWidth = 84;
    private int _progress = -1;
    private bool _busy, _hover, _down;
    private float _spin;
    private readonly Timer _timer;

    public PillButton()
    {
        Font = Theme.PillFont;
        Height = 28;
        Width = MinWidth;
        Cursor = Cursors.Hand;
        _timer = new Timer { Interval = 16 };
        _timer.Tick += (s, e) => { _spin = (_spin + 8f) % 360f; Invalidate(); };
    }

    public bool Working
    {
        get { return _busy || _progress >= 0; }
    }

    public int Progress
    {
        get { return _progress; }
        set
        {
            if (value < 0)
            {
                Busy = true;
                return;
            }
            _progress = Math.Min(100, value);
            _busy = false;
            _timer.Enabled = false;
            Invalidate();
        }
    }

    public bool Busy
    {
        get { return _busy; }
        set
        {
            _busy = value;
            if (value) _progress = -1;
            _timer.Enabled = value;
            Invalidate();
        }
    }

    public void Idle()
    {
        _busy = false;
        _progress = -1;
        _timer.Enabled = false;
        Invalidate();
    }

    public void FitToText()
    {
        int tw = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int w = Math.Max(MinWidth, tw + 30);
        if (Width != w) Width = w;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitToText();
        if (Parent != null) Parent.PerformLayout();
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.HQ(g);

        if (Working)
        {
            float d = Height - 6;
            var rr = new RectangleF(Width - d - 6, (Height - d) / 2f, d, d);
            using (var track = new Pen(Theme.Dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0), 2.6f))
                g.DrawEllipse(track, rr);
            using (var arc = new Pen(Theme.Accent, 2.6f))
            {
                arc.StartCap = arc.EndCap = LineCap.Round;
                if (_progress >= 0)
                {
                    if (_progress > 0) g.DrawArc(arc, rr, -90, 360f * _progress / 100f);
                    using (var sq = new SolidBrush(Theme.Accent))
                        g.FillRectangle(sq, rr.X + d / 2 - 3, rr.Y + d / 2 - 3, 6, 6);
                }
                else g.DrawArc(arc, rr, _spin - 90, 100);
            }
            return;
        }

        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Color bg, fg;
        if (Filled)
        {
            bg = _down ? Gfx.Darken(Theme.Accent, 0.15f) : _hover ? Gfx.Lighten(Theme.Accent, 0.1f) : Theme.Accent;
            fg = Color.White;
        }
        else
        {
            bg = _down ? Theme.PillBgDown : _hover ? Theme.PillBgHover : Theme.PillBg;
            fg = Theme.Accent;
        }
        if (!Enabled) fg = Gfx.Mix(Theme.TextTertiary, bg, 0.2f);

        using (var p = Gfx.RoundRect(r, r.Height / 2))
        using (var b = new SolidBrush(bg))
            g.FillPath(b, p);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}

internal class ToggleSwitch : PaintedControl
{
    private bool _checked;
    private float _pos;
    private readonly Timer _anim;
    public event EventHandler CheckedChanged;

    public ToggleSwitch()
    {
        Size = new Size(40, 24);
        Cursor = Cursors.Hand;
        _anim = new Timer { Interval = 15 };
        _anim.Tick += (s, e) =>
        {
            float target = _checked ? 1f : 0f;
            _pos += (target - _pos) * 0.35f;
            if (Math.Abs(target - _pos) < 0.02f)
            {
                _pos = target;
                _anim.Stop();
            }
            Invalidate();
        };
    }

    public bool Checked
    {
        get { return _checked; }
        set
        {
            if (_checked == value) return;
            _checked = value;
            _anim.Start();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }
    }

    public void SetSilently(bool value)
    {
        _checked = value;
        _pos = value ? 1f : 0f;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Enabled) Checked = !Checked;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.HQ(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var p = Gfx.RoundRect(r, r.Height / 2))
        using (var b = new SolidBrush(Gfx.Mix(Theme.ToggleOff, Theme.ToggleOn, _pos)))
            g.FillPath(b, p);
        float d = Height - 4;
        float x = 2 + _pos * (Width - Height);
        using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(sh, x, 2.8f, d, d);
        using (var kb = new SolidBrush(Color.White)) g.FillEllipse(kb, x, 2, d, d);
        if (!Enabled)
            using (var veil = new SolidBrush(Color.FromArgb(110, BackColor))) g.FillRectangle(veil, ClientRectangle);
    }
}

internal class IconButton : PaintedControl
{
    public string Glyph;
    public bool Toggled;
    private bool _hover;

    public IconButton(string glyph)
    {
        Glyph = glyph;
        Size = new Size(32, 30);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.HQ(g);
        if (_hover || Toggled)
        {
            using (var p = Gfx.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 7))
            using (var b = new SolidBrush(Theme.IconHover))
                g.FillPath(b, p);
        }
        Gfx.DrawGlyph(g, Glyph, 15, Toggled ? Theme.Accent : Theme.TextSecondary, new RectangleF(0, 0, Width, Height));
    }
}

internal abstract class RowBase : PaintedControl
{
    public bool ShowSeparator = true;
    protected int SeparatorInset = 16;

    protected RowBase()
    {
        BackColor = Theme.GroupBg;
    }

    public virtual int HeightFor(int width) { return Height; }

    protected void DrawSeparator(Graphics g)
    {
        if (!ShowSeparator) return;
        using (var pen = new Pen(Theme.Separator)) g.DrawLine(pen, SeparatorInset, Height - 1, Width, Height - 1);
    }
}

// Строка программы в стиле App Store: иконка, имя, бейдж статуса, описание, капсула-кнопка.
internal class ToolRow : RowBase
{
    public readonly ToolEntry Tool;
    public readonly PillButton Button;
    public ToolState State = ToolState.Checking;
    public Image AppIcon;

    private const int IconSize = 44, TextX = 76;

    public ToolRow(ToolEntry tool)
    {
        Tool = tool;
        Height = 78;
        SeparatorInset = TextX;
        Button = new PillButton { Text = "···", BackColor = Theme.GroupBg, Enabled = false };
        Controls.Add(Button);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Button == null) return;
        Button.FitToText();
        Button.Location = new Point(Width - Button.Width - 16, (Height - Button.Height) / 2);
    }

    private string Description
    {
        get { return Tool.Desc; }
    }

    private void Badge(out string text, out Color color)
    {
        text = null;
        color = Theme.TextTertiary;
        switch (State)
        {
            case ToolState.Installed: text = "Установлено"; color = Theme.Success; break;
            case ToolState.UpdateAvailable: text = "Обновление"; color = Theme.Warning; break;
            case ToolState.Script: text = Tool.Action == "web" ? "Сайт" : "Скрипт"; color = Theme.TextSecondary; break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.GroupBg);
        Gfx.HQ(g);

        var iconRect = new Rectangle(18, (Height - IconSize) / 2, IconSize, IconSize);
        if (AppIcon != null)
        {
            // тёмный логотип на тёмной теме не видно — подкладываем светлую плашку
            if (Theme.Dark && IconTone.IsDark(AppIcon))
            {
                using (var p = Gfx.RoundRect(new RectangleF(iconRect.X - 1, iconRect.Y - 1, iconRect.Width + 2, iconRect.Height + 2), 11))
                using (var b = new SolidBrush(Color.FromArgb(232, 232, 237)))
                    g.FillPath(b, p);
                g.DrawImage(AppIcon, Rectangle.Inflate(iconRect, -5, -5));
            }
            else g.DrawImage(AppIcon, iconRect);
        }
        else LetterIcon.Draw(g, iconRect, Tool.Name);

        int textRight = Button.Left - 18;
        int textW = Math.Max(60, textRight - TextX);

        string badge;
        Color badgeColor;
        Badge(out badge, out badgeColor);
        int badgeW = 0;
        if (badge != null)
            badgeW = TextRenderer.MeasureText(badge, Theme.BadgeFont, Size.Empty, TextFormatFlags.NoPadding).Width + 14;

        var titleFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        int titleMax = Math.Max(30, textW - (badge != null ? badgeW + 8 : 0));
        int titleW = Math.Min(titleMax, TextRenderer.MeasureText(g, Tool.Name, Theme.RowTitleFont, new Size(titleMax, 40), titleFlags).Width);
        TextRenderer.DrawText(g, Tool.Name, Theme.RowTitleFont, new Rectangle(TextX, 14, titleW + 1, 22), Theme.TextPrimary, titleFlags);

        if (badge != null)
        {
            var br = new RectangleF(TextX + titleW + 8, 16.5f, badgeW, 17);
            using (var p = Gfx.RoundRect(br, 8.5f))
            using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 48 : 30, badgeColor)))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, badge, Theme.BadgeFont, Rectangle.Round(br), badgeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        TextRenderer.DrawText(g, Description, Theme.BodyFont, new Rectangle(TextX, 38, textW, 34), Theme.TextSecondary,
            TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis |
            TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);

        DrawSeparator(g);
    }
}

// Строка настроек: заголовок, пояснение, справа переключатель/кнопка, снизу — ряд кнопок.
internal class InfoRow : RowBase
{
    public string Title;
    private string _subtitle;
    private Control _accessory;
    private readonly List<Control> _bottom = new List<Control>();
    private const int PadX = 16, PadTop = 13, PadBottom = 14;

    public InfoRow(string title, string subtitle)
    {
        Title = title;
        _subtitle = subtitle;
        Height = 60;
    }

    public string Subtitle
    {
        get { return _subtitle; }
        set
        {
            _subtitle = value;
            var gp = Parent as GroupPanel;
            if (gp != null) gp.Relayout();
            Invalidate();
        }
    }

    public void SetAccessory(Control c)
    {
        _accessory = c;
        c.BackColor = Theme.GroupBg;
        Controls.Add(c);
    }

    public void AddBottom(Control c)
    {
        c.BackColor = Theme.GroupBg;
        _bottom.Add(c);
        Controls.Add(c);
    }

    private int TextWidth(int width)
    {
        return Math.Max(80, width - PadX * 2 - (_accessory != null ? _accessory.Width + 20 : 0));
    }

    private static int TitleHeight
    {
        get { return TextRenderer.MeasureText("Ag", Theme.RowTitleFont, Size.Empty, TextFormatFlags.NoPadding).Height; }
    }

    private int SubtitleHeight(int width)
    {
        if (string.IsNullOrEmpty(_subtitle)) return 0;
        return TextRenderer.MeasureText(_subtitle, Theme.BodyFont, new Size(TextWidth(width), 2000),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl).Height;
    }

    private int TextBlockHeight(int width)
    {
        int sub = SubtitleHeight(width);
        return TitleHeight + (sub > 0 ? 4 + sub : 0);
    }

    public override int HeightFor(int width)
    {
        int h = PadTop + TextBlockHeight(width);
        if (_bottom.Count > 0) h += 12 + 28;
        return h + PadBottom;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int block = TextBlockHeight(Width);
        if (_accessory != null)
        {
            var pb = _accessory as PillButton;
            if (pb != null) pb.FitToText();
            _accessory.Location = new Point(Width - PadX - _accessory.Width, PadTop + Math.Max(0, (block - _accessory.Height) / 2));
        }
        int x = PadX, y = PadTop + block + 12;
        foreach (var c in _bottom)
        {
            var pb = c as PillButton;
            if (pb != null) pb.FitToText();
            c.Location = new Point(x, y);
            x += c.Width + 8;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.GroupBg);
        int tw = TextWidth(Width);
        TextRenderer.DrawText(g, Title, Theme.RowTitleFont, new Rectangle(PadX, PadTop, tw, TitleHeight + 2), Theme.TextPrimary,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        int sub = SubtitleHeight(Width);
        if (sub > 0)
        {
            TextRenderer.DrawText(g, _subtitle, Theme.BodyFont, new Rectangle(PadX, PadTop + TitleHeight + 4, tw, sub + 2), Theme.TextSecondary,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
        }
        DrawSeparator(g);
    }
}

// Сгруппированный список со скруглёнными углами, как в Системных настройках.
internal class GroupPanel : Panel
{
    private const int Radius = 11, Inset = 6;
    private readonly List<RowBase> _rows = new List<RowBase>();
    private bool _inLayout;

    public GroupPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.WindowBg;
        Margin = new Padding(0, 0, 0, 24);
    }

    public IList<RowBase> Rows
    {
        get { return _rows; }
    }

    public void AddRow(RowBase row)
    {
        _rows.Add(row);
        Controls.Add(row);
        Relayout();
    }

    public void Relayout()
    {
        if (_inLayout) return;
        _inLayout = true;
        try
        {
            int y = Inset;
            int w = Math.Max(10, Width - 2);
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                r.ShowSeparator = i < _rows.Count - 1;
                int h = r.HeightFor(w);
                r.SetBounds(1, y, w, h);
                r.Invalidate();
                y += h;
            }
            int total = y + Inset;
            if (Height != total) Height = total;
        }
        finally { _inLayout = false; }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.HQ(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var p = Gfx.RoundRect(r, Radius))
        {
            using (var b = new SolidBrush(Theme.GroupBg)) g.FillPath(b, p);
            using (var pen = new Pen(Theme.GroupBorder)) g.DrawPath(pen, p);
        }
    }
}

internal class SectionHeader : PaintedControl
{
    public SectionHeader(string text)
    {
        Text = text;
        Height = 30;
        Margin = new Padding(0, 2, 0, 0);
        BackColor = Theme.WindowBg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        TextRenderer.DrawText(e.Graphics, Text, Theme.SectionFont, new Rectangle(6, 0, Width - 12, Height - 8), Theme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

internal class AboutHero : PaintedControl
{
    private readonly Bitmap _icon = AppIconArt.Render(256);

    public AboutHero()
    {
        Height = 270;
        Margin = new Padding(0, 6, 0, 10);
        BackColor = Theme.WindowBg;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.HQ(g);
        int size = 128;
        g.DrawImage(_icon, new Rectangle((Width - size) / 2, 8, size, size));
        TextRenderer.DrawText(g, "SwissBox", Theme.HeroFont, new Rectangle(0, 146, Width, 38), Theme.TextPrimary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, "Версия " + AppInfo.Version, Theme.CaptionFont, new Rectangle(0, 184, Width, 18), Theme.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
        int tw = Math.Min(Width - 40, 470);
        TextRenderer.DrawText(g, "Все полезные открытые утилиты в одном окне. Одна кнопка — программа скачана, обновлена и открыта.",
            Theme.BodyFont, new Rectangle((Width - tw) / 2, 212, tw, 44), Theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

internal class EmptyState : PaintedControl
{
    private readonly string _title, _subtitle;

    public EmptyState(string title, string subtitle)
    {
        _title = title;
        _subtitle = subtitle;
        Height = 180;
        BackColor = Theme.WindowBg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.HQ(g);
        Gfx.DrawGlyph(g, Glyphs.Search, 34, Theme.TextTertiary, new RectangleF(0, 30, Width, 50));
        TextRenderer.DrawText(g, _title, Theme.RowTitleFont, new Rectangle(0, 92, Width, 24), Theme.TextPrimary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, _subtitle, Theme.BodyFont, new Rectangle(0, 118, Width, 22), Theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
    }
}

internal class ScrollPage : FlowLayoutPanel
{
    public ScrollPage()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Dock = DockStyle.Fill;
        Padding = new Padding(28, 2, 28, 28);
        BackColor = Theme.WindowBg;
        DoubleBuffered = true;
        Visible = false;
    }

    public void FitChildren()
    {
        int w = Math.Max(240, ClientSize.Width - Padding.Horizontal);
        foreach (Control c in Controls)
        {
            c.Margin = new Padding(0, c.Margin.Top, 0, c.Margin.Bottom);
            if (c.Width != w) c.Width = w;
        }
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        FitChildren();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        FitChildren();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkScrollbars(this);
    }
}

internal class HeaderBar : ChromePanel
{
    public string Title = "";
    public string Status = "";
    public readonly IconButton RefreshButton = new IconButton(Glyphs.Refresh);
    public readonly IconButton LogButton = new IconButton(Glyphs.Log);

    public HeaderBar()
    {
        Dock = DockStyle.Top;
        Height = 58;
        BackColor = Theme.WindowBg;
        RefreshButton.BackColor = Theme.WindowBg;
        LogButton.BackColor = Theme.WindowBg;
        Controls.Add(RefreshButton);
        Controls.Add(LogButton);
        PassThrough = pt => true;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        LogButton.Location = new Point(Width - 18 - LogButton.Width, (Height - LogButton.Height) / 2 + 2);
        RefreshButton.Location = new Point(LogButton.Left - 4 - RefreshButton.Width, LogButton.Top);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        int titleW = TextRenderer.MeasureText(Title, Theme.HeaderFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, Title, Theme.HeaderFont, new Rectangle(28, 2, Math.Max(10, RefreshButton.Left - 40), Height), Theme.TextPrimary, flags);
        int sx = 28 + titleW + 28;
        int sw = RefreshButton.Left - 14 - sx;
        if (sw > 60 && !string.IsNullOrEmpty(Status))
            TextRenderer.DrawText(g, Status, Theme.CaptionFont, new Rectangle(sx, 3, sw, Height), Theme.TextTertiary, flags | TextFormatFlags.Right);
    }
}

// ============================================================================================
//  Каталог
// ============================================================================================

internal static class Catalog
{
    public static readonly CategoryInfo[] Categories =
    {
        new CategoryInfo("Сеть / Загрузки", Glyphs.Download, 0x0A84FF),
        new CategoryInfo("Обход блокировок", Glyphs.Globe, 0x34C759),
        new CategoryInfo("ИИ", Glyphs.Gears, 0x7D4CDB),
        new CategoryInfo("Разработка", Glyphs.Code, 0x007ACC),
        new CategoryInfo("Система", Glyphs.Gear, 0x8E8E93),
        new CategoryInfo("Удобство", Glyphs.Bolt, 0xFF9500),
        new CategoryInfo("Безопасность", Glyphs.Shield, 0x5856D6),
        new CategoryInfo("Графика / Видео", Glyphs.Palette, 0xFF2D55),
        new CategoryInfo("Офис (ArtCraft)", Glyphs.Document, 0xFFB000),
        new CategoryInfo("Диагностика", Glyphs.Pulse, 0xFF3B30),
        new CategoryInfo("Разгон и тесты", Glyphs.Thermo, 0xFF6B00),
        new CategoryInfo("Драйверы", Glyphs.Chip, 0x30B0C7),
        new CategoryInfo("Периферия", Glyphs.Mouse, 0xAF52DE),
        new CategoryInfo("Флешки / ISO", Glyphs.Usb, 0x32ADE6),
        new CategoryInfo("Рантаймы", Glyphs.Code, 0xA2845E),
        new CategoryInfo("Твики / Debloat", Glyphs.Sliders, 0x00C7BE),
        new CategoryInfo("Активация", Glyphs.Key, 0x636366),
    };

    private const string GH = "https://raw.githubusercontent.com/";

    // Официальные логотипы из репозиториев самих проектов (проверены вручную).
    private static readonly Dictionary<string, string> IconUrls = new Dictionary<string, string>
    {
        { "Ollama", GH + "ollama/ollama/main/app/assets/app.ico" },
        { "DeepSeek", "https://github.com/deepseek-ai.png?size=256" },
        { "Everything", "https://www.voidtools.com/favicon.ico" },
        { "WizTree", "https://cdn.jsdelivr.net/gh/teknowledgist/Chocolatey-packages@2b0f9ae47d47b9b699977be5d6a6c98f0f910180/Icons/wiztree_icon.png" },
        { "CPU-Z", "https://cdn.jsdelivr.net/gh/mkevenaar/chocolatey-packages@a2d2b28de250105f84f6a2d0123c435ba7667f57/icons/cpu-z.png" },
        { "HWiNFO", "https://cdn.jsdelivr.net/gh/aeliussaionji/chocopkgs@fad605e78a28a3a84f3abf5e38b0601fe7a32d90/icons/HWiNFO.png" },
        { "Logitech G HUB", "https://cdn.jsdelivr.net/gh/virtualex-itv/chocolatey-packages@96d8d5005c0b04d2f497379838c0ff8f3f941ce0/icons/lghub.png" },
        { "Sysinternals Suite", "https://cdn.jsdelivr.net/gh/chocolatey-community/chocolatey-packages@8a042fbe6c07391d0c2da13f638b1fdde474850f/icons/sysinternals.png" },
        { "Snappy Driver Installer Origin", "https://cdn.jsdelivr.net/gh/tunisiano187/Chocolatey-packages@40c3a3d4bd00e678297a08100b604627ecdaffcf/icons/sdio.png" },
        { "Visual C++ Redistributable x64", "https://cdn.jsdelivr.net/gh/chocolatey-community/chocolatey-packages@d8a28da83b3b9bf058e1fbbeed5ce74329b73245/icons/vcredist140.png" },
        { "Visual C++ Redistributable x86", "https://cdn.jsdelivr.net/gh/chocolatey-community/chocolatey-packages@d8a28da83b3b9bf058e1fbbeed5ce74329b73245/icons/vcredist140.png" },
        { "aria2", "https://cdn.jsdelivr.net/gh/tunisiano187/Chocolatey-packages@7cc8d197a0daa3297060d92a75902352d1cce156/icons/aria2.png" },
        { "Cinebench R23", "https://cdn.jsdelivr.net/gh/Windos/chocolatey@master/_icons/cinebench.png" },
        { "MSI Afterburner", "https://cdn.jsdelivr.net/gh/mikeee/ChocoPackages/icons/msiafterburner.png" },
        { "GPU-Z", "https://cdn.statically.io/gh/TheCakeIsNaOH/chocolatey-packages/76473405f9bd2f91c67e23610608f11d87a50fc8/Icons/gpu-z.png" },
        { "ThrottleStop", "https://cdn.jsdelivr.net/gh/mikeee/ChocoPackages/icons/throttlestop.png" },
        { "OCCT", "https://rawcdn.githack.com/SebastianK90/chocolateyautomaticpackages/31d0b9851a02b653471474a4aef34e0b9a90e4c1/icons/occt.portable.png" },
        { "FurMark 2", "https://cdn.rawgit.com/majkinetor/chocolatey/master/furmark/icon.png" },
        { "Superposition Benchmark", "https://cdn.jsdelivr.net/gh/AdmiringWorm/chocolatey-packages@abf46f64d842811d7ab0977b820a23d8c979444b/automatic/superposition-benchmark/icons/128x128.png" },
        { "Display Driver Uninstaller", "https://cdn.jsdelivr.net/gh/tunisiano187/Chocolatey-packages@a7de863bfffb4460bdffe57e795c48fdf630d3db/icons/ddu.png" },
        { "Intel Driver & Support Assistant", "https://cdn.jsdelivr.net/gh/mkevenaar/chocolatey-packages@ed7af5ef3b4d01795ea3e33a66baba28bc337118/icons/intel-dsa.png" },
        { "Драйверы NVIDIA", "https://cdn.jsdelivr.net/gh/Zoullx/chocolatey-packages/nvidia-app/nvidia-app.png" },
        { "Драйверы AMD Radeon", "https://rawcdn.githack.com/dmariogatto/choco/572836ba4ad4b37a05576e52ac17dcb0bce18082/amd-ryzen-chipset/amd.png" },
        { "Microsoft Activation Scripts", "https://github.com/massgravel.png?size=256" },
        { "Simple Office Installer", "https://github.com/MaximeriX.png?size=256" },
        { "Strata", "https://github.com/Niko1221.png?size=256" },
        { "Hermes Agent", "https://github.com/NousResearch.png?size=256" },
        { "MiniMax Code", "https://github.com/MiniMax-AI.png?size=256" },
        { "Kudu", "https://github.com/AdventDevInc.png?size=256" },
        { "VirtualBox", "https://github.com/VirtualBox.png?size=256" },
        { "Universal MFG Unlocker", "https://github.com/dashdogy.png?size=256" },
        { "Cursor", "https://github.com/cursor.png?size=256" },
        { "Visual Studio Code", GH + "microsoft/vscode/main/resources/win32/code.ico" },
        { "Cherry Studio", "https://github.com/CherryHQ.png?size=256" },
        { "Obsidian", "https://github.com/obsidianmd.png?size=256" },
        { "LM Studio", "https://github.com/lmstudio-ai.png?size=256" },
        { "Jan", GH + "janhq/jan/main/src-tauri/app-icon.png" },
        { "Upscayl", GH + "upscayl/upscayl/main/resources/icons/512x512.png" },
        { "Buzz", GH + "chidiwilliams/buzz/main/buzz/assets/buzz-icon-1024.png" },
        { "Stability Matrix", GH + "LykosAI/StabilityMatrix/main/StabilityMatrix/Assets/STABILITYMATRIX-Square150x150Logo.png" },
        { "LibreHardwareMonitor", GH + "LibreHardwareMonitor/LibreHardwareMonitor/master/LibreHardwareMonitor.Windows.Forms/Resources/icon.ico" },
        { "qBittorrent", GH + "qbittorrent/qBittorrent/master/src/icons/qbittorrent.ico" },
        { "yt-dlp", GH + "yt-dlp/yt-dlp/master/devscripts/logo.ico" },
        { "Motrix", GH + "agalwood/Motrix/main/public/app-icon.png" },
        { "Rufus", GH + "pbatard/rufus/master/res/icons/rufus-512.png" },
        { "Ventoy", GH + "ventoy/Ventoy/master/ICON/logo_256.png" },
        { "PowerToys", GH + "microsoft/PowerToys/main/doc/images/icons/PowerToys%20icon/ICO/PowerToys.ico" },
        { "7-Zip", GH + "ip7z/7zip/main/CPP/7zip/UI/FileManager/7zipLogo.ico" },
        { "Bulk Crap Uninstaller", GH + "Klocman/Bulk-Crap-Uninstaller/master/source/BulkCrapUninstaller/Resources/logo.ico" },
        { "BleachBit", GH + "bleachbit/bleachbit/master/windows/bleachbit.ico" },
        { "UniGetUI", GH + "marticliment/UniGetUI/main/src/SharedAssets/icon.ico" },
        { "WuMgr", GH + "DavidXanatos/wumgr/master/wumgr/wu.ico" },
        { "System Informer", GH + "winsiderss/systeminformer/master/SystemInformer/SystemInformer.ico" },
        { "ShareX", GH + "ShareX/ShareX/develop/ShareX/ShareX_Icon.ico" },
        { "Ditto", GH + "sabrogden/Ditto/master/DittoSetup/appx/ditto_150.png" },
        { "Notepad++", GH + "notepad-plus-plus/notepad-plus-plus/master/PowerEditor/src/icons/npp.ico" },
        { "EarTrumpet", GH + "File-New-Project/EarTrumpet/master/EarTrumpet.Package/Assets/Square44x44Logo.targetsize-256.png" },
        { "Twinkle Tray", GH + "xanderfrangos/twinkle-tray/master/src/assets/logo-512.png" },
        { "Windhawk", GH + "ramensoftware/windhawk/main/src/windhawk/app/rsrc/app.ico" },
        { "ImageGlass", GH + "d2phap/ImageGlass/develop/source/__assets/win/appxmanifest/Assets-msstore/IMAGEGLASS-Square150x150Logo.scale-400.png" },
        { "Sandboxie-Plus", GH + "sandboxie-plus/Sandboxie/master/SandboxiePlus/SandMan/Resources/SandMan.ico" },
        { "simplewall", GH + "henrypp/simplewall/master/src/res/100.ico" },
        { "KeePassXC", GH + "keepassxreboot/keepassxc/develop/share/branding/png/keepassxc-512x512.png" },
        { "LocalSend", GH + "localsend/localsend/main/app/assets/img/logo.ico" },
        { "CrystalDiskInfo", GH + "hiyohiyo/CrystalDiskInfo/master/resN/DiskInfo.ico" },
        { "CrystalDiskMark", GH + "hiyohiyo/CrystalDiskMark/master/res/DiskMark.ico" },
        { "OBS Studio", GH + "obsproject/obs-studio/master/frontend/cmake/linux/icons/obs-logo-512.png" },
        { ".NET Desktop Runtime 8", GH + "dotnet/brand/main/logo/dotnet-logo.jpg" },
        { ".NET Desktop Runtime 6", GH + "dotnet/brand/main/logo/dotnet-logo.jpg" },
        { "Wireshark", "https://gitlab.com/wireshark/wireshark/-/raw/master/resources/icons/wsicon256.png" },
        { "PhotoCraft", GH + "storytold/photocraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.photocraft.png" },
        { "VectorCraft", GH + "storytold/vectorcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.vectorcraft.png" },
        { "FilmCraft", GH + "storytold/filmcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.filmcraft.png" },
        { "LightCraft", GH + "storytold/lightcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.lightcraft.png" },
        { "EffectCraft", GH + "storytold/effectcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.effectcraft.png" },
        { "DesignCraft", GH + "storytold/designcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.designcraft.png" },
        { "PdfCraft", GH + "storytold/pdfcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.pdfcraft.png" },
        { "CadCraft", GH + "storytold/cadcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.cadcraft.png" },
        { "WordCraft", GH + "storytold/wordcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.wordcraft.png" },
        { "GridCraft", GH + "storytold/gridcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.gridcraft.png" },
        { "DeckCraft", GH + "storytold/deckcraft/main/assets/app-icon/hicolor/256x256/apps/ai.storyteller.deckcraft.png" },
    };

    public static ToolEntry[] Build()
    {
        var tools = BuildEntries();
        foreach (var t in tools)
        {
            string url;
            if (IconUrls.TryGetValue(t.Name, out url)) t.IconUrl = url;
        }
        return tools;
    }

    private static ToolEntry[] BuildEntries()
    {
        return new[]
        {
            // --- ИИ ---
            new ToolEntry {
                Name = "Ollama", Category = "ИИ",
                Desc = "Локальные нейросети одной командой: Llama, Qwen, DeepSeek, Gemma — работают у тебя на компьютере, без интернета.",
                Action = "winget", Payload = "Ollama.Ollama", LaunchHint = "ollama app"
            },
            new ToolEntry {
                Name = "LM Studio", Category = "ИИ",
                Desc = "Удобное окно для локальных моделей: поиск, скачивание, чат и OpenAI-совместимый сервер.",
                Action = "winget", Payload = "ElementLabs.LMStudio", LaunchHint = "LM Studio"
            },
            new ToolEntry {
                Name = "Jan", Category = "ИИ",
                Desc = "Открытая альтернатива ChatGPT, которая работает офлайн на твоём железе.",
                Action = "winget", Payload = "Jan.Jan", LaunchHint = "Jan"
            },
            new ToolEntry {
                Name = "Stability Matrix", Category = "ИИ",
                Desc = "Генерация картинок: ставит ComfyUI, Forge, Fooocus и модели в пару кликов. Portable.",
                Action = "portable", FolderId = "stabilitymatrix", LaunchHint = "StabilityMatrix",
                GithubRepo = "LykosAI/StabilityMatrix", AssetMatch = "win-x64", IsZip = true
            },
            new ToolEntry {
                Name = "Upscayl", Category = "ИИ",
                Desc = "Увеличивает и улучшает фото нейросетью — из мыла в чёткую картинку.",
                Action = "winget", Payload = "Upscayl.Upscayl", LaunchHint = "Upscayl"
            },
            new ToolEntry {
                Name = "Buzz", Category = "ИИ",
                Desc = "Расшифровка аудио и видео в текст и субтитры (Whisper), офлайн.",
                Action = "winget", Payload = "ChidiWilliams.Buzz", LaunchHint = "Buzz"
            },
            new ToolEntry {
                Name = "Cursor", Category = "Разработка",
                Desc = "Редактор кода с встроенным ИИ: пишет, правит и объясняет код по запросу. Основан на VS Code.",
                Action = "winget", Payload = "Anysphere.Cursor", LaunchHint = "Cursor"
            },
            new ToolEntry {
                Name = "Visual Studio Code", Category = "Разработка",
                Desc = "Самый популярный бесплатный редактор кода от Microsoft: тысячи расширений под любой язык.",
                Action = "winget", Payload = "Microsoft.VisualStudioCode", LaunchHint = "Code"
            },
            new ToolEntry {
                Name = "Hermes Agent", Category = "ИИ",
                Desc = "Десктоп-приложение для самообучающегося ИИ-агента Hermes (Nous Research): чат, инструменты, память. Версия сообщества, MIT.",
                Action = "winget", Payload = "fathah.HermesDesktop", LaunchHint = "Hermes"
            },
            new ToolEntry {
                Name = "MiniMax Code", Category = "ИИ",
                Desc = "Официальный десктоп-агент MiniMax для разработки: чат, файлы проекта, терминал, браузерный предпросмотр.",
                Action = "winget", Payload = "MiniMax.MiniMaxCode", LaunchHint = "MiniMax Code"
            },
            new ToolEntry {
                Name = "Kudu", Category = "Система",
                Desc = "Открытый чистильщик и оптимизатор Windows (аналог CCleaner): 25+ инструментов, без рекламы, MIT.",
                Action = "winget", Payload = "AdventDevelopmentInc.Kudu", LaunchHint = "Kudu"
            },
            new ToolEntry {
                Name = "VirtualBox", Category = "Система",
                Desc = "Бесплатный аналог VMware Workstation от Oracle: ставь Windows, Linux и что угодно в виртуальных машинах.",
                Action = "winget", Payload = "Oracle.VirtualBox", LaunchHint = "VirtualBox"
            },
            new ToolEntry {
                Name = "Strata", Category = "ИИ",
                Desc = "Движок Niko1221: гоняет 125-миллиардную Qwen3.8-Flash-Next на одной видеокарте, отдаёт OpenAI/Anthropic API на localhost. Нужна карта от 12 ГБ (NVIDIA или AMD RDNA3+).",
                Action = "script",
                Payload = "irm https://github.com/Niko1221/Strata/releases/latest/download/install.ps1 | iex; & (Join-Path $env:LOCALAPPDATA 'Strata\\terminal\\current\\bin\\strata.cmd') launch"
            },

            // --- Драйверы видеокарт ---
            new ToolEntry {
                Name = "Драйверы NVIDIA", Category = "Драйверы",
                Desc = "Официальная страница драйверов GeForce и NVIDIA App — сайт сам подберёт драйвер под твою карту.",
                Action = "web", Payload = "https://www.nvidia.com/ru-ru/drivers/"
            },
            new ToolEntry {
                Name = "Драйверы AMD Radeon", Category = "Драйверы",
                Desc = "Официальные драйверы Radeon (Adrenalin): автоопределение карты или ручной выбор модели, в том числе для старых RX 400/500.",
                Action = "web", Payload = "https://www.amd.com/ru/support/download/drivers.html"
            },
            new ToolEntry {
                Name = "Display Driver Uninstaller", Category = "Драйверы",
                Desc = "Начисто сносит драйвер видеокарты перед установкой нового — лечит артефакты, вылеты и «драйвер не ставится».",
                Action = "winget", Payload = "Wagnardsoft.DisplayDriverUninstaller", LaunchHint = "Display Driver Uninstaller"
            },
            new ToolEntry {
                Name = "NVCleanstall", Category = "Драйверы",
                Desc = "Ставит драйвер NVIDIA без телеметрии и мусора — только то, что отметишь галочками.",
                Action = "winget", Payload = "TechPowerUp.NVCleanstall", LaunchHint = "NVCleanstall"
            },
            new ToolEntry {
                Name = "Intel Driver & Support Assistant", Category = "Драйверы",
                Desc = "Официальная утилита Intel: находит и обновляет драйверы видео, Wi-Fi, Bluetooth и чипсета Intel.",
                Action = "winget", Payload = "Intel.IntelDriverAndSupportAssistant", LaunchHint = "Driver & Support Assistant"
            },

            new ToolEntry {
                Name = "DeepSeek", Category = "ИИ",
                Desc = "Официальный чат DeepSeek. Своего приложения для Windows у DeepSeek нет — открывается их сайт.",
                Action = "web", Payload = "https://chat.deepseek.com/"
            },
            new ToolEntry {
                Name = "Cherry Studio", Category = "ИИ",
                Desc = "Открытый клиент для нейросетей: DeepSeek, Claude, GPT, Gemini и локальные модели в одном окне (через API-ключи).",
                Action = "winget", Payload = "kangfenmao.CherryStudio", LaunchHint = "Cherry Studio"
            },
            new ToolEntry {
                Name = "OmniHub", Category = "Удобство",
                Desc = "Свой мессенджер-хаб: все чаты (Telegram, WhatsApp и другие) в одном окне.",
                Action = "silentInstall", FolderId = "omnihub", LaunchHint = "OmniHub", InstalledHint = "OmniHub"
            },
            new ToolEntry {
                Name = "Obsidian", Category = "Удобство",
                Desc = "Заметки и база знаний в обычных Markdown-файлах: связи между заметками, граф, плагины.",
                Action = "winget", Payload = "Obsidian.Obsidian", LaunchHint = "Obsidian"
            },
            new ToolEntry {
                Name = "Claude", Category = "ИИ",
                Desc = "Официальное приложение Claude от Anthropic для Windows.",
                Action = "winget", Payload = "Anthropic.Claude", LaunchHint = "claude"
            },

            new ToolEntry {
                Name = "Cinebench R23", Category = "Разгон и тесты",
                Desc = "Эталонный бенчмарк процессора от Maxon: очки на одно и все ядра — по ним сравнивают все разгоны.",
                Action = "winget", Payload = "Maxon.CinebenchR23", LaunchHint = "Cinebench"
            },

            // --- Разгон и стресс-тесты ---
            new ToolEntry {
                Name = "MSI Afterburner", Category = "Разгон и тесты",
                Desc = "Разгон видеокарты: частоты, напряжение, кривая вентиляторов, оверлей с FPS и температурами в играх.",
                Action = "winget", Payload = "Guru3D.Afterburner", LaunchHint = "MSIAfterburner"
            },
            new ToolEntry {
                Name = "GPU-Z", Category = "Разгон и тесты",
                Desc = "Всё о видеокарте: чип, память, частоты, датчики в реальном времени. Проверка, не подделка ли карта.",
                Action = "winget", Payload = "TechPowerUp.GPU-Z", LaunchHint = "GPU-Z"
            },
            new ToolEntry {
                Name = "ThrottleStop", Category = "Разгон и тесты",
                Desc = "Андервольт и снятие троттлинга процессоров Intel — особенно на ноутбуках.",
                Action = "winget", Payload = "TechPowerUp.ThrottleStop", LaunchHint = "ThrottleStop"
            },
            new ToolEntry {
                Name = "ZenTimings", Category = "Разгон и тесты",
                Desc = "Тайминги и частоты памяти на AMD Ryzen — главный инструмент для разгона ОЗУ на AMD. Portable.",
                Action = "portable", FolderId = "zentimings", LaunchHint = "ZenTimings",
                GithubRepo = "irusanov/ZenTimings", AssetMatch = "zentimings", IsZip = true
            },
            new ToolEntry {
                Name = "Universal MFG Unlocker", Category = "Графика / Видео",
                Desc = "Мод dashdogy: Multi Frame Generation до 6x и смена пресетов DLSS на лету (RTX 20-50). Это DLL — кладётся в папку игры, меню по Backspace.",
                Action = "portable", FolderId = "mfgunlocker", LaunchHint = "MFGUnlocker",
                GithubRepo = "dashdogy/MFG-Unlocker", AssetMatch = "MFGUnlocker", IsZip = true
            },
            new ToolEntry {
                Name = "OCCT", Category = "Разгон и тесты",
                Desc = "Стресс-тест процессора, видеокарты, памяти и блока питания с мониторингом — ловит нестабильный разгон.",
                Action = "winget", Payload = "OCBase.OCCT.Personal", LaunchHint = "OCCT"
            },
            new ToolEntry {
                Name = "y-cruncher", Category = "Разгон и тесты",
                Desc = "Считает число π на всех ядрах — один из самых жёстких тестов стабильности процессора и памяти. Portable.",
                Action = "portable", FolderId = "y-cruncher", LaunchHint = "y-cruncher",
                GithubRepo = "Mysticial/y-cruncher", AssetMatch = "y-cruncher", IsZip = true
            },
            new ToolEntry {
                Name = "FurMark 2", Category = "Разгон и тесты",
                Desc = "Прогрев видеокарты «бубликом» — проверка охлаждения и стабильности разгона GPU.",
                Action = "winget", Payload = "Geeks3D.FurMark.2", LaunchHint = "FurMark_GUI"
            },
            new ToolEntry {
                Name = "Superposition Benchmark", Category = "Разгон и тесты",
                Desc = "Бенчмарк видеокарты от Unigine: красивая сцена, сравнение очков до и после разгона.",
                Action = "winget", Payload = "Unigine.SuperpositionBenchmark", LaunchHint = "Superposition Benchmark"
            },

            new ToolEntry {
                Name = "Microsoft Activation Scripts", Category = "Активация",
                Desc = "Открытый активатор Windows и Office (massgrave). Откроет своё окно с меню, попросит права администратора.",
                Action = "script", Payload = "irm https://get.activated.win | iex"
            },
            new ToolEntry {
                Name = "WinUtil", Category = "Твики / Debloat",
                Desc = "Твики, чистка и пакетная установка программ от Chris Titus Tech. Всё выбирается в его окне, нужны права администратора.",
                Action = "script", Payload = "irm https://christitus.com/win | iex"
            },
            new ToolEntry {
                Name = "CPU-Z", Category = "Диагностика",
                Desc = "Процессор, память, материнская плата — всё о железе на одном экране.",
                Action = "winget", Payload = "CPUID.CPU-Z", LaunchHint = "cpuz"
            },
            new ToolEntry {
                Name = "HWiNFO", Category = "Диагностика",
                Desc = "Подробный мониторинг железа и всех датчиков: температуры, частоты, напряжения.",
                Action = "winget", Payload = "REALiX.HWiNFO", LaunchHint = "HWiNFO"
            },
            new ToolEntry {
                Name = "Sysinternals Suite", Category = "Диагностика",
                Desc = "Process Explorer, Autoruns и ещё десятки утилит от Microsoft. Portable, без установки.",
                Action = "portable", FolderId = "sysinternals", LaunchHint = "procexp",
                PortableUrl = "https://download.sysinternals.com/files/SysinternalsSuite.zip", IsZip = true
            },
            new ToolEntry {
                Name = "LibreHardwareMonitor", Category = "Диагностика",
                Desc = "Открытый аналог CPU-Z и HWiNFO: процессор, видеокарта, память, датчики. Portable.",
                Action = "portable", FolderId = "librehardwaremonitor", LaunchHint = "LibreHardwareMonitor",
                GithubRepo = "LibreHardwareMonitor/LibreHardwareMonitor",
                AssetMatch = "librehardwaremonitor", AssetExclude = ".net.", IsZip = true
            },
            new ToolEntry {
                Name = "qBittorrent", Category = "Сеть / Загрузки",
                Desc = "Торрент-клиент без рекламы и мусора.",
                Action = "winget", Payload = "qbittorrent.qbittorrent", LaunchHint = "qbittorrent"
            },
            new ToolEntry {
                Name = "Logitech G HUB", Category = "Периферия",
                Desc = "Официальный софт Logitech: профили, макросы, DPI, подсветка.",
                Action = "winget", Payload = "Logitech.GHUB", LaunchHint = "lghub"
            },
            new ToolEntry {
                Name = ".NET Desktop Runtime 8", Category = "Рантаймы",
                Desc = "Нужен современным .NET-программам. Ставь, если программа ругается на отсутствующий .NET.",
                Action = "winget", Payload = "Microsoft.DotNet.DesktopRuntime.8", NoLaunch = true
            },
            new ToolEntry {
                Name = ".NET Desktop Runtime 6", Category = "Рантаймы",
                Desc = "Для программ, которым нужна именно шестая версия .NET.",
                Action = "winget", Payload = "Microsoft.DotNet.DesktopRuntime.6", NoLaunch = true
            },
            new ToolEntry {
                Name = "Visual C++ Redistributable x64", Category = "Рантаймы",
                Desc = "Классическая зависимость игр и программ на C++ (2015–2022, 64-бит).",
                Action = "winget", Payload = "Microsoft.VCRedist.2015+.x64", NoLaunch = true
            },
            new ToolEntry {
                Name = "Visual C++ Redistributable x86", Category = "Рантаймы",
                Desc = "То же самое, но 32-бит — нужно старым программам и играм.",
                Action = "winget", Payload = "Microsoft.VCRedist.2015+.x86", NoLaunch = true
            },
            new ToolEntry {
                Name = "DirectX End-User Runtime", Category = "Рантаймы",
                Desc = "Официальный веб-установщик DirectX от Microsoft — докачивает компоненты для старых игр.",
                Action = "portable", FolderId = "directx", LaunchHint = "dxwebsetup",
                PortableUrl = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe",
                IsZip = false
            },
            new ToolEntry {
                Name = "Snappy Driver Installer Origin", Category = "Драйверы",
                Desc = "Сканирует компьютер, показывает драйверы списком с галочками и ставит выбранные пачкой. Без рекламы.",
                Action = "portable", FolderId = "sdio", LaunchHint = "SDIO",
                PortableUrl = "https://www.glenn.delahoy.com/downloads/sdio/SDIO.zip", IsZip = true
            },
            new ToolEntry {
                Name = "Nova", Category = "Обход блокировок",
                Desc = "Возвращает доступ к YouTube, Discord, Telegram, WhatsApp. Сама подбирает рабочую стратегию, без регистрации и телеметрии.",
                Action = "silentInstall", FolderId = "nova", LaunchHint = "NovaSetup", InstalledHint = "Nova",
                GithubRepo = "confeden/Nova", AssetMatch = "novasetup", IsZip = false,
                SilentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"
            },
            new ToolEntry {
                Name = "SvoyVPN", Category = "Обход блокировок",
                Desc = "Свой VPN-клиент: OpenVPN, Xray и sing-box под одной крышей, готовые ноды RU и мир.",
                Action = "silentInstall", FolderId = "svoyvpn", LaunchHint = "SvoyVPN", InstalledHint = "SvoyVPN"
            },
            new ToolEntry {
                Name = "yt-dlp", Category = "Сеть / Загрузки",
                Desc = "Скачивает видео и аудио с YouTube и сотен сайтов. Консольная — откроется окно с подсказкой.",
                Action = "portable", FolderId = "yt-dlp", LaunchHint = "yt-dlp", Cli = true,
                PortableUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", IsZip = false
            },
            new ToolEntry {
                Name = "aria2", Category = "Сеть / Загрузки",
                Desc = "Консольный многопоточный загрузчик с торрентами. Движок перехвата загрузок SwissBox.",
                Action = "portable", FolderId = "aria2", LaunchHint = "aria2c", Cli = true,
                GithubRepo = "aria2/aria2", AssetMatch = "win-64bit", IsZip = true
            },
            new ToolEntry {
                Name = "Motrix", Category = "Сеть / Загрузки",
                Desc = "Менеджер загрузок с нормальным интерфейсом: многопоточность, торренты, magnet-ссылки.",
                Action = "winget", Payload = "agalwood.Motrix", LaunchHint = "Motrix"
            },
            new ToolEntry {
                Name = "PhotoCraft", Category = "Графика / Видео",
                Desc = "Аналог Photoshop. Открытый (MIT/Apache-2.0), написан на Rust.",
                Action = "portable", FolderId = "photocraft", LaunchHint = "photocraft",
                GithubRepo = "storytold/photocraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "VectorCraft", Category = "Графика / Видео",
                Desc = "Аналог Illustrator — векторная графика.",
                Action = "portable", FolderId = "vectorcraft", LaunchHint = "vectorcraft",
                GithubRepo = "storytold/vectorcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "FilmCraft", Category = "Графика / Видео",
                Desc = "Аналог Premiere — видеомонтаж.",
                Action = "portable", FolderId = "filmcraft", LaunchHint = "filmcraft",
                GithubRepo = "storytold/filmcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "LightCraft", Category = "Графика / Видео",
                Desc = "Аналог Lightroom — каталог и обработка фотографий.",
                Action = "portable", FolderId = "lightcraft", LaunchHint = "lightcraft",
                GithubRepo = "storytold/lightcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "EffectCraft", Category = "Графика / Видео",
                Desc = "Аналог After Effects — моушн-дизайн и композитинг.",
                Action = "portable", FolderId = "effectcraft", LaunchHint = "effectcraft",
                GithubRepo = "storytold/effectcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "DesignCraft", Category = "Графика / Видео",
                Desc = "Аналог InDesign — вёрстка макетов.",
                Action = "portable", FolderId = "designcraft", LaunchHint = "designcraft",
                GithubRepo = "storytold/designcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "PdfCraft", Category = "Графика / Видео",
                Desc = "Аналог Acrobat — редактирование PDF.",
                Action = "portable", FolderId = "pdfcraft", LaunchHint = "pdfcraft",
                GithubRepo = "storytold/pdfcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "CadCraft", Category = "Графика / Видео",
                Desc = "CAD-редактор. Ранняя стадия разработки — может быть сыроват.",
                Action = "portable", FolderId = "cadcraft", LaunchHint = "cadcraft",
                GithubRepo = "storytold/cadcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "Simple Office Installer", Category = "Офис (ArtCraft)",
                Desc = "Ставит любую версию Microsoft Office (365/2024/2021/2019/2016) бесплатно, часть редакций — сразу с активацией. Открытый проект, своё окно.",
                Action = "portable", FolderId = "simpleofficeinstaller", LaunchHint = "SimpleOfficeInstaller",
                GithubRepo = "MaximeriX/SimpleOfficeInstaller", AssetMatch = "SimpleOfficeInstaller", IsZip = false
            },
            new ToolEntry {
                Name = "WordCraft", Category = "Офис (ArtCraft)",
                Desc = "Аналог Word. Ранняя стадия разработки — может быть сыроват.",
                Action = "portable", FolderId = "wordcraft", LaunchHint = "wordcraft",
                GithubRepo = "storytold/wordcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "GridCraft", Category = "Офис (ArtCraft)",
                Desc = "Аналог Excel. Ранняя стадия разработки — может быть сыроват.",
                Action = "portable", FolderId = "gridcraft", LaunchHint = "gridcraft",
                GithubRepo = "storytold/gridcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            new ToolEntry {
                Name = "DeckCraft", Category = "Офис (ArtCraft)",
                Desc = "Аналог PowerPoint. Ранняя стадия разработки — может быть сыроват.",
                Action = "portable", FolderId = "deckcraft", LaunchHint = "deckcraft",
                GithubRepo = "storytold/deckcraft", AssetMatch = "windows-x64-portable", IsZip = true
            },
            // --- Must-have энтузиастов ---
            new ToolEntry {
                Name = "Everything", Category = "Система",
                Desc = "Мгновенный поиск любого файла на всех дисках — находит быстрее, чем успеваешь допечатать.",
                Action = "winget", Payload = "voidtools.Everything", LaunchHint = "Everything"
            },
            new ToolEntry {
                Name = "PowerToys", Category = "Система",
                Desc = "Набор от Microsoft: раскладка окон FancyZones, массовое переименование, текст с экрана, пипетка, быстрый запуск.",
                Action = "winget", Payload = "Microsoft.PowerToys", LaunchHint = "PowerToys"
            },
            new ToolEntry {
                Name = "7-Zip", Category = "Система",
                Desc = "Архиватор: 7z, zip, rar и десятки других форматов. Открытый — лучшая замена WinRAR.",
                Action = "winget", Payload = "7zip.7zip", LaunchHint = "7zFM"
            },
            new ToolEntry {
                Name = "WizTree", Category = "Система",
                Desc = "За секунды показывает, что съело место на диске — самые большие папки и файлы.",
                Action = "winget", Payload = "AntibodySoftware.WizTree", LaunchHint = "WizTree64"
            },
            new ToolEntry {
                Name = "Bulk Crap Uninstaller", Category = "Система",
                Desc = "Удаляет программы вместе с хвостами, можно сразу пачкой. Находит даже то, чего нет в списке Windows.",
                Action = "winget", Payload = "Klocman.BulkCrapUninstaller", LaunchHint = "BCUninstaller"
            },
            new ToolEntry {
                Name = "BleachBit", Category = "Система",
                Desc = "Чистка мусора и следов: кеши, временные файлы, браузеры. Открытая замена CCleaner без рекламы.",
                Action = "winget", Payload = "BleachBit.BleachBit", LaunchHint = "bleachbit"
            },
            new ToolEntry {
                Name = "UniGetUI", Category = "Система",
                Desc = "Окно для winget и других менеджеров пакетов: обновить все установленные программы одной кнопкой.",
                Action = "winget", Payload = "Devolutions.UniGetUI", LaunchHint = "UniGetUI"
            },
            new ToolEntry {
                Name = "WuMgr", Category = "Твики / Debloat",
                Desc = "Аналог Windows Update MiniTool: выбираешь, какие обновления ставить, скрываешь ненужные, можно вообще взять Windows Update под ручной контроль. Portable.",
                Action = "portable", FolderId = "wumgr", LaunchHint = "WuMgr",
                GithubRepo = "DavidXanatos/wumgr", AssetMatch = "WuMgr", IsZip = true
            },
            new ToolEntry {
                Name = "System Informer", Category = "Система",
                Desc = "Бывший Process Hacker: диспетчер задач на стероидах — процессы, службы, сеть, диски. Portable.",
                Action = "portable", FolderId = "systeminformer", LaunchHint = "SystemInformer",
                GithubRepo = "winsiderss/systeminformer", AssetMatch = "-bin", IsZip = true
            },
            new ToolEntry {
                Name = "ShareX", Category = "Удобство",
                Desc = "Скриншоты, запись экрана и GIF, распознавание текста, загрузка на хостинги — всё по горячим клавишам. Portable.",
                Action = "portable", FolderId = "sharex", LaunchHint = "ShareX",
                GithubRepo = "ShareX/ShareX", AssetMatch = "portable-x64", IsZip = true
            },
            new ToolEntry {
                Name = "Ditto", Category = "Удобство",
                Desc = "История буфера обмена: всё, что копировал, под рукой и с поиском. Portable.",
                Action = "portable", FolderId = "ditto", LaunchHint = "Ditto",
                GithubRepo = "sabrogden/Ditto", AssetMatch = "dittoportable", IsZip = true
            },
            new ToolEntry {
                Name = "Notepad++", Category = "Удобство",
                Desc = "Блокнот, который должен был быть в Windows: подсветка кода, вкладки, поиск по папкам. Portable.",
                Action = "portable", FolderId = "notepadpp", LaunchHint = "notepad++",
                GithubRepo = "notepad-plus-plus/notepad-plus-plus", AssetMatch = "portable.x64", IsZip = true
            },
            new ToolEntry {
                Name = "EarTrumpet", Category = "Удобство",
                Desc = "Громкость отдельно для каждой программы прямо из трея.",
                Action = "winget", Payload = "File-New-Project.EarTrumpet", StartAppName = "EarTrumpet"
            },
            new ToolEntry {
                Name = "Twinkle Tray", Category = "Удобство",
                Desc = "Яркость внешних мониторов из трея — как на ноутбуке.",
                Action = "winget", Payload = "xanderfrangos.twinkletray", LaunchHint = "Twinkle Tray"
            },
            new ToolEntry {
                Name = "Windhawk", Category = "Удобство",
                Desc = "Моды для Windows: настрой таскбар, Пуск и проводник так, как Microsoft не даёт.",
                Action = "winget", Payload = "RamenSoftware.Windhawk", LaunchHint = "windhawk"
            },
            new ToolEntry {
                Name = "ImageGlass", Category = "Удобство",
                Desc = "Быстрый просмотрщик картинок: открывает всё, включая WebP, AVIF, HEIC и RAW. Portable.",
                Action = "portable", FolderId = "imageglass", LaunchHint = "ImageGlass",
                GithubRepo = "d2phap/ImageGlass", AssetMatch = "win-x64", IsZip = true
            },
            new ToolEntry {
                Name = "Sandboxie-Plus", Category = "Безопасность",
                Desc = "Запускай сомнительные exe в песочнице — они не тронут систему. Must-have для всего «крякнутого».",
                Action = "winget", Payload = "Sandboxie.Plus", LaunchHint = "SandMan"
            },
            new ToolEntry {
                Name = "simplewall", Category = "Безопасность",
                Desc = "Простой файрвол: запрети любой программе выход в интернет в один клик.",
                Action = "winget", Payload = "Henry++.simplewall", LaunchHint = "simplewall"
            },
            new ToolEntry {
                Name = "KeePassXC", Category = "Безопасность",
                Desc = "Менеджер паролей: база хранится у тебя, а не в чужом облаке. Portable.",
                Action = "portable", FolderId = "keepassxc", LaunchHint = "KeePassXC",
                GithubRepo = "keepassxreboot/keepassxc", AssetMatch = "win64", AssetExclude = "legacy", IsZip = true
            },
            new ToolEntry {
                Name = "LocalSend", Category = "Сеть / Загрузки",
                Desc = "Файлы между телефоном и компьютером по Wi-Fi — без проводов, облаков и регистрации. Portable.",
                Action = "portable", FolderId = "localsend", LaunchHint = "localsend",
                GithubRepo = "localsend/localsend", AssetMatch = "windows-x86-64", IsZip = true
            },
            new ToolEntry {
                Name = "Wireshark", Category = "Сеть / Загрузки",
                Desc = "Показывает весь сетевой трафик по пакетам — видно, что и куда ходит.",
                Action = "winget", Payload = "WiresharkFoundation.Wireshark", LaunchHint = "Wireshark"
            },
            new ToolEntry {
                Name = "CrystalDiskInfo", Category = "Диагностика",
                Desc = "Здоровье дисков по S.M.A.R.T.: предупредит, пока SSD или HDD ещё не умер.",
                Action = "winget", Payload = "CrystalDewWorld.CrystalDiskInfo", LaunchHint = "DiskInfo64"
            },
            new ToolEntry {
                Name = "CrystalDiskMark", Category = "Диагностика",
                Desc = "Замер реальной скорости дисков — чтение и запись.",
                Action = "winget", Payload = "CrystalDewWorld.CrystalDiskMark", LaunchHint = "DiskMark64"
            },
            new ToolEntry {
                Name = "FanControl", Category = "Диагностика",
                Desc = "Свои кривые вентиляторов по любым датчикам — тише и холоднее. Portable.",
                Action = "portable", FolderId = "fancontrol", LaunchHint = "FanControl",
                GithubRepo = "Rem0o/FanControl.Releases", AssetMatch = "net_4_8", IsZip = true
            },
            new ToolEntry {
                Name = "OBS Studio", Category = "Графика / Видео",
                Desc = "Запись экрана и стримы — стандарт для всех, кто пишет видео.",
                Action = "winget", Payload = "OBSProject.OBSStudio", LaunchHint = "obs64"
            },
            new ToolEntry {
                Name = "Rufus", Category = "Флешки / ISO",
                Desc = "Загрузочная флешка из ISO за пару кликов. Один exe, без установки.",
                Action = "portable", FolderId = "rufus", LaunchHint = "rufus",
                GithubRepo = "pbatard/rufus", AssetMatch = "rufus-", IsZip = false
            },
            new ToolEntry {
                Name = "Ventoy", Category = "Флешки / ISO",
                Desc = "Мультизагрузочная флешка: просто кидаешь на неё ISO-файлы, без перезаписи.",
                Action = "portable", FolderId = "ventoy", LaunchHint = "Ventoy2Disk",
                GithubRepo = "ventoy/Ventoy", AssetMatch = "windows", IsZip = true
            },
        };
    }
}

// ============================================================================================
//  Главное окно
// ============================================================================================

internal class MainForm : Form
{
    private const int SidebarWidth = 236;
    private const int CaptionZone = 46;

    private readonly bool _preview;
    private readonly ToolEntry[] _tools;
    private readonly List<ToolRow> _rows = new List<ToolRow>();
    private readonly Dictionary<ToolEntry, ToolStatus> _status = new Dictionary<ToolEntry, ToolStatus>();
    private readonly Dictionary<ToolEntry, bool> _busy = new Dictionary<ToolEntry, bool>();
    private readonly Dictionary<ToolEntry, Image> _brandIcons = new Dictionary<ToolEntry, Image>();
    private readonly System.Threading.Semaphore _iconGate = new System.Threading.Semaphore(4, 4);
    private readonly List<SidebarItem> _navItems = new List<SidebarItem>();
    private readonly List<Control> _pages = new List<Control>();
    private readonly List<string> _pageCategories = new List<string>();
    private readonly System.Threading.Semaphore _checkGate = new System.Threading.Semaphore(4, 4);

    private ChromePanel _sidebar;
    private TrafficLights _lights;
    private SearchField _search;
    private HeaderBar _header;
    private Panel _pagesHost;
    private Panel _logDrawer;
    private TextBox _logBox;
    private ScrollPage _searchPage;
    private readonly Timer _searchDebounce = new Timer { Interval = 220 };
    private readonly ToolTip _tips = new ToolTip();
    private int _currentNav;
    private bool _suppressSearch;
    private bool? _wingetAvailable;
    private InfoRow _wingetRow;

    private InfoRow _updateRow;
    private PillButton _updateButton;
    private int _aboutNavIndex = -1;
    private bool _selfUpdateAvailable;
    private string _selfUpdateUrl;
    private string _selfUpdateVersion;

    private readonly Timer _novaWatchdogTimer = new Timer { Interval = 20000 };
    private bool _novaWatchdogOn;
    private int _novaTgFail, _novaWaFail, _novaYtFail;
    private string _novaLastExePath;
    private volatile bool _novaCheckInFlight;
    private static readonly string[] NovaRouteModes = { "auto", "warp", "opera", "direct" };
    private ToggleSwitch _watchdogSwitch;

    private Process _aria2RpcProcess;
    private const int Aria2RpcPort = 16800;
    private ToggleSwitch _rpcSwitch;
    private InfoRow _rpcRow;
    private const string RpcSubtitle = "Движок для расширения: многопоточные загрузки и торренты. Порт 16800, файлы — в «Загрузки». Работает в фоне, даже если закрыть SwissBox.";

    private static string ToolsRoot
    {
        get { return Path.Combine(Application.StartupPath, "tools"); }
    }

    private static string ExtensionFolder
    {
        get { return Path.Combine(Application.StartupPath, "extension"); }
    }

    public MainForm() : this(false) { }

    internal MainForm(bool preview)
    {
        _preview = preview;
        AutoScaleMode = AutoScaleMode.None;
        Text = "SwissBox";
        Icon = AppIconArt.LoadWindowIcon();
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1120, 760);
        MinimumSize = new Size(880, 660);
        BackColor = Theme.WindowBg;
        Font = Theme.BodyFont;
        DoubleBuffered = true;
        KeyPreview = true;

        _tools = Catalog.Build();
        _novaWatchdogTimer.Tick += (s, e) => NovaWatchdogTick();
        _searchDebounce.Tick += (s, e) =>
        {
            _searchDebounce.Stop();
            RunSearch(_search.Box.Text.Trim());
        };

        var content = new ChromePanel { Dock = DockStyle.Fill, BackColor = Theme.WindowBg, PassThrough = pt => true };
        _pagesHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.WindowBg };
        _logDrawer = BuildLogDrawer();
        _header = new HeaderBar();
        _tips.SetToolTip(_header.RefreshButton, "Проверить установленное и обновления");
        _tips.SetToolTip(_header.LogButton, "Журнал");
        _header.RefreshButton.Click += (s, e) =>
        {
            Log("Проверяю установленное и обновления...");
            ExeIndex.Invalidate();
            InvalidateWingetList();
            CheckAllStatuses(true);
        };
        _header.LogButton.Click += (s, e) => ToggleLog();

        content.Controls.Add(_pagesHost);
        content.Controls.Add(_logDrawer);
        content.Controls.Add(_header);

        _sidebar = BuildSidebar();
        Controls.Add(content);
        Controls.Add(_sidebar);

        BuildPages();
        ShowNav(0);
    }

    // ---------------------------------------------------------------- окно без рамки

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = Theme.Dark ? 1 : 0;
        int round = 2;
        try
        {
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);
        }
        catch { }
        Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20);
    }

    private static int FrameThickness()
    {
        return Native.GetSystemMetrics(Native.SM_CYFRAME) + Native.GetSystemMetrics(Native.SM_CXPADDEDBORDER);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // Оставляем системные невидимые рамки слева/справа/снизу (тень, ресайз, Snap),
            // а заголовок убираем — клиентская область начинается от самого верха окна.
            var before = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
            int originalTop = before.rgrc0.Top;
            base.WndProc(ref m);
            var after = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
            after.rgrc0.Top = originalTop + (Native.IsZoomed(Handle) ? FrameThickness() : 0);
            Marshal.StructureToPtr(after, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == Native.WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if (m.Result.ToInt64() == Native.HTCLIENT)
            {
                long lp = m.LParam.ToInt64();
                var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                bool zoomed = Native.IsZoomed(Handle);
                if (!zoomed && pt.Y < 6)
                {
                    int code = pt.X < 14 ? Native.HTTOPLEFT : pt.X > ClientSize.Width - 14 ? Native.HTTOPRIGHT : Native.HTTOP;
                    m.Result = (IntPtr)code;
                }
                else if (IsCaptionPoint(pt))
                {
                    m.Result = (IntPtr)Native.HTCAPTION;
                }
            }
            return;
        }

        base.WndProc(ref m);
    }

    private bool IsCaptionPoint(Point pt)
    {
        if (pt.X < SidebarWidth) return pt.Y < CaptionZone;
        return pt.Y < _header.Height;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        SetActiveLook(true);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        SetActiveLook(false);
    }

    internal void SetActiveLook(bool active)
    {
        Theme.WindowActive = active;
        if (_lights != null) _lights.Invalidate();
        foreach (var it in _navItems) it.Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.F)
        {
            _search.Box.Focus();
            _search.Box.SelectAll();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape && _search.Box.Text.Length > 0)
        {
            _search.Box.Text = "";
            e.Handled = true;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LoadBrandIcons();
        CheckAllStatuses(false);
        CheckSelfUpdate(false);
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            bool ok = WingetAvailable();
            Ui(() =>
            {
                if (_wingetRow != null)
                    _wingetRow.Subtitle = ok
                        ? "Найден — через него ставятся программы без portable-сборки."
                        : "Не найден — поставь «App Installer» из Microsoft Store, иначе CPU-Z, HWiNFO и ещё пара программ не поставятся.";
            });
        });
        if (!_preview) RestoreBackgroundServices();
    }

    // ---------------------------------------------------------------- сборка интерфейса

    private ChromePanel BuildSidebar()
    {
        var sb = new ChromePanel { Dock = DockStyle.Left, Width = SidebarWidth, BackColor = Theme.SidebarBg };
        sb.PassThrough = pt => pt.Y < CaptionZone;
        sb.Paint += (s, e) =>
        {
            using (var pen = new Pen(Theme.SidebarBorder)) e.Graphics.DrawLine(pen, sb.Width - 1, 0, sb.Width - 1, sb.Height);
        };

        _lights = new TrafficLights { Location = new Point(20, 19), BackColor = Theme.SidebarBg };
        _lights.CloseClicked += (s, e) => Close();
        _lights.MinimizeClicked += (s, e) => WindowState = FormWindowState.Minimized;
        _lights.ZoomClicked += (s, e) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        sb.Controls.Add(_lights);

        _search = new SearchField { Location = new Point(12, 50), Width = SidebarWidth - 25 };
        _search.Box.TextChanged += (s, e) =>
        {
            if (_suppressSearch) return;
            _searchDebounce.Stop();
            _searchDebounce.Start();
        };
        sb.Controls.Add(_search);
        return sb;
    }

    private SidebarItem AddNavItem(string text, string glyph, Color color, int count, int y)
    {
        var item = new SidebarItem
        {
            Text = text,
            Glyph = glyph,
            IconColor = color,
            Count = count,
            Location = new Point(12, y),
            Width = SidebarWidth - 25
        };
        int index = _navItems.Count;
        item.Click += (s, e) => ShowNav(index);
        _navItems.Add(item);
        _sidebar.Controls.Add(item);
        return item;
    }

    private void BuildPages()
    {
        int y = 94;
        foreach (var cat in Catalog.Categories)
        {
            var catTools = new List<ToolEntry>();
            foreach (var t in _tools) if (t.Category == cat.Name) catTools.Add(t);
            if (catTools.Count == 0) continue;

            var page = new ScrollPage();
            var group = new GroupPanel();
            foreach (var t in catTools) group.AddRow(CreateRow(t));
            page.Controls.Add(group);
            if (cat.Name == "Обход блокировок") AddWatchdogSection(page);
            if (cat.Name == "Сеть / Загрузки") AddCatcherSection(page);

            AddNavItem(cat.Name, cat.Glyph, cat.Color, catTools.Count, y);
            AddPage(page, cat.Name);
            y += 30;
        }

        _aboutNavIndex = _navItems.Count;
        AddNavItem("О программе", Glyphs.Info, Color.FromArgb(142, 142, 147), 0, y + 12);
        AddPage(BuildAboutPage(), null);
    }

    private void AddPage(Control page, string category)
    {
        _pages.Add(page);
        _pageCategories.Add(category);
        _pagesHost.Controls.Add(page);
    }

    private ToolRow CreateRow(ToolEntry t)
    {
        var row = new ToolRow(t);
        row.Button.Click += (s, e) => OnToolAction(t);
        _rows.Add(row);
        ApplyStatusToRow(row);
        return row;
    }

    private Panel BuildLogDrawer()
    {
        var drawer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 180,
            BackColor = Theme.LogBg,
            Padding = new Padding(24, 34, 14, 10),
            Visible = false
        };
        drawer.Paint += (s, e) =>
        {
            using (var pen = new Pen(Theme.Separator)) e.Graphics.DrawLine(pen, 0, 0, drawer.Width, 0);
            TextRenderer.DrawText(e.Graphics, "Журнал", Theme.SectionFont, new Point(22, 10), Theme.TextSecondary);
        };
        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Font = Theme.MonoFont,
            BackColor = Theme.LogBg,
            ForeColor = Theme.TextSecondary
        };
        _logBox.HandleCreated += (s, e) => Native.DarkScrollbars(_logBox);
        drawer.Controls.Add(_logBox);
        return drawer;
    }

    private void ToggleLog()
    {
        _logDrawer.Visible = !_logDrawer.Visible;
        _header.LogButton.Toggled = _logDrawer.Visible;
        _header.LogButton.Invalidate();
        if (_logDrawer.Visible)
        {
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    private void AddWatchdogSection(ScrollPage page)
    {
        page.Controls.Add(new SectionHeader("Автопереключение"));
        var group = new GroupPanel();
        var row = new InfoRow("Watchdog для Telegram, WhatsApp и YouTube",
            "Раз в ~20 секунд проверяет доступность. Если сервис не отвечает дважды подряд — переключает его режим в самой Nova (auto → warp → opera → direct). Без перезапуска и без рук.");
        _watchdogSwitch = new ToggleSwitch();
        _watchdogSwitch.CheckedChanged += (s, e) => SetWatchdog(_watchdogSwitch.Checked, true);
        row.SetAccessory(_watchdogSwitch);
        group.AddRow(row);
        page.Controls.Add(group);
    }

    private void AddCatcherSection(ScrollPage page)
    {
        page.Controls.Add(new SectionHeader("Перехват загрузок из браузера"));
        var group = new GroupPanel();

        _rpcRow = new InfoRow("aria2 RPC-сервер", RpcSubtitle);
        _rpcSwitch = new ToggleSwitch();
        _rpcSwitch.CheckedChanged += (s, e) => SetAria2Rpc(_rpcSwitch.Checked, true);
        _rpcRow.SetAccessory(_rpcSwitch);
        group.AddRow(_rpcRow);

        var ext = new InfoRow("Расширение SwissBox Catcher",
            "Подключается один раз в каждом браузере: «Режим разработчика» → «Загрузить распакованное» → папка расширения. Дальше само ловит загрузки и magnet-ссылки.");
        AddBrowserPill(ext, "Chrome", "chrome://extensions/",
            "%ProgramFiles%\\Google\\Chrome\\Application\\chrome.exe",
            "%ProgramFiles(x86)%\\Google\\Chrome\\Application\\chrome.exe",
            "%LocalAppData%\\Google\\Chrome\\Application\\chrome.exe");
        AddBrowserPill(ext, "Edge", "edge://extensions/",
            "%ProgramFiles(x86)%\\Microsoft\\Edge\\Application\\msedge.exe",
            "%ProgramFiles%\\Microsoft\\Edge\\Application\\msedge.exe");
        AddBrowserPill(ext, "Яндекс", "browser://extensions/",
            "%LocalAppData%\\Yandex\\YandexBrowser\\Application\\browser.exe");
        AddBrowserPill(ext, "Vivaldi", "vivaldi://extensions/",
            "%LocalAppData%\\Vivaldi\\Application\\vivaldi.exe");
        var folder = new PillButton { Text = "Папка расширения", MinWidth = 60 };
        folder.Click += (s, e) => OpenFolder(ExtensionFolder);
        ext.AddBottom(folder);
        group.AddRow(ext);

        page.Controls.Add(group);
    }

    private void AddBrowserPill(InfoRow row, string name, string url, params string[] exeCandidates)
    {
        var pill = new PillButton { Text = name, MinWidth = 60 };
        pill.Click += (s, e) => OpenBrowserExtensionsPage(name, exeCandidates, url);
        row.AddBottom(pill);
    }

    private Control BuildAboutPage()
    {
        var page = new ScrollPage();
        page.Controls.Add(new AboutHero());

        var group = new GroupPanel();

        _updateRow = new InfoRow("Обновления SwissBox", "Проверяю...");
        _updateButton = new PillButton { Text = "Проверить" };
        _updateButton.Click += (s, e) =>
        {
            if (_selfUpdateAvailable) DoSelfUpdate();
            else CheckSelfUpdate(true);
        };
        _updateRow.SetAccessory(_updateButton);
        group.AddRow(_updateRow);

        var where = new InfoRow("Где лежат программы", ToolsRoot);
        var open = new PillButton { Text = "Показать" };
        open.Click += (s, e) =>
        {
            try { Directory.CreateDirectory(ToolsRoot); } catch { }
            OpenFolder(ToolsRoot);
        };
        where.SetAccessory(open);
        group.AddRow(where);

        _wingetRow = new InfoRow("winget", "Проверяю...");
        group.AddRow(_wingetRow);

        group.AddRow(new InfoRow("Оформление",
            Theme.Dark ? "Тёмное — как в Windows. Переключится само вместе с темой системы." : "Светлое — как в Windows. Переключится само вместе с темой системы."));
        group.AddRow(new InfoRow("Как это работает",
            "Portable-программы качаются официальными ссылками и запускаются из папки рядом с SwissBox — без установки в систему. " +
            "Программы без portable-сборки ставятся через winget. MAS и WinUtil запускаются их собственными официальными командами."));
        page.Controls.Add(group);
        return page;
    }

    // ---------------------------------------------------------------- навигация и поиск

    internal void ShowNav(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        _currentNav = index;
        if (_search.Box.Text.Length > 0)
        {
            _suppressSearch = true;
            _search.Box.Text = "";
            _suppressSearch = false;
        }
        DisposeSearchPage();
        for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index;
        for (int i = 0; i < _navItems.Count; i++)
        {
            _navItems[i].Selected = i == index;
            _navItems[i].Invalidate();
        }
        _header.Title = _navItems[index].Text;
        _header.Invalidate();
    }

    internal void RunSearch(string query)
    {
        if (query.Length == 0)
        {
            DisposeSearchPage();
            ShowNav(_currentNav);
            return;
        }

        DisposeSearchPage();
        var page = new ScrollPage();
        var matches = new List<ToolEntry>();
        foreach (var t in _tools)
        {
            if (Contains(t.Name, query) || Contains(t.Desc, query) || Contains(t.Category, query))
                matches.Add(t);
        }

        if (matches.Count == 0)
        {
            page.Controls.Add(new EmptyState("Ничего не нашлось", "Попробуй другое название"));
        }
        else
        {
            var group = new GroupPanel();
            foreach (var t in matches) group.AddRow(CreateRow(t));
            page.Controls.Add(group);
        }

        _searchPage = page;
        _pagesHost.Controls.Add(page);
        foreach (var p in _pages) p.Visible = false;
        page.Visible = true;
        page.BringToFront();
        foreach (var it in _navItems)
        {
            it.Selected = false;
            it.Invalidate();
        }
        _header.Title = "Поиск";
        _header.Invalidate();
        foreach (var t in matches)
            if (!_status.ContainsKey(t)) CheckStatus(t, false);
    }

    private static bool Contains(string text, string query)
    {
        return text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void DisposeSearchPage()
    {
        if (_searchPage == null) return;
        foreach (Control c in _searchPage.Controls)
        {
            var g = c as GroupPanel;
            if (g == null) continue;
            foreach (var r in g.Rows)
            {
                var tr = r as ToolRow;
                if (tr != null) _rows.Remove(tr);
            }
        }
        _pagesHost.Controls.Remove(_searchPage);
        _searchPage.Dispose();
        _searchPage = null;
    }

    // ---------------------------------------------------------------- статус программ

    private void Ui(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(a); } catch { }
        }
        else a();
    }

    private void ForRows(ToolEntry t, Action<ToolRow> a)
    {
        foreach (var r in _rows.ToArray())
            if (r.Tool == t && !r.IsDisposed) a(r);
    }

    private void CheckAllStatuses(bool force)
    {
        foreach (var t in _tools) CheckStatus(t, force);
    }

    private void CheckStatus(ToolEntry t, bool force)
    {
        if (t.Action == "script" || t.Action == "web")
        {
            SetStatus(t, new ToolStatus { Script = true });
            return;
        }

        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            var st = new ToolStatus();
            // рантаймы ждут общий «winget export» — не занимаем ими слоты быстрых проверок
            bool gated = !t.NoLaunch;
            if (gated) _checkGate.WaitOne();
            try
            {
                if (t.Action == "portable")
                {
                    st.Path = FindLocalPortableExe(t);
                    st.Present = st.Path != null;
                    st.Update = st.Present && HasUpdate(t, force);
                }
                else if (t.Action == "silentInstall")
                {
                    st.Path = FindInstalledExe(t.InstalledHint ?? t.LaunchHint);
                    st.Present = st.Path != null;
                    st.Update = st.Present && HasUpdate(t, force);
                }
                else if (t.NoLaunch)
                {
                    st.Present = IsWingetPackageInstalled(t.Payload);
                }
                else
                {
                    st.Path = TryResolveWingetLaunchPath(t);
                    st.Present = st.Path != null;
                }
                if (st.Path != null) st.Icon = IconLoader.Load(st.Path);
            }
            catch { }
            finally
            {
                if (gated) _checkGate.Release();
            }
            Ui(() => SetStatus(t, st));
        });
    }

    // Фирменные иконки: один раз качаются в tools\.icons\, дальше берутся оттуда мгновенно.
    private void LoadBrandIcons()
    {
        string dir = Path.Combine(ToolsRoot, ".icons");
        foreach (var tool in _tools)
        {
            if (string.IsNullOrEmpty(tool.IconUrl)) continue;
            var t = tool;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Image img = null;
                _iconGate.WaitOne();
                try
                {
                    string ext = Path.GetExtension(new Uri(t.IconUrl).LocalPath);
                    string file = Path.Combine(dir, Regex.Replace(t.Name, "[^A-Za-z0-9]+", "_") + ext);
                    if (!File.Exists(file))
                    {
                        Directory.CreateDirectory(dir);
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        string tmp = file + ".part";
                        using (var wc = new WebClient())
                        {
                            wc.Headers.Add("User-Agent", "SwissBox");
                            wc.DownloadFile(t.IconUrl, tmp);
                        }
                        File.Move(tmp, file);
                    }
                    img = IconLoader.LoadImageFile(file);
                }
                catch { }
                finally { _iconGate.Release(); }
                if (img == null) return;
                Ui(() =>
                {
                    _brandIcons[t] = img;
                    ForRows(t, ApplyStatusToRow);
                });
            });
        }
    }

    private void SetStatus(ToolEntry t, ToolStatus st)
    {
        ToolStatus old;
        if (st.Icon == null && _status.TryGetValue(t, out old) && old.Icon != null && old.Path == st.Path) st.Icon = old.Icon;
        _status[t] = st;
        ForRows(t, ApplyStatusToRow);
        UpdateAttentionDots();
    }

    private void ApplyStatusToRow(ToolRow row)
    {
        ToolStatus st;
        var t = row.Tool;
        bool busy = _busy.ContainsKey(t);
        row.Button.Filled = false;
        Image brand;
        _brandIcons.TryGetValue(t, out brand);
        row.AppIcon = brand;

        if (!_status.TryGetValue(t, out st))
        {
            row.State = ToolState.Checking;
            row.Button.Text = "···";
            row.Button.Enabled = false;
        }
        else
        {
            if (st.Script)
            {
                row.State = ToolState.Script;
                row.Button.Text = t.Action == "web" ? "Открыть" : "Запустить";
            }
            else if (!st.Present)
            {
                row.State = ToolState.NotInstalled;
                row.Button.Text = t.NoLaunch ? "Установить" : "Получить";
            }
            else if (st.Update)
            {
                row.State = ToolState.UpdateAvailable;
                row.Button.Text = "Обновить";
                row.Button.Filled = true;
            }
            else
            {
                row.State = ToolState.Installed;
                row.Button.Text = t.NoLaunch ? "Обновить" : "Открыть";
            }
            row.AppIcon = brand ?? st.Icon;
            row.Button.Enabled = !busy;
        }

        if (busy && !row.Button.Working) row.Button.Busy = true;
        row.PerformLayout();
        row.Invalidate();
    }

    private void UpdateAttentionDots()
    {
        for (int i = 0; i < _navItems.Count; i++)
        {
            string cat = _pageCategories[i];
            bool attention = false;
            if (cat != null)
            {
                foreach (var kv in _status)
                    if (kv.Key.Category == cat && kv.Value.Update) attention = true;
            }
            else if (i == _aboutNavIndex) attention = _selfUpdateAvailable;
            if (_navItems[i].Attention != attention)
            {
                _navItems[i].Attention = attention;
                _navItems[i].Invalidate();
            }
        }
    }

    private void BeginBusy(ToolEntry t)
    {
        _busy[t] = true;
        ForRows(t, r =>
        {
            r.Button.Enabled = false;
            r.Button.Busy = true;
        });
    }

    private void ProgressOf(ToolEntry t, int percent)
    {
        Ui(() => ForRows(t, r =>
        {
            if (percent >= 100) r.Button.Busy = true;
            else r.Button.Progress = percent;
        }));
    }

    private void EndBusy(ToolEntry t)
    {
        ExeIndex.Invalidate();
        if (t.Action == "winget") InvalidateWingetList();
        Ui(() =>
        {
            _busy.Remove(t);
            ForRows(t, r =>
            {
                r.Button.Idle();
                r.Button.Enabled = true;
            });
            CheckStatus(t, false);
        });
    }

    private void OnToolAction(ToolEntry t)
    {
        if (_busy.ContainsKey(t)) return;
        if (t.Action == "winget") HandleWinget(t);
        else if (t.Action == "portable") HandlePortable(t);
        else if (t.Action == "silentInstall") HandleSilentInstall(t);
        else if (t.Action == "web")
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = t.Payload, UseShellExecute = true });
                Log("Открываю " + t.Name + " в браузере.");
            }
            catch (Exception ex) { Log("Не открылся " + t.Name + ": " + ex.Message); }
        }
        else RunOfficialScript(t.Payload, t.Name);
    }

    // ---------------------------------------------------------------- portable

    private void HandlePortable(ToolEntry t)
    {
        BeginBusy(t);
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string existing = FindLocalPortableExe(t);
                if (existing != null && !HasUpdate(t, false))
                {
                    LaunchDirect(t, existing);
                    return;
                }
                LogThreadSafe((existing != null ? "Обновляю " : "Качаю ") + t.Name + "...");
                string exe = DownloadAndExtractPortable(t, p => ProgressOf(t, p));
                if (exe == null)
                {
                    LogThreadSafe(t.Name + ": скачать или найти exe не вышло.");
                    return;
                }
                LaunchDirect(t, exe);
            }
            catch (Exception ex)
            {
                LogThreadSafe(t.Name + ": " + FriendlyError(ex));
            }
            finally { EndBusy(t); }
        });
    }

    private string DownloadAndExtractPortable(ToolEntry tool, Action<int> onProgress)
    {
        string url = ResolveDownloadUrl(tool);
        if (string.IsNullOrEmpty(url))
        {
            LogThreadSafe(tool.Name + ": не нашёл ссылку на скачивание.");
            return null;
        }

        string toolDir = Path.Combine(ToolsRoot, tool.FolderId);
        Directory.CreateDirectory(toolDir);
        string fingerprint = GetUrlFingerprint(url);

        string fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = tool.FolderId + (tool.IsZip ? ".zip" : ".exe");
        string downloadPath = Path.Combine(toolDir, fileName);

        DownloadWithProgress(url, downloadPath, tool.Name, onProgress);

        string exePath;
        if (tool.IsZip)
        {
            onProgress(-1);
            LogThreadSafe(tool.Name + ": распаковываю...");
            string extractDir = Path.Combine(toolDir, "app");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(downloadPath, extractDir);
            exePath = FindExe(extractDir, tool.LaunchHint, 5);
        }
        else exePath = downloadPath;

        if (exePath != null)
        {
            WriteMarker(toolDir, url, fingerprint);
            WriteLastCheck(toolDir, false);
        }
        return exePath;
    }

    private void DownloadWithProgress(string url, string downloadPath, string toolName, Action<int> onProgress)
    {
        const int maxAttempts = 3;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Exception error = null;
            int lastPercent = -2;
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "SwissBox");
                var done = new System.Threading.ManualResetEvent(false);
                wc.DownloadProgressChanged += (s, e) =>
                {
                    int p = e.TotalBytesToReceive > 0 ? e.ProgressPercentage : -1;
                    if (p == lastPercent) return;
                    lastPercent = p;
                    onProgress(p);
                };
                wc.DownloadFileCompleted += (s, e) =>
                {
                    if (e.Error != null && !e.Cancelled) error = e.Error;
                    done.Set();
                };
                wc.DownloadFileAsync(new Uri(url), downloadPath);
                done.WaitOne();
            }

            if (error == null) return;
            if (attempt == maxAttempts) throw error;

            LogThreadSafe(toolName + ": обрыв скачивания (" + FriendlyError(error) + "), пробую ещё раз (" + attempt + "/" + maxAttempts + ")...");
            onProgress(-1);
            System.Threading.Thread.Sleep(3000);
        }
    }

    // ---------------------------------------------------------------- тихая установка (Nova)

    private void HandleSilentInstall(ToolEntry t)
    {
        BeginBusy(t);
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string existing = FindInstalledExe(t.InstalledHint ?? t.LaunchHint);
                if (existing != null && !HasUpdate(t, false))
                {
                    LaunchDirect(t, existing);
                    return;
                }
                SilentInstallAndRun(t);
            }
            catch (Exception ex)
            {
                LogThreadSafe(t.Name + ": " + FriendlyError(ex));
            }
            finally { EndBusy(t); }
        });
    }

    private void SilentInstallAndRun(ToolEntry tool)
    {
        LogThreadSafe("Качаю " + tool.Name + "...");
        string url = ResolveDownloadUrl(tool);
        if (string.IsNullOrEmpty(url))
        {
            LogThreadSafe(tool.Name + ": не нашёл ссылку на скачивание.");
            return;
        }

        string toolDir = Path.Combine(ToolsRoot, tool.FolderId);
        Directory.CreateDirectory(toolDir);
        string fingerprint = GetUrlFingerprint(url);

        string fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = tool.FolderId + ".exe";
        string installerPath = Path.Combine(toolDir, fileName);

        DownloadWithProgress(url, installerPath, tool.Name, p => ProgressOf(tool, p));

        LogThreadSafe(tool.Name + ": ставлю тихо, без окна мастера установки...");
        ProgressOf(tool, -1);
        using (var proc = Process.Start(new ProcessStartInfo { FileName = installerPath, Arguments = tool.SilentArgs, UseShellExecute = true }))
            proc.WaitForExit();
        ExeIndex.Invalidate();

        string hint = string.IsNullOrEmpty(tool.InstalledHint) ? tool.LaunchHint : tool.InstalledHint;
        string exePath = FindInstalledExe(hint);
        if (exePath == null)
        {
            System.Threading.Thread.Sleep(2000);
            exePath = FindInstalledExe(hint);
        }
        if (exePath == null)
        {
            LogThreadSafe(tool.Name + ": установлено, но exe не нашёл — поищи в Пуске.");
            return;
        }

        WriteMarker(toolDir, url, fingerprint);
        WriteLastCheck(toolDir, false);
        LaunchDirect(tool, exePath);
    }

    // ---------------------------------------------------------------- winget

    private bool WingetAvailable()
    {
        if (_wingetAvailable.HasValue) return _wingetAvailable.Value;
        bool ok = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit(5000);
                ok = p.HasExited && p.ExitCode == 0;
            }
        }
        catch { ok = false; }
        _wingetAvailable = ok;
        return ok;
    }

    private void HandleWinget(ToolEntry t)
    {
        BeginBusy(t);
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                if (!t.NoLaunch)
                {
                    string existing = TryResolveWingetLaunchPath(t);
                    if (existing != null)
                    {
                        LaunchDirect(t, existing);
                        EndBusy(t);
                        return;
                    }
                }
                if (!WingetAvailable())
                {
                    LogThreadSafe("winget не найден — поставь «App Installer» из Microsoft Store.");
                    Ui(() => MessageBox.Show(this, "winget не найден.\nПоставь «App Installer» из Microsoft Store и перезапусти SwissBox.",
                        "SwissBox", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                    EndBusy(t);
                    return;
                }
                bool upgrade = t.NoLaunch && IsWingetPackageInstalled(t.Payload);
                RunWinget(t, upgrade);
            }
            catch (Exception ex)
            {
                LogThreadSafe(t.Name + ": " + FriendlyError(ex));
                EndBusy(t);
            }
        });
    }

    private void RunWinget(ToolEntry t, bool upgrade)
    {
        LogThreadSafe((upgrade ? "Обновляю " : "Ставлю ") + t.Name + " через winget...");
        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = (upgrade ? "upgrade" : "install") + " --id " + t.Payload +
                        " -e --source winget --silent --accept-package-agreements --accept-source-agreements",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (s, e) => OnWingetLine(t, e.Data);
        proc.ErrorDataReceived += (s, e) => OnWingetLine(t, e.Data);
        proc.Exited += (s, e) =>
        {
            int code = proc.ExitCode;
            proc.Dispose();
            try
            {
                ExeIndex.Invalidate();
                if (code == 0)
                {
                    if (t.NoLaunch) LogThreadSafe(t.Name + ": готово.");
                    else
                    {
                        string path = TryResolveWingetLaunchPath(t);
                        if (path == null)
                        {
                            System.Threading.Thread.Sleep(1500);
                            path = TryResolveWingetLaunchPath(t);
                        }
                        if (path != null) LaunchDirect(t, path);
                        else LogThreadSafe(t.Name + ": установлено, но exe не нашёл — поищи в Пуске.");
                    }
                }
                else LogThreadSafe(t.Name + ": " + WingetCodeText(code));
            }
            finally { EndBusy(t); }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
    }

    private static string WingetCodeText(int code)
    {
        switch (code)
        {
            case -1978335189: return "уже последняя версия, обновлять нечего.";
            case -1978335135: return "уже установлено.";
            case -1978335212: return "winget не нашёл такой пакет.";
            case -1978335138: return "winget не смог проверить сертификат источника. Попробуй позже или «winget source reset --force».";
            default: return "winget завершился с кодом " + code + ".";
        }
    }

    private void OnWingetLine(ToolEntry t, string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        var m = Regex.Match(line, @"([\d.,]+)\s*(KB|MB|GB)\s*/\s*([\d.,]+)\s*(KB|MB|GB)");
        if (m.Success)
        {
            double got = ToBytes(m.Groups[1].Value, m.Groups[2].Value);
            double total = ToBytes(m.Groups[3].Value, m.Groups[4].Value);
            if (total > 0) ProgressOf(t, (int)(got * 100 / total));
            return;
        }
        var pm = Regex.Match(line, @"(\d{1,3})\s*%\s*$");
        if (pm.Success && (line.IndexOf('█') >= 0 || line.IndexOf('▒') >= 0 || line.Trim().Length <= 5))
        {
            ProgressOf(t, int.Parse(pm.Groups[1].Value));
            return;
        }
        if (line.IndexOf('█') >= 0 || line.IndexOf('▒') >= 0) return;
        if (!IsMeaningfulLine(line)) return;
        LogThreadSafe(line.Trim());
    }

    private static double ToBytes(string number, string unit)
    {
        double v;
        if (!double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
        switch (unit)
        {
            case "GB": return v * 1024 * 1024 * 1024;
            case "MB": return v * 1024 * 1024;
            default: return v * 1024;
        }
    }

    private static bool IsMeaningfulLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        string t = line.Trim();
        if (t.Length == 0) return false;
        if (t.Length == 1 && (t[0] == '/' || t[0] == '-' || t[0] == '\\' || t[0] == '|')) return false;
        return true;
    }

    private static readonly object WingetListGate = new object();
    private static string _wingetInstalled;

    private static void InvalidateWingetList()
    {
        lock (WingetListGate) _wingetInstalled = null;
    }

    // Один «winget export» на все программы сразу: в JSON полные ID без обрезания колонок.
    private static bool IsWingetPackageInstalled(string packageId)
    {
        string list;
        lock (WingetListGate)
        {
            if (_wingetInstalled == null) _wingetInstalled = ExportWingetList();
            list = _wingetInstalled;
        }
        return Regex.IsMatch(list, "\"PackageIdentifier\"\\s*:\\s*\"" + Regex.Escape(packageId) + "\"", RegexOptions.IgnoreCase);
    }

    private static string ExportWingetList()
    {
        string file = Path.Combine(Path.GetTempPath(), "swissbox-winget-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "export -o \"" + file + "\" --source winget --accept-source-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(60000);
            }
            return File.Exists(file) ? File.ReadAllText(file) : "";
        }
        catch { return ""; }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    // ---------------------------------------------------------------- поиск exe

    private static string FindLocalPortableExe(ToolEntry tool)
    {
        return FindExe(Path.Combine(ToolsRoot, tool.FolderId), tool.LaunchHint, 5);
    }

    private static string FindInstalledExe(string hint)
    {
        if (string.IsNullOrEmpty(hint)) return null;
        return ExeIndex.Find(hint);
    }

    private static string FindStartApp(string name)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"(Get-StartApps | Where-Object { $_.Name -like '*" + name +
                            "*' } | Select-Object -First 1).AppID\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string id = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(10000);
                return string.IsNullOrEmpty(id) ? null : "shell:AppsFolder\\" + id;
            }
        }
        catch { return null; }
    }

    private static string TryResolveWingetLaunchPath(ToolEntry tool)
    {
        if (!string.IsNullOrEmpty(tool.StartAppName)) return FindStartApp(tool.StartAppName);
        string hint = tool.LaunchHint;
        if (string.IsNullOrEmpty(hint)) return null;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string hit = FindExe(Path.Combine(local, @"Microsoft\WinGet\Links"), hint, 0);
        if (hit != null) return hit;
        hit = FindExe(Path.Combine(local, @"Microsoft\WinGet\Packages"), hint, 3);
        if (hit != null) return hit;
        return FindInstalledExe(hint);
    }

    private static string FindExe(string root, string hint, int maxDepth)
    {
        if (string.IsNullOrEmpty(hint)) return null;
        string exact = SafeSearchFiles(root, hint + ".exe", maxDepth);
        if (exact != null) return exact;
        string wild = SafeSearchFiles(root, hint + "*.exe", maxDepth);
        if (wild != null) return wild;
        return SafeSearchFiles(root, hint + "*.dll", maxDepth);
    }

    private static string SafeSearchFiles(string root, string pattern, int maxDepth)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
        try
        {
            string first = null;
            foreach (var f in Directory.EnumerateFiles(root, pattern))
            {
                if (Path.GetFileName(f).IndexOf("64", StringComparison.Ordinal) >= 0) return f; // при выборе — 64-битная
                if (first == null) first = f;
            }
            if (first != null) return first;
            if (maxDepth > 0)
            {
                var dirs = new List<string>(Directory.EnumerateDirectories(root));
                dirs.Sort((a, b) => DirRank(a).CompareTo(DirRank(b)));
                foreach (var d in dirs)
                {
                    var r = SafeSearchFiles(d, pattern, maxDepth - 1);
                    if (r != null) return r;
                }
            }
        }
        catch { }
        return null;
    }

    // В архивах с несколькими сборками сначала смотрим 64-битную папку, ARM и 32-бит — в конце.
    private static int DirRank(string dir)
    {
        string n = Path.GetFileName(dir).ToLowerInvariant();
        if (n.Contains("arm")) return 3;
        if (n.Contains("64")) return 0;
        if (n.Contains("32") || n.Contains("86")) return 2;
        return 1;
    }

    // ---------------------------------------------------------------- обновления

    private static string MarkerPath(string toolDir) { return Path.Combine(toolDir, ".source"); }
    private static string LastCheckPath(string toolDir) { return Path.Combine(toolDir, ".lastcheck"); }

    private static void WriteMarker(string toolDir, string url, string fingerprint)
    {
        try { File.WriteAllText(MarkerPath(toolDir), url + "\n" + fingerprint); } catch { }
    }

    private static void WriteLastCheck(string toolDir, bool update)
    {
        try { File.WriteAllText(LastCheckPath(toolDir), DateTime.UtcNow.ToString("o") + "\n" + (update ? "1" : "0")); } catch { }
    }

    private static bool NeedsUpdate(string toolDir, string url, string fingerprint)
    {
        try
        {
            string path = MarkerPath(toolDir);
            if (!File.Exists(path)) return true;
            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 2) return true;
            return lines[0] != url || lines[1] != fingerprint;
        }
        catch { return true; }
    }

    // Проверка раз в 6 часов на программу — чтобы не упираться в лимит GitHub (60 запросов в час).
    private static bool HasUpdate(ToolEntry tool, bool force)
    {
        try
        {
            string toolDir = Path.Combine(ToolsRoot, tool.FolderId);
            string checkFile = LastCheckPath(toolDir);
            if (!force && File.Exists(checkFile))
            {
                string[] lines = File.ReadAllLines(checkFile);
                DateTime when;
                if (lines.Length >= 2 &&
                    DateTime.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when) &&
                    (DateTime.UtcNow - when.ToUniversalTime()).TotalHours < 6)
                    return lines[1] == "1";
            }

            string url = ResolveDownloadUrl(tool);
            if (string.IsNullOrEmpty(url)) return false;
            bool upd = NeedsUpdate(toolDir, url, GetUrlFingerprint(url));
            if (Directory.Exists(toolDir)) WriteLastCheck(toolDir, upd);
            return upd;
        }
        catch { return false; }
    }

    private static string GetUrlFingerprint(string url)
    {
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "HEAD";
            req.UserAgent = "SwissBox";
            req.Timeout = 8000;
            using (var resp = (HttpWebResponse)req.GetResponse())
                return (resp.Headers["ETag"] ?? "") + "|" + (resp.Headers["Last-Modified"] ?? "") + "|" + resp.ContentLength;
        }
        catch { return ""; }
    }

    private static string ResolveDownloadUrl(ToolEntry tool)
    {
        if (!string.IsNullOrEmpty(tool.GithubRepo)) return ResolveGithubAsset(tool);
        return tool.PortableUrl;
    }

    private static readonly Dictionary<string, KeyValuePair<DateTime, string>> ReleaseCache =
        new Dictionary<string, KeyValuePair<DateTime, string>>();

    private static string GetReleaseJson(string api)
    {
        lock (ReleaseCache)
        {
            KeyValuePair<DateTime, string> hit;
            if (ReleaseCache.TryGetValue(api, out hit) && (DateTime.UtcNow - hit.Key).TotalMinutes < 10) return hit.Value;
        }

        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var req = (HttpWebRequest)WebRequest.Create(api);
        req.UserAgent = "SwissBox";
        req.Accept = "application/vnd.github+json";
        req.Timeout = 15000;
        string json;
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var reader = new StreamReader(resp.GetResponseStream()))
            json = reader.ReadToEnd();

        lock (ReleaseCache) ReleaseCache[api] = new KeyValuePair<DateTime, string>(DateTime.UtcNow, json);
        return json;
    }

    private static string ResolveGithubAsset(ToolEntry tool)
    {
        string apiBase = string.IsNullOrEmpty(tool.ForgeApiBase) ? "https://api.github.com/repos/" : tool.ForgeApiBase;
        string json = GetReleaseJson(apiBase + tool.GithubRepo + "/releases/latest");

        string want = (tool.AssetMatch ?? "").ToLowerInvariant();
        string skip = (tool.AssetExclude ?? "").ToLowerInvariant();
        string wantExt = tool.IsZip ? ".zip" : ".exe";

        foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"(?<url>[^\"]+)\""))
        {
            string url = m.Groups["url"].Value;
            string name = Path.GetFileName(new Uri(url).LocalPath).ToLowerInvariant();
            if (!name.EndsWith(wantExt)) continue;
            if (name.Contains("arm64") || name.Contains("sha256")) continue;
            if (want.Length > 0 && !name.Contains(want)) continue;
            if (skip.Length > 0 && name.Contains(skip)) continue;
            return url;
        }
        return null;
    }

    // ---------------------------------------------------------------- самообновление SwissBox

    private void CheckSelfUpdate(bool manual)
    {
        if (manual)
        {
            _updateButton.Busy = true;
            _updateButton.Enabled = false;
            _updateRow.Subtitle = "Проверяю...";
        }
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string version = null, url = null, error = null;
            try
            {
                string json = GetReleaseJson("https://api.github.com/repos/" + AppInfo.GithubRepo + "/releases/latest");
                var tm = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(?<v>[^\"]+)\"");
                if (tm.Success) version = tm.Groups["v"].Value;
                foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"(?<url>[^\"]+)\""))
                {
                    string u = m.Groups["url"].Value;
                    if (u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { url = u; break; }
                }
            }
            catch (Exception ex) { error = FriendlyError(ex); }

            Ui(() =>
            {
                _updateButton.Busy = false;
                _updateButton.Enabled = true;
                if (error != null)
                {
                    _updateRow.Subtitle = "Не удалось проверить: " + error;
                    _updateButton.Text = "Проверить";
                    _updateButton.Filled = false;
                    return;
                }
                if (version == null || url == null)
                {
                    _updateRow.Subtitle = "Не удалось разобрать ответ GitHub.";
                    _updateButton.Text = "Проверить";
                    _updateButton.Filled = false;
                    return;
                }
                if (IsNewerVersion(version, AppInfo.Version))
                {
                    _selfUpdateAvailable = true;
                    _selfUpdateUrl = url;
                    _selfUpdateVersion = version;
                    _updateRow.Subtitle = "Доступна версия " + version + " (у тебя " + AppInfo.Version + ").";
                    _updateButton.Text = "Обновить";
                    _updateButton.Filled = true;
                }
                else
                {
                    _selfUpdateAvailable = false;
                    _updateRow.Subtitle = "Установлена последняя версия (" + AppInfo.Version + ").";
                    _updateButton.Text = "Проверить ещё раз";
                    _updateButton.Filled = false;
                }
                UpdateAttentionDots();
            });
        });
    }

    private static bool IsNewerVersion(string remote, string local)
    {
        try
        {
            var r = Array.ConvertAll(remote.Split('.'), s => int.Parse(Regex.Match(s, @"\d+").Value));
            var l = Array.ConvertAll(local.Split('.'), s => int.Parse(Regex.Match(s, @"\d+").Value));
            for (int i = 0; i < Math.Max(r.Length, l.Length); i++)
            {
                int rv = i < r.Length ? r[i] : 0;
                int lv = i < l.Length ? l[i] : 0;
                if (rv != lv) return rv > lv;
            }
            return false;
        }
        catch { return false; }
    }

    private void DoSelfUpdate()
    {
        if (string.IsNullOrEmpty(_selfUpdateUrl)) return;
        _updateButton.Busy = true;
        _updateButton.Enabled = false;
        _updateRow.Subtitle = "Скачиваю версию " + _selfUpdateVersion + "...";
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                string tempExe = Path.Combine(Path.GetTempPath(), "SwissBox_update_" + Guid.NewGuid().ToString("N") + ".exe");

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "SwissBox");
                    wc.DownloadFile(_selfUpdateUrl, tempExe);
                }

                int pid = Process.GetCurrentProcess().Id;
                string bat = Path.Combine(Path.GetTempPath(), "SwissBox_update_" + Guid.NewGuid().ToString("N") + ".bat");
                string batScript =
                    "@echo off\r\n" +
                    ":loop\r\n" +
                    "tasklist /fi \"PID eq " + pid + "\" | find \"" + pid + "\" >nul\r\n" +
                    "if not errorlevel 1 (\r\n" +
                    "  timeout /t 1 /nobreak >nul\r\n" +
                    "  goto loop\r\n" +
                    ")\r\n" +
                    "move /y \"" + tempExe + "\" \"" + currentExe + "\" >nul\r\n" +
                    "start \"\" \"" + currentExe + "\"\r\n" +
                    "del \"%~f0\"\r\n";
                File.WriteAllText(bat, batScript);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"" + bat + "\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                LogThreadSafe("SwissBox: обновляюсь до версии " + _selfUpdateVersion + ", перезапуск...");
                Ui(() => Application.Exit());
            }
            catch (Exception ex)
            {
                string msg = FriendlyError(ex);
                Ui(() =>
                {
                    _updateButton.Busy = false;
                    _updateButton.Enabled = true;
                    _updateRow.Subtitle = "Не удалось обновить: " + msg;
                });
            }
        });
    }

    private static string FriendlyError(Exception ex)
    {
        var we = ex as WebException;
        if (we != null)
        {
            var resp = we.Response as HttpWebResponse;
            if (resp != null)
            {
                int code = (int)resp.StatusCode;
                if (code == 403 || code == 429) return "GitHub временно ограничил запросы (около 60 в час без входа). Попробуй минут через 30.";
                if (code == 404) return "файл на сервере не найден (404).";
                if (code >= 500) return "сервер недоступен (" + code + "), попробуй позже.";
            }
            if (we.Status == WebExceptionStatus.Timeout) return "сервер не ответил вовремя.";
            if (we.Status == WebExceptionStatus.NameResolutionFailure || we.Status == WebExceptionStatus.ConnectFailure)
                return "нет связи с сервером.";
        }
        return ex.Message;
    }

    // ---------------------------------------------------------------- запуск

    private void LaunchDirect(ToolEntry tool, string path)
    {
        try
        {
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + path + "\"" });
            }
            else if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + path + "\"" });
            }
            else if (tool.Cli)
            {
                string dir = Path.GetDirectoryName(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/k \"cd /d \"" + dir + "\" && " + Path.GetFileName(path) + " --help\"",
                    UseShellExecute = true,
                    WorkingDirectory = dir
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(path)
                });
            }
            LogThreadSafe("Открываю " + tool.Name);
        }
        catch (Exception ex)
        {
            LogThreadSafe("Не удалось открыть " + tool.Name + ": " + ex.Message);
        }
    }

    private void RunOfficialScript(string command, string displayName)
    {
        Log("Открываю " + displayName + " — своё окно, попросит права администратора...");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoExit -Command \"" + command + "\"",
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Win32Exception) { Log(displayName + ": запуск отменён в окне UAC."); }
        catch (Exception ex) { Log("Не удалось запустить " + displayName + ": " + ex.Message); }
    }

    private void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + path + "\"" }); }
        catch (Exception ex) { Log("Не открылась папка: " + ex.Message); }
    }

    private void OpenBrowserExtensionsPage(string browserName, string[] exeCandidates, string internalUrl)
    {
        string exe = null;
        foreach (var c in exeCandidates)
        {
            string expanded = Environment.ExpandEnvironmentVariables(c);
            if (File.Exists(expanded))
            {
                exe = expanded;
                break;
            }
        }
        if (exe == null)
        {
            Log(browserName + ": браузер не найден по стандартным путям.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = exe, Arguments = internalUrl, UseShellExecute = true });
            OpenFolder(ExtensionFolder);
            Log(browserName + ": открыл страницу расширений и папку — включи «Режим разработчика», нажми «Загрузить распакованное» и укажи эту папку.");
        }
        catch (Exception ex)
        {
            Log(browserName + ": не получилось открыть — " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- фоновые службы: watchdog и aria2

    private void RestoreBackgroundServices()
    {
        if (AppSettings.GetBool("watchdog") && _watchdogSwitch != null)
        {
            _watchdogSwitch.SetSilently(true);
            SetWatchdog(true, false);
        }

        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            bool up = IsAria2RpcUp();
            Ui(() =>
            {
                if (_rpcSwitch == null) return;
                if (up) _rpcSwitch.SetSilently(true);
                else if (AppSettings.GetBool("aria2rpc"))
                {
                    _rpcSwitch.SetSilently(true);
                    SetAria2Rpc(true, false);
                }
            });
        });
    }

    private void SetWatchdog(bool on, bool persist)
    {
        _novaWatchdogOn = on;
        _novaTgFail = _novaWaFail = _novaYtFail = 0;
        if (on) _novaWatchdogTimer.Start();
        else _novaWatchdogTimer.Stop();
        Log("Nova watchdog: " + (on ? "включён." : "выключен."));
        if (persist) AppSettings.SetBool("watchdog", on);
    }

    private void NovaWatchdogTick()
    {
        if (!_novaWatchdogOn || _novaCheckInFlight) return;
        _novaCheckInFlight = true;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                bool tgOk = GroupReachable(new[] { "api.telegram.org", "telegram.org" }, 443);
                bool waOk = GroupReachable(new[] { "web.whatsapp.com", "static.whatsapp.net" }, 443);
                bool ytOk = GroupReachable(new[] { "www.youtube.com", "youtube.com" }, 443);
                // YouTube в конфиге Nova идёт через группу "browser" — своей группы у него нет
                CheckAndCycle(tgOk, ref _novaTgFail, "telegram");
                CheckAndCycle(waOk, ref _novaWaFail, "whatsapp");
                CheckAndCycle(ytOk, ref _novaYtFail, "browser");
            }
            catch { }
            finally { _novaCheckInFlight = false; }
        });
    }

    private void CheckAndCycle(bool ok, ref int failCount, string group)
    {
        if (ok)
        {
            failCount = 0;
            return;
        }
        failCount++;
        LogThreadSafe("Nova watchdog: " + group + " недоступен (" + failCount + "/2).");
        if (failCount >= 2)
        {
            failCount = 0;
            CycleNovaRoute(group);
        }
    }

    private void CycleNovaRoute(string group)
    {
        string path = FindNovaRoutingSettingsPath();
        if (path == null)
        {
            LogThreadSafe("Nova watchdog: не нашёл routing_settings.json (Nova ещё ни разу не запускалась?) — перезапускаю её целиком.");
            RestartNovaProcess();
            return;
        }
        try
        {
            string json = File.ReadAllText(path);
            var m = Regex.Match(json, "\"" + group + "\"\\s*:\\s*\"(\\w+)\"");
            if (!m.Success)
            {
                LogThreadSafe("Nova watchdog: в конфиге Nova нет маршрута «" + group + "».");
                return;
            }
            string current = m.Groups[1].Value;
            int idx = Array.IndexOf(NovaRouteModes, current);
            string next = NovaRouteModes[(idx < 0 ? 0 : idx + 1) % NovaRouteModes.Length];
            File.WriteAllText(path, Regex.Replace(json, "(\"" + group + "\"\\s*:\\s*\")\\w+(\")", "$1" + next + "$2"));
            LogThreadSafe("Nova watchdog: " + group + " " + current + " → " + next + " (Nova подхватит сама за ~3 сек).");
        }
        catch (Exception ex)
        {
            LogThreadSafe("Nova watchdog: не смог переключить " + group + " — " + ex.Message);
        }
    }

    private static string FindNovaRoutingSettingsPath()
    {
        string exe = FindInstalledExe("Nova");
        if (exe == null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        string path = Path.Combine(Path.GetDirectoryName(exe), "temp", "routing_settings.json");
        return File.Exists(path) ? path : null;
    }

    private void RestartNovaProcess()
    {
        string exePath = null;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // только основной процесс: его помощники (nova-go и т.п.) сами завершаются вместе с ним
                if (!string.Equals(p.ProcessName, "Nova", StringComparison.OrdinalIgnoreCase)) continue;
                exePath = p.MainModule.FileName;
                p.Kill();
                p.WaitForExit(5000);
            }
            catch { }
        }
        if (exePath == null) exePath = _novaLastExePath ?? FindInstalledExe("Nova");
        if (exePath == null)
        {
            LogThreadSafe("Nova watchdog: Nova не запущена и не найдена — запусти её один раз сам.");
            return;
        }
        try
        {
            System.Threading.Thread.Sleep(500);
            Process.Start(exePath);
            _novaLastExePath = exePath;
            LogThreadSafe("Nova watchdog: Nova перезапущена.");
        }
        catch (Exception ex)
        {
            LogThreadSafe("Nova watchdog: не смог перезапустить — " + ex.Message);
        }
    }

    private static bool GroupReachable(string[] hosts, int port)
    {
        foreach (var h in hosts)
            if (TcpReachable(h, port, 3000)) return true;
        return false;
    }

    private static bool TcpReachable(string host, int port, int timeoutMs)
    {
        try
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect(host, port, null, null);
                bool signaled = result.AsyncWaitHandle.WaitOne(timeoutMs);
                if (!signaled || !client.Connected) return false;
                client.EndConnect(result);
                return true;
            }
        }
        catch { return false; }
    }

    private static bool IsAria2RpcUp()
    {
        return TcpReachable("127.0.0.1", Aria2RpcPort, 400);
    }

    private ToolEntry FindToolByFolderId(string folderId)
    {
        foreach (var t in _tools) if (t.FolderId == folderId) return t;
        return null;
    }

    private void SetAria2Rpc(bool on, bool persist)
    {
        if (persist) AppSettings.SetBool("aria2rpc", on);

        if (!on)
        {
            StopAria2Rpc();
            _rpcRow.Subtitle = RpcSubtitle;
            Log("aria2 RPC: остановлен.");
            return;
        }

        _rpcSwitch.Enabled = false;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string error = null;
            try
            {
                if (IsAria2RpcUp())
                {
                    LogThreadSafe("aria2 RPC: уже работает на порту " + Aria2RpcPort + ".");
                    return;
                }
                var tool = FindToolByFolderId("aria2");
                string exe = FindLocalPortableExe(tool);
                if (exe == null)
                {
                    LogThreadSafe("aria2 RPC: качаю aria2...");
                    exe = DownloadAndExtractPortable(tool, p => Ui(() =>
                        _rpcRow.Subtitle = p >= 0 ? "Скачиваю aria2… " + p + "%" : "Готовлю aria2…"));
                }
                if (exe == null)
                {
                    error = "не удалось получить aria2c.exe";
                    return;
                }
                string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                _aria2RpcProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--enable-rpc --rpc-listen-port=" + Aria2RpcPort +
                                " --rpc-allow-origin-all --rpc-listen-all=false --continue=true --dir=\"" + downloadsDir + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                LogThreadSafe("aria2 RPC: запущен на порту " + Aria2RpcPort + ", файлы — в " + downloadsDir + ".");
            }
            catch (Exception ex)
            {
                error = FriendlyError(ex);
            }
            finally
            {
                string err = error;
                Ui(() =>
                {
                    _rpcSwitch.Enabled = true;
                    _rpcRow.Subtitle = RpcSubtitle;
                    if (err != null)
                    {
                        _rpcSwitch.SetSilently(false);
                        Log("aria2 RPC: " + err);
                    }
                });
            }
        });
    }

    private void StopAria2Rpc()
    {
        try
        {
            if (_aria2RpcProcess != null && !_aria2RpcProcess.HasExited) _aria2RpcProcess.Kill();
        }
        catch { }
        _aria2RpcProcess = null;

        // мог остаться с прошлого запуска SwissBox — гасим только наш aria2c из tools\
        foreach (var p in Process.GetProcessesByName("aria2c"))
        {
            try
            {
                if (p.MainModule.FileName.StartsWith(ToolsRoot, StringComparison.OrdinalIgnoreCase)) p.Kill();
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- журнал

    private void LogThreadSafe(string text)
    {
        Ui(() => Log(text));
    }

    private void Log(string text)
    {
        _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + "\r\n");
        _header.Status = text;
        _header.Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _novaWatchdogTimer.Stop();
        base.OnFormClosed(e);
    }
}
