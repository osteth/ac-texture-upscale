using System.Reflection;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Options;

var datDir = args.Length > 1 ? args[1] : @"C:\Users\ostet\ac-decomp\dats";

switch (args.FirstOrDefault())
{
    case "probe":
        Probe();
        break;
    case "stats":
        Stats(datDir);
        break;
    case "sample":
        Sample(datDir, args.Length > 2 ? args[2] : @"C:\Users\ostet\ac-decomp\work\sample", args.Length > 3 ? int.Parse(args[3]) : 4);
        break;
    case "encode":
        EncodeBatch(args[1], args[2], args[3], int.Parse(args[4]));
        break;
    case "verifydat":
        VerifyDat(args[1], args[2], args.Length > 3 ? args[3] : null);
        break;
    case "compact":
        Compact(datDir, args[2], args[3], args[4], args.Length > 5 ? int.Parse(args[5]) : 2);
        break;
    case "todxt":
        ToDxt(args[1], args[2], args[3], args[4]);
        break;
    case "packbins":
        PackBins(datDir, args[2], args[3], args[4], args.Length > 5 ? int.Parse(args[5]) : 2);
        break;
    case "fullmanifest":
        FullManifest(datDir, args[2]);
        break;
    case "extract":
        Extract(datDir, args[2], args[3]);
        break;
    case "collect":
        Collect(datDir, Convert.ToUInt32(args[2], 16), args[3], args[4]);
        break;
    case "whereused":
        WhereUsed(datDir, args[2], args[3]);
        break;
    case "classify":
        Classify(datDir, args[2], args[3]);
        break;
    case "pack":
        Pack(datDir, args[2], args[3], args[4]);
        break;
    default:
        Console.WriteLine("usage: texextract probe | stats [datDir] | sample [datDir] [outDir] [perFormat] | pack srcDatDir manifest.tsv pngDir outDatDir");
        break;
}

// Worker side: for each manifest row, load the model output PNG from upDir, resize it to scale x the original
// size, re-encode to the original pixel format and write <stem>.bin (the RenderSurface SourceData) to outDir.
// Needs only the batch folder (manifest.tsv + .idx/.pal sidecars), never the dats.
static void EncodeBatch(string jobDir, string upDir, string outDir, int scale)
{
    Directory.CreateDirectory(outDir);
    var rows = File.ReadAllLines(Path.Combine(jobDir, "manifest.tsv")).Skip(1).Select(l => l.Split('\t')).ToList();
    int ok = 0, failed = 0;
    Parallel.ForEach(rows, new ParallelOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount }, r =>
    {
        var stem = Path.GetFileNameWithoutExtension(r[0]);
        try
        {
            var format = Enum.Parse<PixelFormat>(r[3]);
            int sw = int.Parse(r[4]), sh = int.Parse(r[5]), w = sw * scale, h = sh * scale;
            var img = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(Path.Combine(upDir, r[0])), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            var rgba = img.Width == w && img.Height == h ? img.Data : ResizeLanczos(img.Data, img.Width, img.Height, w, h);
            byte[] srcData = Array.Empty<byte>(); uint[]? pal = null;
            if (format == PixelFormat.PFID_INDEX16)
            {
                srcData = File.ReadAllBytes(Path.Combine(jobDir, stem + ".idx"));
                var pb = File.ReadAllBytes(Path.Combine(jobDir, stem + ".pal"));
                pal = Enumerable.Range(0, pb.Length / 4).Select(i => BitConverter.ToUInt32(pb, i * 4)).ToArray();
            }
            var data = EncodeCore(format, rgba, w, h, sw, sh, srcData, pal) ?? throw new Exception($"no encoder for {format}");
            File.WriteAllBytes(Path.Combine(outDir, stem + ".bin"), data);
            Interlocked.Increment(ref ok);
        }
        catch (Exception ex) { Interlocked.Increment(ref failed); Console.Error.WriteLine($"{r[0]}: {ex.Message}"); }
    });
    Console.WriteLine($"encoded {ok}, failed {failed}");
    if (failed > 0) System.Environment.Exit(2);
}

// Compares two dat files: every header field, and every entry's metadata and raw bytes. Entries listed in an
// optional manifest are expected to differ (upgraded textures) and are only checked for presence + metadata.
static void VerifyDat(string aPath, string bPath, string? manifestPath)
{
    var expectChanged = manifestPath == null ? new HashSet<uint>()
        : File.ReadAllLines(manifestPath).Skip(1).Where(l => aPath.Contains("highres") == (l.Split('\t')[1] == "highres"))
              .Select(l => Convert.ToUInt32(l.Split('\t')[2], 16)).ToHashSet();
    using var a = new DatDatabase(o => { o.FilePath = aPath; o.AccessType = DatAccessType.Read; });
    using var b = new DatDatabase(o => { o.FilePath = bPath; o.AccessType = DatAccessType.Read; });
    var skipHeader = new HashSet<string> { "FileSize", "FirstFreeBlock", "LastFreeBlock", "FreeBlockCount", "RootBlock" };
    var hdrMembers = typeof(DatReaderWriter.Lib.IO.DatHeader).GetMembers(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m is FieldInfo || (m is PropertyInfo pi && pi.GetIndexParameters().Length == 0)).Where(m => !skipHeader.Contains(m.Name));
    foreach (var m in hdrMembers)
    {
        object? Get(object h) => m is FieldInfo fi ? fi.GetValue(h) : ((PropertyInfo)m).GetValue(h);
        string va = Show(Get(a.Header)), vb = Show(Get(b.Header));
        Console.WriteLine($"  header {m.Name,-16} {(va == vb ? "same" : $"DIFFERENT: {va} vs {vb}")}");
    }
    int same = 0, changed = 0, metaDiff = 0, bytesDiff = 0, missing = 0;
    var diffFields = new Dictionary<string, int>();
    string? example = null;
    var bIds = b.Tree.ToDictionary(e => e.Id);
    foreach (var e in a.Tree)
    {
        if (!bIds.TryGetValue(e.Id, out var f)) { missing++; continue; }
        var d = new List<string>();
        if (e.Flags != f.Flags) d.Add("Flags");
        if (e.Iteration != f.Iteration) d.Add("Iteration");
        if (e.RawDate != f.RawDate) d.Add("RawDate");
        if (e.Version != f.Version) d.Add("Version");
        if (d.Count > 0)
        {
            metaDiff++;
            foreach (var k in d) diffFields[k] = diffFields.GetValueOrDefault(k) + 1;
            example ??= $"{e.Id:X8}: flags {e.Flags}/{f.Flags} iter {e.Iteration}/{f.Iteration} date {e.RawDate}/{f.RawDate} ver {e.Version}/{f.Version}";
        }
        if (expectChanged.Contains(e.Id)) { changed++; continue; }
        a.TryGetFileBytes(e.Id, out byte[] x, false); b.TryGetFileBytes(e.Id, out byte[] y, false);
        if (x.AsSpan().SequenceEqual(y)) same++; else bytesDiff++;
    }
    Console.WriteLine($"  entries: a={a.Tree.Count()} b={bIds.Count}; identical {same}, expected-changed {changed}, " +
                      $"byte mismatches {bytesDiff}, metadata mismatches {metaDiff}, missing in b {missing}");
    if (metaDiff > 0) Console.WriteLine($"  metadata fields differing: {string.Join(", ", diffFields.Select(k => $"{k.Key}={k.Value}"))}; e.g. {example}");

    static string Show(object? v) => v switch
    {
        null => "null",
        System.Collections.IEnumerable s when v is not string => string.Join(",", s.Cast<object>()),
        _ => v.ToString() ?? ""
    };
}

