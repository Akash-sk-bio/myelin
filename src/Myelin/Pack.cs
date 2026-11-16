using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Myelin;

/// <summary>Renders a palette into a cached folder of multi-size .cur/.ani files.</summary>
public static class Pack
{
    public const int Version = 3;
    public static readonly int[] Sizes = { 32, 48, 64, 96, 128 };
    // Windows' ANI loader rejects frames holding more than 4 images (verified: any 4 load, 5 fail).
    public static readonly int[] AniSizes = { 32, 48, 64, 128 };

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Myelin", "packs");

    public static string DirFor(Palette p) =>
        Path.Combine(Root, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(p.CacheKey)))[..12].ToLowerInvariant());

    static string NameOf(CursorDef d, int frames) => d.File + (frames > 1 ? ".ani" : ".cur");

    [DllImport("user32.dll")] static extern uint GetDpiForSystem();

    /// <summary>The one pixel size Windows will actually use right now (pointer size × display scale).</summary>
    public static int QuickSize()
    {
        int baseSize = 32;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            if (k?.GetValue("CursorBaseSize") is int b && b >= 32) baseSize = b;
        }
        catch { }
        float scale = Math.Max(1f, GetDpiForSystem() / 96f);
        return Math.Clamp((int)MathF.Round(baseSize * scale), 32, 256);
    }

    /// <param name="quick">0 = full multi-size pack; otherwise a fast single-size pack of that pixel size.</param>
    public static Dictionary<string, string> Ensure(Palette p, Action<float> progress = null, int quick = 0)
    {
        var dir = DirFor(p) + (quick > 0 ? "-q" + quick : "");
        var ok = Path.Combine(dir, "pack.ok");
        if (File.Exists(ok) && File.ReadAllText(ok) == p.CacheKey)
        {
            var map = new Art(p).All().ToDictionary(d => d.Role, d => Path.Combine(dir, NameOf(d, d.Frames(p.Motion).Count)));
            if (map.Values.All(File.Exists)) return map;
        }
        return Build(p, dir, progress, quick);
    }

    public static Dictionary<string, string> Build(Palette p, string dir, Action<float> progress = null, int quick = 0)
    {
        Directory.CreateDirectory(dir);
        var defs = new Art(p).All();
        var frames = defs.Select(d => d.Frames(p.Motion)).ToList();
        var jobs = new List<(int D, int F)>();
        for (int i = 0; i < defs.Count; i++)
            for (int j = 0; j < frames[i].Count; j++) jobs.Add((i, j));

        var blobs = frames.Select(f => new byte[f.Count][]).ToArray();
        int done = 0;
        Parallel.ForEach(jobs, job =>
        {
            var d = defs[job.D];
            var layers = frames[job.D][job.F].Make();
            var sizes = quick > 0 ? new[] { quick } : frames[job.D].Count > 1 ? AniSizes : Sizes;
            var imgs = sizes.Select(s => (s, Neon.Render(layers, s),
                                          (int)MathF.Round(d.Hx * s / 32f), (int)MathF.Round(d.Hy * s / 32f))).ToList();
            blobs[job.D][job.F] = Writer.Cur(imgs);
            progress?.Invoke(Interlocked.Increment(ref done) / (float)jobs.Count);
        });

        var map = new Dictionary<string, string>();
        for (int i = 0; i < defs.Count; i++)
        {
            var path = Path.Combine(dir, NameOf(defs[i], frames[i].Count));
            File.WriteAllBytes(path, frames[i].Count > 1
                ? Writer.Ani(blobs[i], frames[i].Select(f => f.Jiffies).ToArray())
                : blobs[i][0]);
            map[defs[i].Role] = path;
        }
        File.WriteAllText(Path.Combine(dir, "pack.ok"), p.CacheKey);
        return map;
    }

    /// <summary>Delete stale cached packs (only inside our own folder).</summary>
    public static void Prune(string keep)
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            var stale = new DirectoryInfo(Root).GetDirectories()
                .Where(d => !string.Equals(d.FullName.TrimEnd('\\'), keep.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.LastWriteTimeUtc).Skip(6);
            foreach (var d in stale) { try { d.Delete(true); } catch { } }
        }
        catch { }
    }
}

