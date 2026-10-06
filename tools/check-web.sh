#!/usr/bin/env bash
# Checks for the web frontend that the .NET build cannot make, run by .githooks/pre-commit and by
# CI. Each one is a review finding that kept coming back:
#
#   - patterns the codebase has dropped, and why (a reviewer found -webkit-box-reflect back in
#     PR #204 after it had been replaced by markup in FlowStage);
#   - a line budget per source file, so a file past 1000 lines is split rather than grown.
#
# Run from anywhere: bash tools/check-web.sh
set -u

cd "$(git rev-parse --show-toplevel)" || exit 1

failed=0
fail() { echo "check-web: $*" >&2; failed=1; }

# Dropped patterns: "regex|the reason, said where the reader is". Comments that mention a pattern
# to say it is gone are allowed: a line whose match sits after "used to be" is not counted.
dropped=(
    '-webkit-box-reflect|Firefox never implemented it and mobile browsers drop it; the reflection is a flipped copy in Components/FlowStage.razor'
)

for entry in "${dropped[@]}"; do
    pattern=${entry%%|*}
    reason=${entry#*|}
    hits=$(git grep -n -e "$pattern" -- 'src/ReadingTracker.Web/*.css' 'src/ReadingTracker.Web/*.razor' \
        'src/ReadingTracker.Web/*.js' 'src/ReadingTracker.Web/*.html' | grep -v 'used to be' || true)
    [ -n "$hits" ] && fail "$pattern is dropped: $reason"$'\n'"$hits"
done

# Line budget. A file already past it may not grow past its ceiling here; lower the ceiling when
# a file shrinks, and delete its line when it is back under the budget.
budget=1000
declare -A ceiling=(
    [src/ReadingTracker.Web/wwwroot/css/app.css]=6696
    [src/ReadingTracker.Web/Pages/Home.razor]=1225
)

while IFS= read -r file; do
    lines=$(wc -l < "$file")
    limit=${ceiling[$file]:-$budget}
    if [ "$lines" -gt "$limit" ]; then
        if [ "$limit" -eq "$budget" ]; then
            fail "$file is $lines lines, past the $budget-line budget: split it rather than grow it."
        else
            fail "$file is $lines lines, past its ceiling of $limit: it is already too big, so move code out of it rather than into it."
        fi
    fi
done < <(git ls-files 'src/*.cs' 'src/*.razor' 'src/*.js' 'src/*.css')

[ "$failed" -eq 0 ] && echo "check-web: clean."
exit "$failed"
