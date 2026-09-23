#!/usr/bin/env bash
# SessionStart hook, and after `gh pr merge`: deletes the local branches already merged into
# origin/master, and the remote-tracking refs of branches deleted on GitHub.
#
# `git branch -d` only, never -D: it refuses a branch with commits master does not have, so an
# unmerged branch (a prototype, work in progress) survives even if it is somehow listed here.
# master and the branch checked out are never touched.
set -u

cd "${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel)}" || exit 0

git fetch --prune --quiet origin 2>/dev/null || exit 0

current=$(git rev-parse --abbrev-ref HEAD)
deleted=()

while read -r branch; do
    [ "$branch" = master ] || [ "$branch" = "$current" ] && continue
    git branch -d "$branch" >/dev/null 2>&1 && deleted+=("$branch")
done < <(git for-each-ref --format='%(refname:short)' --merged origin/master refs/heads)

if [ ${#deleted[@]} -gt 0 ]; then
    printf '{"systemMessage": "Deleted merged branches: %s"}\n' "${deleted[*]}"
fi
