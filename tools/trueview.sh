#!/usr/bin/env bash
# dev PC check (not part of the product): open a DWG/DXF with the console engine of an installed DWG TrueView (accoreconsole.exe), switch to paper space and back, print what it reports
# usage: tools/trueview.sh file [timeout]
TV="/c/Program Files/Autodesk/DWG TrueView 2027 - English"
f="$(cygpath -w "$(realpath "$1")")"
s="$(cygpath -w "$(realpath "${TV_SCRIPT:-$(dirname "$0")/trueview_quit.scr}")")"
MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' timeout "${2:-90}" "$TV/accoreconsole.exe" /i "$f" /s "$s" /l en-US 2>&1 | tr -d '\000' | grep -v -E "^\s*$|CoreHeartBeat" | tail -25
echo "exit ${PIPESTATUS[0]}"
