#!/usr/bin/env bash
# Checks on the web frontend (src/ReadingTracker.Web) that the .NET build cannot make, run by
# .githooks/pre-commit and by CI. Each one is a review finding that kept coming back:
#
#   - declarations the codebase has dropped, and why (a reviewer found -webkit-box-reflect back
#     in PR #204 after it had been replaced by markup in FlowStage);
#   - a 1000-line budget per source file, measured against where the branch left master: a file
#     may not cross it, and a file already past it may grow by at most $allowance lines in a
#     branch. Splitting such a file is the way to make room. Only files the branch changes are
#     measured, and a moved file is measured against where it came from.
#
# Run from anywhere: bash tools/check-web.sh
set -u

cd "$(git rev-parse --show-toplevel)" || exit 1

web=src/ReadingTracker.Web
files=("$web/*.cs" "$web/*.razor" "$web/*.js" "$web/*.css" "$web/*.html")
budget=1000
allowance=50

failed=0
fail() { echo "check-web: $*" >&2; failed=1; }

# A declaration the codebase has dropped: its name, an extended regex for using it (the CSS
# declaration, or the property named in quotes from JS) that a comment naming it does not match,
# and the reason, said where the check fails.
dropped() { # name regex reason
    local hits
    hits=$(git grep -n -E -e "$2" -- "${files[@]}")
    [ -n "$hits" ] && fail "$1 is dropped: $3"$'\n'"$hits"
}

dropped -webkit-box-reflect "-webkit-box-reflect([[:space:]]*:|['\"][[:space:]]*[:,])" \
    'Firefox never implemented it and mobile browsers drop it; the reflection is a flipped copy in Components/FlowStage.razor'

# The line budget.
base=$(git merge-base HEAD origin/master 2>/dev/null)
if [ -z "$base" ]; then
    fail "no merge-base with origin/master to measure file sizes against: run git fetch origin (CI needs fetch-depth: 0)."
else
    # Each changed file once, with the lines added and removed since the base. A rename's record
    # has an empty path, and the old and new paths follow it.
    while IFS=$'\t' read -r -d '' added removed path; do
        if [ -z "$path" ]; then
            IFS= read -r -d '' _ && IFS= read -r -d '' path
        fi
        [ "$added" = - ] && continue # binary
        [ -f "$path" ] || continue   # deleted
        lines=$(wc -l < "$path")
        [ "$lines" -le "$budget" ] && continue
        before=$((lines - added + removed))
        if [ "$before" -le "$budget" ]; then
            fail "$path is $lines lines, past the $budget-line budget: split it rather than grow it."
        elif [ $((lines - before)) -gt "$allowance" ]; then
            fail "$path grew from $before to $lines lines since ${base:0:7}; a file already past $budget may grow by $allowance at most, so move code out of it. (If that growth came from master, not this branch, origin/master is stale: git fetch origin.)"
        fi
    done < <(git -c core.quotePath=false diff -M --numstat -z "$base" -- "${files[@]}")
fi

[ "$failed" -eq 0 ] && echo "check-web: clean."
exit "$failed"
