#!/usr/bin/env bash
# Build this project's mods and run them against a test root private to this project.
#
#   ./run-tests.sh                 this project's tests. The everyday loop.
#   ./run-tests.sh --retirement    instead: the suite that asks whether the game is still broken
#   ./run-tests.sh --all           everything installed in the test root, no filter at all
#   ./run-tests.sh -- --atomtest-bench            the measurements
#   ./run-tests.sh -- --atomtest-filter=Counter      an explicit filter wins over all three
#
# Why the default excludes the retirement suite: those tests assert that the *unmodified* game
# still has each defect this mod exists for, so a failure there is good news and means a part of
# the mod can be retired. That is a different question from whether the mod is correct, and
# mixing the two makes a red suite unreadable -- which is exactly what happened to RNGTick when
# the 2026-09-12 game build fixed the defect it was working around. See test/RetirementTests.cs.
#
# The default filter also selects only this project's assemblies. The harness discovers tests by
# scanning every loaded assembly, so without it a stale mod zip left in the test root's Mods
# directory joins the run and can fail it for reasons that have nothing to do with this project.
set -euo pipefail
cd "$(dirname "$0")"

MOD_ID="PixelArt"
# shellcheck disable=SC1091
. lib/harness.sh
require_harness

# This project's own tests: the mod's suite, and the conformance suite that names no mod. The
# harness matches --atomtest-filter as an unanchored regex over the assembly-qualified test name,
# so anchoring keeps it from also selecting a harness test that happens to mention the mod.
MINE='^(PixelArt|PixelArtConformance)\.'
RETIREMENT='^PixelArt\.Test\.RetirementTests\.'

ARGS=(); ALL=0; ONLY_RETIREMENT=0; HAS_FILTER=0; HAS_SEPARATOR=0
for arg in "$@"; do
    case "$arg" in
        --all)                  ALL=1 ;;
        --retirement)           ONLY_RETIREMENT=1 ;;
        --atomtest-filter=*)    HAS_FILTER=1; ARGS+=("$arg") ;;
        --)                     HAS_SEPARATOR=1; ARGS+=("$arg") ;;
        *)                      ARGS+=("$arg") ;;
    esac
done

if [ "$HAS_FILTER" = 0 ] && [ "$ALL" = 0 ]; then
    # Everything after the harness's own -- goes to the game, and a second -- would be passed
    # along as a game argument rather than starting a new group, so append into the existing one.
    [ "$HAS_SEPARATOR" = 1 ] || ARGS+=(--)
    if [ "$ONLY_RETIREMENT" = 1 ]; then
        ARGS+=("--atomtest-filter=$RETIREMENT")
        echo "==> asking whether the game is still broken; a failure here means something can be retired"
    else
        ARGS+=("--atomtest-filter=$MINE" "--atomtest-exclude=$RETIREMENT")
        echo "==> running this project's tests; ./run-tests.sh --retirement for the other question"
    fi
fi

seed_test_root
extract_harness_assembly

./build.sh --install
exec "$HARNESS/run-tests.sh" --no-build --mod "$HARNESS_ZIP" ${ARGS[@]+"${ARGS[@]}"}
