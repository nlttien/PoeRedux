using LibBundle3.Nodes;
using System.Text;
using PoeRedux.Services;

namespace PoeRedux.Patches;

/// <summary>
/// Thêm icon minimap cho mọi quái: chèn MinimapIcon vào khối client { } của metadata/monsters/monster.ot (mọi quái kế thừa).
/// Đặt trong client { } như boss Hellscape của game → chỉ client dùng, không lệch dữ liệu với server.
/// Ẩn icon khi quái chết theo mẫu quái Prophecy (Life.on_death = HideMinimapIcon()).
/// </summary>
public class MonsterIcons : IPatch
{
    private const string Marker = "// PoeRedux: monster minimap icons";

    public string Name => "Monster Icons Patch";
    public object Description => "Shows every monster as an icon on the minimap.";

    /// <summary>Tên trong data/minimapicons (vd LootFilterSmallRedCircle, LootFilterMediumYellowStar, ExpeditionRegularMonster).</summary>
    public string Icon { get; set; } = "LootFilterSmallRedCircle";

    public void Apply(DirectoryNode root)
    {
        var monsters = root.Children.OfType<DirectoryNode>().FirstOrDefault(d => d.Name == "metadata")?
            .Children.OfType<DirectoryNode>().FirstOrDefault(d => d.Name == "monsters");
        var file = monsters?.Children.OfType<FileNode>().FirstOrDefault(f => f.Name == "monster.ot");
        if (file is null)
            return;

        var record = file.Record;
        string data = Encoding.Unicode.GetString(record.Read().ToArray());
        List<string> lines = data.Split("\r\n").ToList();

        // Đã patch (có thể với icon khác) → bỏ khối cũ rồi chèn lại
        int old = lines.FindIndex(l => l.Contains(Marker));
        if (old != -1)
        {
            int end = lines.FindIndex(old, l => l.Contains(Marker + " end"));
            if (end == -1) return;
            lines.RemoveRange(old, end - old + 1);
        }

        int client = lines.FindIndex(l => l.Trim() == "client");
        int close = lines.FindLastIndex(l => l.Trim() == "}");
        if (client == -1 || close <= client)
            return;

        lines.InsertRange(close,
        [
            $"\t{Marker}",
            "\tMinimapIcon",
            "\t{",
            $"\t\ticon = \"{Icon}\"",
            "\t}",
            "\tLife",
            "\t{",
            "\t\ton_death = { HideMinimapIcon(); }",
            "\t\ton_spawned_dead = { HideMinimapIcon(); }",
            "\t}",
            $"\t{Marker} end",
        ]);

        BackupManager.RecordOriginal(record);
        record.Write(Encoding.Unicode.GetBytes(string.Join("\r\n", lines)));
    }
}
