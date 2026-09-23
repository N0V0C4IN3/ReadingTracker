#!/usr/bin/env bash
# Stop hook: takes the unused usings out of every .cs file changed since the last commit.
#
# `dotnet format` loads a whole project to do it — about 18s a project, 2.5 minutes for the
# solution — so it runs per project and only over the files that changed, in the background
# (the hook is async), and not at all when those files are as it last left them.
set -u

cd "${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel)}" || exit 0

git_dir=$(git rev-parse --absolute-git-dir) || exit 0
lock="$git_dir/claude-usings.lock"
stamp="$git_dir/claude-usings.stamp"

# Changed or new .cs files, tracked or not, still on disk.
mapfile -t files < <(
    { git diff --name-only --diff-filter=ACMR HEAD -- '*.cs'; git ls-files --others --exclude-standard -- '*.cs'; } |
        sort -u | while read -r f; do [ -f "$f" ] && printf '%s\n' "$f"; done
)
[ ${#files[@]} -eq 0 ] && exit 0

fingerprint() { git hash-object -- "${files[@]}" | git hash-object --stdin; }

[ -f "$stamp" ] && [ "$(cat "$stamp")" = "$(fingerprint)" ] && exit 0

# One run at a time: a turn that ends while the last run is still going leaves it to finish.
mkdir "$lock" 2>/dev/null || exit 0
trap 'rmdir "$lock"' EXIT

# Each file under the nearest project above it.
declare -A by_project
for f in "${files[@]}"; do
    dir=$(dirname "$f")
    while [ "$dir" != "." ] && [ -z "$(ls "$dir"/*.csproj 2>/dev/null)" ]; do dir=$(dirname "$dir"); done
    project=$(ls "$dir"/*.csproj 2>/dev/null | head -1)
    [ -n "$project" ] && by_project["$project"]+="$f"$'\n'
done

for project in "${!by_project[@]}"; do
    mapfile -t included < <(printf '%s' "${by_project[$project]}")
    dotnet format style "$project" --diagnostics IDE0005 --severity warn --include "${included[@]}" >/dev/null 2>&1
done

fingerprint > "$stamp"
