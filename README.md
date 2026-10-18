# PoeRedux cho Linux — bản phát hành

Nhánh này chỉ dùng để phát hành (không chứa mã nguồn). Tải file `PoeRedux-*-x86_64.AppImage` ở mục **Releases**.

## Chạy
```bash
chmod +x PoeRedux-*-x86_64.AppImage
./PoeRedux-*-x86_64.AppImage                                   # giao diện (bấm đúp cũng được)
./PoeRedux-*-x86_64.AppImage list                              # có tham số = chạy lệnh poeredux
./PoeRedux-*-x86_64.AppImage apply /đường-dẫn/Content.ggpk hide-effects
```

- **Tắt game trước khi patch.** Ghi vào `Content.ggpk` khi game (hoặc rsync…) đang mở file sẽ làm hỏng dữ liệu.
- Backup bản gốc nằm cạnh game: `PoeReduxBackups/` — chép game sang máy khác thì chép cả thư mục này.
- Vào hideout/map bị treo hoặc disconnect sau khi patch: mở giao diện → **Sửa lỗi hideout**.