// Builds portal + highres from scratch so replaced textures leave no dead space: every retail entry is copied
// byte-for-byte with its original B-tree metadata (flags, iteration, date), except manifest textures, which are
// written once from their .bin at `scale`. Header fields (versions, transactions, master map, ...) are copied from
// retail. cell and local are copied unchanged.
static void Compact(string srcDatDir, string manifestPath, string binDir, string outDatDir, int scale)
{
    Directory.CreateDirectory(outDatDir);
    foreach (var n in new[] { "client_cell_1.dat", "client_local_English.dat" })
    {
        File.Copy(Path.Combine(srcDatDir, n), Path.Combine(outDatDir, n), true);
        File.SetAttributes(Path.Combine(outDatDir, n), FileAttributes.Normal);
    }
    var rows = File.ReadAllLines(manifestPath).Skip(1).Select(l => l.Split('\t')).ToList();
    foreach (var (label, file) in new[] { ("portal", "client_portal.dat"), ("highres", "client_highres.dat") })
    {
        var replace = rows.Where(r => r[1] == label).ToDictionary(r => Convert.ToUInt32(r[2], 16));
        var srcPath = Path.Combine(srcDatDir, file);
        var dstPath = Path.Combine(outDatDir, file);
        if (File.Exists(dstPath)) File.Delete(dstPath);

        using var src = new DatDatabase(o => { o.FilePath = srcPath; o.AccessType = DatAccessType.Read; });
        var sh = src.Header;

        var dstOpts = new DatDatabaseOptions { FilePath = dstPath, AccessType = DatAccessType.ReadWrite };
        using (File.Create(dstPath)) { }
        var alloc = new DatReaderWriter.Lib.IO.BlockAllocators.StreamBlockAllocator(dstOpts);
        alloc.InitNew(sh.Type, sh.SubSet, sh.BlockSize, 1024);
        alloc.SetVersion(sh.Version, sh.EngineVersion, sh.GameVersion, sh.MajorVersion, sh.MinorVersion);
        using var dst = new DatDatabase(o => { o.FilePath = dstPath; o.AccessType = DatAccessType.ReadWrite; }, alloc);

        int copied = 0, replaced = 0;
        var entries = src.Tree.ToList();
        foreach (var e in entries)
        {
            if (replace.TryGetValue(e.Id, out var r))
            {
                var rs = src.Get<RenderSurface>(e.Id)!;
                rs.Width *= scale; rs.Height *= scale;
                rs.Format = Enum.Parse<PixelFormat>(r[3]);
                rs.SourceData = File.ReadAllBytes(Path.Combine(binDir, Path.GetFileNameWithoutExtension(r[0]) + ".bin"));
                if (!dst.TryWriteFile(rs, e)) throw new Exception($"write failed {e.Id:X8}");
                replaced++;
            }
            else
            {
                // false = raw stored bytes (no decompression), so compressed entries are copied as-is.
                if (!src.TryGetFileBytes(e.Id, out byte[] bytes, false)) throw new Exception($"read failed {e.Id:X8}");
                if (bytes.Length != e.Size) throw new Exception($"{e.Id:X8}: read {bytes.Length} bytes but entry size is {e.Size}");
                if (!dst.TryWriteFileBytes(e.Id, bytes, bytes.Length, e)) throw new Exception($"copy failed {e.Id:X8}");
                copied++;
            }
        }

        // Writing stamps entries with the current time; put back each entry's original date.
        foreach (var e in entries)
        {
            if (!dst.Tree.TryGetFile(e.Id, out var f)) throw new Exception($"missing after write {e.Id:X8}");
            if (f.RawDate == e.RawDate) continue;
            f.RawDate = e.RawDate;
            dst.Tree.Insert(f);
        }

        // Carry over the header fields that InitNew/SetVersion don't cover (arrays are copied element-wise,
        // since the header may expose them as read-only references). Transactions is deliberately left empty: it is
        // a write journal holding retail block offsets, which would point at unrelated data in the rebuilt file.
        foreach (var name in new[] { "MasterMapId", "UseLRU", "NewLRU", "OldLRU" })
        {
            var t = typeof(DatReaderWriter.Lib.IO.DatHeader);
            var m = (MemberInfo?)t.GetProperty(name) ?? t.GetField(name);
            object? Get(object h) => m is FieldInfo fi ? fi.GetValue(h) : ((PropertyInfo)m!).GetValue(h);
            var sv = Get(sh);
            if (sv is Array sa && Get(dst.Header) is Array da && sa.Length == da.Length) Array.Copy(sa, da, sa.Length);
            else if (m is FieldInfo fw) fw.SetValue(dst.Header, sv);
            else if (m is PropertyInfo pw && pw.CanWrite) pw.SetValue(dst.Header, sv);
        }
        typeof(DatReaderWriter.Lib.IO.BlockAllocators.StreamBlockAllocator)
            .GetMethod("WriteHeader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(alloc, null);
        Console.WriteLine($"{file}: {entries.Count} entries ({copied} copied, {replaced} upgraded), {new FileInfo(dstPath).Length / 1048576.0:F0} MB");
    }
}

// Re-encodes uncompressed 2x bins as DXT to save space: R8G8B8 -> DXT1, A8R8G8B8 -> DXT1 when fully opaque,
// otherwise DXT5. Writes the new bins to outBinDir and a manifest whose format column holds the new format
// (packbins then switches the RenderSurface's format). Rows already compressed are passed through unchanged.
static void ToDxt(string manifestPath, string binDir, string outBinDir, string outManifest)
{
    Directory.CreateDirectory(outBinDir);
    var lines = File.ReadAllLines(manifestPath);
    var outRows = new string[lines.Length - 1];
    int converted = 0, kept = 0; long before = 0, after = 0;
    Parallel.For(0, lines.Length - 1, i =>
    {
        var r = lines[i + 1].Split('\t');
        var stem = Path.GetFileNameWithoutExtension(r[0]);
        var fmt = Enum.Parse<PixelFormat>(r[3]);
        int w = int.Parse(r[4]) * 2, h = int.Parse(r[5]) * 2;
        var src = Path.Combine(binDir, stem + ".bin");
        if (fmt is not (PixelFormat.PFID_R8G8B8 or PixelFormat.PFID_A8R8G8B8) || w % 4 != 0 || h % 4 != 0)
        {
            outRows[i] = lines[i + 1];
            if (File.Exists(src)) File.Copy(src, Path.Combine(outBinDir, stem + ".bin"), true);
            Interlocked.Increment(ref kept);
            return;
        }
        var data = File.ReadAllBytes(src);
        var bpp = fmt == PixelFormat.PFID_R8G8B8 ? 3 : 4;
        var rgba = new byte[w * h * 4];
        var opaque = true;
        for (var p = 0; p < w * h; p++)
        {
            rgba[p * 4] = data[p * bpp + 2]; rgba[p * 4 + 1] = data[p * bpp + 1]; rgba[p * 4 + 2] = data[p * bpp];
            rgba[p * 4 + 3] = bpp == 4 ? data[p * 4 + 3] : (byte)255;
            if (rgba[p * 4 + 3] != 255) opaque = false;
        }
        var target = opaque ? PixelFormat.PFID_DXT1 : PixelFormat.PFID_DXT5;
        var dxt = EncodeCore(target, rgba, w, h, w, h, Array.Empty<byte>(), null)!;
        File.WriteAllBytes(Path.Combine(outBinDir, stem + ".bin"), dxt);
        r[3] = target.ToString();
        outRows[i] = string.Join('\t', r);
        Interlocked.Increment(ref converted);
        Interlocked.Add(ref before, data.Length); Interlocked.Add(ref after, dxt.Length);
    });
    File.WriteAllLines(outManifest, outRows.Prepend(lines[0]));
    Console.WriteLine($"converted {converted} to DXT ({before / 1048576.0:F0} MB -> {after / 1048576.0:F0} MB), kept {kept}");
}

// Laptop side: writes worker-encoded <stem>.bin files into a fresh copy of the dats at scale x size.
static void PackBins(string srcDatDir, string manifestPath, string binDir, string outDatDir, int scale)
{
    Directory.CreateDirectory(outDatDir);
    foreach (var n in new[] { "client_portal.dat", "client_cell_1.dat", "client_local_English.dat", "client_highres.dat" })
    {
        var dst = Path.Combine(outDatDir, n);
        File.Copy(Path.Combine(srcDatDir, n), dst, true);
        File.SetAttributes(dst, FileAttributes.Normal);
    }
    var rows = File.ReadAllLines(manifestPath).Skip(1).Select(l => l.Split('\t')).ToList();
    int written = 0, missing = 0;
    var failedRows = new List<string>();
    using (var dats = new DatCollection(outDatDir, DatAccessType.ReadWrite))
    {
        foreach (var r in rows)
        {
            var bin = Path.Combine(binDir, Path.GetFileNameWithoutExtension(r[0]) + ".bin");
            if (!File.Exists(bin)) { missing++; continue; }
            var db = r[1] == "highres" ? dats.HighRes : (DatDatabase)dats.Portal;
            var rs = db.Get<RenderSurface>(Convert.ToUInt32(r[2], 16))!;
            var data = File.ReadAllBytes(bin);
            var (w, h) = (rs.Width * scale, rs.Height * scale);
            rs.Format = Enum.Parse<PixelFormat>(r[3]);  // the manifest may switch uncompressed textures to DXT
            var expected = rs.Format switch
            {
                PixelFormat.PFID_DXT1 => w * h / 2, PixelFormat.PFID_DXT3 or PixelFormat.PFID_DXT5 => w * h,
                PixelFormat.PFID_INDEX16 => w * h * 2, PixelFormat.PFID_A8R8G8B8 => w * h * 4, PixelFormat.PFID_R8G8B8 => w * h * 3, _ => -1
            };
            if (rs.Format is PixelFormat.PFID_DXT1 or PixelFormat.PFID_DXT3 or PixelFormat.PFID_DXT5)
                expected = ((w + 3) / 4) * ((h + 3) / 4) * (rs.Format == PixelFormat.PFID_DXT1 ? 8 : 16);
            if (data.Length != expected) throw new Exception($"{r[0]}: {data.Length} bytes, expected {expected} for {w}x{h} {rs.Format}");
            rs.Width = w; rs.Height = h; rs.SourceData = data;
            try
            {
                if (!db.TryWriteFile(rs)) throw new Exception("TryWriteFile returned false");
                written++;
            }
            catch (Exception ex)
            {
                // Report and keep going, so one run lists every texture the dat format can't take.
                failedRows.Add($"{r[0]}\t{w}x{h}\t{data.Length}\t{ex.GetType().Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"wrote {written}, missing {missing}, failed {failedRows.Count}; iterations portal={dats.Portal.Iteration.CurrentIteration} highres={dats.HighRes.Iteration.CurrentIteration}");
    }
    if (failedRows.Count > 0)
    {
        File.WriteAllLines(Path.Combine(outDatDir, "pack_failures.tsv"), failedRows);
        foreach (var f in failedRows.Take(15)) Console.WriteLine("  FAILED " + f);
    }
}

// Separable Lanczos-3 resize of RGBA8 with premultiplied alpha (so transparent edges don't bleed dark fringes).
static byte[] ResizeLanczos(byte[] src, int sw, int sh, int dw, int dh)
{
    static double L(double x) { x = Math.Abs(x); if (x < 1e-8) return 1; if (x >= 3) return 0; var px = Math.PI * x; return 3 * Math.Sin(px) * Math.Sin(px / 3) / (px * px); }
    static (int[] start, double[][] wts) Weights(int s, int d)
    {
        double scale = (double)s / d, support = 3 * Math.Max(scale, 1), f = Math.Max(scale, 1);
        var start = new int[d]; var wts = new double[d][];
        for (var i = 0; i < d; i++)
        {
            var center = (i + 0.5) * scale - 0.5;
            int lo = (int)Math.Floor(center - support) + 1, hi = (int)Math.Floor(center + support);
            var w = new double[hi - lo + 1]; double sum = 0;
            for (var j = lo; j <= hi; j++) sum += w[j - lo] = L((j - center) / f);
            for (var k = 0; k < w.Length; k++) w[k] /= sum;
            start[i] = lo; wts[i] = w;
        }
        return (start, wts);
    }
    var pre = new double[sw * sh * 4];
    for (var i = 0; i < sw * sh; i++)
    {
        var a = src[i * 4 + 3] / 255.0;
        pre[i * 4] = src[i * 4] * a; pre[i * 4 + 1] = src[i * 4 + 1] * a; pre[i * 4 + 2] = src[i * 4 + 2] * a; pre[i * 4 + 3] = a;
    }
    var (hx, hw) = Weights(sw, dw);
    var tmp = new double[dw * sh * 4];
    Parallel.For(0, sh, y =>
    {
        for (var x = 0; x < dw; x++)
        {
            double r = 0, g = 0, b = 0, a = 0; var w = hw[x];
            for (var k = 0; k < w.Length; k++)
            {
                var o = (y * sw + Math.Clamp(hx[x] + k, 0, sw - 1)) * 4;
                r += pre[o] * w[k]; g += pre[o + 1] * w[k]; b += pre[o + 2] * w[k]; a += pre[o + 3] * w[k];
            }
            var t = (y * dw + x) * 4; tmp[t] = r; tmp[t + 1] = g; tmp[t + 2] = b; tmp[t + 3] = a;
        }
    });
    var (vy, vw) = Weights(sh, dh);
    var dst = new byte[dw * dh * 4];
    Parallel.For(0, dh, y =>
    {
        var w = vw[y];
        for (var x = 0; x < dw; x++)
        {
            double r = 0, g = 0, b = 0, a = 0;
            for (var k = 0; k < w.Length; k++)
            {
                var o = (Math.Clamp(vy[y] + k, 0, sh - 1) * dw + x) * 4;
                r += tmp[o] * w[k]; g += tmp[o + 1] * w[k]; b += tmp[o + 2] * w[k]; a += tmp[o + 3] * w[k];
            }
            var d = (y * dw + x) * 4;
            a = Math.Clamp(a, 0, 1);
            var inv = a > 1e-6 ? 1 / a : 0;
            dst[d] = (byte)Math.Clamp(Math.Round(r * inv), 0, 255); dst[d + 1] = (byte)Math.Clamp(Math.Round(g * inv), 0, 255);
            dst[d + 2] = (byte)Math.Clamp(Math.Round(b * inv), 0, 255); dst[d + 3] = (byte)Math.Round(a * 255);
        }
    });
    return dst;
}

// Manifest of every 3D-model texture (referenced by any SurfaceTexture) in an encodable format, from both
// portal and highres. Terrain (LSCAPE) and UI art are never referenced by SurfaceTextures, so they drop out.
static void FullManifest(string datDir, string outManifest)
{
    using var dats = new DatCollection(datDir, DatAccessType.Read);
    var used3d = new HashSet<uint>();
    foreach (var id in dats.Portal.GetAllIdsOfType<SurfaceTexture>())
        if (dats.Portal.TryGet<SurfaceTexture>(id, out var st))
            foreach (var t in st.Textures) used3d.Add(t.DataId);

    var encodable = new[] { PixelFormat.PFID_DXT1, PixelFormat.PFID_DXT3, PixelFormat.PFID_DXT5, PixelFormat.PFID_INDEX16,
                            PixelFormat.PFID_A8R8G8B8, PixelFormat.PFID_R8G8B8 };
    var rows = new List<string>();
    var skipped = new Dictionary<string, int>();
    foreach (var (label, db) in new (string, DatDatabase)[] { ("portal", dats.Portal), ("highres", dats.HighRes) })
        foreach (var id in db.GetAllIdsOfType<RenderSurface>().OrderBy(i => i))
        {
            if (!db.TryGet<RenderSurface>(id, out var rs)) continue;
            var why = !used3d.Contains(id) ? "not used on 3D models" : !encodable.Contains(rs.Format) ? rs.Format.ToString() : rs.Width < 4 || rs.Height < 4 ? "smaller than 4px" : null;
            if (why != null) { skipped[$"{label}: {why}"] = skipped.GetValueOrDefault($"{label}: {why}") + 1; continue; }
            rows.Add($"{label}_{rs.Format.ToString().Replace("PFID_", "")}_{id:X8}.png\t{label}\t{id:X8}\t{rs.Format}\t{rs.Width}\t{rs.Height}\t{rs.DefaultPaletteId:X8}");
        }
    File.WriteAllLines(outManifest, rows.Prepend("file\tdat\tid\tformat\twidth\theight\tpalette"));
    long px = rows.Sum(r => { var p = r.Split('\t'); return long.Parse(p[4]) * long.Parse(p[5]); });
    Console.WriteLine($"{rows.Count} textures to upscale ({px / 1e6:F0} megapixels)");
    foreach (var g in rows.GroupBy(r => r.Split('\t')[1] + " " + r.Split('\t')[3]).OrderBy(g => g.Key)) Console.WriteLine($"  {g.Key,-30} {g.Count()}");
    foreach (var s in skipped.OrderBy(k => k.Key)) Console.WriteLine($"  skipped {s.Key}: {s.Value}");
}

// Decodes every texture listed in a manifest to an RGBA PNG named as in the manifest's file column.
static void Extract(string datDir, string manifestPath, string outDir)
{
    Directory.CreateDirectory(outDir);
    using var dats = new DatCollection(datDir, DatAccessType.Read);
    int ok = 0, failed = 0;
    foreach (var r in File.ReadAllLines(manifestPath).Skip(1).Select(l => l.Split('\t')))
    {
        var db = r[1] == "highres" ? dats.HighRes : (DatDatabase)dats.Portal;
        var rs = db.Get<RenderSurface>(Convert.ToUInt32(r[2], 16))!;
        var rgba = Decode(rs, dats);
        if (rgba == null) { failed++; Console.WriteLine($"no decoder: {r[0]}"); continue; }
        using (var fs = File.Create(Path.Combine(outDir, r[0])))
            new StbImageWriteSharp.ImageWriter().WritePng(rgba, rs.Width, rs.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, fs);
        // Palettized textures carry their original indices and palette so workers can re-encode without dats.
        if (rs.Format == PixelFormat.PFID_INDEX16 && dats.Portal.TryGet<Palette>(rs.DefaultPaletteId, out var pal))
        {
            var stem = Path.Combine(outDir, Path.GetFileNameWithoutExtension(r[0]));
            File.WriteAllBytes(stem + ".idx", rs.SourceData);
            File.WriteAllBytes(stem + ".pal", PaletteArgb(pal).SelectMany(BitConverter.GetBytes).ToArray());
        }
        ok++;
    }
    Console.WriteLine($"extracted {ok}, failed {failed}");
}

// Builds a manifest of every texture visible in one landblock: its dungeon cells (surfaces + static objects),
// outdoor scenery/buildings, plus extra Setup/ClothingTable ids (world-db objects and the player's gear).
static void Collect(string datDir, uint landblock, string extraIdsPath, string outManifest)
{
    using var dats = new DatCollection(datDir, DatAccessType.Read);
    var portal = dats.Portal;
    var roots = new HashSet<uint>();
    foreach (var l in File.ReadAllLines(extraIdsPath).Where(l => l.Trim().Length > 0)) roots.Add(Convert.ToUInt32(l.Trim(), 16));

    var prefix = landblock << 16;
    var lbiId = prefix | 0xFFFE;
    if (dats.Cell.TryGet<LandBlockInfo>(lbiId, out var lbi))
    {
        foreach (var v in CollectIds(lbi, 0)) roots.Add(v);
        for (uint c = 0x100; c < 0x100 + lbi.NumCells; c++)
            if (dats.Cell.TryGet<EnvCell>(prefix | c, out var cell))
            {
                foreach (var s in cell.Surfaces) roots.Add(0x08000000u | (Convert.ToUInt32(s) & 0xFFFFu));
                foreach (var v in CollectIds(cell.StaticObjects, 0)) roots.Add(v);
            }
        Console.WriteLine($"landblock {landblock:X4}: {lbi.NumCells} cells");
    }

    var found = new HashSet<uint>();
    var seen = new HashSet<uint>();
    var stack = new Stack<uint>(roots);
    while (stack.Count > 0)
    {
        var id = stack.Pop();
        if (!seen.Add(id)) continue;
        switch (id >> 24)
        {
            case 0x02 when portal.TryGet<Setup>(id, out var su): foreach (var p in su.Parts) stack.Push(IdOf(p)); break;
            case 0x01 when portal.TryGet<GfxObj>(id, out var g): foreach (var s in g.Surfaces) stack.Push(IdOf(s)); break;
            case 0x08 when portal.TryGet<Surface>(id, out var sf): if (sf.OrigTextureId != 0) stack.Push(sf.OrigTextureId); break;
            case 0x05 when portal.TryGet<SurfaceTexture>(id, out var st): foreach (var t in st.Textures) stack.Push(t.DataId); break;
            case 0x10 when portal.TryGet<ClothingTable>(id, out var ct):
                foreach (var v in CollectIds(ct, 0)) if ((v >> 24) is 0x01 or 0x05 or 0x08) stack.Push(v);
                break;
            case 0x06: found.Add(id); break;
        }
    }

    var encodable = new[] { PixelFormat.PFID_DXT1, PixelFormat.PFID_DXT3, PixelFormat.PFID_DXT5, PixelFormat.PFID_INDEX16,
                            PixelFormat.PFID_A8R8G8B8, PixelFormat.PFID_R8G8B8 };
    var rows = new List<string>();
    var skipped = new Dictionary<string, int>();
    foreach (var id in found.OrderBy(i => i))
        foreach (var (label, db) in new (string, DatDatabase)[] { ("portal", portal), ("highres", dats.HighRes) })
            if (db.TryGet<RenderSurface>(id, out var rs))
            {
                if (!encodable.Contains(rs.Format)) { skipped[rs.Format.ToString()] = skipped.GetValueOrDefault(rs.Format.ToString()) + 1; continue; }
                var name = $"{label}_{rs.Format.ToString().Replace("PFID_", "")}_{id:X8}.png";
                rows.Add($"{name}\t{label}\t{id:X8}\t{rs.Format}\t{rs.Width}\t{rs.Height}\t{rs.DefaultPaletteId:X8}");
            }
    File.WriteAllLines(outManifest, rows.Prepend("file\tdat\tid\tformat\twidth\theight\tpalette"));
    long px = rows.Sum(r => { var p = r.Split('\t'); return long.Parse(p[4]) * long.Parse(p[5]); });
    Console.WriteLine($"{found.Count} textures referenced; {rows.Count} portal/highres entries to upscale ({px / 1e6:F1} megapixels)");
    foreach (var g in rows.GroupBy(r => r.Split('\t')[3])) Console.WriteLine($"  {g.Key,-20} {g.Count()}");
    foreach (var s in skipped) Console.WriteLine($"  skipped {s.Key}: {s.Value}");
}

// Traces each manifest RenderSurface up to the Setups (models) and ClothingTables that display it, so the
// world database can be searched for objects that show the texture. Writes "renderSurface\tkind\tid" rows.
static void WhereUsed(string datDir, string manifestPath, string outPath)
{
    using var dats = new DatCollection(datDir, DatAccessType.Read);
    var portal = dats.Portal;
    var targets = File.ReadAllLines(manifestPath).Skip(1).Select(l => Convert.ToUInt32(l.Split('\t')[2], 16)).ToHashSet();

    var texToRs = new Dictionary<uint, List<uint>>();            // SurfaceTexture -> target RenderSurfaces
    foreach (var id in portal.GetAllIdsOfType<SurfaceTexture>())
        if (portal.TryGet<SurfaceTexture>(id, out var st))
            foreach (var t in st.Textures.Where(t => targets.Contains(t.DataId)))
                (texToRs.TryGetValue(id, out var l) ? l : texToRs[id] = new()).Add(t.DataId);

    var surfToRs = new Dictionary<uint, List<uint>>();           // Surface -> target RenderSurfaces
    foreach (var id in portal.GetAllIdsOfType<Surface>())
        if (portal.TryGet<Surface>(id, out var s) && texToRs.TryGetValue(s.OrigTextureId, out var rs))
            surfToRs[id] = rs;

    var gfxToRs = new Dictionary<uint, HashSet<uint>>();         // GfxObj -> target RenderSurfaces
    foreach (var id in portal.GetAllIdsOfType<GfxObj>())
        if (portal.TryGet<GfxObj>(id, out var g))
            foreach (var sid in g.Surfaces.Select(IdOf))
                if (surfToRs.TryGetValue(sid, out var rs))
                    (gfxToRs.TryGetValue(id, out var h) ? h : gfxToRs[id] = new()).UnionWith(rs);

    var rows = new HashSet<string>();
    foreach (var id in portal.GetAllIdsOfType<Setup>())
        if (portal.TryGet<Setup>(id, out var su))
            foreach (var pid in su.Parts.Select(IdOf))
                if (gfxToRs.TryGetValue(pid, out var rs))
                    foreach (var r in rs) rows.Add($"{r:X8}\tsetup\t{id}");

    // Armor/clothing swap textures in via ClothingTable texture changes (SurfaceTexture ids), or add GfxObjs.
    foreach (var id in portal.GetAllIdsOfType<ClothingTable>())
        if (portal.TryGet<ClothingTable>(id, out var ct))
            foreach (var v in CollectIds(ct, 0))
            {
                if (texToRs.TryGetValue(v, out var rs1)) foreach (var r in rs1) rows.Add($"{r:X8}\tclothing\t{id}");
                if (gfxToRs.TryGetValue(v, out var rs2)) foreach (var r in rs2) rows.Add($"{r:X8}\tclothing\t{id}");
            }

    File.WriteAllLines(outPath, rows.OrderBy(r => r));
    foreach (var g in rows.Select(r => r.Split('\t')).GroupBy(r => r[0]).OrderBy(g => g.Key))
        Console.WriteLine($"{g.Key}: {g.Count(r => r[1] == "setup")} setups, {g.Count(r => r[1] == "clothing")} clothing tables");
    Console.WriteLine($"not found: {string.Join(", ", targets.Where(t => !rows.Any(r => r.StartsWith($"{t:X8}"))).Select(t => t.ToString("X8")))}");
}

static uint IdOf(object o) => o switch
{
    uint u => u,
    _ => (uint)(o.GetType().GetProperty("DataId")?.GetValue(o) ?? o.GetType().GetField("DataId")?.GetValue(o) ?? 0u)
};

// Every uint (or DataId) reachable inside a dat object; used to find texture/GfxObj references in nested types.
static IEnumerable<uint> CollectIds(object? o, int depth)
{
    if (o == null || depth > 8) yield break;
    var t = o.GetType();
    if (o is uint u) { yield return u; yield break; }
    if (t.IsPrimitive || o is string || t.IsEnum) yield break;
    if (t.Name.StartsWith("QualifiedDataId")) { yield return IdOf(o); yield break; }
    if (o is System.Collections.IDictionary dict)
    {
        foreach (System.Collections.DictionaryEntry e in dict)
        {
            foreach (var x in CollectIds(e.Key, depth + 1)) yield return x;
            foreach (var x in CollectIds(e.Value, depth + 1)) yield return x;
        }
        yield break;
    }
    if (o is System.Collections.IEnumerable seq)
    {
        foreach (var e in seq) foreach (var x in CollectIds(e, depth + 1)) yield return x;
        yield break;
    }
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        foreach (var x in CollectIds(f.GetValue(o), depth + 1)) yield return x;
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.Name is not ("HeaderFlags" or "DBObjType")))
        foreach (var x in CollectIds(p.GetValue(o), depth + 1)) yield return x;
}

// Splits a manifest by usage: RenderSurfaces reached through a SurfaceTexture are mapped onto 3D models/terrain
// (UVs are normalized, so a bigger texture is safe); everything else is drawn directly by the UI at pixel size.
static void Classify(string datDir, string manifestPath, string outPrefix)
{
    using var dats = new DatCollection(datDir, DatAccessType.Read);
    var used3d = new HashSet<uint>();
    foreach (var id in dats.Portal.GetAllIdsOfType<SurfaceTexture>())
        if (dats.Portal.TryGet<SurfaceTexture>(id, out var st))
            foreach (var t in st.Textures) used3d.Add(t.DataId);
    Console.WriteLine($"{used3d.Count} RenderSurfaces are referenced by SurfaceTextures (3D)");

    var lines = File.ReadAllLines(manifestPath);
    var header = lines[0];
    var rows = lines.Skip(1).ToList();
    var world = rows.Where(r => used3d.Contains(Convert.ToUInt32(r.Split('\t')[2], 16))).ToList();
    var other = rows.Except(world).ToList();
    File.WriteAllLines(outPrefix + "_3d.tsv", world.Prepend(header));
    File.WriteAllLines(outPrefix + "_ui.tsv", other.Prepend(header));
    Console.WriteLine($"3D: {world.Count}  UI/other: {other.Count}");
    foreach (var r in other) Console.WriteLine("  UI/other: " + r.Split('\t')[0]);
}

// Copies portal + highres dats to outDatDir and replaces each manifest texture with its upscaled PNG,
// re-encoded to the texture's original pixel format. Iterations are left untouched so ACE accepts the dats.
static void Pack(string srcDatDir, string manifestPath, string pngDir, string outDatDir)
{
    Directory.CreateDirectory(outDatDir);
    foreach (var n in new[] { "client_portal.dat", "client_cell_1.dat", "client_local_English.dat", "client_highres.dat" })
    {
        var dst = Path.Combine(outDatDir, n);
        File.Copy(Path.Combine(srcDatDir, n), dst, true);
        File.SetAttributes(dst, FileAttributes.Normal);
    }
    var rows = File.ReadAllLines(manifestPath).Skip(1).Select(l => l.Split('\t')).ToList();
    using (var dats = new DatCollection(outDatDir, DatAccessType.ReadWrite))
    {
        var before = (dats.Portal.Iteration.CurrentIteration, dats.HighRes.Iteration.CurrentIteration);
        foreach (var r in rows)
        {
            var (file, db) = (r[0], r[1] == "highres" ? dats.HighRes : (DatDatabase)dats.Portal);
            var id = Convert.ToUInt32(r[2], 16);
            var rs = db.Get<RenderSurface>(id)!;
            var img = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(Path.Combine(pngDir, file)), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            var data = Encode(rs, img.Data, img.Width, img.Height, dats);
            if (data == null) { Console.WriteLine($"skip {file}: no encoder for {rs.Format}"); continue; }
            var (ow, oh) = (rs.Width, rs.Height);
            rs.Width = img.Width; rs.Height = img.Height; rs.SourceData = data;
            if (!db.TryWriteFile(rs)) throw new Exception($"write failed for {id:X8}");
            Console.WriteLine($"{r[1],-7} {id:X8} {rs.Format,-26} {ow}x{oh} -> {img.Width}x{img.Height}");
        }
        Console.WriteLine($"iterations before: portal={before.Item1} highres={before.Item2}");
    }
    // Reopen read-only to confirm the writes landed and the iterations didn't move.
    using var check = new DatCollection(outDatDir, DatAccessType.Read);
    Console.WriteLine($"iterations after:  portal={check.Portal.Iteration.CurrentIteration} highres={check.HighRes.Iteration.CurrentIteration}");
    var bad = rows.Count(r =>
    {
        var db = r[1] == "highres" ? check.HighRes : (DatDatabase)check.Portal;
        return !db.TryGet<RenderSurface>(Convert.ToUInt32(r[2], 16), out var rs) || rs.Width != int.Parse(r[4]) * 2;
    });
    Console.WriteLine($"verified {rows.Count - bad}/{rows.Count} textures read back at 2x");
}

// Encodes RGBA8 back to the surface's own format. Palettized surfaces only pick from the palette indices in
// the 3x3 source neighbourhood, so every pixel stays inside the same palette-swap range (armor dyes etc.).
static byte[]? Encode(RenderSurface rs, byte[] rgba, int w, int h, DatCollection dats)
{
    uint[]? pal = null;
    if (rs.Format == PixelFormat.PFID_INDEX16)
    {
        if (!dats.Portal.TryGet<Palette>(rs.DefaultPaletteId, out var p)) return null;
        pal = PaletteArgb(p);
    }
    return EncodeCore(rs.Format, rgba, w, h, rs.Width, rs.Height, rs.SourceData, pal);
}

static uint[] PaletteArgb(Palette p) =>
    p.Colors.Select(c => (uint)c.Alpha << 24 | (uint)c.Red << 16 | (uint)c.Green << 8 | c.Blue).ToArray();

// Encodes RGBA8 (w x h) into `format`. For INDEX16, srcData/srcW/srcH are the original indices and pal the
// ARGB palette; this needs no dat access, so it can run on the GPU workers.
static byte[]? EncodeCore(PixelFormat format, byte[] rgba, int w, int h, int srcW, int srcH, byte[] srcData, uint[]? pal)
{
    var px = w * h;
    switch (format)
    {
        case PixelFormat.PFID_DXT1:
        case PixelFormat.PFID_DXT3:
        case PixelFormat.PFID_DXT5:
            var hasAlpha = false;
            for (var i = 3; i < rgba.Length; i += 4) if (rgba[i] < 128) { hasAlpha = true; break; }
            var enc = new BCnEncoder.Encoder.BcEncoder();
            enc.OutputOptions.GenerateMipMaps = false;
            enc.OutputOptions.Quality = BCnEncoder.Encoder.CompressionQuality.BestQuality;
            enc.OutputOptions.Format = format == PixelFormat.PFID_DXT1
                ? (hasAlpha ? BCnEncoder.Shared.CompressionFormat.Bc1WithAlpha : BCnEncoder.Shared.CompressionFormat.Bc1)
                : format == PixelFormat.PFID_DXT3 ? BCnEncoder.Shared.CompressionFormat.Bc2 : BCnEncoder.Shared.CompressionFormat.Bc3;
            return enc.EncodeToRawBytes(rgba, w, h, BCnEncoder.Encoder.PixelFormat.Rgba32)[0];
        case PixelFormat.PFID_A8R8G8B8:
            var a = new byte[px * 4];
            for (var i = 0; i < px; i++) { a[i * 4] = rgba[i * 4 + 2]; a[i * 4 + 1] = rgba[i * 4 + 1]; a[i * 4 + 2] = rgba[i * 4]; a[i * 4 + 3] = rgba[i * 4 + 3]; }
            return a;
        case PixelFormat.PFID_R8G8B8:
        case PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8:
            var b = new byte[px * 3];
            for (var i = 0; i < px; i++) { b[i * 3] = rgba[i * 4 + 2]; b[i * 3 + 1] = rgba[i * 4 + 1]; b[i * 3 + 2] = rgba[i * 4]; }
            return b;
        case PixelFormat.PFID_INDEX16:
            if (pal == null) return null;
            var (sw, sh, src) = (srcW, srcH, srcData);
            var outIdx = new byte[px * 2];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                int sx = x * sw / w, sy = y * sh / h, o = (y * w + x) * 4;
                int best = BitConverter.ToUInt16(src, (sy * sw + sx) * 2), bestD = int.MaxValue;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = Math.Clamp(sx + dx, 0, sw - 1), ny = Math.Clamp(sy + dy, 0, sh - 1);
                    int idx = BitConverter.ToUInt16(src, (ny * sw + nx) * 2);
                    if (idx >= pal.Length) continue;
                    var c = pal[idx];
                    int dr = (int)(c >> 16 & 0xFF) - rgba[o], dg = (int)(c >> 8 & 0xFF) - rgba[o + 1], db = (int)(c & 0xFF) - rgba[o + 2], da = (int)(c >> 24) - rgba[o + 3];
                    var d = dr * dr + dg * dg + db * db + da * da;
                    if (d < bestD) { bestD = d; best = idx; }
                }
                BitConverter.TryWriteBytes(outIdx.AsSpan((y * w + x) * 2), (ushort)best);
            }
            return outIdx;
        default:
            return null;
    }
}

