using LibBundledGGPK3;
using PoeRedux;
using PoeRedux.Patches;
using PoeRedux.Services;
using System.Globalization;

const long MinFreeBytes = 2L << 30;   // patch ghi bundle mới vào cuối Content.ggpk → cần chỗ trống trên ổ game

var argv = args.ToList();
var game = Option("--game") is "poe2" ? PoeGame.PoE2 : PoeGame.PoE1;
var zoom = Option("--zoom") is { } z ? double.Parse(z, CultureInfo.InvariantCulture) : 2.4;
var backupDir = Option("--backup-dir");
var monsterIcon = Option("--monster-icon");
var playerIcon = Option("--player-icon") is { } pi ? int.Parse(pi, CultureInfo.InvariantCulture) : 3;
var party = Flag("--party");
var effects = Option("--effects") is { } ef ? double.Parse(ef, CultureInfo.InvariantCulture) : 0.5;
var force = Flag("--force");
var allowBlack = Flag("--allow-black-screen");
var dryRun = Flag("--dry-run");
var raw = Flag("--raw");
var cdnOpt = Option("--cdn");

if (argv.Count == 0 || argv[0] is "-h" or "--help" or "help")
    return Usage();

try
{
    return argv[0] switch
    {
        "list" => List(),
        "apply" when argv.Count >= 3 => Apply(Ggpk(argv[1]), argv.Skip(2).ToList()),
        "status" when argv.Count >= 2 => Status(Ggpk(argv[1])),
        "restore" when argv.Count >= 2 => Restore(Ggpk(argv[1]), argv.Skip(2).Select(f => f.TrimStart('/').ToLowerInvariant()).ToList()),
        "cat" when argv.Count >= 3 => Cat(Ggpk(argv[1]), argv[2]),
        "ls" when argv.Count >= 3 => Ls(Ggpk(argv[1]), argv[2]),
        "grep" when argv.Count >= 4 => Grep(Ggpk(argv[1]), argv[2], argv[3]),
        "preview-reduce" when argv.Count >= 3 => PreviewReduce(Ggpk(argv[1]), argv[2]),
        "preview-hide" when argv.Count >= 3 => PreviewHide(Ggpk(argv[1]), argv[2]),
        "fix-hideout" when argv.Count >= 2 => FixHideout(Ggpk(argv[1])),
        "repair" when argv.Count >= 2 => RepairCmd(Ggpk(argv[1])),
        _ => Usage(),
    };
}
catch (CliError e)
{
    Console.Error.WriteLine($"⛔ {e.Message}");
    return 2;
}

int Usage()
{
    Console.WriteLine("""
        poeredux — PoeRedux bản dòng lệnh (Linux)

          poeredux list [--game poe1|poe2]                  liệt kê patch
          poeredux apply <Content.ggpk> <patch>... [opts]   patch, rồi tự đọc lại kiểm tra từng file
          poeredux status <Content.ggpk> [--game ...]       file nào đang patch / gốc / game đã cập nhật
          poeredux restore <Content.ggpk> [file…] [--force] restore an toàn (chỉ các file/thư mục kết thúc '/' nêu ra) (bỏ qua file game đã cập nhật)
          poeredux repair <Content.ggpk> [--dry-run]        KHÔNG cần backup: so với file gốc trên CDN của game, tải
                                                            bản gốc về ghi đè mọi file bị sửa (chữa màn hình đen)
          poeredux fix-hideout <Content.ggpk>               chữa lỗi treo khi về hideout (gỡ patch icon quái ở monster.ot)
          poeredux ls <Content.ggpk> <thư mục>              liệt kê file trong game (vd: metadata/monsters)
          poeredux grep <Content.ggpk> <thư mục> <chuỗi>    tìm file văn bản (.ot/.otc/.ao/.epk/.hlsl…) có chuỗi
          poeredux cat <Content.ggpk> <file>                in nội dung 1 file (tự giải mã UTF-16; --raw = byte gốc)

        Tuỳ chọn:
          --game poe1|poe2        mặc định poe1
          --zoom 2.4              mức zoom cho patch camera (1.2–2.4, lớn = nhìn xa hơn)
          --player-icon 3         player-icon: cỡ icon người chơi trên minimap (gốc 1, 2–6 = to dần); --party: cả người cùng party
          --effects 0.5           reduce-effects: phần hiệu ứng giữ lại (0.5 = còn 50%, 0.3 = còn 30%)
          --monster-icon <tên>    icon cho patch monster-icons (mặc định LootFilterSmallRedCircle;
                                  tên khác: LootFilterMediumYellowStar, ExpeditionRegularMonster…)
          --backup-dir <dir>      mặc định: <thư mục game>/PoeReduxBackups (cùng ổ với game)
          --allow-black-screen    cho phép patch black-screen (làm màn hình ĐEN — đúng chức năng của nó)
          --force                 restore cả backup cũ không có mã băm (bản PoeRedux trước)
        """);
    return 1;
}

