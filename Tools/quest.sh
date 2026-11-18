#!/usr/bin/env bash
# Quest helper for Worldeck. Run from the repo root in Git Bash.
#
#   Tools/quest.sh devices          list connected headsets (accept the USB prompt inside the headset first)
#   Tools/quest.sh install          install Builds/Worldeck-dev.apk (replaces any previous build)
#   Tools/quest.sh install-judge    install Builds/Worldeck-judge.apk (the release build judges get)
#   Tools/quest.sh fresh            uninstall first (first-launch experience, progress cleared), then install the judge build
#   Tools/quest.sh run              launch the app
#   Tools/quest.sh log              follow the app's [Worldeck] lines and errors, Ctrl+C to stop
#   Tools/quest.sh pull             copy play logs to Tools/sessions/
#   Tools/quest.sh stats            pull, then run Tools/stats.py (the proof card numbers)
#   Tools/quest.sh timeline [--errors] [--area=Voice,AI]   pull, then print the newest run as a readable timeline
#   Tools/quest.sh shot             save a headset screenshot to Tools/shots/
#   Tools/quest.sh videos           copy the headset's recordings (Camera > Record video) to Recordings/ (not in git)
#   Tools/quest.sh perms            show the installed app's permissions
set -euo pipefail

ADB="${ADB:-/c/Unity/Editors/6000.0.85f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
PKG="com.ujwal240.worldeck"
APK="Builds/Worldeck-dev.apk"
FILES="/sdcard/Android/data/$PKG/files"
export MSYS_NO_PATHCONV=1

case "${1:-}" in
  devices)       "$ADB" devices -l ;;
  install)       "$ADB" install -r -g "$APK" ;;
  install-judge) "$ADB" install -r -g Builds/Worldeck-judge.apk ;;
  fresh)         "$ADB" uninstall "$PKG" >/dev/null 2>&1 || true; "$ADB" install -g Builds/Worldeck-judge.apk ;;
  run)           "$ADB" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null && echo "launched $PKG" ;;
  log)           "$ADB" logcat -c; "$ADB" logcat -v time Unity:V '*:S' | grep --line-buffered -E "Worldeck|Exception|Error" ;;
  pull)          mkdir -p Tools/sessions; "$ADB" pull "$FILES/sessions/." Tools/sessions/ && ls -l Tools/sessions | tail -n +2 ;;
  stats)         "$0" pull; python -I Tools/stats.py Tools/sessions ;;
  timeline)      "$0" pull >/dev/null; python -I Tools/timeline.py Tools/sessions "${@:2}" ;;
  shot)          mkdir -p Tools/shots; f="Tools/shots/shot-$(date +%Y%m%d-%H%M%S).png"; "$ADB" exec-out screencap -p > "$f" && echo "$f" ;;
  perms)         "$ADB" shell dumpsys package "$PKG" | sed -n '/requested permissions/,/install permissions/p' ;;
  videos)        mkdir -p Recordings; "$ADB" pull /sdcard/Oculus/VideoShots/. Recordings/ && ls -l Recordings | tail -n +2 ;;
  *) sed -n '2,13p' "$0"; exit 1 ;;
esac
