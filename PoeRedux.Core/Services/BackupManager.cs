using LibBundle3.Records;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PoeRedux.Services;

public enum PoeGame
{
    PoE1,
    PoE2,
}

/// <summary>Trạng thái 1 file đã backup so với nội dung hiện tại trong game.</summary>
public enum FileState
{
    /// <summary>Đang là bản PoeRedux đã ghi.</summary>
    Patched,
    /// <summary>Đã là bản gốc (restore rồi, hoặc patch chưa ghi được).</summary>
    Original,
    /// <summary>Patch ghi lại y nguyên bản gốc — không tìm thấy đoạn cần sửa (game đã đổi nội dung file).</summary>
    Unchanged,
    /// <summary>Khác cả bản gốc lẫn bản đã patch — game đã cập nhật file này.</summary>
    ChangedByGame,
    /// <summary>File không còn trong game.</summary>
    Missing,
    /// <summary>Backup cũ không có mã băm — không biết.</summary>
    Unknown,
}

public record FileCheck(string Path, FileState State);

public record RestoreReport(int Restored, int AlreadyOriginal, int SkippedChanged, int Missing, bool Legacy)
{
    /// <summary>Không còn file nào cần giữ backup.</summary>
    public bool Complete => SkippedChanged == 0 && Missing == 0;
}

public static class BackupManager
{
    private const string ManifestEntry = "__poeredux/manifest.json";

    private sealed class Hashes
    {
        public string Original { get; set; } = "";
        public string Patched { get; set; } = "";
    }

    private static readonly object _lock = new();
    private static ZipArchive? _zip;
    private static HashSet<string>? _knownPaths;
    private static Dictionary<string, Hashes>? _manifest;
    private static List<FileRecord>? _session;

    /// <summary>
    /// Thư mục chứa backup. Mặc định: %LOCALAPPDATA%\PoeRedux\Backups (Windows) / ~/.local/share/PoeRedux/Backups (Linux).
    /// Bản dòng lệnh đặt cạnh Content.ggpk để backup nằm cùng ổ với game.
    /// </summary>
    public static string? BackupDirectory { get; set; }

    public static string GetBackupFilePath(PoeGame game)
    {
        var dir = BackupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PoeRedux", "Backups");
        Directory.CreateDirectory(dir);
        var name = game == PoeGame.PoE2 ? "poe2.zip" : "poe1.zip";
        return Path.Combine(dir, name);
    }

    public static bool HasBackup(PoeGame game) => File.Exists(GetBackupFilePath(game));

    /// <summary>Đường dẫn các file đã ghi trong phiên patch hiện tại (theo thứ tự ghi).</summary>
    public static List<string> SessionPaths
    {
        get { lock (_lock) return _session?.Select(r => r.Path).ToList() ?? []; }
    }

    public static int CountBackedUpFiles(PoeGame game)
    {
        if (!HasBackup(game)) return 0;
        try
        {
            using var zip = ZipFile.OpenRead(GetBackupFilePath(game));
            return zip.Entries.Count(IsDataEntry);
        }
        catch { return 0; }
    }

