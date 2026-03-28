#!/bin/bash
# Wine wrapper for IntuneWinAppUtil.exe on Linux
# Usage: run-intunewin.sh -c <source> -s <source> -o <output> -q
exec wine /app/tools/IntuneWinAppUtil.exe "$@"
