using LibBundle3;
using LibBundle3.Records;
using LibBundledGGPK3;
using System.Text.RegularExpressions;

namespace PoeRedux.Cli;

/// <summary>
/// Sửa Content.ggpk KHÔNG cần backup: so chỉ mục file trong máy với _.index.bin gốc trên CDN của game (cùng phiên bản).
/// Bản cài chuẩn đặt mỗi file đúng (bundle, offset, size) như CDN; file bị tool patch sửa thì được ghi sang chỗ khác
/// → lệch. Tải bundle gốc của những file lệch về và ghi đè lại nội dung gốc.
/// </summary>
public static class Repair
{
    /// <summary>CDN của phiên bản đang cài — đọc dòng "Web root: https://patch.poecdn.com/x.y.z/" cuối cùng trong logs/Client.txt.</summary>
    public static string? CdnFromLog(string ggpk)
    {
        var log = Path.Combine(Path.GetDirectoryName(ggpk)!, "logs", "Client.txt");
        if (!File.Exists(log)) return null;
        string? url = null;
        using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length > 8 << 20) fs.Seek(-(8 << 20), SeekOrigin.End);   // log rất dài — chỉ đọc 8 MB cuối
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } line)
        {
            var m = Regex.Match(line, @"\] Web root: (https?://\S+/)");
            if (m.Success) url = m.Groups[1].Value;
        }
        return url;
    }

    public static int Run(string ggpk, string cdn, string cacheDir, bool dryRun, Action<string> log)
    {
        var bundles2 = Path.Combine(cacheDir, "Bundles2");
        Directory.CreateDirectory(bundles2);
        // _.index.bin cũ của phiên bản khác → tải lại
        var stamp = Path.Combine(cacheDir, "cdn.txt");
        if (!File.Exists(stamp) || File.ReadAllText(stamp) != cdn)
        {
            foreach (var f in Directory.EnumerateFiles(bundles2, "*", SearchOption.AllDirectories)) File.Delete(f);
            File.WriteAllText(stamp, cdn);
        }

        using var server = new ServerBundleFactory(bundles2, cdn);
        if (!File.Exists(Path.Combine(bundles2, "_.index.bin")))
        {
            log($"Tải chỉ mục gốc {cdn}Bundles2/_.index.bin …");
            server.DownloadIndex();
        }
        using var remote = new LibBundle3.Index(Path.Combine(bundles2, "_.index.bin"), true, server);
        using var local = new BundledGGPK(ggpk, false);
        local.Index.ParsePaths();

        var changed = new List<(FileRecord Local, FileRecord Remote)>();
        int onlyLocal = 0;
        foreach (var (hash, lr) in local.Index.Files)
        {
            if (!remote.Files.TryGetValue(hash, out var rr)) { onlyLocal++; continue; }
            if (lr.Size != rr.Size || lr.Offset != rr.Offset || lr.BundleRecord.Path != rr.BundleRecord.Path)
                changed.Add((lr, rr));
        }
        log($"So {local.Index.Files.Count} file với CDN: {changed.Count} file khác bản gốc" +
            (onlyLocal > 0 ? $", {onlyLocal} file chỉ có trong máy" : ""));
        foreach (var g in changed.GroupBy(c => Path.GetExtension(c.Local.Path)).OrderByDescending(g => g.Count()))
            log($"  {g.Key,-8} {g.Count(),6}   vd: {g.First().Local.Path}");
        // Đọc theo bundle gốc để mỗi bundle chỉ tải/giải nén 1 lần.
        var bundles = changed.GroupBy(c => c.Remote.BundleRecord.Path).ToList();
        long bytes = bundles.Sum(g => (long)g.First().Remote.BundleRecord.UncompressedSize);
        long write = changed.Sum(c => (long)c.Remote.Size);
        log($"Cần tải {bundles.Count} bundle gốc (~{bytes >> 20} MB chưa nén; tải về nhỏ hơn vì nén) · ghi lại {write >> 20} MB");
        if (dryRun || changed.Count == 0)
            return 0;
        int done = 0, n = 0;
        foreach (var g in bundles)
        {
            Bundle? bundle = null;
            try
            {
                var br = g.First().Remote.BundleRecord;
                if (!br.TryGetBundle(out bundle, out var ex))
                    throw new IOException($"không tải được {br.Path}: {ex?.Message}");
                foreach (var (lr, rr) in g)
                {
                    lr.Write(rr.Read(bundle).Span);
                    n++;
                }
            }
            finally
            {
                bundle?.Dispose();
                // bundle tải về chỉ dùng 1 lần — xoá để không đầy ổ
                var cached = Path.Combine(bundles2, g.Key);
                if (File.Exists(cached)) File.Delete(cached);
            }
            if (++done % 20 == 0 || done == bundles.Count)
                log($"  {done}/{bundles.Count} bundle · {n} file");
        }
        local.Index.Save();
        log($"✅ Đã khôi phục {n} file về bản gốc.");
        return 0;
    }
}
