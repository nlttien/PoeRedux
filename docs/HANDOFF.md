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
4. Icon quái (monster-icons) nếu muốn làm lại: phải loại minion của người chơi (chỉ gắn cho quái phe địch), thử ở hideout.
5. **hide-effects / hide-monsters** (thêm 2026-10-17, `Patches/Hide.cs`): tắt HẾT hiệu ứng kể cả mesh 3D (lốc xoáy Tornado =
   `SkinMesh` trong `tornado.ao` — reduce-effects chỉ giảm hạt nên vẫn thấy) / quái tàng hình. Ẩn mesh = giữ khối `SkinMesh` nhưng
   rỗng ruột (không xoá component → tránh lỗi kiểu R11). Không nằm trong `apply all`. Xem trước: `poeredux preview-hide <ggpk> <file>`.
   - Máy này: đã áp `hide-effects` (60478 file) — **CHƯA thử trong game**: vào town/hideout/map, xem lốc xoáy mất chưa, có crash không.
     Lỗi → `restore <ggpk> metadata/effects/ metadata/particles/` (chia đôi thư mục để tìm file thủ phạm).
   - `hide-monsters` CHƯA áp — chỉ áp sau khi hide-effects ổn (1 patch mới/lần). ⚠ minion/spectre của mình cũng tàng hình.

## Môi trường
- .NET 10 SDK: `/games/gege-home/.dotnet` (`. /games/gege-home/dotnet-env.sh`). Ổ `/` gần đầy — mọi build/tmp để trên `/games`.
- appimagetool 1.9.0: `/games/gege-home/tools/appimagetool`.
- Backup patch: `/games/nonsteam/POE/PoeReduxBackups/poe1.zip` (+ cache CDN trong `cdn/`).
- Game: 3.29.3.3, chạy qua umu-run/Proton 11 (process `Main`, argv0 `S:\nonsteam\POE\PathOfExile.exe`).
