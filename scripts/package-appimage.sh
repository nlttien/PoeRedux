#!/usr/bin/env bash
# Đóng gói PoeRedux (Linux) thành 1 file AppImage: bấm đúp là mở giao diện — không giải nén, không cài .NET.
#   scripts/package-appimage.sh [thư mục ra]      (mặc định ./dist)
# Cần: appimagetool (APPIMAGETOOL=đường dẫn, mặc định tìm trong PATH), .NET 10 SDK.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(mkdir -p "${1:-$REPO/dist}" && cd "${1:-$REPO/dist}" && pwd)"
DOTNET="${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}"
DOTNET="${DOTNET:-$(command -v dotnet)}"
TOOL="${APPIMAGETOOL:-$(command -v appimagetool)}"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
APPDIR="$STAGE/PoeRedux.AppDir"
BIN="$APPDIR/usr/bin"
mkdir -p "$BIN"

"$DOTNET" publish "$REPO/PoeRedux.Cli" -c Release -r linux-x64 --self-contained -o "$BIN" --nologo -v q \
    -p:DebugType=none -p:GenerateDocumentationFile=false
rm -f "$BIN"/*.xml
install -m 755 "$REPO/scripts/poeredux-gui.sh" "$BIN/poeredux-gui"

# Bấm đúp → giao diện. Có tham số → chạy thẳng lệnh poeredux (vd: ./PoeRedux.AppImage fix-hideout <ggpk>).
cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "$0")")"
if [ $# -gt 0 ]; then
    exec "$HERE/usr/bin/poeredux" "$@"
fi
if ! command -v zenity >/dev/null; then
    msg="PoeRedux cần zenity để hiện giao diện. Cài: sudo dnf install zenity (Fedora/Nobara) hoặc sudo apt install zenity (Ubuntu)."
    kdialog --error "$msg" 2>/dev/null || notify-send PoeRedux "$msg" 2>/dev/null || echo "$msg" >&2
    exit 1
fi
exec "$HERE/usr/bin/poeredux-gui"
EOF
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/poeredux.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=PoeRedux
Comment=Patch Path of Exile (zoom camera, giảm hiệu ứng, sửa lỗi hideout…)
Exec=poeredux-gui
Icon=poeredux
Terminal=false
Categories=Game;
EOF

cat > "$APPDIR/poeredux.svg" <<'EOF'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <rect width="256" height="256" rx="48" fill="#1b1b1f"/>
  <circle cx="128" cy="128" r="84" fill="none" stroke="#c8963e" stroke-width="16"/>
  <path d="M100 76h40a30 30 0 0 1 0 60h-40zM100 76v104" fill="none" stroke="#e8d5a8" stroke-width="18" stroke-linejoin="round" stroke-linecap="round"/>
</svg>
EOF
ln -s poeredux.svg "$APPDIR/.DirIcon"

VER="$(git -C "$REPO" rev-parse --short HEAD)"
IMG="$OUT/PoeRedux-$VER-x86_64.AppImage"
ARCH=x86_64 "$TOOL" --no-appstream "$APPDIR" "$IMG" >/dev/null
chmod +x "$IMG"
echo "$IMG"
