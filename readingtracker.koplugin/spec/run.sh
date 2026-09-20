#!/usr/bin/env sh
# Runs the plugin's busted suite the way CI does, on LuaJIT, without installing Lua locally.
# Usage: spec/run.sh [busted args]
set -e
cd "$(dirname "$0")/.."
docker image inspect readingtracker-busted >/dev/null 2>&1 || docker build -q -t readingtracker-busted -f spec/busted.Dockerfile spec

# Git Bash on Windows rewrites a "/plugin" argument into a Windows path unless told not to, and
# wants the host side spelled with a drive letter.
case "$(uname -s)" in
  MINGW*|MSYS*) here="$(pwd -W)" ;;
  *) here="$(pwd)" ;;
esac

MSYS_NO_PATHCONV=1 exec docker run --rm -v "$here:/plugin" readingtracker-busted "$@"