int List()
{
    foreach (var p in PatchCatalog.For(game))
        Console.WriteLine($"  {PatchCatalog.Slug(p),-24} {p.Name} — {p.Description}");
    return 0;
}

int Apply(string ggpk, List<string> names)
{
    var catalog = PatchCatalog.For(game);
    var chosen = names.Contains("all")
        ? catalog.Where(p => p is not (BlackScreen or HideEffects or HideMonsters)).ToList()   // hide-*: chọn riêng
        : names.Select(n => catalog.FirstOrDefault(p => PatchCatalog.Slug(p) == n)
                            ?? throw new CliError($"không có patch '{n}' — xem: poeredux list --game {GameArg()}")).ToList();
    if (chosen.Any(p => p is BlackScreen) && !allowBlack)
        throw new CliError("patch black-screen làm màn hình game ĐEN (đúng chức năng). Muốn thật thì thêm --allow-black-screen");
    foreach (var p in chosen.OfType<Camera>())
        p.ZoomLevel = Math.Clamp(zoom, 1.2, 2.4);
    foreach (var p in chosen.OfType<PlayerIcon>())
        (p.Size, p.IncludeParty) = (Math.Clamp(playerIcon, 1, 6), party);
    foreach (var p in chosen.OfType<ReduceEffects>())
        p.Factor = Math.Clamp(effects, 0.05, 0.95);
    if (monsterIcon != null)
        foreach (var p in chosen.OfType<MonsterIcons>())
            p.Icon = monsterIcon;

    Preflight(ggpk);
    Console.WriteLine($"Backup: {BackupManager.GetBackupFilePath(game)}");

    var touched = new Dictionary<IPatch, List<string>>();
    var failed = new Dictionary<IPatch, string>();
    using (var bundled = new BundledGGPK(ggpk, false))
    {
        var index = bundled.Index;
        index.ParsePaths();
        var tree = index.BuildTree(true);
        BackupManager.Begin(game);
        try
        {
            foreach (var p in chosen)
            {
                var before = BackupManager.SessionPaths.Count;
                Console.Write($"  … {p.Name}");
                try
                {
                    p.Apply(tree);
                    index.Save();
                }
                catch (Exception e)
                {
                    failed[p] = e.Message;
                }
                touched[p] = BackupManager.SessionPaths.Skip(before).Distinct().ToList();
                Console.WriteLine(failed.TryGetValue(p, out var err) ? $"  ⛔ lỗi: {err}" : $"  ({touched[p].Count} file)");
            }
        }
        finally
        {
            BackupManager.End();
        }
    }

    // Đọc lại từ đầu (mở file mới) — xác nhận game sẽ thấy đúng nội dung đã patch.
    Console.WriteLine("Kiểm tra lại Content.ggpk:");
    List<FileCheck> checks;
    using (var bundled = new BundledGGPK(ggpk, false))
    {
        bundled.Index.ParsePaths();
        checks = BackupManager.Check(bundled.Index, game);
    }
    var state = checks.ToDictionary(c => c.Path, c => c.State);
    int bad = 0;
    foreach (var p in chosen)
    {
        var files = touched[p];
        var ok = files.Count(f => state.GetValueOrDefault(f) == FileState.Patched);
        var same = files.Count(f => state.GetValueOrDefault(f) == FileState.Unchanged);
        string verdict;
        if (failed.ContainsKey(p)) { verdict = "⛔ lỗi khi patch"; bad++; }
        else if (same > 0 && ok + same == files.Count)
        {
            verdict = $"⚠ {same}/{files.Count} file không đổi gì — patch không tìm thấy đoạn cần sửa (patch lỗi thời so với bản game)";
            bad++;
        }
        else if (files.Count == 0) verdict = "— không có file nào cần đổi (đã patch từ trước, hoặc game đổi cấu trúc)";
        else if (ok == files.Count) verdict = $"✅ {ok}/{files.Count} file đã đổi";
        else { verdict = $"⚠ chỉ {ok}/{files.Count} file đã đổi"; bad++; }
        Console.WriteLine($"  {PatchCatalog.Slug(p),-24} {verdict}");
    }
    return bad == 0 ? 0 : 3;
}

