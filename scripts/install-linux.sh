#!/usr/bin/env bash
# Build PoeRedux (bản Linux) và tạo biểu tượng trong menu ứng dụng + Desktop.
#   scripts/install-linux.sh [thư mục cài]     (mặc định ~/.local/opt/poeredux)
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${1:-$HOME/.local/opt/poeredux}"
DOTNET="$(command -v dotnet || true)"
[ -n "${DOTNET_ROOT:-}" ] && DOTNET="$DOTNET_ROOT/dotnet"
[ -x "$DOTNET" ] || { echo "Cần .NET 10 SDK (dotnet) — hoặc đặt DOTNET_ROOT"; exit 1; }

"$DOTNET" publish "$REPO/PoeRedux.Cli" -c Release -r linux-x64 --no-self-contained -o "$DEST" --nologo -v q
install -m 755 "$REPO/scripts/poeredux-gui.sh" "$DEST/poeredux-gui"
dirname "$(readlink -f "$DOTNET")" > "$DEST/dotnet-root"

DESKTOP_FILE="[Desktop Entry]
Type=Application
Name=PoeRedux
Comment=Patch Path of Exile (zoom camera, icon quái, tắt hiệu ứng…)
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
