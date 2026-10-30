using System.Buffers.Binary;
using System.Text;

namespace NexysDraft.Core.Pdf;

/// <summary>
/// TrueType reader + subsetter for PDF embedding (CIDFontType2, Identity-H): cmap (formats 4/12) → glyph ids, hmtx advances, and a
/// "glyph-zeroing" subset that keeps glyph ids stable (loca kept full length, unused glyphs empty, composite components included).
/// Only what a PDF viewer needs is kept: head, hhea, hmtx, maxp, loca, glyf (+ cvt/fpgm/prep when present).
/// </summary>
public sealed class TrueTypeFont
{
    readonly byte[] _d; readonly Dictionary<string, (int off, int len)> _tables = new();
    public int UnitsPerEm { get; private set; } = 1000; public int Ascender, Descender, CapHeight, NumGlyphs; public (int x0, int y0, int x1, int y1) BBox;
    readonly int[] _advance; readonly int[] _loca; readonly bool _longLoca; readonly int _glyfOff, _glyfLen;
    readonly Dictionary<int, int> _cmap = new();
    public string FamilyName { get; private set; } = "Font";

    public TrueTypeFont(byte[] data)
    {
        _d = data;
        int numTables = BE16(4);
        for (int i = 0; i < numTables; i++) { int e = 12 + i * 16; string tag = Encoding.ASCII.GetString(_d, e, 4); _tables[tag] = (BE32(e + 8), BE32(e + 12)); }
        var head = _tables["head"]; UnitsPerEm = BE16(head.off + 18); _longLoca = (short)BE16(head.off + 50) != 0;
        BBox = ((short)BE16(head.off + 36), (short)BE16(head.off + 38), (short)BE16(head.off + 40), (short)BE16(head.off + 42));
        var maxp = _tables["maxp"]; NumGlyphs = BE16(maxp.off + 4);
        var hhea = _tables["hhea"]; Ascender = (short)BE16(hhea.off + 4); Descender = (short)BE16(hhea.off + 6); int numH = BE16(hhea.off + 34);
        var hmtx = _tables["hmtx"]; _advance = new int[NumGlyphs]; int last = 0;
        for (int g = 0; g < NumGlyphs; g++) { if (g < numH) last = BE16(hmtx.off + g * 4); _advance[g] = last; }
        var loca = _tables["loca"]; _loca = new int[NumGlyphs + 1];
        for (int g = 0; g <= NumGlyphs; g++) _loca[g] = _longLoca ? BE32(loca.off + g * 4) : BE16(loca.off + g * 2) * 2;
        var glyf = _tables["glyf"]; _glyfOff = glyf.off; _glyfLen = glyf.len;
        CapHeight = (int)(Ascender * 0.72);
        if (_tables.TryGetValue("OS/2", out var os2) && os2.len >= 90 && BE16(os2.off) >= 2) { int ch = (short)BE16(os2.off + 88); if (ch > 0) CapHeight = ch; }
        ParseCmap();
        if (_tables.TryGetValue("name", out var name)) FamilyName = ReadFamilyName(name.off) ?? "Font";
    }
    public static TrueTypeFont Load(string path) => new(File.ReadAllBytes(path));

    int BE16(int o) => BinaryPrimitives.ReadUInt16BigEndian(_d.AsSpan(o, 2));
    int BE32(int o) => (int)BinaryPrimitives.ReadUInt32BigEndian(_d.AsSpan(o, 4));

    void ParseCmap()
    {
        if (!_tables.TryGetValue("cmap", out var cm)) return;
        int n = BE16(cm.off + 2); int best = -1, bestScore = -1;
        for (int i = 0; i < n; i++)
        {
            int pid = BE16(cm.off + 4 + i * 8), eid = BE16(cm.off + 6 + i * 8), off = BE32(cm.off + 8 + i * 8);
            int fmt = BE16(cm.off + off); int score = pid == 3 && eid == 10 && fmt == 12 ? 3 : pid == 3 && eid == 1 && fmt == 4 ? 2 : pid == 0 && (fmt == 4 || fmt == 12) ? 1 : 0;
            if (score > bestScore) { bestScore = score; best = cm.off + off; }
        }
        if (best < 0) return;
        int format = BE16(best);
        if (format == 4)
        {
            int segX2 = BE16(best + 6), seg = segX2 / 2; int endP = best + 14, startP = endP + segX2 + 2, deltaP = startP + segX2, rangeP = deltaP + segX2;
            for (int s = 0; s < seg; s++)
            {
                int end = BE16(endP + s * 2), start = BE16(startP + s * 2), delta = BE16(deltaP + s * 2), ro = BE16(rangeP + s * 2);
                if (start == 0xFFFF) continue;
                for (int c = start; c <= end && c != 0xFFFF; c++)
                {
                    int g;
                    if (ro == 0) g = (c + delta) & 0xFFFF;
                    else { int gi = rangeP + s * 2 + ro + (c - start) * 2; if (gi + 1 >= _d.Length) continue; g = BE16(gi); if (g != 0) g = (g + delta) & 0xFFFF; }
                    if (g != 0) _cmap[c] = g;
                }
            }
        }
        else if (format == 12)
        {
            int groups = BE32(best + 12);
            for (int i = 0; i < groups; i++) { int p = best + 16 + i * 12; int sc = BE32(p), ec = BE32(p + 4), sg = BE32(p + 8); for (int c = sc; c <= ec && c - sc < 70000; c++) _cmap[c] = sg + (c - sc); }
        }
    }
    string? ReadFamilyName(int off)
    {
        int count = BE16(off + 2), str = BE16(off + 4); string? best = null;
        for (int i = 0; i < count; i++)
        {
            int r = off + 6 + i * 12; int pid = BE16(r), eid = BE16(r + 2), lang = BE16(r + 4), nid = BE16(r + 6), len = BE16(r + 8), so = BE16(r + 10);
            if (nid != 1) continue;
            if (pid == 3 && eid == 1 && lang == 0x409) return Encoding.BigEndianUnicode.GetString(_d, off + str + so, len);
            if (best == null && pid == 3) best = Encoding.BigEndianUnicode.GetString(_d, off + str + so, len);
        }
        return best;
    }