int Status(string ggpk)
{
    var path = BackupManager.GetBackupFilePath(game);
    if (!BackupManager.HasBackup(game))
    {
        Console.WriteLine($"Chưa có backup ({path}).");
        return 0;
    }
    using var bundled = new BundledGGPK(ggpk, false);
    bundled.Index.ParsePaths();
    var checks = BackupManager.Check(bundled.Index, game);
    Console.WriteLine($"Backup {path}: {checks.Count} file");
    foreach (var g in checks.GroupBy(c => c.State))
    {
        Console.WriteLine($"  {Describe(g.Key)}: {g.Count()}");
        foreach (var c in g.Take(8))
            Console.WriteLine($"      {c.Path}");
        if (g.Count() > 8)
            Console.WriteLine($"      … +{g.Count() - 8}");
    }
    return 0;
}

int Restore(string ggpk, List<string> only)
{
    if (!BackupManager.HasBackup(game))
        throw new CliError($"không có backup ({BackupManager.GetBackupFilePath(game)}) — dùng PackCheck.exe để sửa file game");
    Preflight(ggpk);
    RestoreReport r;
    using (var bundled = new BundledGGPK(ggpk, false))
    {
        bundled.Index.ParsePaths();
        r = BackupManager.RestoreSafe(bundled.Index, game, force, null, only.Count > 0 ? only : null);
    }
    Console.WriteLine($"Đã restore {r.Restored} file · đã là bản gốc {r.AlreadyOriginal} · bỏ qua {r.SkippedChanged} · không còn trong game {r.Missing}");
    if (r.Legacy && !force && r.SkippedChanged > 0)
        Console.WriteLine("  Backup cũ (không có mã băm) — không biết file nào an toàn. Chắc chắn game CHƯA cập nhật từ lúc patch thì chạy lại với --force; không thì dùng PackCheck.exe.");
    else if (r.SkippedChanged > 0)
        Console.WriteLine("  File bị bỏ qua: game đã cập nhật chúng sau khi patch → bản gốc trong backup đã cũ. Dùng PackCheck.exe nếu game lỗi.");
    if (r.Complete && r.SkippedChanged == 0 && only.Count == 0)
    {
        BackupManager.DeleteBackup(game);
        Console.WriteLine("  Backup đã xong việc — đã xoá.");
    }
    return 0;
}

string Ggpk(string p)
{
    var full = Path.GetFullPath(p);
    if (!File.Exists(full))
        throw new CliError($"không thấy {full}");
    BackupManager.BackupDirectory = backupDir ?? Path.Combine(Path.GetDirectoryName(full)!, "PoeReduxBackups");
    return full;
}

void Preflight(string ggpk)
{
    // Patch khi game đang mở: game giữ Content.ggpk và ghi lại → patch mất / file lệch.
    var users = ProcessesUsing(ggpk);
    if (users.Count > 0)
        throw new CliError($"game đang mở Content.ggpk ({string.Join(", ", users)}) — thoát game rồi chạy lại");
    foreach (var (label, dir) in new[] { ("ổ game", Path.GetDirectoryName(ggpk)!), ("ổ backup", BackupManager.BackupDirectory!) })
    {
        Directory.CreateDirectory(dir);
        var free = new DriveInfo(dir).AvailableFreeSpace;
        if (free < MinFreeBytes)
            throw new CliError($"{label} ({dir}) chỉ còn {free >> 20} MB trống — cần ≥ {MinFreeBytes >> 30} GB");
    }
}

