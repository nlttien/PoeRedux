using LibBundle3.Nodes;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PoeRedux.Services;

namespace PoeRedux.Patches;

/// <summary>
/// Giảm (không xoá) hiệu ứng kỹ năng: nhân số hạt phát ra của mọi file .pet theo <see cref="Factor"/>.
/// Ba kiểu khai báo trong .pet của game:
///   cũ  : particles_per_second v1 … v7
///   v3  : particles_per_second &lt;dims&gt; &lt;n&gt; (t v… "Interp") × n      — chỉ nhân v, giữ t
///   GPU : "emit_burst": x  /  "particles_count": n  /  "particles_per_second": x   (JSON trong "gpu_emitters")
/// Luôn tính từ bản gốc (lấy từ backup nếu đã patch) → áp lại với hệ số khác không bị cộng dồn.
/// </summary>
public class ReduceEffects : IPatch
{
    public string Name => "Reduce Effects Patch";
    public object Description => "Reduces skill particle effects by a factor (default 50%) instead of removing them.";

    /// <summary>Hệ số giữ lại: 0.5 = còn 50% số hạt.</summary>
    public double Factor { get; set; } = 0.5;

    /// <summary>Thư mục hiệu ứng kỹ năng (người chơi + quái, cả skin MTX).</summary>
    private static readonly string[] Roots =
    [
        "metadata/effects/spells/",
        "metadata/effects/microtransactions/",
        "metadata/particles/",
    ];

    /// <summary>Không phải kỹ năng (môi trường), hoặc cần nhìn rõ (cơ chế boss/league — giống danh sách bảo vệ của Particles Patch).</summary>
    private static readonly string[] Skip =
    [
        "metadata/particles/enviro_effects/",
        "metadata/particles/doodads/",
        "metadata/particles/waterfall",
        "metadata/particles/water_drips/",
        "metadata/particles/forest_matte/",
        "metadata/particles/cinematic/",
        "metadata/particles/story_particles/",
        "metadata/particles/quest_",
        "metadata/particles/ggg_logo/",
        "metadata/particles/monster_effects/league_legion/rewardsystem",
        "metadata/particles/monster_effects/league_legion/endgame",
        "metadata/particles/monster_effects/league_delve/general",
        "metadata/particles/monster_effects/atlasexiles/adjudicator",
        "metadata/particles/monster_effects/atlasexiles/orion",
        "metadata/particles/monster_effects/atlasofworldsbosses/chimera",
        "metadata/particles/ground_effects_v2/smoke_blind_chimera",
    ];

    private static readonly Regex JsonNumber = new(
        @"(""(?:emit_burst|particles_per_second|particles_count)""\s*:\s*)(-?[0-9.eE+-]+)", RegexOptions.Compiled);

    private readonly List<FileNode> _files = [];

    public int Changed { get; private set; }

    public void Apply(DirectoryNode root)
    {
        if (Factor is <= 0 or >= 1) return;
        _files.Clear();
        Collect(root);
        foreach (var file in _files)
        {
            var record = file.Record;
            var original = BackupManager.ReadOriginal(record);
            var unicode = original.Length >= 2 && original[0] == 0xFF && original[1] == 0xFE;
            var text = unicode ? Encoding.Unicode.GetString(original, 2, original.Length - 2) : Encoding.UTF8.GetString(original);

            var reduced = Reduce(text);
            if (reduced == text) continue;

            byte[] bytes = unicode ? [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(reduced)] : Encoding.UTF8.GetBytes(reduced);
            if (bytes.AsSpan().SequenceEqual(record.Read().Span)) continue;   // đã đúng hệ số này
            BackupManager.RecordOriginal(record);
            record.Write(bytes);
            Changed++;
        }
    }

    private void Collect(DirectoryNode dir)
    {
        foreach (var node in dir.Children)
        {
            if (node is DirectoryNode d)
            {
                Collect(d);
            }
            else if (node is FileNode f && f.Name.EndsWith(".pet", StringComparison.OrdinalIgnoreCase))
            {
                var path = f.Record.Path;
                if (Roots.Any(r => path.StartsWith(r, StringComparison.Ordinal)) && !Skip.Any(s => path.StartsWith(s, StringComparison.Ordinal)))
                    _files.Add(f);
            }
        }
    }

    public string Reduce(string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("particles_per_second ", StringComparison.Ordinal))
                lines[i] = line[..(line.Length - trimmed.Length)] + ScaleCurve(trimmed);
            else if (trimmed.StartsWith('"'))
                lines[i] = JsonNumber.Replace(line, m => m.Groups[1].Value + ScaleJson(m.Groups[1].Value, m.Groups[2].Value));
        }
        return string.Join('\n', lines);
    }

    /// <summary>
    /// "particles_per_second …":
    ///   toàn số                         → bảng cũ, nhân hết
    ///   có chữ nội suy (Linear/"Linear") → đường cong: mỗi điểm là [t v… Interp]; chỉ nhân v (số "dims" token ngay trước Interp).
    ///   Gặp cả dạng "1 7 0 v "Linear" …" (có dims, n) lẫn "2 0 v Linear 1 v Linear" (chỉ n).
    /// </summary>
    private string ScaleCurve(string line)
    {
        var cr = line.EndsWith('\r');
        var tokens = line.TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var interp = Enumerable.Range(1, tokens.Length - 1).Where(k => !TryNum(tokens[k], out _)).ToList();
        if (interp.Count == 0)
        {
            for (int k = 1; k < tokens.Length; k++)
                if (TryNum(tokens[k], out var v)) tokens[k] = Fmt(v * Factor);
        }
        else
        {
            // dims = số giá trị mỗi điểm = khoảng cách giữa 2 Interp liên tiếp − 2 (t + Interp); chỉ 1 điểm → 1
            int dims = interp.Count > 1 ? interp[1] - interp[0] - 2 : 1;
            if (dims < 1) return line;
            foreach (var k0 in interp)
                for (int k = k0 - dims; k < k0; k++)
                    if (k > 0 && TryNum(tokens[k], out var v)) tokens[k] = Fmt(v * Factor);
        }
        return string.Join(' ', tokens) + (cr ? "\r" : "");
    }

    private string ScaleJson(string key, string value)
    {
        if (!TryNum(value, out var v) || v <= 0) return value;   // -1 = không dùng
        if (key.Contains("particles_count"))
            return Math.Max(1, (int)Math.Round(v * Factor)).ToString(CultureInfo.InvariantCulture);
        var r = Fmt(v * Factor);
        return value.Contains('.') && !r.Contains('.') ? r + ".0" : r;   // giữ kiểu số thực của JSON
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