    public int Gid(int codepoint) => _cmap.TryGetValue(codepoint, out var g) ? g : 0;
    /// <summary>Advance width in 1/1000 em.</summary>
    public int Width1000(int gid) => gid < _advance.Length ? (int)Math.Round(_advance[gid] * 1000.0 / UnitsPerEm) : 0;
    public double WidthEm(int gid) => gid < _advance.Length ? _advance[gid] / (double)UnitsPerEm : 0;

    /// <summary>Glyph ids used by the text (incl. composite components) → subset font program.</summary>
    public byte[] Subset(IEnumerable<int> gids)
    {
        var keep = new HashSet<int> { 0 }; var stack = new Stack<int>(gids);
        while (stack.Count > 0)
        {
            int g = stack.Pop(); if (g < 0 || g >= NumGlyphs || !keep.Add(g)) continue;
            int off = _loca[g], len = _loca[g + 1] - off; if (len < 10) continue;
            int p = _glyfOff + off; if ((short)BE16(p) >= 0) continue;   // simple glyph
            p += 10;
            while (true)
            {
                int flags = BE16(p), gi = BE16(p + 2); stack.Push(gi); p += 4;
                p += (flags & 1) != 0 ? 4 : 2;
                if ((flags & 8) != 0) p += 2; else if ((flags & 0x40) != 0) p += 4; else if ((flags & 0x80) != 0) p += 8;
                if ((flags & 0x20) == 0) break;
            }
        }
        // new glyf + long loca
        var glyf = new MemoryStream(); var loca = new byte[(NumGlyphs + 1) * 4];
        for (int g = 0; g < NumGlyphs; g++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(g * 4), (uint)glyf.Length);
            if (!keep.Contains(g)) continue;
            int off = _loca[g], len = _loca[g + 1] - off; if (len <= 0 || off + len > _glyfLen) continue;
            glyf.Write(_d, _glyfOff + off, len); while (glyf.Length % 4 != 0) glyf.WriteByte(0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(NumGlyphs * 4), (uint)glyf.Length);
        var head = Slice("head"); head[50] = 0; head[51] = 1;   // indexToLocFormat = long
        var tables = new List<(string tag, byte[] data)> { ("head", head), ("hhea", Slice("hhea")), ("hmtx", Slice("hmtx")), ("maxp", Slice("maxp")), ("loca", loca), ("glyf", glyf.ToArray()) };
        foreach (var t in new[] { "cvt ", "fpgm", "prep" }) if (_tables.ContainsKey(t)) tables.Add((t, Slice(t)));
        tables.Sort((a, b) => string.CompareOrdinal(a.tag, b.tag));
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        int n = tables.Count; int es = 1; while ((es << 1) <= n) es <<= 1; int entrySel = (int)Math.Log2(es);
        WriteU32(ms, 0x00010000); WriteU16(ms, n); WriteU16(ms, es * 16); WriteU16(ms, entrySel); WriteU16(ms, n * 16 - es * 16);
        int dataOff = 12 + n * 16; var offsets = new List<int>(); int cur = dataOff;
        foreach (var t in tables) { offsets.Add(cur); cur += (t.data.Length + 3) & ~3; }
        for (int i = 0; i < n; i++) { ms.Write(Encoding.ASCII.GetBytes(tables[i].tag.PadRight(4))); WriteU32(ms, 0); WriteU32(ms, (uint)offsets[i]); WriteU32(ms, (uint)tables[i].data.Length); }
        foreach (var t in tables) { ms.Write(t.data); int pad = ((t.data.Length + 3) & ~3) - t.data.Length; for (int i = 0; i < pad; i++) ms.WriteByte(0); }
        return ms.ToArray();
    }
    byte[] Slice(string tag) { var t = _tables[tag]; var b = new byte[t.len]; Array.Copy(_d, t.off, b, 0, t.len); return b; }
    static void WriteU16(Stream s, int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
    static void WriteU32(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

    /// <summary>Malgun Gothic (Windows) — the single font of the PDF export; falls back to Gulim / Arial.</summary>
    public static string? FindSystemFont()
    {
        string fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        foreach (var f in new[] { "malgun.ttf", "gulim.ttc", "arial.ttf" }) { string p = Path.Combine(fonts, f); if (File.Exists(p)) return p; }
        return null;
    }
}