static List<string> ProcessesUsing(string file)
{
    var found = new List<string>();
    foreach (var proc in Directory.EnumerateDirectories("/proc"))
    {
        if (!int.TryParse(Path.GetFileName(proc), out var pid) || pid == Environment.ProcessId) continue;
        try
        {
            foreach (var fd in Directory.EnumerateFileSystemEntries(Path.Combine(proc, "fd")))
            {
                if (new FileInfo(fd).LinkTarget == file)
                {
                    found.Add($"PID {pid} {File.ReadAllText(Path.Combine(proc, "comm")).Trim()}");
                    break;
                }
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
    }
    return found;
}

int FixHideout(string ggpk)
{
    // File mà patch cũ làm treo game khi vào hideout (monster-icons: minion/spectre cũng kế thừa monster.ot).
    string[] HideoutBreakers = ["metadata/monsters/monster.ot"];
    Preflight(ggpk);
    List<string> bad;
    using (var bundled = new BundledGGPK(ggpk, false))
    {
        bundled.Index.ParsePaths();
        bad = HideoutBreakers.Where(f => bundled.Index.TryGetFile(f, out var r) && Decode(r.Read().Span).Contains("PoeRedux")).ToList();
    }
    if (bad.Count == 0)
    {
        Console.WriteLine("✅ Không có patch nào gây treo hideout — monster.ot đang là bản gốc.");
        return 0;
    }

    // 1) Có backup → trả về bản gốc từ backup
    if (BackupManager.HasBackup(game))
    {
        using var bundled = new BundledGGPK(ggpk, false);
        bundled.Index.ParsePaths();
        var r = BackupManager.RestoreSafe(bundled.Index, game, force: true, null, bad);
        Console.WriteLine($"Gỡ từ backup: {r.Restored} file");
        bad = bad.Where(f => bundled.Index.TryGetFile(f, out var rec) && Decode(rec.Read().Span).Contains("PoeRedux")).ToList();
    }
    // 2) Không có backup (máy khác / patch bằng bản cũ) → tải bản gốc của riêng các file đó từ máy chủ game
    if (bad.Count > 0)
    {
        var cdn = cdnOpt ?? PoeRedux.Cli.Repair.CdnFromLog(ggpk)
            ?? throw new CliError("không đọc được phiên bản game từ logs/Client.txt — chỉ định --cdn https://patch.poecdn.com/<phiên bản>/");
        PoeRedux.Cli.Repair.Run(ggpk, cdn, Path.Combine(BackupManager.BackupDirectory!, "cdn"), false, Console.WriteLine, bad);
    }

    using (var bundled = new BundledGGPK(ggpk, false))
    {
        bundled.Index.ParsePaths();
        var still = HideoutBreakers.Where(f => bundled.Index.TryGetFile(f, out var r) && Decode(r.Read().Span).Contains("PoeRedux")).ToList();
        if (still.Count > 0)
            throw new CliError($"vẫn còn patch trong {string.Join(", ", still)}");
    }
    Console.WriteLine("✅ Đã gỡ — vào game về hideout lại được. Các patch khác (zoom, giảm hiệu ứng…) vẫn giữ.");
    return 0;
}

int RepairCmd(string ggpk)
{
    var cdn = cdnOpt ?? PoeRedux.Cli.Repair.CdnFromLog(ggpk)
        ?? throw new CliError("không đọc được phiên bản game từ logs/Client.txt — chỉ định --cdn https://patch.poecdn.com/<phiên bản>/");
    if (!dryRun)
        Preflight(ggpk);
    var r = PoeRedux.Cli.Repair.Run(ggpk, cdn, Path.Combine(BackupManager.BackupDirectory!, "cdn"), dryRun, Console.WriteLine);
    if (!dryRun && BackupManager.HasBackup(game))
    {
        BackupManager.DeleteBackup(game);   // game đã về bản gốc → backup cũ hết tác dụng
        Console.WriteLine("  Đã xoá backup cũ (game đã về bản gốc).");
    }
    return r;
}

int PreviewReduce(string ggpk, string file)
{
    using var bundled = new BundledGGPK(ggpk, false);
    bundled.Index.ParsePaths();
    if (!bundled.Index.TryGetFile(file.Trim('/'), out var record))
        throw new CliError($"không có file '{file}'");
    var before = Decode(record.Read().Span).Split('\n');
    var after = new ReduceEffects { Factor = effects }.Reduce(string.Join('\n', before)).Split('\n');
    for (int i = 0; i < before.Length; i++)
        if (before[i] != after[i])
            Console.WriteLine($"- {before[i].Trim()}\n+ {after[i].Trim()}");
    return 0;
}

/// <summary>Chỉ đọc: file sau khi qua hide-effects / hide-monsters (patch nào nhận file đó).</summary>
int PreviewHide(string ggpk, string file)
{
    using var bundled = new BundledGGPK(ggpk, false);
    bundled.Index.ParsePaths();
    if (!bundled.Index.TryGetFile(file.Trim('/'), out var record))
        throw new CliError($"không có file '{file}'");
    var text = Decode(record.Read().Span);
    foreach (var p in new HideBase[] { new HideEffects(), new HideMonsters() })
        if (p.Preview(record.Path, text) is { } next && next != text)
        {
            Console.WriteLine($"── {PatchCatalog.Slug(p)}: {text.Length} → {next.Length} ký tự");
            Console.WriteLine(next);
            return 0;
        }
    Console.WriteLine("(hide-effects / hide-monsters không đổi file này)");
    return 0;
}

int Ls(string ggpk, string dir)
{
    using var bundled = new BundledGGPK(ggpk, false);
    bundled.Index.ParsePaths();
    if (!bundled.Index.TryFindNode(dir.Trim('/'), out var node) || node is not LibBundle3.Nodes.DirectoryNode d)
        throw new CliError($"không có thư mục '{dir}'");
    foreach (var c in d.Children.OrderBy(c => c is LibBundle3.Nodes.FileNode).ThenBy(c => c.Name))
        Console.WriteLine(c is LibBundle3.Nodes.FileNode f ? $"  {f.Record.Size,10}  {c.Name}" : $"  {"<dir>",10}  {c.Name}/");
    return 0;
}

int Grep(string ggpk, string dir, string needle)
{
    using var bundled = new BundledGGPK(ggpk, false);
    bundled.Index.ParsePaths();
    if (!bundled.Index.TryFindNode(dir.Trim('/'), out var node))
        throw new CliError($"không có thư mục '{dir}'");
    string[] text = [".pet", ".trl", ".ot", ".otc", ".ao", ".aoc", ".epk", ".hlsl", ".ffx", ".fxgraph", ".txt", ".it", ".itc", ".mat", ".env", ".json"];
    var files = LibBundle3.Index.Recursefiles(node).Where(f => text.Contains(Path.GetExtension(f.Name))).Select(f => f.Record).ToList();
    int hits = 0;
    foreach (var r in LibBundle3.Index.SortByBundle(files))
    {
        var t = Decode(r.Read().Span);
        var i = t.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (i < 0) continue;
        var line = t[(t.LastIndexOf('\n', i) + 1)..];
        var end = line.IndexOf('\n');
        Console.WriteLine($"{r.Path}: {(end < 0 ? line : line[..end]).Trim()}");
        hits++;
    }
    Console.Error.WriteLine($"{hits}/{files.Count} file khớp");
    return 0;
}

static string Decode(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
    ? System.Text.Encoding.Unicode.GetString(bytes[2..])
    : System.Text.Encoding.UTF8.GetString(bytes);

int Cat(string ggpk, string file)
{
    using var bundled = new BundledGGPK(ggpk, false);
    bundled.Index.ParsePaths();
    if (!bundled.Index.TryGetFile(file.Trim('/'), out var record))
        throw new CliError($"không có file '{file}'");
    var bytes = record.Read().Span;
    if (raw)
    {
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(bytes);
        return 0;
    }
    // File văn bản của PoE (.ot, .otc, .epk, .ao…) là UTF-16 LE có BOM FF FE.
    var text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
        ? System.Text.Encoding.Unicode.GetString(bytes[2..])
        : System.Text.Encoding.UTF8.GetString(bytes);
    Console.Write(text);
    return 0;
}

static string Describe(FileState s) => s switch
{
    FileState.Unchanged => "patch không đổi gì (không tìm thấy đoạn cần sửa)",
    FileState.Patched => "đang là bản đã patch",
    FileState.Original => "đang là bản gốc",
    FileState.ChangedByGame => "game đã cập nhật (backup cũ, không restore)",
    FileState.Missing => "không còn trong game",
    _ => "không rõ (backup cũ không có mã băm)",
};

string GameArg() => game == PoeGame.PoE2 ? "poe2" : "poe1";

string? Option(string name)
{
    var i = argv.IndexOf(name);
    if (i < 0 || i + 1 >= argv.Count) return null;
    var v = argv[i + 1];
    argv.RemoveRange(i, 2);
    return v;
}

bool Flag(string name) => argv.Remove(name);

sealed class CliError(string message) : Exception(message);
