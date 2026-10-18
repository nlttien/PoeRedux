#!/usr/bin/env bash
# PoeRedux cho Linux — cửa sổ chọn bằng chuột (zenity) bọc quanh lệnh `poeredux`.
# Cài: scripts/install-linux.sh
set -u

APP_DIR="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
BIN="$APP_DIR/poeredux"
CONF_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/poeredux"
CONF="$CONF_DIR/settings"
TITLE="PoeRedux"
mkdir -p "$CONF_DIR"

# .NET cài ngoài hệ thống (ổ / đầy) — install-linux.sh ghi đường dẫn vào dotnet-root
if [ -f "$APP_DIR/dotnet-root" ]; then
    export DOTNET_ROOT="$(cat "$APP_DIR/dotnet-root")"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1

GGPK="/games/nonsteam/POE/Content.ggpk"
GAME="poe1"
LAST_PATCHES="camera"
ZOOM="2.4"
EFFECTS="50"
PICON="3"
# shellcheck source=/dev/null
[ -f "$CONF" ] && . "$CONF"

save() {
    printf 'GGPK=%q\nGAME=%q\nLAST_PATCHES=%q\nZOOM=%q\nEFFECTS=%q\nPICON=%q\n' "$GGPK" "$GAME" "$LAST_PATCHES" "$ZOOM" "$EFFECTS" "$PICON" > "$CONF"
}

err() { zenity --error --title="$TITLE" --width=420 --text="$1" 2>/dev/null; }

pick_ggpk() {
    local f
    f=$(zenity --file-selection --title="Chọn Content.ggpk của Path of Exile" \
        --file-filter="Content.ggpk | *.ggpk" --filename="$(dirname "$GGPK")/" 2>/dev/null) || return 1
    GGPK="$f"
    save
}

# Chạy lệnh poeredux, hiện thanh chờ, rồi hiện kết quả.
run() {
    local title="$1"; shift
    local log rc
    log=$(mktemp)
    ( "$BIN" "$@" --game "$GAME" >"$log" 2>&1; echo $? >"$log.rc" ) &
    local pid=$! t0
    t0=$(date +%s%3N)
    # đồng hồ đếm lên (ms) + dòng log mới nhất, cập nhật mỗi 0.1s
    ( while kill -0 $pid 2>/dev/null; do
        printf '# ⏱ %s ms — %s\n' "$(LC_ALL=C printf "%'d" $(( $(date +%s%3N) - t0 )))" "$(tail -n1 "$log" 2>/dev/null)"
        sleep 0.1
      done ) |
        zenity --progress --pulsate --auto-close --no-cancel --title="$TITLE" --width=560 --text="$title…" 2>/dev/null
    wait $pid
    rc=$(cat "$log.rc" 2>/dev/null || echo 1)
    printf '\n⏱ Tổng thời gian: %s ms\n' "$(LC_ALL=C printf "%'d" $(( $(date +%s%3N) - t0 )))" >>"$log"
    zenity --text-info --title="$TITLE — $title" --width=760 --height=480 --filename="$log" 2>/dev/null
    rm -f "$log" "$log.rc"
    return "$rc"
}

