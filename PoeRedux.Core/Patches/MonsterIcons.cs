using LibBundle3.Nodes;
using System.Text;
using PoeRedux.Services;

namespace PoeRedux.Patches;

/// <summary>
/// Thêm icon minimap cho mọi quái, theo đúng bố cục quái Prophecy của game (vd prophecyspider.ot):
/// MinimapIcon ở tầng chung của metadata/monsters/monster.ot (mọi quái kế thừa) + client { Life { on_death = HideMinimapIcon() } }.
/// KHÔNG đặt MinimapIcon chỉ trong client { } — game báo "Component MinimapIcon does not exist" khi object chưa có component đó.
/// </summary>
public class MonsterIcons : IPatch
{
    private const string Marker = "// PoeRedux: monster minimap icons";

    public string Name => "Monster Icons Patch (Experimental)";
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

        // Đã patch (có thể với icon khác / bản cũ đặt trong client) → bỏ mọi khối cũ rồi chèn lại
        for (int old; (old = lines.FindIndex(l => l.Contains(Marker) && !l.Contains(Marker + " end"))) != -1;)
        {
            int end = lines.FindIndex(old, l => l.Contains(Marker + " end"));
            if (end == -1) return;
            lines.RemoveRange(old, end - old + 1);
        }

        int client = lines.FindIndex(l => l.Trim() == "client");
        int close = lines.FindLastIndex(l => l.Trim() == "}");
        if (client == -1 || close <= client)
            return;

        // client { Life { … } } trước (chỉ số sau client), rồi MinimapIcon tầng chung ngay trước dòng "client"
        lines.InsertRange(close,
        [
            $"\t{Marker}",
            "\tLife",
            "\t{",
            "\t\ton_death = { HideMinimapIcon(); }",
            "\t\ton_spawned_dead = { HideMinimapIcon(); }",
            "\t}",
            $"\t{Marker} end",
        ]);
        lines.InsertRange(client,
        [
            Marker,
            "MinimapIcon",
            "{",
            $"\ticon = \"{Icon}\"",
            "}",
            $"{Marker} end",
            "",
        ]);

        BackupManager.RecordOriginal(record);
        record.Write(Encoding.Unicode.GetBytes(string.Join("\r\n", lines)));
    }
}
