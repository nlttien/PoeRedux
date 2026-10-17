using LibBundle3.Nodes;
using System.Text;
using PoeRedux.Services;

namespace PoeRedux.Patches;

/// <summary>
/// Phần dùng chung của hide-effects / hide-monsters: duyệt file theo thư mục, đọc từ bản GỐC (backup nếu đã patch)
/// để áp lại hay áp sau reduce-effects đều ra cùng kết quả.
/// Chỉ dùng kỹ thuật black-screen của upstream đã chạy thực tế (áp cho cả game), ở đây giới hạn theo thư mục:
///   .pet/.trl → "0" · .epk → rỗng · .mat → material tàng hình (invisible.fxgraph) · .ao → bỏ khối client trừ Keep.
/// ✗ KHÔNG làm rỗng khối SkinMesh (bản 1, 2026-10-17): về hideout sau map load 297 giây.
/// </summary>
public abstract class HideBase : IPatch
{
    public abstract string Name { get; }
    public abstract object Description { get; }

    /// <summary>Khối client giữ nguyên (animation điều khiển event/socket, âm thanh, khung xương).</summary>
    protected static readonly HashSet<string> Keep = new(StringComparer.Ordinal)
        { "ClientAnimationController", "SoundEvents", "BoneGroups", "AnimatedRender", "SkinMesh" };

    /// <summary>
    /// Material tàng hình: graph có sẵn của game invisible.fxgraph (AlphaTest, alpha = 0 → loại mọi pixel).
    /// ✗ Material rỗng không graph (Black/Mat.cs) KHÔNG ẩn — game vẽ mesh bằng màu xanh lá mặc định (thử 2026-10-17).
    /// </summary>
    protected const string EmptyMaterial =
        "{\"version\":4,\"defaultgraph\":{\"version\":3,\"overriden_blend_mode\":\"AlphaTest\"}," +
        "\"graphinstances\":[{\"parent\":\"Metadata/Effects/Graphs/General/invisible.fxgraph\"}]}";

    /// <summary>Thay hẳn nội dung (luôn UTF-16 + BOM như upstream) — khác với sửa văn bản giữ nguyên mã hoá.</summary>
    protected sealed record Replace(string Text);

    /// <summary>Cơ chế cần nhìn thấy để né / làm league (gộp danh sách bảo vệ của Effects + Particles Patch).</summary>
    protected static readonly string[] Protect =
    [
        "metadata/effects/spells/monsters_effects/league_expedition/dynamic_marker",
        "metadata/effects/spells/monsters_effects/atlasofworldsbosses",
        "metadata/effects/spells/monsters_effects/league_azmeri/guiding_light",
        "metadata/effects/spells/monsters_effects/league_azmeri/monster_fx",
        "metadata/effects/spells/monsters_effects/league_azmeri/resources/",
        "metadata/effects/spells/monsters_effects/league_legion/rewardsystem",
        "metadata/effects/spells/monsters_effects/league_blight/rewardsystem",
        "metadata/effects/spells/monsters_effects/league_archnemesis",
        "metadata/effects/spells/monsters_effects/league_ritual/cold_ritual",
        "metadata/effects/spells/monsters_effects/league_ultimatum/mechanics/fx/arena_limit.pet",
        "metadata/effects/spells/monsters_effects/league_sanctum",
        "metadata/effects/spells/monsters_effects/league_hellscape/mechanics",
        "metadata/effects/spells/monsters_effects/atlasexiles/adjudicator",
        "metadata/effects/spells/monsters_effects/atlasexiles/orion",
        "metadata/effects/spells/monsters_effects/prophecy_league",
        "metadata/effects/spells/ground_effects/chimera_smoke",
        "metadata/effects/spells/ground_effects/evil",
        "metadata/effects/spells/ground_effects_v2/smoke_blind_chimera",
        "metadata/effects/spells/ground_effects/caustic",
        "metadata/effects/spells/ground_effects_v2/caustic_arrow_ground",
        "metadata/effects/spells/ground_effects_v2/desecrated",
        "metadata/effects/spells/ground_effects_v3/caustic",
        "metadata/particles/monster_effects/league_legion/rewardsystem",
        "metadata/particles/monster_effects/league_legion/endgame",
        "metadata/particles/monster_effects/league_delve/general",
        "metadata/particles/monster_effects/atlasexiles/adjudicator",
        "metadata/particles/monster_effects/atlasexiles/orion",
        "metadata/particles/monster_effects/atlasofworldsbosses/chimera",
        "metadata/particles/enviro_effects/act3/blood_temple",
        "metadata/particles/ground_effects_v2/smoke_blind_chimera",
    ];

    public int Changed { get; private set; }

    /// <summary>Xem trước (chỉ đọc): nội dung mới của file, null = patch không đổi file này.</summary>
    public string? Preview(string path, string text) =>
        (Roots.Any(r => path.StartsWith(r, StringComparison.Ordinal)) ? Transform(path, text) : null) switch
        {
            Replace r => r.Text,
            string t => t,
            _ => null,
        };