/// <summary>Binary writers for Windows cursor formats (BMP-32bpp images, same layout as aero_*.cur).</summary>
public static class Writer
{
    public static byte[] Cur(IList<(int S, byte[] Px, int Hx, int Hy)> imgs)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)2); w.Write((ushort)imgs.Count);
        int off = 6 + 16 * imgs.Count;
        var dibs = new List<byte[]>();
        foreach (var im in imgs)
        {
            var dib = Dib(im.S, im.Px);
            dibs.Add(dib);
            byte dim = (byte)(im.S >= 256 ? 0 : im.S);
            w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)Math.Clamp(im.Hx, 0, im.S - 1));
            w.Write((ushort)Math.Clamp(im.Hy, 0, im.S - 1));
            w.Write(dib.Length); w.Write(off);
            off += dib.Length;
        }
        foreach (var d in dibs) w.Write(d);
        return ms.ToArray();
    }

    static byte[] Dib(int S, byte[] px)
    {
        int maskStride = (S + 31) / 32 * 4, xor = S * S * 4, and = maskStride * S;
        var b = new byte[40 + xor + and];
        BitConverter.TryWriteBytes(b.AsSpan(0), 40);
        BitConverter.TryWriteBytes(b.AsSpan(4), S);
        BitConverter.TryWriteBytes(b.AsSpan(8), S * 2);
        BitConverter.TryWriteBytes(b.AsSpan(12), (short)1);
        BitConverter.TryWriteBytes(b.AsSpan(14), (short)32);
        BitConverter.TryWriteBytes(b.AsSpan(20), xor + and);
        for (int y = 0; y < S; y++) Buffer.BlockCopy(px, (S - 1 - y) * S * 4, b, 40 + y * S * 4, S * 4);
        int mo = 40 + xor;
        for (int y = 0; y < S; y++)
        {
            int src = S - 1 - y;
            for (int x = 0; x < S; x++)
                if (px[(src * S + x) * 4 + 3] == 0) b[mo + y * maskStride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }
        return b;
    }

    public static byte[] Ani(byte[][] frames, int[] jiffies)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(0); w.Write("ACON"u8);
        w.Write("anih"u8); w.Write(36);
        w.Write(36); w.Write(frames.Length); w.Write(frames.Length); w.Write(0); w.Write(0);
        w.Write(32); w.Write(1); w.Write(Math.Max(1, jiffies[0])); w.Write(1); // AF_ICON
        w.Write("rate"u8); w.Write(frames.Length * 4);
        foreach (var j in jiffies) w.Write(Math.Max(1, j));
        long list = ms.Position;
        w.Write("LIST"u8); w.Write(0); w.Write("fram"u8);
        foreach (var f in frames)
        {
            w.Write("icon"u8); w.Write(f.Length); w.Write(f);
            if ((f.Length & 1) == 1) w.Write((byte)0);
        }
        long end = ms.Position;
        ms.Position = list + 4; w.Write((int)(end - list - 8));
        ms.Position = 4; w.Write((int)(end - 8));
        return ms.ToArray();
    }
}

/// <summary>Per-user cursor scheme install / restore (HKCU only, no admin).</summary>
public static class Scheme
{
    public static readonly string[] Roles =
    {
        "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No",
        "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person"
    };

