# Where this project's TestHarness comes from, and where its private test root lives.
#
# Sourced by build.sh, run-tests.sh, and play.sh so all three agree. Sets:
#
#   MOD_ID                 the mod id, also the suffix of the default test root
#   TEST_ROOT              exported; a root private to this project
#   ATOMCRAFT_HARNESS      the tooling: run-tests.sh, bootstrap.sh, lib/
#   ATOMCRAFT_HARNESS_ZIP  the pinned TestHarness.zip
#
# Two things, and neither of them is a development checkout. This project does not build the
# harness and does not read its sources: a tree someone is working in can be mid-edit and
# uncompilable, and when that happened it took a consuming project's tests down with it for
# reasons that had nothing to do with that project.
#
# ATOMCRAFT_HARNESS_ZIP supplies both the assembly these mods compile against and the harness
# mod the game loads, so the two can never disagree.
#
# Set them in the environment, in ./harness.conf here, or in the harness's own per-user config
# at $XDG_CONFIG_HOME/atomcraft-test/config. The environment wins. There are no path defaults:
# a path guessed from a sibling directory name goes stale silently the first time that
# directory is renamed, which is exactly how two mods in this tree ended up pointing at a
# TestHarness that had not been there for days.

MOD_ID="${MOD_ID:?lib/harness.sh needs MOD_ID set before sourcing}"

# Lowercased, for the cache path. A root shared between projects means one project's mod zips
# and results land where another is also running, so a failure caused here surfaces over there.
export TEST_ROOT="${TEST_ROOT:-$HOME/.cache/atomcraft-test-$(printf '%s' "$MOD_ID" | tr '[:upper:]' '[:lower:]')}"

_env_harness="${ATOMCRAFT_HARNESS:-}"
_env_zip="${ATOMCRAFT_HARNESS_ZIP:-}"
for _candidate in ./harness.conf "${XDG_CONFIG_HOME:-$HOME/.config}/atomcraft-test/config"; do
    [ -f "$_candidate" ] || continue
    # shellcheck disable=SC1090
    . "$_candidate"
    break
done
[ -n "$_env_harness" ] && ATOMCRAFT_HARNESS="$_env_harness"
[ -n "$_env_zip" ] && ATOMCRAFT_HARNESS_ZIP="$_env_zip"
HARNESS="${ATOMCRAFT_HARNESS:-}"
HARNESS_ZIP="${ATOMCRAFT_HARNESS_ZIP:-}"

require_harness() {
    if [ -z "$HARNESS" ] || [ -z "$HARNESS_ZIP" ]; then
        cat >&2 <<EOF
error: this project does not guess where the TestHarness is, and does not build it. Point it
at a release of https://github.com/sparr/atomcraft-mod-TestHarness:

    ATOMCRAFT_HARNESS=/path/to/testharness-release      # the tooling: run-tests.sh, bootstrap.sh, lib/
    ATOMCRAFT_HARNESS_ZIP=/path/to/TestHarness.zip      # the pinned mod, and the assembly to build against

Put both lines in ./harness.conf, which is gitignored. See harness.conf.example. Do not point
either at a tree you are developing the harness in: a mid-edit checkout will fail this
project's tests for reasons that have nothing to do with this project.
EOF
        exit 1
    fi

    for _script in run-tests.sh bootstrap.sh lib/common.sh; do
        [ -e "$HARNESS/$_script" ] || {
            echo "error: ATOMCRAFT_HARNESS=$HARNESS has no $_script. It should be an extracted" \
                 "release source archive, or a checkout parked on a release tag." >&2
            exit 1
        }
    done
    [ -f "$HARNESS_ZIP" ] || { echo "error: no file at ATOMCRAFT_HARNESS_ZIP=$HARNESS_ZIP" >&2; exit 1; }
}

# Provision the private root once, delegating to the harness's own --seed-from rather than
# reimplementing the copy. The harness knows whether it managed to hardlink and says so; four
# separate hand-rolled copies of this block did not, and drifted.
#
# Note for a root that already exists: the harness discovers tests by scanning every loaded
# assembly, so any stale mod zip left in $TEST_ROOT/install/Mods is still loaded and can take a
# run down whatever the filter says. Clear the Mods directory if a run fails before any test does.
seed_test_root() {
    [ -d "$TEST_ROOT/install" ] && return 0
    local shared="${SHARED_TEST_ROOT:-$HOME/.cache/atomcraft-test}"
    if [ -d "$shared/install" ]; then
        echo "==> seeding $TEST_ROOT from $shared"
        "$HARNESS/bootstrap.sh" --seed-from "$shared"
    else
        echo "==> no patched game at $shared; running the harness bootstrap"
        "$HARNESS/bootstrap.sh"
    fi
}

# The assembly these mods compile against, taken out of the same zip the game will load. Doing
# it from the zip rather than from a build output is what makes "compiled against" and "ran
# against" the same bytes by construction rather than by habit.
extract_harness_assembly() {
    mkdir -p "$TEST_ROOT/harness"
    unzip -o -j "$HARNESS_ZIP" '*/Atomcraft.TestHarness.dll' -d "$TEST_ROOT/harness" >/dev/null
    echo "==> harness: $HARNESS_ZIP"
}