// Extracts an evenly spaced test set per pixel format (textures >= 64px) as PNGs, plus a manifest
// recording everything needed to re-encode them back into the dat.
static void Sample(string datDir, string outDir, int perFormat)
{
    var wanted = new[] { PixelFormat.PFID_DXT1, PixelFormat.PFID_DXT5, PixelFormat.PFID_INDEX16, PixelFormat.PFID_A8R8G8B8,
                         PixelFormat.PFID_R8G8B8, PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8 };
    Directory.CreateDirectory(outDir);
    var manifest = new List<string>();
    using var dats = new DatCollection(datDir, DatAccessType.Read);
    foreach (var (label, db) in new (string, DatDatabase)[] { ("portal", dats.Portal), ("highres", dats.HighRes) })
    {
        var byFormat = new Dictionary<PixelFormat, List<uint>>();
        foreach (var id in db.GetAllIdsOfType<RenderSurface>())
        {
            if (!db.TryGet<RenderSurface>(id, out var rs) || rs.Width < 64 || rs.Height < 64 || !wanted.Contains(rs.Format)) continue;
            (byFormat.TryGetValue(rs.Format, out var l) ? l : byFormat[rs.Format] = new List<uint>()).Add(id);
        }
        foreach (var (fmt, ids) in byFormat)
        {
            var n = Math.Min(perFormat, ids.Count);
            for (var i = 0; i < n; i++)
            {
                var id = ids[(int)((long)i * ids.Count / n)];
                var rs = db.Get<RenderSurface>(id)!;
                var rgba = Decode(rs, dats);
                if (rgba == null) { Console.WriteLine($"skip {id:X8} {fmt}: no decoder"); continue; }
                var name = $"{label}_{fmt.ToString().Replace("PFID_", "")}_{id:X8}.png";
                using (var fs = File.Create(Path.Combine(outDir, name)))
                    new StbImageWriteSharp.ImageWriter().WritePng(rgba, rs.Width, rs.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, fs);
                manifest.Add($"{name}\t{label}\t{id:X8}\t{fmt}\t{rs.Width}\t{rs.Height}\t{rs.DefaultPaletteId:X8}");
                Console.WriteLine($"{name} {rs.Width}x{rs.Height}");
            }
        }
    }
    File.WriteAllLines(Path.Combine(outDir, "manifest.tsv"), manifest.Prepend("file\tdat\tid\tformat\twidth\theight\tpalette"));
}

