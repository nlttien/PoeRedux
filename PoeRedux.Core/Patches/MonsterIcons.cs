using LibBundle3.Nodes;
using System.Text;
using PoeRedux.Services;

namespace PoeRedux.Patches;

/// <summary>
/// Icon minimap cho quái — bản 4 (2026-10-17), KHÔNG thêm component phía server vào quái.
///
/// Vì sao bản 1–3 hỏng: MinimapIcon là component chung client/server. Thêm nó vào monster.ot ở máy mình thì danh sách
/// component lệch với server → vùng đầu (town, không quái) vào được, vùng có quái/minion thì treo / báo lỗi rồi disconnect.
/// Đặt chỉ trong client { } thì "Component MinimapIcon does not exist" (client chỉ bổ sung component đã có).
///
/// Bản 4: dùng object CHỈ CLIENT (BaseEvents { client_only = true } — như quạ/nhện game tự sinh, server không biết tới):
///   · <see cref="MarkerPath"/> (nhện của áo choàng MTX WitchDoctor, chỉ áo đó dùng) bị thay thành "điểm đánh dấu": có MinimapIcon,
///     mỗi 0.1s MoveToObject( parent, force ) bám theo quái đã sinh ra nó; quái không còn → tự Delete().
///     Quái cùng phe với nhân vật mình (minion/spectre — GetTeam() như vaaloverseer.ot, GetLocalPlayer() như goldtrackingtreasurer.ot)
///     → Delete(): IfStat( is_player_minion ) lúc xuất hiện KHÔNG lọc được (thử 2026-10-17, minion vẫn có icon).
///   · monster.ot chỉ thêm khối client { Life { … } }: quái xuất hiện (không phải minion người chơi) → SpawnObject(điểm đánh dấu);
///     quái chết → xoá điểm đánh dấu. Mẫu: witchdoctorcloakheld.ao (SpawnObject critter), theelderuber.ot (MoveToObject … force).
/// CHƯA thử trong game. Lỗi → fix-hideout gỡ monster.ot (điểm đánh dấu không ai sinh ra thì vô hại).
/// </summary>
public class MonsterIcons : IPatch
{
    private const string Marker = "// PoeRedux: monster minimap icons";
    public const string MarkerPath = "metadata/critters/spider/witchdoctorspidercloak.ot";
    private const string MarkerMeta = "Metadata/Critters/Spider/WitchDoctorSpiderCloak";

    public string Name => "Monster Icons Patch (Experimental)";
    public object Description => "Shows every monster as an icon on the minimap (client-only marker objects).";

    /// <summary>Tên trong data/minimapicons (vd LootFilterSmallRedCircle, LootFilterMediumYellowStar, ExpeditionRegularMonster).</summary>
    public string Icon { get; set; } = "LootFilterSmallRedCircle";

    public void Apply(DirectoryNode root)
    {
        if (Find(root, "metadata/monsters/monster.ot") is not { } monster || Find(root, MarkerPath) is not { } marker)
            return;

        Write(marker.Record, MarkerObject());

        // monster.ot luôn tính từ bản gốc → áp lại (đổi icon, bản cũ) không chồng khối
        var original = BackupManager.ReadOriginal(monster.Record);
        var lines = Decode(original).Split("\r\n").ToList();
        int close = lines.FindLastIndex(l => l.Trim() == "}");
        if (lines.FindIndex(l => l.Trim() == "client") is var client and >= 0 && close > client)
        {
            lines.InsertRange(close,
            [
                $"\t{Marker}",
                "\tLife",
                "\t{",
                $"\t\ton_spawned_alive = {{ IfStat( is_player_minion, 1, {{}}, {{ this.poeredux_marker = SpawnObject( '{MarkerMeta}' ); }} ); }}",
                "\t\ton_death = { IfExists( poeredux_marker, (){ poeredux_marker.Delete(); }, (){} ); }",
                "\t}",
                $"\t{Marker} end",
            ]);
            Write(monster.Record, string.Join("\r\n", lines));
        }
    }

    private string MarkerObject() => string.Join("\r\n",
    [
        "version 3",
        "extends \"nothing\"",
        "",
        Marker,
        "BaseEvents",
        "{",
        "\tclient_only = true",
        "}",
        "",
        "Positioned",
        "{",
        "\tobject_size = 0",
        "\tblocking = false",
        "}",
        "",
        "MinimapIcon",
        "{",
        $"\ticon = \"{Icon}\"",
        "}",
        "",
        "client",
        "{",
        "\tBaseEvents",
        "\t{",
        "\t\ton_construction_complete = { StartStateTimer( follow, 0.1, 0.1 ); }",
        "\t}",
        "",
        "\tStateMachine",
        "\t{",
        "\t\ton_timer_follow =",
        "\t\t{",
        "\t\t\tIf( VariableExists( parent ), ()",
        "\t\t\t{",
        "\t\t\t\tlocal player = GetLocalPlayer();",
        "\t\t\t\tIf( parent.GetTeam() == player.GetTeam(), (){ Delete(); }, (){ MoveToObject( parent, force ); StartStateTimer( follow, 0.1, 0.1 ); } );",
        "\t\t\t}, (){ Delete(); } );",
        "\t\t}",
        "\t}",
        "}",
        "",
    ]);

    private static void Write(LibBundle3.Records.FileRecord record, string text)
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)];
        if (bytes.AsSpan().SequenceEqual(record.Read().Span)) return;
        BackupManager.RecordOriginal(record);
        record.Write(bytes);
    }

    private static string Decode(byte[] b) => b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE
        ? Encoding.Unicode.GetString(b, 2, b.Length - 2)
        : Encoding.UTF8.GetString(b);

    private static FileNode? Find(DirectoryNode root, string path)
    {
        var parts = path.Split('/');
        var dir = root;
        foreach (var name in parts[..^1])
        {
            dir = dir.Children.OfType<DirectoryNode>().FirstOrDefault(d => d.Name == name);
            if (dir is null) return null;
        }
        return dir.Children.OfType<FileNode>().FirstOrDefault(f => f.Name == parts[^1]);
    }
}
