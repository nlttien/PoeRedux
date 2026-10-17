using LibBundle3.Nodes;
using System.Buffers.Binary;
using System.Text;
using PoeRedux.Services;

namespace PoeRedux.Patches;

/// <summary>
/// Phóng to icon người chơi (MyPlayer) trên minimap và bản đồ lớn (Tab): sửa bảng data/minimapicons.datc64.
/// Dòng 34 byte (đo 2026-10-17, bản 3.29.3): [0] Id (offset chuỗi u64) · [8] MinimapIconRadius i32 · [12] LargemapIconRadius i32
/// · [16] bool ×2 · [18] PointerMaxDistance i32 · … — MyPlayer/PartyMember/OtherPlayer = 1, Portal = 2, MissionTarget = 3.
/// Chỉ client dùng bảng này để vẽ → không liên quan server.
/// </summary>
public class PlayerIcon : IPatch
{
    private const string TablePath = "data/minimapicons.datc64";
    private const int MinimapRadius = 8, LargemapRadius = 12;

    public string Name => "Player Icon Patch";
    public object Description => "Makes your own player icon bigger on the minimap and the large map.";

    /// <summary>Bán kính icon (gốc 1). 3 ≈ cỡ icon mục tiêu nhiệm vụ.</summary>
    public int Size { get; set; } = 3;

    /// <summary>Phóng to cả người cùng party (PartyMember).</summary>
    public bool IncludeParty { get; set; }

    public void Apply(DirectoryNode root)
    {
        var data = root.Children.OfType<DirectoryNode>().FirstOrDefault(d => d.Name == "data");
        var file = data?.Children.OfType<FileNode>().FirstOrDefault(f => f.Name == "minimapicons.datc64");
        if (file is null)
            return;

        var record = file.Record;
        var bytes = BackupManager.ReadOriginal(record);    // luôn tính từ bản gốc → đổi Size bao nhiêu lần cũng đúng
        var targets = IncludeParty ? new[] { "MyPlayer", "PartyMember" } : ["MyPlayer"];
        if (!Resize(bytes, targets, Size))
            return;
        if (bytes.AsSpan().SequenceEqual(record.Read().Span))
            return;
        BackupManager.RecordOriginal(record);
        record.Write(bytes);
    }

    /// <summary>Sửa tại chỗ bán kính icon của các dòng có Id trong <paramref name="ids"/>. Trả false nếu không đọc được bảng.</summary>
    public static bool Resize(byte[] dat, IReadOnlyCollection<string> ids, int size)
    {
        if (dat.Length < 12) return false;
        int rows = BinaryPrimitives.ReadInt32LittleEndian(dat);
        int magic = dat.AsSpan().IndexOf(new byte[] { 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB });
        if (rows <= 0 || magic < 4 || (magic - 4) % rows != 0) return false;
        int rowSize = (magic - 4) / rows;
        if (rowSize < LargemapRadius + 4) return false;

        int changed = 0;
        for (int i = 0; i < rows; i++)
        {
            int row = 4 + i * rowSize;
            long off = BinaryPrimitives.ReadInt64LittleEndian(dat.AsSpan(row));
            if (off < 0 || magic + off >= dat.Length) continue;
            if (!ids.Contains(ReadString(dat, magic + (int)off))) continue;
            BinaryPrimitives.WriteInt32LittleEndian(dat.AsSpan(row + MinimapRadius), size);
            BinaryPrimitives.WriteInt32LittleEndian(dat.AsSpan(row + LargemapRadius), size);
            changed++;
        }
        return changed > 0;
    }

    private static string ReadString(byte[] dat, int start)
    {
        int end = start;
        while (end + 1 < dat.Length && (dat[end] != 0 || dat[end + 1] != 0)) end += 2;
        return Encoding.Unicode.GetString(dat, start, end - start);
    }
}