// Decodes a RenderSurface to tightly packed RGBA8. D3D formats are little-endian, so A8R8G8B8 is stored B,G,R,A.
static byte[]? Decode(RenderSurface rs, DatCollection dats)
{
    var src = rs.SourceData;
    var px = rs.Width * rs.Height;
    var dst = new byte[px * 4];
    switch (rs.Format)
    {
        case PixelFormat.PFID_DXT1:
        case PixelFormat.PFID_DXT3:
        case PixelFormat.PFID_DXT5:
            var cf = rs.Format == PixelFormat.PFID_DXT1 ? BCnEncoder.Shared.CompressionFormat.Bc1WithAlpha
                   : rs.Format == PixelFormat.PFID_DXT3 ? BCnEncoder.Shared.CompressionFormat.Bc2
                   : BCnEncoder.Shared.CompressionFormat.Bc3;
            var colors = new BCnEncoder.Decoder.BcDecoder().DecodeRaw(src, rs.Width, rs.Height, cf);
            for (var i = 0; i < px; i++) { dst[i * 4] = colors[i].r; dst[i * 4 + 1] = colors[i].g; dst[i * 4 + 2] = colors[i].b; dst[i * 4 + 3] = colors[i].a; }
            return dst;
        case PixelFormat.PFID_A8R8G8B8:
            for (var i = 0; i < px; i++) { dst[i * 4] = src[i * 4 + 2]; dst[i * 4 + 1] = src[i * 4 + 1]; dst[i * 4 + 2] = src[i * 4]; dst[i * 4 + 3] = src[i * 4 + 3]; }
            return dst;
        case PixelFormat.PFID_R8G8B8:
        case PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8:
            for (var i = 0; i < px; i++) { dst[i * 4] = src[i * 3 + 2]; dst[i * 4 + 1] = src[i * 3 + 1]; dst[i * 4 + 2] = src[i * 3]; dst[i * 4 + 3] = 255; }
            return dst;
        case PixelFormat.PFID_INDEX16:
        case PixelFormat.PFID_P8:
            if (!dats.Portal.TryGet<Palette>(rs.DefaultPaletteId, out var pal)) return null;
            for (var i = 0; i < px; i++)
            {
                int idx = rs.Format == PixelFormat.PFID_P8 ? src[i] : BitConverter.ToUInt16(src, i * 2);
                var c = idx < pal.Colors.Count ? pal.Colors[idx] : default;
                dst[i * 4] = c.Red; dst[i * 4 + 1] = c.Green; dst[i * 4 + 2] = c.Blue; dst[i * 4 + 3] = c.Alpha;
            }
            return dst;
        default:
            return null;
    }
}

