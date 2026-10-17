# PoeRedux (fork nlttien/PoeRedux) — hướng dẫn cho agent

Công cụ patch `Content.ggpk` của Path of Exile (tắt sương mù, zoom camera, giảm hiệu ứng…). Fork của
Gineticus/PoeRedux, thêm bản **chạy native trên Linux** (CLI + giao diện zenity + AppImage).
Trạng thái mới nhất và việc đang dở: **`docs/HANDOFF.md`** — đọc trước khi làm.

## Cấu trúc
| Thư mục | Vai trò |
|---|---|
| `PoeRedux.Core/` | Patch (`Patches/`, `Patches2/` cho PoE2), `PatchCatalog.cs` (danh sách patch), `Services/BackupManager.cs` |
| `PoeRedux.Cli/` | `poeredux` — CLI Linux (`Program.cs`), `Repair.cs` (khôi phục từ CDN), `native/liboo2core.so` |
| `PoeRedux/` | Giao diện WPF gốc (Windows) — dùng chung `PoeRedux.Core` |
| `scripts/` | `poeredux-gui.sh` (zenity), `install-linux.sh`, `package-linux.sh` (tar.gz), `package-appimage.sh` |

## Lệnh
```bash
. /games/gege-home/dotnet-env.sh                 # .NET 10 SDK nằm ở /games (ổ / gần đầy — KHÔNG cài vào ~)
dotnet build PoeRedux.slnx -c Release
scripts/install-linux.sh /games/gege-home/poeredux                                  # bản cài máy này
APPIMAGETOOL=/games/gege-home/tools/appimagetool scripts/package-appimage.sh /games/gege-home/dist
P=/games/gege-home/poeredux/poeredux; G=/games/nonsteam/POE/Content.ggpk
$P list | $P status $G | $P apply $G <patch>… | $P restore $G [file|thư-mục/] | $P fix-hideout $G | $P repair $G --dry-run
$P ls|cat|grep $G …                              # đọc file trong game để viết patch (cat --raw = byte gốc)
```

## Quy tắc (bài học đã trả giá — xem FIELD_JOURNAL [R10], [R11] ở ~/Projects/exileapi-agent-skill)
- **KHÔNG BAO GIỜ ghi vào Content.ggpk khi có process đang mở nó** (game, wineserver, rsync…). CLI có `Preflight`;
  mọi chương trình thử nghiệm tự viết cũng phải kiểm `/proc/*/fd` trước (2026-10-17 đã ghi nhầm khi game đang mở).
- Patch theo hệ số/giá trị phải tính từ **bản gốc** (`BackupManager.ReadOriginal`) để áp lại không cộng dồn.
- Sửa base class của game (`metadata/monsters/monster.ot`…) ảnh hưởng MỌI lớp con kể cả minion của người chơi —
  `monster-icons` từng làm treo hideout, đã bỏ khỏi `PatchCatalog`. Thử 1 patch gameplay mới mỗi lần, qua town/hideout/map.
- Sau patch luôn đọc lại xác nhận (CLI `apply` đã tự làm). Chưa thử trong game thì nói rõ là chưa thử.
- User chấp nhận rủi ro ban tài khoản — làm patch gameplay không cần cảnh báo.
- Commit nhỏ, push lên remote `fork` (nlttien/PoeRedux), nhánh `feat/linux-cli`. Không push/PR lên `origin` (Gineticus).
- Release: AppImage → `gh release create v1.1.17-linux.N <file> -R nlttien/PoeRedux --target <sha>`.
