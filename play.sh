#!/usr/bin/env bash
# Launch a real, playable Atomcraft with ONLY this mod loaded, for hands-on testing.
#
#   ./play.sh            # build the mod and launch the game on your display
#   ./play.sh --demo     # also load PixelArt.Demo, which draws one of everything where you spawn
#   ./play.sh --debug    # instead load the harness and this mod's test mod, for the Alt-held
#                        #   cell readout
#   ./play.sh --verify   # boot headless under Xvfb, confirm the mod loaded, and exit
#
# Note that PixelArt on its own draws nothing: it is the layer other mods draw through, so a
# plain ./play.sh loads a mod with no visible effect and is useful only for confirming it boots
# clean. --demo is the one to reach for: it is a real player's installation, this mod plus a
# consumer and nothing else, and it is how to judge whether what the library draws is any good.
#
# What this does and does not touch:
#   - It runs against a hardlinked copy of the harness's already-patched game install, in a
#     throwaway Wine prefix. It never patches or launches your real Steam copy, and its saves
#     live in that private prefix, so your real worlds are untouched.
#   - The copy's Mods folder holds this mod alone. The test harness in particular is kept out:
#     it suppresses the game's own automatic saves, which is right for a test run and ruinous
#     for a play session. --debug is the one deliberate exception: a mod's play-time diagnostics
#     live in its test mod (they need harness types the shipped mod must not reference), so
#     seeing them means loading the harness and accepting the lost autosaves. Without this flag
#     that tooling is reachable by tests but not by a person playing the game, which is the one
#     use it was built for.
#   - EXTRA_MODS="../Other/build/Other.zip" ./play.sh installs companions too, for when the
#     combination is what you want to judge.
#
# Prerequisite: a patched game copy must already exist (the harness makes one). If it does
# not, this stops and tells you.
set -euo pipefail
cd "$(dirname "$0")"

MOD_ID="PixelArt"
MOD_NAME="PixelArt"
# shellcheck disable=SC1091
. lib/harness.sh
require_harness

# shellcheck disable=SC1091
. "$HARNESS/lib/common.sh"
load_config
require_runner

VERIFY=0
DEBUG=0
DEMO=0
for arg in "$@"; do
  case "$arg" in
    --verify) VERIFY=1 ;;
    --debug)  DEBUG=1 ;;
    --demo)   DEMO=1 ;;
    *) echo "unknown argument: $arg (expected --demo, --verify or --debug)" >&2; exit 1 ;;
  esac
done

# --- the patched source install --------------------------------------------------------------
SRC="$INSTALL"                    # from the harness config: the patched game copy
BACKUP="$SRC/$DATA_DIR_NAME/Atomcraft.dll.backup"
if [ ! -f "$SRC/AtomCraft.exe" ] || [ ! -f "$BACKUP" ]; then
  cat >&2 <<MSG
No patched game copy at:
  $SRC
This script runs against the harness's patched copy so it never modifies your Steam install.
Create one first:
  ( cd $HARNESS && ./bootstrap.sh )
Once it exists, re-run this script.
MSG
  exit 1
fi

# --- build the mod ---------------------------------------------------------------------------
# --debug also needs the test mod, whose Initialize registers the Alt-held overlay. Stage the
# harness assembly first, because build.sh only compiles the test mod when it can already see
# that assembly. It comes out of the pinned zip -- the same bytes the loader will run -- rather
# than from a harness checkout that might be mid-edit.
if [ "$DEBUG" = 1 ]; then
  echo "==> staging the harness assembly (for the debug overlay)"
  extract_harness_assembly
fi

echo "==> building the $MOD_NAME mod"
./build.sh >/dev/null
ZIP="build/$MOD_ID.zip"
[ -f "$ZIP" ] || { echo "build did not produce $ZIP" >&2; exit 1; }
if [ "$DEBUG" = 1 ]; then
  [ -f "build/$MOD_ID.Test.zip" ] || { echo "build did not produce build/$MOD_ID.Test.zip" >&2; exit 1; }
fi

# --- an isolated play install: the patched copy, this mod the only mod ------------------------
PLAY_ROOT="${PLAY_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/atomcraft-$(printf '%s' "$MOD_ID" | tr '[:upper:]' '[:lower:]')-play}"
PLAY_INSTALL="$PLAY_ROOT/install"
PLAY_PREFIX="$PLAY_ROOT/prefix"

# Refresh the clone whenever the patched source is newer than what we cloned, so a game
# update (re-bootstrapped upstream) is picked up. Hardlinked, so it costs almost nothing.
if [ ! -f "$PLAY_INSTALL/AtomCraft.exe" ] || [ "$SRC/AtomCraft.exe" -nt "$PLAY_INSTALL/AtomCraft.exe" ]; then
  echo "==> cloning the patched game copy into $PLAY_INSTALL"
  rm -rf "$PLAY_INSTALL"
  mkdir -p "$PLAY_ROOT"
  cp -al "$SRC" "$PLAY_INSTALL" 2>/dev/null || cp -a "$SRC" "$PLAY_INSTALL"