    const string Key = @"Control Panel\Cursors";
    public const string Name = "Myelin";
    static readonly string[] Legacy = { "BioPulse" };
    static bool Ours(string scheme) => scheme.StartsWith(Name) || Legacy.Any(scheme.StartsWith);
    static string BackupPath => Path.Combine(Settings.Dir, "cursor-backup.json");

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);
    static void Refresh() => SystemParametersInfo(0x57 /*SPI_SETCURSORS*/, 0, IntPtr.Zero, 0x01 | 0x02);

    public static bool IsActive()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return (k?.GetValue("") as string ?? "").StartsWith(Name);
    }

    /// <summary>True when a previous BioPulse build's pointers are installed.</summary>
    public static bool LegacyActive()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        var s = k?.GetValue("") as string ?? "";
        return Legacy.Any(s.StartsWith);
    }

    public static void Apply(Dictionary<string, string> files, string label)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        Backup(k);
        foreach (var r in Roles)
            if (files.TryGetValue(r, out var f)) k.SetValue(r, f, RegistryValueKind.ExpandString);
        k.SetValue("", $"{Name} · {label}");
        k.SetValue("Scheme Source", 1, RegistryValueKind.DWord);
        using (var s = Registry.CurrentUser.CreateSubKey(Key + @"\Schemes"))
            { s.SetValue(Name, string.Join(",", Roles.Select(r => files.GetValueOrDefault(r, ""))), RegistryValueKind.ExpandString); foreach (var l in Legacy) s.DeleteValue(l, false); }
        Refresh();
    }

    static void Backup(RegistryKey k)
    {
        if (File.Exists(BackupPath)) return;
        if (Ours(k.GetValue("") as string ?? "")) return; // never back up our own (or BioPulse's) pointers as "original"
        var d = new Dictionary<string, string>();
        foreach (var r in Roles.Append(""))
            d[r] = k.GetValue(r, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
        d["Scheme Source"] = (k.GetValue("Scheme Source") ?? 2).ToString();
        Directory.CreateDirectory(Settings.Dir);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(d));
    }

    public static void Restore()
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        Dictionary<string, string> d = null;
        try { if (File.Exists(BackupPath)) d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(BackupPath)); }
        catch { }
        d ??= Defaults();
        foreach (var r in Roles) k.SetValue(r, d.GetValueOrDefault(r, ""), RegistryValueKind.ExpandString);
        k.SetValue("", d.GetValueOrDefault("", "Windows Default"));
        k.SetValue("Scheme Source", int.TryParse(d.GetValueOrDefault("Scheme Source", "2"), out var ss) ? ss : 2, RegistryValueKind.DWord);
        using (var s = Registry.CurrentUser.OpenSubKey(Key + @"\Schemes", true)) { s?.DeleteValue(Name, false); foreach (var l in Legacy) s?.DeleteValue(l, false); }
        Refresh();
        try { File.Delete(BackupPath); } catch { }
    }

    static Dictionary<string, string> Defaults()
    {
        const string c = @"%SystemRoot%\cursors\";
        return new()
        {
            ["Arrow"] = c + "aero_arrow.cur", ["Help"] = c + "aero_helpsel.cur", ["AppStarting"] = c + "aero_working.ani",
            ["Wait"] = c + "aero_busy.ani", ["Crosshair"] = "", ["IBeam"] = "", ["NWPen"] = c + "aero_pen.cur",
            ["No"] = c + "aero_unavail.cur", ["SizeNS"] = c + "aero_ns.cur", ["SizeWE"] = c + "aero_ew.cur",
            ["SizeNWSE"] = c + "aero_nwse.cur", ["SizeNESW"] = c + "aero_nesw.cur", ["SizeAll"] = c + "aero_move.cur",
            ["UpArrow"] = c + "aero_up.cur", ["Hand"] = c + "aero_link.cur", ["Pin"] = c + "aero_pin.cur",
            ["Person"] = c + "aero_person.cur", [""] = "Windows Default", ["Scheme Source"] = "2"
        };
    }

    /// <summary>Portable pack: cursor files + right-click-Install INF.</summary>
    public static string Export(Palette p, string outDir)
    {
        var files = Pack.Ensure(p);
        var safe = new string(p.Name.Where(char.IsLetterOrDigit).ToArray());
        outDir = Path.Combine(outDir, "Myelin_" + safe);
        Directory.CreateDirectory(outDir);
        foreach (var f in files.Values) File.Copy(f, Path.Combine(outDir, Path.GetFileName(f)), true);

        var names = Roles.Select(r => Path.GetFileName(files[r])).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("; Myelin cursor scheme — right-click this file → Install, then pick it in Mouse Properties › Pointers.");
        sb.AppendLine("[Version]\r\nsignature=\"$CHICAGO$\"\r\n");
        sb.AppendLine("[DefaultInstall]\r\nCopyFiles = Scheme.Cur\r\nAddReg    = Scheme.Reg\r\n");
        sb.AppendLine("[DestinationDirs]\r\nScheme.Cur = 10,\"%CUR_DIR%\"\r\n");
        sb.Append("[Scheme.Reg]\r\nHKCU,\"Control Panel\\Cursors\\Schemes\",\"%SCHEME_NAME%\",0x00020000,\"");
        sb.Append(string.Join(",", Roles.Select(r => $"%10%\\%CUR_DIR%\\%{r}%")));
        sb.AppendLine("\"\r\n");
        sb.AppendLine("[Scheme.Cur]");
        foreach (var n in names) sb.AppendLine(n);
        sb.AppendLine("\r\n[Strings]");
        sb.AppendLine($"CUR_DIR = \"Cursors\\Myelin_{safe}\"");
        sb.AppendLine($"SCHEME_NAME = \"Myelin {safe}\"");
        for (int i = 0; i < Roles.Length; i++) sb.AppendLine($"{Roles[i]} = \"{names[i]}\"");
        File.WriteAllText(Path.Combine(outDir, "install.inf"), sb.ToString());
        return outDir;
    }
}
