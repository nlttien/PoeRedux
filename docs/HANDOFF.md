# Bàn giao — 2026-10-17

## Đã xong (nhánh `feat/linux-cli` trên fork nlttien/PoeRedux)
- CLI Linux native + giao diện zenity + AppImage. Release mới nhất: `v1.1.17-linux.3`
  (`PoeRedux-4b5f8bc-x86_64.AppImage`). Biểu tượng PoeRedux trên Desktop máy này trỏ vào
  `/games/gege-home/dist/PoeRedux-4b5f8bc-x86_64.AppImage`.
- Patch mới: `reduce-effects` (giảm số hạt hiệu ứng kỹ năng theo %, đã áp 50% trên máy này, chạy tốt),
  `player-icon` (phóng to icon MyPlayer trong `data/minimapicons.datc64`, cột +8/+12 = bán kính, gốc 1).
- Restore an toàn (mã băm gốc + đã patch), restore theo file/thư mục, `repair` từ CDN, `fix-hideout`.
- `monster-icons`: thử 2 bản đều hỏng (bản 1 lỗi "Component MinimapIcon does not exist"; bản 2 treo hideout vì minion
  kế thừa monster.ot) → đã bỏ khỏi `PatchCatalog`, giữ code để tham khảo.

## Đang dở / cần kiểm
1. ~~Máy này: gỡ monster.ot + áp player-icon~~ — XONG 2026-10-17 (sau khi rsync xong): `fix-hideout` gỡ được bằng
   CDN (1 file); `player-icon` cỡ 3 đã áp ✅. **Lỗi nhỏ cần sửa:** `fix-hideout` báo "Gỡ từ backup: 0 file" — backup có
   monster.ot nhưng mã băm bản đã patch trong manifest không khớp bản đang có (ghi nhầm bằng chương trình thử nghiệm) →
   `RestoreSafe` coi là ChangedByGame và bỏ qua dù `force: true` (force chỉ áp cho Unknown). Với file trong danh sách
   gây lỗi nên restore bất kể trạng thái (miễn backup có bản gốc).
2. **Máy nhận bản chép qua rsync** nhận cả `monster.ot` lỗi → chạy AppImage → "Sửa lỗi hideout" (có PoeReduxBackups đi kèm).
3. `player-icon` chưa xem trong game — xác nhận icon to lên thật (cột +8/+12 có đúng là cỡ vẽ, không phải bán kính khác).
4. Icon quái (monster-icons): bản 3 (minion tự `HideMinimapIcon` qua `IfStat(is_player_minion)`) VẪN treo hideout → bỏ.
   Đừng thêm component vào monster.ot nữa. Muốn thấy quái: bảng realtime exileapi-rebuild (đọc RAM), không sửa file game.
5. **hide-effects / hide-monsters** (`Patches/Hide.cs`): tắt hết hiệu ứng kể cả mesh 3D (lốc xoáy Tornado) / quái tàng hình.
   - Bản 1 (làm rỗng khối `SkinMesh`): về hideout sau map load **297 giây** → đã restore, bỏ cách này.
   - Bản 2 (hiện tại) chỉ dùng kỹ thuật black-screen upstream, giới hạn thư mục: .pet/.trl → "0", .epk → rỗng,
     .mat trong art/particles + art/models/effects → material rỗng, .ao kỹ năng bỏ khối client (giữ SkinMesh).
     Máy này đã áp (156319 file, ~9,5 phút) — **CHƯA thử trong game** (map → hideout, xem load + lốc xoáy).
   - `hide-monsters` đã áp (gồm art/textures/monsters) — chưa xác nhận ổn ở hideout khi không có monster-icons. ⚠ minion cũng tàng hình. Không nằm trong `apply all`. Xem trước: `preview-hide`.

## Môi trường
- .NET 10 SDK: `/games/gege-home/.dotnet` (`. /games/gege-home/dotnet-env.sh`). Ổ `/` gần đầy — mọi build/tmp để trên `/games`.
- appimagetool 1.9.0: `/games/gege-home/tools/appimagetool`.
- Backup patch: `/games/nonsteam/POE/PoeReduxBackups/poe1.zip` (+ cache CDN trong `cdn/`).
- Game: 3.29.3.3, chạy qua umu-run/Proton 11 (process `Main`, argv0 `S:\nonsteam\POE\PathOfExile.exe`).