    protected abstract string[] Roots { get; }

    /// <summary>Nội dung mới (string = giữ mã hoá gốc, <see cref="Replace"/> = UTF-16), hoặc null = giữ nguyên file.</summary>
    protected abstract object? Transform(string path, string text);

    public void Apply(DirectoryNode root)
    {
        var files = new List<FileNode>();
        foreach (var r in Roots)
            if (Navigate(root, r.TrimEnd('/').Split('/')) is { } dir)
                Collect(dir, files);

        foreach (var file in files)
        {
            var record = file.Record;
            // chỉ .ao cần đọc bản gốc (sửa văn bản); .pet/.epk/.mat thay hẳn → bỏ qua đọc zip backup (phần chậm nhất)
            var original = Ext(record.Path, ".ao") ? BackupManager.ReadOriginal(record) : [];
            var unicode = original.Length >= 2 && original[0] == 0xFF && original[1] == 0xFE;
            var text = unicode ? Encoding.Unicode.GetString(original, 2, original.Length - 2) : Encoding.UTF8.GetString(original);
            byte[] bytes;
            switch (Transform(record.Path, text))
            {
                case Replace r:
                    bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(r.Text)];
                    break;
                case string next when next != text:
                    bytes = unicode ? [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(next)] : Encoding.UTF8.GetBytes(next);
                    break;
                default:
                    continue;
            }
            if (bytes.AsSpan().SequenceEqual(record.Read().Span)) continue;   // đã đúng
            BackupManager.RecordOriginal(record);
            record.Write(bytes);
            Changed++;
        }
    }

    protected static bool Protected(string path) => Protect.Any(p => path.StartsWith(p, StringComparison.Ordinal));

    private static void Collect(DirectoryNode dir, List<FileNode> files)
    {
        foreach (var node in dir.Children)
        {
            if (node is DirectoryNode d) Collect(d, files);
            else if (node is FileNode f) files.Add(f);
        }
    }

    private static DirectoryNode? Navigate(DirectoryNode root, string[] path)
    {
        var cur = root;
        foreach (var name in path)
        {
            cur = cur.Children.OfType<DirectoryNode>().FirstOrDefault(d => d.Name == name);
            if (cur is null) return null;
        }
        return cur;
    }

    protected static bool Ext(string path, params string[] exts) =>
        exts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Tắt HẾT hiệu ứng kỹ năng (người chơi + quái + skin MTX): hạt .pet/.trl, gói hiệu ứng .epk, khối client của .ao kỹ năng
/// (như Effects Patch), và material mesh hiệu ứng trong art/particles + art/models/effects — mesh 3D như lốc xoáy Tornado
/// (tornado.sm, material art/particles/tornado_spell/*.mat) mà Effects/Reduce Effects không đụng tới.
/// Giữ cơ chế boss/league cần né (<see cref="HideBase.Protect"/>).
/// </summary>
public class HideEffects : HideBase
{
    public override string Name => "Hide Effects Patch";
    public override object Description => "Removes ALL skill effects incl. 3D meshes (tornado…), particles and trails. Boss/league mechanics stay visible.";

    protected override string[] Roots => ["metadata/effects/", "metadata/particles/", "art/particles/", "art/models/effects/"];

    protected override object? Transform(string path, string text)
    {
        if (Protected(path)) return null;
        if (Ext(path, ".pet", ".trl")) return new Replace("0");
        if (Ext(path, ".epk")) return new Replace("");
        if (Ext(path, ".mat")) return new Replace(EmptyMaterial);
        if (Ext(path, ".ao") && path.StartsWith("metadata/effects/spells/", StringComparison.Ordinal))
            return Effects.StripClientBlocks(text, Keep);
        return null;
    }
}

/// <summary>
/// Quái tàng hình: material model quái (art/models/monsters) rỗng; .ao/.epk quái mất đồ gắn, hạt, decal — vẫn còn trong game
/// (máu, va chạm, AI, âm thanh, thanh máu trên đầu). ⚠ Minion/spectre/totem của NGƯỜI CHƠI dùng chung file → cũng tàng hình.
/// </summary>
public class HideMonsters : HideBase
{
    public override string Name => "Hide Monsters Patch";
    public override object Description => "Makes monsters invisible (no model/particles; life bars, sounds, AI unchanged). Your minions too.";

    protected override string[] Roots => ["metadata/monsters/", "art/models/monsters/"];

    protected override object? Transform(string path, string text) =>
        Protected(path) ? null
        : Ext(path, ".mat") ? new Replace(EmptyMaterial)
        : Ext(path, ".epk") ? new Replace("")
        : Ext(path, ".ao") ? Effects.StripClientBlocks(text, Keep)
        : null;
}
