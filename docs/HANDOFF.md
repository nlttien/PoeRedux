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
1. **Trên máy này** (game ở `/games/nonsteam/POE`): `monster.ot` còn patch monster-icons (ghi nhầm khi game mở).
   Phiên cũ có tiến trình nền chờ rsync (một máy khác đang kéo thư mục game) xong rồi chạy `fix-hideout` + áp
   `player-icon --player-icon 3`. Nếu phiên cũ đã đóng, tiến trình đó có thể đã mất → kiểm:
   ```bash
   P=/games/gege-home/poeredux/poeredux; G=/games/nonsteam/POE/Content.ggpk
   $P cat $G metadata/monsters/monster.ot | grep -c PoeRedux     # >0 = chưa gỡ → $P fix-hideout $G
   $P status $G | grep -i minimapicons                            # chưa có = player-icon chưa áp
   ```
2. **Máy nhận bản chép qua rsync** nhận cả `monster.ot` lỗi → chạy AppImage → "Sửa lỗi hideout" (có PoeReduxBackups đi kèm).
3. `player-icon` chưa xem trong game — xác nhận icon to lên thật (cột +8/+12 có đúng là cỡ vẽ, không phải bán kính khác).
4. Icon quái (monster-icons) nếu muốn làm lại: phải loại minion của người chơi (chỉ gắn cho quái phe địch), thử ở hideout.

## Môi trường
- .NET 10 SDK: `/games/gege-home/.dotnet` (`. /games/gege-home/dotnet-env.sh`). Ổ `/` gần đầy — mọi build/tmp để trên `/games`.
- appimagetool 1.9.0: `/games/gege-home/tools/appimagetool`.
- Backup patch: `/games/nonsteam/POE/PoeReduxBackups/poe1.zip` (+ cache CDN trong `cdn/`).
- Game: 3.29.3.3, chạy qua umu-run/Proton 11 (process `Main`, argv0 `S:\nonsteam\POE\PathOfExile.exe`).