static void Probe()
{
    var asm = typeof(RenderSurface).Assembly;
    foreach (var name in new[] { "DatReaderWriter.Enums.PixelFormat", "DatReaderWriter.Options.DatAccessType" })
    {
        var t = asm.GetType(name);
        if (t == null) { Console.WriteLine($"{name}: not found"); continue; }
        Console.WriteLine($"{name}: " + string.Join(", ", Enum.GetNames(t).Zip(Enum.GetValues(t).Cast<object>(), (n, v) => $"{n}={Convert.ToUInt32(v)}")));
    }
    foreach (var t in new[] { typeof(RenderSurface), typeof(Palette), typeof(SurfaceTexture) })
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            Console.WriteLine($"{t.Name}.{f.Name}: {f.FieldType}");

    // How many bytes each format stores vs. a single uncompressed mip level, to detect embedded mipmaps.
    using var dats = new DatCollection(@"C:\Users\ostet\ac-decomp\dats", DatAccessType.Read);
    Console.WriteLine($"portal iteration: {dats.Portal.Iteration.CurrentIteration} highres: {dats.HighRes.Iteration.CurrentIteration}");
    var seen = new HashSet<PixelFormat>();
    foreach (var id in dats.Portal.GetAllIdsOfType<RenderSurface>())
    {
        var rs = dats.Portal.Get<RenderSurface>(id)!;
        if (rs.Width < 64 || !seen.Add(rs.Format)) continue;
        Console.WriteLine($"{id:X8} {rs.Format,-26} {rs.Width}x{rs.Height} bytes={rs.SourceData.Length} perPixel={(double)rs.SourceData.Length / (rs.Width * rs.Height):F3} pal={rs.DefaultPaletteId:X8}");
    }
}

static void Stats(string datDir)
{
    using var dats = new DatCollection(datDir, DatAccessType.Read);
    foreach (var (label, db) in new (string, DatDatabase)[] { ("portal", dats.Portal), ("highres", dats.HighRes) })
    {
        var ids = db.GetAllIdsOfType<RenderSurface>().ToList();
        var byFormat = new Dictionary<string, (int count, long pixels, int maxW, int maxH)>();
        foreach (var id in ids)
        {
            if (!db.TryGet<RenderSurface>(id, out var rs)) continue;
            var key = rs.Format.ToString();
            byFormat.TryGetValue(key, out var s);
            byFormat[key] = (s.count + 1, s.pixels + (long)rs.Width * rs.Height, Math.Max(s.maxW, rs.Width), Math.Max(s.maxH, rs.Height));
        }
        Console.WriteLine($"[{label}] iteration={db.Iteration} RenderSurfaces={ids.Count}");
        foreach (var kv in byFormat.OrderByDescending(k => k.Value.count))
            Console.WriteLine($"  {kv.Key,-20} count={kv.Value.count,6} megapixels={kv.Value.pixels / 1e6,8:F1} max={kv.Value.maxW}x{kv.Value.maxH}");
    }
}