    public static void Begin(PoeGame game)
    {
        lock (_lock)
        {
            End();

            var path = GetBackupFilePath(game);
            _knownPaths = new HashSet<string>(StringComparer.Ordinal);
            _session = [];

            try
            {
                _zip = ZipFile.Open(path, ZipArchiveMode.Update);
                foreach (var entry in _zip.Entries)
                    if (IsDataEntry(entry))
                        _knownPaths.Add(entry.FullName);
                _manifest = ReadManifest(_zip);
            }
            catch
            {
                // Backup hỏng: KHÔNG xoá (bản cũ xoá luôn → mất bản gốc). Đổi tên để còn cứu được, rồi tạo mới.
                _zip?.Dispose();
                _zip = null;
                if (File.Exists(path))
                    File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}");
                _knownPaths.Clear();
                _zip = ZipFile.Open(path, ZipArchiveMode.Update);
                _manifest = null;
            }
            _manifest ??= [];
        }
    }

    /// <summary>
    /// Nội dung gốc của file: lấy từ backup nếu file đã từng bị patch (để patch theo hệ số không bị cộng dồn khi áp lại),
    /// không thì đọc thẳng từ game. Gọi trong phiên Begin/End.
    /// </summary>
    public static byte[] ReadOriginal(FileRecord record)
    {
        lock (_lock)
        {
            if (_zip != null && _knownPaths != null && _knownPaths.Contains(record.Path) && _zip.GetEntry(record.Path) is { } entry)
            {
                using var ms = new MemoryStream();
                using (var es = entry.Open())
                    es.CopyTo(ms);
                return ms.ToArray();
            }
        }
        return record.Read().ToArray();
    }

    public static void RecordOriginal(FileRecord record)
    {
        if (record == null) return;

        lock (_lock)
        {
            if (_zip == null || _knownPaths == null || _manifest == null || _session == null) return;

            var path = record.Path ?? string.Empty;
            if (string.IsNullOrEmpty(path)) return;
            _session.Add(record);
            if (!_knownPaths.Add(path)) return;

            var originalBytes = record.Read();

            var entry = _zip.CreateEntry(path, CompressionLevel.Optimal);
            using (var es = entry.Open())
                es.Write(originalBytes.Span);
            _manifest[path] = new Hashes { Original = Hash(originalBytes.Span) };
        }
    }

    /// <summary>Kết thúc phiên patch: ghi mã băm bản đã patch của mọi file vừa sửa vào manifest rồi đóng zip.</summary>
    public static void End()
    {
        lock (_lock)
        {
            if (_zip != null && _manifest != null && _session != null)
            {
                foreach (var record in _session)
                {
                    if (!_manifest.TryGetValue(record.Path, out var h))
                        _manifest[record.Path] = h = new Hashes();   // file có trong backup cũ (không manifest)
                    h.Patched = Hash(record.Read().Span);
                }
                WriteManifest(_zip, _manifest);
            }
            _zip?.Dispose();
            _zip = null;
            _knownPaths = null;
            _manifest = null;
            _session = null;
        }
    }

    /// <summary>So từng file trong backup với nội dung hiện tại của game.</summary>
    public static List<FileCheck> Check(LibBundle3.Index index, PoeGame game)
    {
        var result = new List<FileCheck>();
        var path = GetBackupFilePath(game);
        if (!File.Exists(path)) return result;

        using var zip = ZipFile.OpenRead(path);
        var manifest = ReadManifest(zip);
        foreach (var entry in zip.Entries.Where(IsDataEntry))
            result.Add(new FileCheck(entry.FullName, StateOf(index, entry, manifest)));
        return result;
    }

    /// <summary>
    /// Restore an toàn: chỉ ghi lại bản gốc cho file ĐANG LÀ bản PoeRedux đã patch.
    /// File game đã cập nhật (khác cả 2 mã băm) thì bỏ qua — ghi bản gốc cũ đè lên sẽ làm game lệch phiên bản.
    /// Backup cũ không có mã băm: chỉ restore khi <paramref name="force"/>.
    /// </summary>
    public static RestoreReport RestoreSafe(LibBundle3.Index index, PoeGame game, bool force = false, Action<int, int>? progress = null, ICollection<string>? only = null)
    {
        var path = GetBackupFilePath(game);
        if (!File.Exists(path)) return new RestoreReport(0, 0, 0, 0, false);

        using var zip = ZipFile.OpenRead(path);
        var manifest = ReadManifest(zip);
        var legacy = manifest == null;
        var entries = zip.Entries.Where(IsDataEntry).Where(e => only == null || only.Contains(e.FullName)).ToList();
        int restored = 0, original = 0, changed = 0, missing = 0, done = 0;

        foreach (var entry in entries)
        {
            progress?.Invoke(++done, entries.Count);
            var state = StateOf(index, entry, manifest);
            switch (state)
            {
                case FileState.Missing:
                    missing++;
                    continue;
                case FileState.Original:
                case FileState.Unchanged:
                    original++;
                    continue;
                case FileState.ChangedByGame:
                    changed++;
                    continue;
                case FileState.Unknown when !force:
                    changed++;
                    continue;
            }
            index.TryGetFile(entry.FullName, out var record);
            using var ms = new MemoryStream();
            using (var es = entry.Open())
                es.CopyTo(ms);
            record!.Write(ms.GetBuffer().AsSpan(0, (int)ms.Length));
            restored++;
        }
        if (restored > 0)
            index.Save();
        return new RestoreReport(restored, original, changed, missing, legacy);
    }

    /// <summary>Bản cũ (giao diện WPF dùng): trả về số file đã restore.</summary>
    public static int Restore(LibBundle3.Index index, PoeGame game, Action<int, int>? progress = null)
        => RestoreSafe(index, game, force: false, progress).Restored;

    public static void DeleteBackup(PoeGame game)
    {
        var path = GetBackupFilePath(game);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private static FileState StateOf(LibBundle3.Index index, ZipArchiveEntry entry, Dictionary<string, Hashes>? manifest)
    {
        var path = entry.FullName;
        if (!index.TryGetFile(path, out var record))
            return FileState.Missing;
        var now = Hash(record.Read().Span);
        if (manifest == null || !manifest.TryGetValue(path, out var h) || h.Original == "" || h.Patched == "")
        {
            // Backup cũ không mã băm: chỉ biết chắc khi nội dung hiện tại trùng bản gốc trong backup.
            using var es = entry.Open();
            return now == Convert.ToHexString(SHA256.HashData(es)) ? FileState.Original : FileState.Unknown;
        }
        if (h.Patched == h.Original) return now == h.Original ? FileState.Unchanged : FileState.ChangedByGame;
        if (now == h.Patched) return FileState.Patched;
        if (now == h.Original) return FileState.Original;
        return FileState.ChangedByGame;
    }

    private static bool IsDataEntry(ZipArchiveEntry e) => !e.FullName.EndsWith('/') && e.FullName != ManifestEntry;

    private static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));

    private static Dictionary<string, Hashes>? ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestEntry);
        if (entry == null) return null;
        using var s = entry.Open();
        return JsonSerializer.Deserialize<Dictionary<string, Hashes>>(s);
    }

    private static void WriteManifest(ZipArchive zip, Dictionary<string, Hashes> manifest)
    {
        zip.GetEntry(ManifestEntry)?.Delete();
        var entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
        using var s = entry.Open();
        JsonSerializer.Serialize(s, manifest, new JsonSerializerOptions { WriteIndented = true });
    }
}