fi

if [ "$DEBUG" = 1 ]; then
  echo "==> installing $MOD_ID with the harness and $MOD_ID.Test (debug overlay)"
elif [ "$DEMO" = 1 ]; then
  echo "==> installing $MOD_ID and $MOD_ID.Demo"
else
  echo "==> installing $MOD_ID as the only mod"
fi
rm -f "$PLAY_INSTALL/Mods/"*.zip 2>/dev/null || true
mkdir -p "$PLAY_INSTALL/Mods"
cp "$ZIP" "$PLAY_INSTALL/Mods/$MOD_ID.zip"

# The demo is a peer mod that depends only on this one, so this is exactly what a player who
# installed both would be running: no harness, and nothing suppressing the game's autosaves.
if [ "$DEMO" = 1 ]; then
  DEMO_ZIP="build/$MOD_ID.Demo.zip"
  [ -f "$DEMO_ZIP" ] || { echo "build did not produce $DEMO_ZIP" >&2; exit 1; }
  cp "$DEMO_ZIP" "$PLAY_INSTALL/Mods/$MOD_ID.Demo.zip"
fi

# The pinned harness zip, not one built from a checkout, so what runs here is what the tests
# ran against.
if [ "$DEBUG" = 1 ]; then
  cp "$HARNESS_ZIP" "$PLAY_INSTALL/Mods/TestHarness.zip"
  cp "build/$MOD_ID.Test.zip" "$PLAY_INSTALL/Mods/$MOD_ID.Test.zip"
fi

for extra in ${EXTRA_MODS:-}; do
  [ -f "$extra" ] || { echo "no such mod zip: $extra" >&2; exit 1; }
  echo "==> also installing $(basename "$extra")"
  cp "$extra" "$PLAY_INSTALL/Mods/"
done

mkdir -p "$PLAY_PREFIX"

# launch_game reads these globals; point them at the isolated play copy.
INSTALL="$PLAY_INSTALL"
PREFIX="$PLAY_PREFIX"

# --- launch ---------------------------------------------------------------------------------
if [ "$VERIFY" = 1 ]; then
  # Boot headless-with-a-framebuffer just long enough to confirm the loader picked the mod
  # up, then quit. Uses a private Xvfb so nothing lands on your desktop.
  command -v Xvfb >/dev/null 2>&1 || { echo "Xvfb required for --verify" >&2; exit 1; }
  DISP=":$(( (RANDOM % 400) + 100 ))"
  Xvfb "$DISP" -screen 0 1920x1080x24 -nolisten tcp >/dev/null 2>&1 &
  XPID=$!
  trap 'kill "$XPID" 2>/dev/null || true' EXIT
  sleep 1

  LOG_DIR="$PLAY_ROOT/out"; mkdir -p "$LOG_DIR"
  echo "==> verify boot (Xvfb $DISP), quitting after a few seconds"
  set +e
  HEADFUL=1 DISPLAY="$DISP" timeout --foreground -k 5 120 \
    bash -c '. "'"$HARNESS"'/lib/common.sh"; load_config
             INSTALL="'"$PLAY_INSTALL"'"; PREFIX="'"$PLAY_PREFIX"'"; HEADFUL=1
             launch_game -s GodotMonoModLoader.gd --audio-driver Dummy --quit-after 900' \
    >"$LOG_DIR/verify.log" 2>&1
  set -e

  GLOG="$(HEADFUL=1 PREFIX="$PLAY_PREFIX" godot_log 2>/dev/null || true)"
  if [ -n "$GLOG" ] && grep -qa "\[$MOD_ID\] initialized" "$GLOG"; then
    echo "==> OK"
    grep -a "\[$MOD_ID\]" "$GLOG" | tail -3 | sed 's/^/    /'
    echo "    loaded mods: $(grep -aoE 'Loading Mod: [A-Za-z.]+' "$GLOG" | sed 's/Loading Mod: //' | sort -u | tr '\n' ' ')"
    exit 0
  fi
  echo "==> FAILED: no '[$MOD_ID] initialized' in the game log" >&2
  [ -n "$GLOG" ] && echo "    log: $GLOG" >&2
  exit 1
fi

echo "==> launching Atomcraft with $MOD_ID on display ${DISPLAY:-:0}"
echo "    install: $PLAY_INSTALL"
echo "    prefix:  $PLAY_PREFIX  (saves here are separate from your real game)"
echo "    settings: <prefix>/.../app_userdata/Atomcraft/$MOD_ID.json"
[ "$DEBUG" = 1 ] && echo "    debug:    hold Alt to outline the cells around the cursor and name the one under it"
[ "$DEMO" = 1 ] && echo "    demo:     the catalogue is drawn where you spawn; hold Alt for the painter"
export DISPLAY="${DISPLAY:-:0}"
HEADFUL=1 launch_game -s GodotMonoModLoader.gd
