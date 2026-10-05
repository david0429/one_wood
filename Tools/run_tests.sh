#!/usr/bin/env bash
# Runs the Unity EditMode tests in batch mode and prints a summary.
# Usage: Tools/run_tests.sh [extra Unity args, e.g. -testFilter ShotValidatorTests]
# Set UNITY_EDITOR to override the editor path. The project must not be open in the editor.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO/OneWood"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/$VERSION/Editor/Unity}"
OUT="$PROJECT/Logs/tests"
RESULTS="$OUT/editmode-results.xml"
LOG="$OUT/editmode.log"

[[ -x "$UNITY" ]] || { echo "Unity $VERSION not found at $UNITY (set UNITY_EDITOR)" >&2; exit 2; }
mkdir -p "$OUT"
rm -f "$RESULTS"

echo "Running EditMode tests with Unity $VERSION..."
set +e
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode -testResults "$RESULTS" -logFile "$LOG" "$@"
CODE=$?
set -e

if [[ ! -f "$RESULTS" ]]; then
  echo "No test results produced (exit $CODE). Compiler errors or another editor instance? See $LOG:" >&2
  grep -E "error CS|Multiple Unity instances|another Unity instance" "$LOG" | head -20 >&2 || true
  exit "${CODE:-1}"
fi

python3 - "$RESULTS" <<'EOF'
import sys, xml.etree.ElementTree as ET
run = ET.parse(sys.argv[1]).getroot()
a = run.attrib
print(f"Result: {a.get('result')}  total {a.get('total')}  passed {a.get('passed')}  failed {a.get('failed')}  skipped {a.get('skipped')}  ({float(a.get('duration', 0)):.1f}s)")
for case in run.iter("test-case"):
    if case.get("result") == "Failed":
        msg = case.find("failure/message")
        print(f"  FAILED {case.get('fullname')}\n    {(msg.text or '').strip()[:500]}")
EOF
echo "Results: $RESULTS"
exit "$CODE"
