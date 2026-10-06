#!/usr/bin/env bash
# Checks on the web frontend (src/ReadingTracker.Web) that the .NET build cannot make, run by
# .githooks/pre-commit and by CI. Each one is a review finding that kept coming back:
#
#   - declarations the codebase has dropped, and why (a reviewer found -webkit-box-reflect back
#     in PR #204 after it had been replaced by markup in FlowStage);
#   - a 1000-line budget per source file, measured against master: a file may not cross it, and
#     a file already past it (app.css, Home.razor) may grow by at most $allowance lines in a
#     branch. Splitting such a file is the way to make room; its new pieces are measured fresh.
#
# Run from anywhere: bash tools/check-web.sh
set -u

cd "$(git rev-parse --show-toplevel)" || exit 1

web=src/ReadingTracker.Web
budget=1000
allowance=50

failed=0
fail() { echo "check-web: $*" >&2; failed=1; }

# Dropped declarations, as extended regexes, each with the reason said where it fails. Match the
# declaration (name and colon), so a comment that names it to say it is gone is not caught.
patterns=('-webkit-box-reflect[[:space:]]*:')
reasons=('Firefox never implemented it and mobile browsers drop it; the reflection is a flipped copy in Components/FlowStage.razor')

for i in "${!patterns[@]}"; do
    hits=$(git grep -n -E -e "${patterns[$i]}" -- "$web/*.css" "$web/*.razor" "$web/*.js" "$web/*.html")
    [ -n "$hits" ] && fail "${patterns[$i]} is dropped: ${reasons[$i]}"$'\n'"$hits"
done

# The line budget, against where this branch left master. With no master to measure against, a
# file past the budget is reported but not failed: there is no telling whether this branch grew it.
base=$(git merge-base HEAD origin/master 2>/dev/null || git merge-base HEAD master 2>/dev/null)

while IFS= read -r -d '' file; do
    [ -f "$file" ] || continue
    lines=$(wc -l < "$file")
    [ "$lines" -le "$budget" ] && continue
    if [ -z "$base" ]; then
        echo "check-web: $file is $lines lines (no master to compare with, so not failed)." >&2
        continue
    fi
    before=$(git show "$base:$file" 2>/dev/null | wc -l)
    if [ "$before" -le "$budget" ]; then
        fail "$file is $lines lines, past the $budget-line budget: split it rather than grow it."
    elif [ $((lines - before)) -gt "$allowance" ]; then
        fail "$file grew from $before to $lines lines; a file already past $budget may grow by $allowance at most. Move code out of it (a new file is measured fresh)."
    fi
done < <(git -c core.quotePath=false ls-files -z -- "$web/*.cs" "$web/*.razor" "$web/*.js" "$web/*.css" "$web/*.html")

[ "$failed" -eq 0 ] && echo "check-web: clean."
exit "$failed"
