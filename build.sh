#!/usr/bin/env bash
# Build this project's mods, and optionally stage them where the harness's test game will
# load them.
#
#   ./build.sh [--install] [--release]
#
# The harness documents its own build-mod.sh for this, and it supplies the same properties.
# This exists for two reasons. Its restore step falls through to the configured package
# sources, which can stall for many minutes on a machine with poor reach to nuget.org even
# though every package needed is already in the local cache; restoring with no sources at all
# uses the global packages folder and nothing else, which is instant when the cache is warm and
# an immediate, legible error when it is not. And it keeps this project off the harness's
# scripts for building, so a harness checkout being mid-edit cannot break a build here.
set -euo pipefail
cd "$(dirname "$0")"

MOD_ID="PixelArt"
# shellcheck disable=SC1091
. lib/harness.sh

# Debug by default, because that is what you are running while you work and a stack trace with
# line numbers is worth more than the speed. Release is what ships: a Debug assembly carries
# DebuggableAttribute with DisableOptimizations, which turns the JIT off for it entirely --
# nothing is inlined, locals are not enregistered, and RNGTick measured its postfix at four
# times the Release cost. Cut every release with --release, and never publish a zip built
# without it.
CONFIG="Debug"

INSTALL_IT=0
for arg in "$@"; do
    case "$arg" in
        --install) INSTALL_IT=1 ;;
        --release) CONFIG="Release" ;;
        *) echo "unknown argument: $arg (expected --install or --release)" >&2; exit 1 ;;
    esac
done

GAME_DIR="${GAME_DIR:-$HOME/Games/Steam/steamapps/common/Atomcraft}"
INSTALL="${INSTALL:-$TEST_ROOT/install}"

ARGS=(-p:GameInstallDir="$GAME_DIR"
      -p:AppData="$TEST_ROOT/scratch-appdata"
      -p:TestHarnessDir="$TEST_ROOT/harness")
[ "$INSTALL_IT" = 1 ] && ARGS+=(-p:TestInstallDir="$INSTALL")

OFFLINE_CONFIG="$(mktemp)"
trap 'rm -f "$OFFLINE_CONFIG"' EXIT
cat > "$OFFLINE_CONFIG" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
  </packageSources>
</configuration>
XML

# The shipped mod and the demo, which depends only on it. Neither needs the harness, so both
# build in a checkout that has never run the suite.
PROJECTS=(src/PixelArt.csproj demo/PixelArt.Demo.csproj)
# The test mods need the harness assembly. Skip them rather than fail when it is not there.
if [ -f "$TEST_ROOT/harness/Atomcraft.TestHarness.dll" ]; then
    PROJECTS+=(test/PixelArt.Test.csproj conformance/PixelArtConformance.csproj)
else
    echo "==> no harness assembly under $TEST_ROOT/harness; skipping the test mod(s)"
fi

echo "==> configuration: $CONFIG"
for project in "${PROJECTS[@]}"; do
    echo "==> $project"
    nice -n 19 dotnet restore "$project" --configfile "$OFFLINE_CONFIG" \
        -p:Configuration="$CONFIG" "${ARGS[@]}" >/dev/null
    nice -n 19 dotnet build "$project" --no-restore -v q --nologo -c "$CONFIG" "${ARGS[@]}"
done
