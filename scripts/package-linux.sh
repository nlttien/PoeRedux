#!/usr/bin/env bash
# Đóng gói PoeRedux (Linux) thành 1 file .tar.gz mang sang máy khác — kèm sẵn .NET, máy đích không cần cài.
#   scripts/package-linux.sh [thư mục ra]      (mặc định ./dist)
# Máy đích: tar xzf poeredux-linux-x64.tar.gz && poeredux/install.sh
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(mkdir -p "${1:-$REPO/dist}" && cd "${1:-$REPO/dist}" && pwd)"
DOTNET="${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}"
DOTNET="${DOTNET:-$(command -v dotnet)}"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/poeredux"

"$DOTNET" publish "$REPO/PoeRedux.Cli" -c Release -r linux-x64 --self-contained -o "$APP" --nologo -v q \
    -p:DebugType=none -p:GenerateDocumentationFile=false
rm -f "$APP"/*.xml
install -m 755 "$REPO/scripts/poeredux-gui.sh" "$APP/poeredux-gui"

cat > "$APP/install.sh" <<'EOF'
#!/usr/bin/env bash
# Cài PoeRedux vào ~/.local/opt/poeredux + biểu tượng trong menu ứng dụng và Desktop.
set -euo pipefail
SRC="$(cd "$(dirname "$0")" && pwd)"
DEST="${1:-$HOME/.local/opt/poeredux}"
if [ "$SRC" != "$DEST" ]; then
    mkdir -p "$DEST"
    cp -a "$SRC/." "$DEST/"
fi
rm -f "$DEST/dotnet-root"   # bản tự chứa .NET
command -v zenity >/dev/null || echo "⚠ Cần cài zenity cho giao diện (vd: sudo dnf install zenity / sudo apt install zenity)"

DESKTOP_FILE="[Desktop Entry]
Type=Application
Name=PoeRedux
Comment=Patch Path of Exile (zoom camera, giảm hiệu ứng…)
Exec=$DEST/poeredux-gui
Icon=applications-games
Terminal=false
Categories=Game;"
mkdir -p "$HOME/.local/share/applications"
echo "$DESKTOP_FILE" > "$HOME/.local/share/applications/poeredux.desktop"
DESK="$(xdg-user-dir DESKTOP 2>/dev/null || echo "$HOME/Desktop")"
if [ -d "$DESK" ]; then
    echo "$DESKTOP_FILE" > "$DESK/poeredux.desktop"
    chmod +x "$DESK/poeredux.desktop"
    gio set "$DESK/poeredux.desktop" metadata::trusted true 2>/dev/null || true
fi
echo "Đã cài vào $DEST — mở PoeRedux từ menu ứng dụng hoặc Desktop."
echo "Dòng lệnh: $DEST/poeredux --help"
EOF
chmod +x "$APP/install.sh"

cat > "$APP/README.txt" <<'EOF'
PoeRedux cho Linux (đã kèm .NET — không cần cài thêm, chỉ cần zenity cho giao diện)

Cài:   ./install.sh                 → biểu tượng PoeRedux trong menu ứng dụng + Desktop
Dùng:  bấm biểu tượng → Patch → tick patch → OK   (game phải TẮT)
       Lần đầu tool hỏi đường dẫn Content.ggpk.

Menu:
  Patch       chọn patch (camera zoom, reduce-effects giảm hiệu ứng kỹ năng theo %, tắt sương mù…)
  Trạng thái  file nào đang được patch
  Gỡ patch    trả các file đã patch về bản gốc (backup nằm cạnh Content.ggpk: PoeReduxBackups/)
  Sửa game    game lỗi / màn hình đen: tải bản gốc từ máy chủ game, không cần backup

Dòng lệnh: ./poeredux --help
  ./poeredux apply  <Content.ggpk> camera reduce-effects --zoom 2.4 --effects 0.5
  ./poeredux restore <Content.ggpk> [file | thư-mục/]
  ./poeredux repair <Content.ggpk> [--dry-run]
EOF

VER="$(git -C "$REPO" rev-parse --short HEAD)"
TAR="$OUT/poeredux-linux-x64-$VER.tar.gz"
tar -C "$STAGE" -czf "$TAR" poeredux
echo "$TAR"
