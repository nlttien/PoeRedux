using LibBundle3.Nodes;
using System.Text;
using PoeRedux.Services;

namespace PoeRedux.Patches;

/// <summary>
/// Phần dùng chung của hide-effects / hide-monsters: duyệt file theo thư mục, đọc từ bản GỐC (backup nếu đã patch)
/// để áp lại hay áp sau reduce-effects đều ra cùng kết quả.
/// Ẩn mesh = giữ khối SkinMesh nhưng xoá ruột: component vẫn còn nên script/khối con tham chiếu tới nó không lỗi
/// "Component … does not exist" (FIELD_JOURNAL R11), chỉ không còn gì để vẽ.
/// </summary>
public abstract class HideBase : IPatch
{
    public abstract string Name { get; }
    public abstract object Description { get; }

    /// <summary>Khối client giữ nguyên (animation điều khiển event/socket, âm thanh, khung xương).</summary>
    protected static readonly HashSet<string> Keep = new(StringComparer.Ordinal)
        { "ClientAnimationController", "SoundEvents", "BoneGroups", "AnimatedRender" };

    protected static readonly HashSet<string> Empty = new(StringComparer.Ordinal) { "SkinMesh" };

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
        Roots.Any(r => path.StartsWith(r, StringComparison.Ordinal)) ? Transform(path, text) : null;

    protected abstract string[] Roots { get; }

    /// <summary>Nội dung mới, hoặc null = giữ nguyên file.</summary>
    protected abstract string? Transform(string path, string text);

    public void Apply(DirectoryNode root)
    {
        var files = new List<FileNode>();
        foreach (var r in Roots)
            if (Navigate(root, r.TrimEnd('/').Split('/')) is { } dir)
                Collect(dir, files);

        foreach (var file in files)
        {
            var record = file.Record;
            var original = BackupManager.ReadOriginal(record);
            var unicode = original.Length >= 2 && original[0] == 0xFF && original[1] == 0xFE;
            var text = unicode ? Encoding.Unicode.GetString(original, 2, original.Length - 2) : Encoding.UTF8.GetString(original);
            var next = Transform(record.Path, text);
            if (next is null || next == text) continue;

            byte[] bytes = unicode || next == "0" ? [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(next)] : Encoding.UTF8.GetBytes(next);
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
/// Tắt HẾT hiệu ứng kỹ năng (người chơi + quái + skin MTX): hạt .pet/.trl → rỗng, và .ao trong metadata/effects mất mọi
/// thứ vẽ được kể cả mesh 3D (vd lốc xoáy Tornado = SkinMesh tornado.sm — Effects/Reduce Effects không đụng tới).
/// Giữ cơ chế boss/league cần né (<see cref="HideBase.Protect"/>).
/// </summary>
public class HideEffects : HideBase
{
    public override string Name => "Hide Effects Patch";
    public override object Description => "Removes ALL skill effects incl. 3D meshes (tornado…), particles and trails. Boss/league mechanics stay visible.";

    protected override string[] Roots => ["metadata/effects/", "metadata/particles/"];

    protected override string? Transform(string path, string text)
    {
        if (Protected(path)) return null;
        if (Ext(path, ".pet", ".trl")) return "0";
        if (Ext(path, ".ao")) return Effects.StripClientBlocks(text, Keep, Empty);
        return null;
    }
}

/// <summary>
/// Quái tàng hình: .ao trong metadata/monsters mất mesh, đồ gắn, hạt, vết decal — vẫn còn trong game (máu, va chạm, AI,
/// âm thanh, thanh máu trên đầu). ⚠ Minion/spectre/totem của NGƯỜI CHƠI dùng chung file → cũng tàng hình.
/// </summary>
public class HideMonsters : HideBase
{
    public override string Name => "Hide Monsters Patch";
    public override object Description => "Makes monsters invisible (no model/particles; life bars, sounds, AI unchanged). Your minions too.";

    protected override string[] Roots => ["metadata/monsters/"];

    protected override string? Transform(string path, string text) =>
        Ext(path, ".ao") && !Protected(path) ? Effects.StripClientBlocks(text, Keep, Empty) : null;
}