do_patch() {
    local rows=() slug name sel chosen
    while IFS= read -r line; do
        slug=$(awk '{print $1}' <<<"$line")
        [ -z "$slug" ] && continue
        name=$(sed -E 's/^ +[^ ]+ +//' <<<"$line")
        sel=FALSE
        [[ " $LAST_PATCHES " == *" $slug "* ]] && sel=TRUE
        rows+=("$sel" "$slug" "$name")
    done < <("$BIN" list --game "$GAME")

    chosen=$(zenity --list --checklist --title="$TITLE — chọn patch" --width=820 --height=560 \
        --text="Tick patch muốn dùng (game phải TẮT). Patch xong tool tự kiểm tra lại." \
        --column="" --column="Mã" --column="Patch" --print-column=2 --separator=" " \
        "${rows[@]}" 2>/dev/null) || return
    [ -z "$chosen" ] && { err "Chưa chọn patch nào."; return; }

    local args=()
    if [[ " $chosen " == *" camera "* ]]; then
        local z
        z=$(zenity --scale --title="$TITLE — zoom camera" --text="Mức zoom ×10 (12 = gần, 24 = xa nhất)" \
            --min-value=12 --max-value=24 --step=1 --value="$(awk -v z="$ZOOM" 'BEGIN{print int(z*10+0.5)}')" 2>/dev/null) || return
        ZOOM=$(awk -v z="$z" 'BEGIN{printf "%.1f", z/10}')
        args+=(--zoom "$ZOOM")
    fi
    if [[ " $chosen " == *" reduce-effects "* ]]; then
        EFFECTS=$(zenity --scale --title="$TITLE — giảm hiệu ứng kỹ năng" --text="Giữ lại bao nhiêu % hạt hiệu ứng (50 = giảm một nửa)" \
            --min-value=5 --max-value=95 --step=5 --value="$EFFECTS" 2>/dev/null) || return
        args+=(--effects "$(awk -v e="$EFFECTS" 'BEGIN{printf "%.2f", e/100}')")
    fi
    if [[ " $chosen " == *" black-screen "* ]]; then
        zenity --question --title="$TITLE — black-screen" --width=460 \
            --text="Patch black-screen làm màn hình game ĐEN HOÀN TOÀN (dùng khi treo máy / chạy bot).\n\nTiếp tục?" 2>/dev/null || return
        args+=(--allow-black-screen)
    fi
    if [[ " $chosen " == *" player-icon "* ]]; then
        PICON=$(zenity --scale --title="$TITLE — icon người chơi" --text="Cỡ icon của bạn trên minimap (gốc = 1)" \
            --min-value=1 --max-value=6 --step=1 --value="$PICON" 2>/dev/null) || return
        args+=(--player-icon "$PICON")
    fi
    LAST_PATCHES="$chosen"
    save
    # shellcheck disable=SC2086
    run "Đang patch: $chosen" apply "$GGPK" $chosen "${args[@]}"
}

do_repair() {
    local log
    log=$(mktemp)
    "$BIN" repair "$GGPK" --dry-run --game "$GAME" >"$log" 2>&1
    zenity --question --title="$TITLE — sửa game về bản gốc" --width=640 \
        --text="$(head -c 3000 "$log")\n\nTải bản gốc từ máy chủ game và ghi đè? (game phải TẮT, có thể mất vài phút)" 2>/dev/null \
        && run "Đang sửa game về bản gốc" repair "$GGPK"
    rm -f "$log"
}

[ -x "$BIN" ] || { err "Không thấy $BIN — chạy lại scripts/install-linux.sh"; exit 1; }
[ -f "$GGPK" ] || pick_ggpk || exit 0

while true; do
    choice=$(zenity --list --title="$TITLE" --width=560 --height=380 \
        --text="Game: <b>$GAME</b>   File: $GGPK" \
        --column="Việc" --column="Mô tả" --hide-header \
        "Patch"    "Chọn patch (zoom camera, icon quái, tắt sương mù…) rồi áp dụng" \
        "Trạng thái" "Xem file nào đang được patch" \
        "Gỡ patch" "Trả các file đã patch về bản gốc (dùng backup)" \
        "Sửa lỗi hideout" "Không về hideout được (game treo): gỡ patch gây lỗi, giữ các patch khác" \
        "Sửa game" "Game lỗi / màn hình đen: tải bản gốc từ máy chủ game" \
        "Đổi game" "Chuyển PoE1 ⇄ PoE2 hoặc chọn file Content.ggpk khác" \
        2>/dev/null) || exit 0
    case "$choice" in
        "Patch")      do_patch ;;
        "Trạng thái") run "Trạng thái" status "$GGPK" ;;
        "Gỡ patch")   zenity --question --title="$TITLE" --text="Gỡ mọi patch về bản gốc? (game phải TẮT)" 2>/dev/null \
                          && run "Đang gỡ patch" restore "$GGPK" ;;
        "Sửa lỗi hideout") run "Đang sửa lỗi hideout" fix-hideout "$GGPK" ;;
        "Sửa game")   do_repair ;;
        "Đổi game")
            g=$(zenity --list --radiolist --title="$TITLE" --column="" --column="Game" \
                $([ "$GAME" = poe1 ] && echo TRUE || echo FALSE) poe1 \
                $([ "$GAME" = poe2 ] && echo TRUE || echo FALSE) poe2 2>/dev/null) && GAME="$g"
            pick_ggpk
            save ;;
    esac
done
