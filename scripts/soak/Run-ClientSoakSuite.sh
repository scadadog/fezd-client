#!/usr/bin/env bash
# Overnight single-client FEZD soak suite driven by fezd-client (macOS / Linux).
#
# Simulates one client hitting the gateway with successive deploy --simulator jobs:
#   1) Steady Monte Carlo (random inter-job gaps, small/large rotation)
#   2) Interrupt + recover (cancel mid-session, then full prove deploy)
#   3) Cancel storm (N mid-cancels, then M full deploys)
#
# From dist/performance-testing (auto-finds ../fezd-client + ../nextera-remote-client.fezd.env):
#   ./Run-ClientSoakSuite.sh --app-password '***' --what-if
#   ./Run-ClientSoakSuite.sh --app-password '***' --family M340 --phases Steady --steady-trials 10
#   ./Run-ClientSoakSuite.sh --app-password '***' --family All --phases All
#
# Prerequisite: fezd-server serve is running on the gateway.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PARENT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

CONNECTION=""
PROJECTS_FILE=""
CLIENT_PATH=""
OUT_ROOT=""
APP_PASSWORD="${FEZD_APP_PASSWORD:-}"
FAMILY="All"
PHASES="All"
SEED=42
DELAY_MIN=1
DELAY_MAX=120
STEADY_TRIALS=60
STEADY_DURATION=28800
INTERRUPT_CYCLES=20
STORM_CANCELS=10
STORM_PROVE=5
MAX_FAILURES=5
SKIP_HEALTH=0
STRICT=0
WHAT_IF=0

usage() {
  cat <<'EOF'
Usage: ./Run-ClientSoakSuite.sh [options]

  --connection <file>     .fezd.env (default: auto-detect in dist/)
  --projects-file <file>  projects.manifest.json (default: next to script)
  --client <path>         fezd-client binary (default: auto-detect)
  --out-root <dir>        suite output root (default: ./suite-runs)
  --app-password <pwd>    or set FEZD_APP_PASSWORD
  --family All|M340|M580  (default All)
  --phases All|Steady|Interrupt|Storm  (default All)
  --seed <n>              (default 42)
  --delay-min <sec>       (default 1)
  --delay-max <sec>       (default 120)
  --steady-trials <n>     (default 60)
  --steady-duration <sec> (default 28800)
  --interrupt-cycles <n>  (default 20)
  --storm-cancels <n>     (default 10)
  --storm-prove <n>       (default 5)
  --max-failures <n>      (default 5; 0 = never stop)
  --skip-health
  --strict                abort suite if a phase has any fail
  --what-if               resolve paths and exit
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --connection) CONNECTION="$2"; shift 2 ;;
    --projects-file) PROJECTS_FILE="$2"; shift 2 ;;
    --client|--client-path) CLIENT_PATH="$2"; shift 2 ;;
    --out-root) OUT_ROOT="$2"; shift 2 ;;
    --app-password) APP_PASSWORD="$2"; shift 2 ;;
    --family) FAMILY="$2"; shift 2 ;;
    --phases) PHASES="$2"; shift 2 ;;
    --seed) SEED="$2"; shift 2 ;;
    --delay-min) DELAY_MIN="$2"; shift 2 ;;
    --delay-max) DELAY_MAX="$2"; shift 2 ;;
    --steady-trials) STEADY_TRIALS="$2"; shift 2 ;;
    --steady-duration) STEADY_DURATION="$2"; shift 2 ;;
    --interrupt-cycles) INTERRUPT_CYCLES="$2"; shift 2 ;;
    --storm-cancels) STORM_CANCELS="$2"; shift 2 ;;
    --storm-prove) STORM_PROVE="$2"; shift 2 ;;
    --max-failures) MAX_FAILURES="$2"; shift 2 ;;
    --skip-health) SKIP_HEALTH=1; shift ;;
    --strict) STRICT=1; shift ;;
    --what-if) WHAT_IF=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

die() { echo "ERROR: $*" >&2; exit 1; }

resolve_client() {
  local hint="${1:-}"
  local c
  for c in \
    "$hint" \
    "$SCRIPT_DIR/fezd-client" \
    "$PARENT_DIR/fezd-client" \
    "$PARENT_DIR/dist/fezd-client" \
    "$(command -v fezd-client 2>/dev/null || true)"
  do
    [[ -n "$c" && -x "$c" ]] && { echo "$(cd "$(dirname "$c")" && pwd)/$(basename "$c")"; return 0; }
  done
  die "fezd-client not found. Pass --client or place it in dist/."
}

resolve_connection() {
  local hint="${1:-}"
  local c
  for c in \
    "$hint" \
    "$SCRIPT_DIR/nextera-remote-client.fezd.env" \
    "$PARENT_DIR/nextera-remote-client.fezd.env" \
    "$PARENT_DIR/dist/nextera-remote-client.fezd.env"
  do
    [[ -n "$c" && -f "$c" ]] && { echo "$(cd "$(dirname "$c")" && pwd)/$(basename "$c")"; return 0; }
  done
  # any *.fezd.env in parent
  local hit
  hit="$(ls -1 "$PARENT_DIR"/*.fezd.env 2>/dev/null | head -1 || true)"
  [[ -n "$hit" && -f "$hit" ]] && { echo "$(cd "$(dirname "$hit")" && pwd)/$(basename "$hit")"; return 0; }
  die "Connection file not found. Pass --connection or place a *.fezd.env in dist/."
}

resolve_path() {
  local raw="$1" base="$2"
  if [[ "$raw" = /* && -e "$raw" ]]; then echo "$raw"; return 0; fi
  local cand
  for cand in "$base/$raw" "$SCRIPT_DIR/$raw" "$PARENT_DIR/$raw"; do
    if [[ -e "$cand" ]]; then
      echo "$(cd "$(dirname "$cand")" && pwd)/$(basename "$cand")"
      return 0
    fi
  done
  echo "$(cd "$base" 2>/dev/null && pwd)/$raw"
}

utc_stamp() { date -u +"%Y%m%dT%H%M%SZ"; }
utc_now() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }

csv_escape() {
  local v="${1:-}"
  if [[ "$v" == *[,\"$'\n'$'\r']* ]]; then
    v="${v//\"/\"\"}"
    printf '"%s"' "$v"
  else
    printf '%s' "$v"
  fi
}

json_escape() {
  python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()[:-1] if False else sys.argv[1]))' "$1" 2>/dev/null \
    || { local s="$1"; s="${s//\\/\\\\}"; s="${s//\"/\\\"}"; s="${s//$'\n'/\\n}"; printf '"%s"' "$s"; }
}

extract_session_id() {
  # [INFO] Session <id> accepted ...
  printf '%s' "$1" | grep -Eo 'Session[[:space:]]+[A-Za-z0-9-]+[[:space:]]+accepted' \
    | head -1 | awk '{print $2}' || true
}

extract_server_version() {
  printf '%s' "$1" | grep -Eo 'server version[[:space:]]*:[[:space:]]*[^[:space:]]+' \
    | head -1 | awk -F: '{gsub(/^[[:space:]]+/,"",$2); print $2}' || true
}

CLIENT_VERSION="unknown"
SERVER_VERSION=""
GLOBAL_TRIAL=0
RNG_STATE="$SEED"

# Simple LCG for portable stratified gaps (no $RANDOM bias across shells).
rng_next() {
  RNG_STATE=$(( (RNG_STATE * 1103515245 + 12345) & 0x7fffffff ))
  echo "$RNG_STATE"
}

gap_sample() {
  local trial="$1"
  local span
  span="$(python3 -c "print(max(0.0, float('$DELAY_MAX')-float('$DELAY_MIN')))")"
  local bucket=$(( (trial - 1) % 3 ))
  local strata=(early mid late)
  local stratum="${strata[$bucket]}"
  local lo hi gap
  lo="$(python3 -c "print(float('$DELAY_MIN') + float('$span') * $bucket / 3.0)")"
  hi="$(python3 -c "print(float('$DELAY_MIN') + float('$span') * ($bucket + 1) / 3.0)")"
  local r
  r="$(rng_next)"
  gap="$(python3 -c "print(round(float('$lo') + (($r % 10000)/10000.0) * (float('$hi')-float('$lo')), 3))")"
  echo "$gap|$stratum"
}

CLIENT="$(resolve_client "$CLIENT_PATH")"
CONNECTION="$(resolve_connection "$CONNECTION")"

if [[ -z "$PROJECTS_FILE" ]]; then
  PROJECTS_FILE="$SCRIPT_DIR/projects.manifest.json"
fi
[[ -f "$PROJECTS_FILE" ]] || die "Projects file not found: $PROJECTS_FILE"
PROJECTS_FILE="$(cd "$(dirname "$PROJECTS_FILE")" && pwd)/$(basename "$PROJECTS_FILE")"
PROJECTS_BASE="$(dirname "$PROJECTS_FILE")"

if [[ -z "$OUT_ROOT" ]]; then
  OUT_ROOT="$SCRIPT_DIR/suite-runs"
fi
mkdir -p "$OUT_ROOT"
OUT_ROOT="$(cd "$OUT_ROOT" && pwd)"

CLIENT_VERSION="$("$CLIENT" --version 2>&1 | head -1 | awk '{print $2}' || echo unknown)"

STAMP="$(utc_stamp)"
SUITE_DIR="$OUT_ROOT/suite-$STAMP"
mkdir -p "$SUITE_DIR"

# Load projects via python into a temp TSV: label|family|sizeClass|path|bytes|sizeMb|expected|cancelAt
PROJECTS_TSV="$SUITE_DIR/projects.tsv"
FAMILY_FILTER="$FAMILY" PROJECTS_FILE_ENV="$PROJECTS_FILE" PROJECTS_BASE_ENV="$PROJECTS_BASE" \
SCRIPT_DIR_ENV="$SCRIPT_DIR" PARENT_DIR_ENV="$PARENT_DIR" python3 - <<'PY' > "$PROJECTS_TSV"
import json, os, sys
from pathlib import Path

manifest = Path(os.environ["PROJECTS_FILE_ENV"])
base = Path(os.environ["PROJECTS_BASE_ENV"])
family = os.environ["FAMILY_FILTER"]
script = Path(os.environ["SCRIPT_DIR_ENV"])
parent = Path(os.environ["PARENT_DIR_ENV"])
rows = json.loads(manifest.read_text())
out = []
for p in rows:
    if family != "All" and p.get("family") != family:
        continue
    raw = p["path"]
    candidates = []
    rp = Path(raw)
    if rp.is_absolute():
        candidates.append(rp)
    candidates += [base / raw, script / raw, parent / raw]
    resolved = None
    for c in candidates:
        try:
            if c.exists():
                resolved = c.resolve()
                break
        except OSError:
            pass
    if resolved is None:
        print(f"ERROR: project not found: {raw} label={p.get('label')}", file=sys.stderr)
        sys.exit(1)
    size = resolved.stat().st_size
    expected = int(p.get("expectedDeploySec") or 120)
    cancel = int(p.get("cancelAtSec") or max(15, int(expected * 0.4)))
    print("|".join([
        str(p.get("label") or resolved.stem),
        str(p.get("family") or ""),
        str(p.get("sizeClass") or ""),
        str(resolved),
        str(size),
        f"{size/1024/1024:.4f}",
        str(expected),
        str(cancel),
    ]))
PY

PROJECT_ROWS=()
while IFS= read -r line || [[ -n "$line" ]]; do
  [[ -n "$line" ]] && PROJECT_ROWS+=("$line")
done < "$PROJECTS_TSV"
[[ ${#PROJECT_ROWS[@]} -gt 0 ]] || die "No projects matched family=$FAMILY"

echo "FEZD client soak suite (bash)"
echo "  client      : $CLIENT ($CLIENT_VERSION)"
echo "  connection  : $CONNECTION"
echo "  out-dir     : $SUITE_DIR"
echo "  family      : $FAMILY"
echo "  phases      : $PHASES"
if [[ -n "$APP_PASSWORD" ]]; then
  echo "  app-password: set (${#APP_PASSWORD} chars)"
  case "$APP_PASSWORD" in
    your-pwd|YOUR_PWD|changeme|password|secret|'***'|'*')
      echo "WARNING: --app-password looks like a placeholder ('$APP_PASSWORD')." >&2
      echo "         Use the real Control Expert application password for these .ZEF files." >&2
      ;;
  esac
else
  echo "  app-password: NOT SET (pass --app-password or export FEZD_APP_PASSWORD)"
fi
echo "  projects    : ${#PROJECT_ROWS[@]}"
for row in "${PROJECT_ROWS[@]}"; do
  IFS='|' read -r label fam size path bytes sizeMb expected cancelAt <<<"$row"
  printf '    - %s [%s/%s] %s MB  cancelAt=%ss  %s\n' "$label" "$fam" "$size" "$sizeMb" "$cancelAt" "$path"
done

if [[ "$WHAT_IF" -eq 1 ]]; then
  echo "WhatIf: exiting before health / phases."
  exit 0
fi

if [[ "$SKIP_HEALTH" -eq 0 ]]; then
  echo "Running health..."
  set +e
  "$CLIENT" health --connection "$CONNECTION" >/tmp/fezd-soak-health.out 2>/tmp/fezd-soak-health.err
  HEALTH_EC=$?
  set -e
  HEALTH_OUT="$(cat /tmp/fezd-soak-health.out /tmp/fezd-soak-health.err 2>/dev/null || true)"
  echo "$HEALTH_OUT"
  [[ "$HEALTH_EC" -eq 0 ]] || die "Gateway health failed (exit $HEALTH_EC). Start fezd-server serve on the gateway."
  SERVER_VERSION="$(extract_server_version "$HEALTH_OUT")"
fi

python3 - <<PY > "$SUITE_DIR/suite.json"
import json
meta = {
  "stamp": "$STAMP",
  "startedUtc": "$(utc_now)",
  "clientPath": "$CLIENT",
  "clientVersion": "$CLIENT_VERSION",
  "serverVersion": "$SERVER_VERSION",
  "connection": "$CONNECTION",
  "family": "$FAMILY",
  "phases": "$PHASES",
  "seed": $SEED,
  "delayMinSec": $DELAY_MIN,
  "delayMaxSec": $DELAY_MAX,
  "steadyTrials": $STEADY_TRIALS,
  "steadyDurationSec": $STEADY_DURATION,
  "interruptCycles": $INTERRUPT_CYCLES,
  "stormCancels": $STORM_CANCELS,
  "stormProve": $STORM_PROVE,
  "maxFailures": $MAX_FAILURES,
  "projects": [],
  "phaseResults": []
}
print(json.dumps(meta, indent=2))
PY

# Append projects into suite.json
python3 - <<PY
import json
from pathlib import Path
suite = Path("$SUITE_DIR/suite.json")
meta = json.loads(suite.read_text())
projects = []
for line in Path("$PROJECTS_TSV").read_text().splitlines():
    label, fam, size, path, bytes_, sizeMb, expected, cancelAt = line.split("|")
    projects.append({
        "label": label, "family": fam, "sizeClass": size, "path": path,
        "bytes": int(bytes_), "sizeMb": float(sizeMb),
        "expectedDeploySec": int(expected), "cancelAtSec": int(cancelAt)
    })
meta["projects"] = projects
suite.write_text(json.dumps(meta, indent=2) + "\n")
PY

CSV_HEADER="trial,utc,phase,trialKind,family,sizeClass,seed,stratum,gapSec,projectPath,projectLabel,projectBytes,projectSizeMb,sessionId,cancelAtSec,interrupted,automationMs,pass,exitCode,detail,clientVersion,serverVersion"

PHASE_RESULTS=()

ensure_phase_dir() {
  local name="$1"
  local dir="$SUITE_DIR/$name"
  mkdir -p "$dir"
  echo "$CSV_HEADER" > "$dir/trials.csv"
  : > "$dir/trials.jsonl"
  echo "$dir"
}

append_trial() {
  local phase_dir="$1"
  shift
  # args as key=value ... use nameref-style via env vars set by caller
  local trial="$TRIAL" utc="$UTC" phase="$PHASE" trialKind="$TRIAL_KIND" family="$PFAMILY" sizeClass="$PSIZE" \
    seed="$SEED" stratum="$STRATUM" gapSec="$GAP_SEC" projectPath="$PPATH" projectLabel="$PLABEL" \
    projectBytes="$PBYTES" projectSizeMb="$PSIZEMB" sessionId="$SESSION_ID" cancelAtSec="$CANCEL_AT" \
    interrupted="$INTERRUPTED" automationMs="$AUTO_MS" pass="$PASS" exitCode="$EXIT_CODE" detail="$DETAIL"

  {
    printf '%s,' "$trial"
    csv_escape "$utc"; printf ','
    csv_escape "$phase"; printf ','
    csv_escape "$trialKind"; printf ','
    csv_escape "$family"; printf ','
    csv_escape "$sizeClass"; printf ','
    printf '%s,' "$seed"
    csv_escape "$stratum"; printf ','
    printf '%s,' "$gapSec"
    csv_escape "$projectPath"; printf ','
    csv_escape "$projectLabel"; printf ','
    printf '%s,%s,' "$projectBytes" "$projectSizeMb"
    csv_escape "$sessionId"; printf ','
    printf '%s,%s,%s,%s,%s,' "$cancelAtSec" "$interrupted" "$automationMs" "$pass" "$exitCode"
    csv_escape "$detail"; printf ','
    csv_escape "$CLIENT_VERSION"; printf ','
    csv_escape "$SERVER_VERSION"; printf '\n'
  } >> "$phase_dir/trials.csv"

  python3 - <<PY >> "$phase_dir/trials.jsonl"
import json
print(json.dumps({
  "trial": int("$trial"),
  "utc": "$utc",
  "phase": "$phase",
  "trialKind": "$trialKind",
  "family": "$family",
  "sizeClass": "$sizeClass",
  "seed": int("$seed"),
  "stratum": "$stratum",
  "gapSec": float("$gapSec"),
  "projectPath": """$projectPath""",
  "projectLabel": "$projectLabel",
  "projectBytes": int("$projectBytes"),
  "projectSizeMb": float("$projectSizeMb"),
  "sessionId": "$sessionId",
  "cancelAtSec": int("$cancelAtSec" or 0),
  "interrupted": ("$interrupted" == "true"),
  "automationMs": int("$automationMs"),
  "pass": ("$pass" == "true"),
  "exitCode": int("$exitCode"),
  "detail": """${detail//$'\n'/ | }""",
  "clientVersion": "$CLIENT_VERSION",
  "serverVersion": "$SERVER_VERSION",
}))
PY

  local status="FAIL"
  [[ "$pass" == "true" ]] && status="PASS"
  echo "  [$phase] trial=$trial kind=$trialKind label=$projectLabel gap=${gapSec}s auto=${automationMs}ms $status $detail"
}

write_phase_summary() {
  local phase_dir="$1" phase_name="$2"
  python3 - <<PY
import json, statistics
from pathlib import Path
from collections import defaultdict
phase_dir = Path("$phase_dir")
rows = []
for line in (phase_dir / "trials.jsonl").read_text().splitlines():
    if line.strip():
        rows.append(json.loads(line))
n = len(rows)
passed = sum(1 for r in rows if r.get("pass"))
def pct(a,b): return 0 if b==0 else 100.0*a/b
def perc(vals, p):
    if not vals: return 0
    s = sorted(vals)
    i = int(round((len(s)-1)*p))
    return s[max(0,min(i,len(s)-1))]
lines = [
  "FEZD client soak phase summary",
  f"phase: $phase_name",
  f"out-dir: {phase_dir}",
  f"clientVersion: $CLIENT_VERSION",
  f"serverVersion: $SERVER_VERSION",
  f"seed: $SEED",
  f"trials: {n}",
  f"pass: {passed}/{n} ({pct(passed,n):.1f}%)",
  "",
  "By trialKind:",
]
for kind in sorted({r.get("trialKind") for r in rows}):
    g = [r for r in rows if r.get("trialKind")==kind]
    gp = sum(1 for r in g if r.get("pass"))
    ms = [int(r.get("automationMs") or 0) for r in g]
    lines.append(f"  {kind}: n={len(g)} pass={gp}/{len(g)} ({pct(gp,len(g)):.1f}%) automationMs p50={perc(ms,0.5)} p95={perc(ms,0.95)}")
lines.append("")
lines.append("By project label:")
for label in sorted({r.get("projectLabel") for r in rows}):
    g = [r for r in rows if r.get("projectLabel")==label]
    gp = sum(1 for r in g if r.get("pass"))
    ms = [int(r.get("automationMs") or 0) for r in g]
    lines.append(f"  {label}: n={len(g)} pass={gp}/{len(g)} ({pct(gp,len(g)):.1f}%) automationMs p50={perc(ms,0.5)} p95={perc(ms,0.95)}")
(phase_dir / "summary.txt").write_text("\n".join(lines) + "\n")
print(f"{phase_name}|{n}|{passed}|{pct(passed,n):.1f}")
PY
}

load_project_fields() {
  local row="$1"
  IFS='|' read -r PLABEL PFAMILY PSIZE PPATH PBYTES PSIZEMB PEXPECTED PCANCEL <<<"$row"
}

deploy_args() {
  local zef="$1"
  DEPLOY_ARGS=(deploy "$zef" --connection "$CONNECTION" --simulator --run --force)
  if [[ -n "$APP_PASSWORD" ]]; then
    DEPLOY_ARGS+=(--app-password "$APP_PASSWORD")
  fi
}

run_steady_one() {
  local phase_dir="$1" phase="$2" row="$3" gap="$4" stratum="$5"
  load_project_fields "$row"
  GLOBAL_TRIAL=$((GLOBAL_TRIAL + 1))
  TRIAL="$GLOBAL_TRIAL"; UTC="$(utc_now)"; PHASE="$phase"; TRIAL_KIND="steady"
  STRATUM="$stratum"; GAP_SEC="$gap"; SESSION_ID=""; CANCEL_AT=0; INTERRUPTED="false"
  DETAIL=""; PASS="false"; EXIT_CODE=-1; AUTO_MS=0

  deploy_args "$PPATH"
  local timeout=$(( PEXPECTED * 4 ))
  [[ $timeout -lt 300 ]] && timeout=300
  local start end out ec
  start="$(python3 -c 'import time; print(int(time.time()*1000))')"
  set +e
  out="$("$CLIENT" "${DEPLOY_ARGS[@]}" 2>&1)"
  ec=$?
  set -e
  end="$(python3 -c 'import time; print(int(time.time()*1000))')"
  AUTO_MS=$((end - start))
  EXIT_CODE="$ec"
  SESSION_ID="$(extract_session_id "$out")"
  if [[ $ec -eq 0 ]]; then
    PASS="true"; DETAIL="deploy-sim OK"
  else
    PASS="false"
    DETAIL="$(printf '%s\n' "$out" | grep -E '.' | tail -3 | tr '\n' '|' | sed 's/|$//')"
    [[ -z "$DETAIL" ]] && DETAIL="deploy failed exit=$ec"
  fi
  append_trial "$phase_dir"
  [[ "$PASS" == "true" ]]
}

run_interrupt_one() {
  local phase_dir="$1" phase="$2" row="$3" gap="$4" stratum="$5" kind="${6:-interrupt}" skip_recover="${7:-0}"
  load_project_fields "$row"
  local cancel_at="$PCANCEL"
  local timeout=$(( PEXPECTED * 4 ))
  [[ $timeout -lt 300 ]] && timeout=300

  GLOBAL_TRIAL=$((GLOBAL_TRIAL + 1))
  TRIAL="$GLOBAL_TRIAL"; UTC="$(utc_now)"; PHASE="$phase"; TRIAL_KIND="$kind"
  STRATUM="$stratum"; GAP_SEC="$gap"; SESSION_ID=""; CANCEL_AT="$cancel_at"; INTERRUPTED="true"
  DETAIL=""; PASS="false"; EXIT_CODE=-1; AUTO_MS=0

  deploy_args "$PPATH"
  local out_log="$phase_dir/interrupt-${TRIAL}.stdout.log"
  local err_log="$phase_dir/interrupt-${TRIAL}.stderr.log"
  local comb_log="$phase_dir/interrupt-${TRIAL}.log"
  : > "$out_log"; : > "$err_log"

  local start end
  start="$(python3 -c 'import time; print(int(time.time()*1000))')"
  set +e
  "$CLIENT" "${DEPLOY_ARGS[@]}" >"$out_log" 2>"$err_log" &
  local pid=$!
  set -e

  local deadline=$(( $(date +%s) + timeout ))
  local cancel_deadline=$(( $(date +%s) + cancel_at ))
  local cancel_sent=0
  local sid=""

  while kill -0 "$pid" 2>/dev/null; do
    local combined
    combined="$(cat "$out_log" "$err_log" 2>/dev/null || true)"
    if [[ -z "$sid" ]]; then
      sid="$(extract_session_id "$combined")"
      [[ -n "$sid" ]] && SESSION_ID="$sid"
    fi
    if [[ $cancel_sent -eq 0 && -n "$sid" && $(date +%s) -ge $cancel_deadline ]]; then
      echo "    -> cancel session $sid (after ~${cancel_at}s)"
      set +e
      "$CLIENT" cancel "$sid" --connection "$CONNECTION" >>"$comb_log" 2>&1
      set -e
      cancel_sent=1
    fi
    if [[ $cancel_sent -eq 0 && -z "$sid" && $(date +%s) -ge $((cancel_deadline + 45)) ]]; then
      DETAIL="No session id observed before cancel window; waiting for deploy exit"
      break
    fi
    if [[ $(date +%s) -gt $deadline ]]; then
      kill "$pid" 2>/dev/null || true
      sleep 1
      kill -9 "$pid" 2>/dev/null || true
      break
    fi
    sleep 0.4
  done

  set +e
  wait "$pid"
  EXIT_CODE=$?
  set -e
  end="$(python3 -c 'import time; print(int(time.time()*1000))')"
  AUTO_MS=$((end - start))
  cat "$out_log" "$err_log" > "$comb_log" 2>/dev/null || true
  if [[ -z "$SESSION_ID" ]]; then
    SESSION_ID="$(extract_session_id "$(cat "$comb_log")")"
  fi

  if [[ $cancel_sent -eq 1 ]]; then
    PASS="true"
    DETAIL="cancel requested session=$SESSION_ID exit=$EXIT_CODE"
  elif [[ $EXIT_CODE -eq 0 ]]; then
    PASS="false"
    DETAIL="deploy finished before cancel window (too fast for cancelAtSec=$cancel_at)"
  else
    PASS="false"
    DETAIL="cancel not sent; exit=$EXIT_CODE"
  fi
  append_trial "$phase_dir"

  if [[ "$skip_recover" -eq 1 ]]; then
    return 0
  fi

  sleep 5

  GLOBAL_TRIAL=$((GLOBAL_TRIAL + 1))
  TRIAL="$GLOBAL_TRIAL"; UTC="$(utc_now)"; PHASE="$phase"; TRIAL_KIND="recover"
  STRATUM="recover"; GAP_SEC=5; SESSION_ID=""; CANCEL_AT=0; INTERRUPTED="false"
  DETAIL=""; PASS="false"; EXIT_CODE=-1; AUTO_MS=0
  deploy_args "$PPATH"
  start="$(python3 -c 'import time; print(int(time.time()*1000))')"
  set +e
  local out
  out="$("$CLIENT" "${DEPLOY_ARGS[@]}" 2>&1)"
  EXIT_CODE=$?
  set -e
  end="$(python3 -c 'import time; print(int(time.time()*1000))')"
  AUTO_MS=$((end - start))
  SESSION_ID="$(extract_session_id "$out")"
  if [[ $EXIT_CODE -eq 0 ]]; then
    PASS="true"; DETAIL="recover deploy OK after cancel"
  else
    PASS="false"
    DETAIL="$(printf '%s\n' "$out" | grep -E '.' | tail -3 | tr '\n' '|' | sed 's/|$//')"
    [[ -z "$DETAIL" ]] && DETAIL="recover failed exit=$EXIT_CODE"
  fi
  append_trial "$phase_dir"
  [[ "$PASS" == "true" ]]
}

wait_gap() {
  local gap="$1" label="$2"
  python3 -c "import sys; g=float(sys.argv[1]); raise SystemExit(0 if g<=0 else 1)" "$gap" && return 0
  printf '  waiting gap=%.2fs before %s...\n' "$gap" "$label"
  sleep "$gap"
}

filter_family_rows() {
  local fam="$1"
  FAMILY_ROWS=()
  local row
  for row in "${PROJECT_ROWS[@]}"; do
    IFS='|' read -r _ f _ <<<"$row"
    [[ "$f" == "$fam" ]] && FAMILY_ROWS+=("$row")
  done
}

run_steady_phase() {
  local phase_name="$1"
  echo ""
  echo "=== PHASE $phase_name (steady Monte Carlo) ==="
  local phase_dir
  phase_dir="$(ensure_phase_dir "$phase_name")"
  local start_ts consecutive=0 cursor=0 t
  start_ts="$(date +%s)"
  for ((t=1; t<=STEADY_TRIALS; t++)); do
    if (( $(date +%s) - start_ts >= STEADY_DURATION )); then
      echo "Duration cap reached (${STEADY_DURATION}s)."
      break
    fi
    local row="${FAMILY_ROWS[$((cursor % ${#FAMILY_ROWS[@]}))]}"
    cursor=$((cursor + 1))
    local gs stratum gap
    gs="$(gap_sample "$t")"
    gap="${gs%%|*}"; stratum="${gs##*|}"
    if [[ $t -gt 1 ]] || python3 -c "import sys; raise SystemExit(0 if float(sys.argv[1])>0 else 1)" "$gap"; then
      wait_gap "$gap" "trial $t"
    fi
    if run_steady_one "$phase_dir" "$phase_name" "$row" "$gap" "$stratum"; then
      consecutive=0
    else
      consecutive=$((consecutive + 1))
      if [[ $MAX_FAILURES -gt 0 && $consecutive -ge $MAX_FAILURES ]]; then
        echo "Stopping after $consecutive consecutive failures."
        break
      fi
    fi
  done
  local summary
  summary="$(write_phase_summary "$phase_dir" "$phase_name")"
  PHASE_RESULTS+=("$summary")
  if [[ $STRICT -eq 1 ]]; then
    local pass n
    n="$(echo "$summary" | cut -d'|' -f2)"
    pass="$(echo "$summary" | cut -d'|' -f3)"
    [[ "$pass" == "$n" ]] || die "Strict: phase $phase_name failed ($pass/$n)"
  fi
}

run_interrupt_phase() {
  local phase_name="$1"
  echo ""
  echo "=== PHASE $phase_name (interrupt + recover) ==="
  local phase_dir
  phase_dir="$(ensure_phase_dir "$phase_name")"
  local consecutive=0 cursor=0 t
  for ((t=1; t<=INTERRUPT_CYCLES; t++)); do
    local row="${FAMILY_ROWS[$((cursor % ${#FAMILY_ROWS[@]}))]}"
    cursor=$((cursor + 1))
    local gs gap stratum
    gs="$(gap_sample "$t")"
    gap="${gs%%|*}"; stratum="${gs##*|}"
    # clamp interrupt gaps a bit shorter
    gap="$(python3 -c "print(max(5.0, min(float('$gap'), max(30.0, float('$DELAY_MAX')/2))))")"
    wait_gap "$gap" "interrupt cycle $t"
    if run_interrupt_one "$phase_dir" "$phase_name" "$row" "$gap" "$stratum" "interrupt" 0; then
      consecutive=0
    else
      consecutive=$((consecutive + 1))
      if [[ $MAX_FAILURES -gt 0 && $consecutive -ge $MAX_FAILURES ]]; then
        echo "Stopping interrupt phase after $consecutive consecutive recover failures."
        break
      fi
    fi
  done
  local summary
  summary="$(write_phase_summary "$phase_dir" "$phase_name")"
  PHASE_RESULTS+=("$summary")
}

run_storm_phase() {
  local phase_name="$1"
  echo ""
  echo "=== PHASE $phase_name (cancel storm + prove) ==="
  local phase_dir
  phase_dir="$(ensure_phase_dir "$phase_name")"
  local row="${FAMILY_ROWS[0]}"
  local t
  for ((t=1; t<=STORM_CANCELS; t++)); do
    wait_gap 5 "storm cancel $t"
    run_interrupt_one "$phase_dir" "$phase_name" "$row" 5 "storm" "storm-cancel" 1 || true
  done
  local prove_fails=0
  for ((t=1; t<=STORM_PROVE; t++)); do
    wait_gap 10 "storm prove $t"
    load_project_fields "$row"
    GLOBAL_TRIAL=$((GLOBAL_TRIAL + 1))
    TRIAL="$GLOBAL_TRIAL"; UTC="$(utc_now)"; PHASE="$phase_name"; TRIAL_KIND="storm-prove"
    STRATUM="prove"; GAP_SEC=10; SESSION_ID=""; CANCEL_AT=0; INTERRUPTED="false"
    DETAIL=""; PASS="false"; EXIT_CODE=-1; AUTO_MS=0
    deploy_args "$PPATH"
    local timeout=$(( PEXPECTED * 4 )); [[ $timeout -lt 300 ]] && timeout=300
    local start end out
    start="$(python3 -c 'import time; print(int(time.time()*1000))')"
    set +e
    out="$("$CLIENT" "${DEPLOY_ARGS[@]}" 2>&1)"
    EXIT_CODE=$?
    set -e
    end="$(python3 -c 'import time; print(int(time.time()*1000))')"
    AUTO_MS=$((end - start))
    SESSION_ID="$(extract_session_id "$out")"
    if [[ $EXIT_CODE -eq 0 ]]; then PASS="true"; DETAIL="storm-prove OK"
    else PASS="false"; DETAIL="$(printf '%s\n' "$out" | grep -E '.' | tail -3 | tr '\n' '|' | sed 's/|$//')"
      [[ -z "$DETAIL" ]] && DETAIL="storm-prove failed exit=$EXIT_CODE"
      prove_fails=$((prove_fails + 1))
    fi
    append_trial "$phase_dir"
  done
  echo "Storm prove failures: $prove_fails / $STORM_PROVE"
  local summary
  summary="$(write_phase_summary "$phase_dir" "$phase_name")"
  PHASE_RESULTS+=("$summary")
}

# Families to run
FAMILIES=()
if [[ "$FAMILY" == "All" ]]; then
  while IFS= read -r f; do FAMILIES+=("$f"); done < <(printf '%s\n' "${PROJECT_ROWS[@]}" | cut -d'|' -f2 | sort -u)
else
  FAMILIES=("$FAMILY")
fi

want_steady=0; want_interrupt=0; want_storm=0
case "$PHASES" in
  All) want_steady=1; want_interrupt=1; want_storm=1 ;;
  Steady) want_steady=1 ;;
  Interrupt) want_interrupt=1 ;;
  Storm) want_storm=1 ;;
  *) die "Unknown --phases $PHASES" ;;
esac

for fam in "${FAMILIES[@]}"; do
  filter_family_rows "$fam"
  [[ ${#FAMILY_ROWS[@]} -gt 0 ]] || continue
  local_key="$(printf '%s' "$fam" | tr '[:upper:]' '[:lower:]')"
  if [[ $want_steady -eq 1 ]]; then
    run_steady_phase "01-steady-$local_key"
  fi
  if [[ $want_interrupt -eq 1 ]]; then
    run_interrupt_phase "03-interrupt-$local_key"
  fi
  if [[ $want_storm -eq 1 ]]; then
    run_storm_phase "05-cancel-storm-$local_key"
  fi
done

# Suite rollup
python3 - <<PY
import json
from pathlib import Path
suite = Path("$SUITE_DIR/suite.json")
meta = json.loads(suite.read_text())
meta["finishedUtc"] = "$(utc_now)"
results = []
total_n = total_p = 0
for item in """$(printf '%s\n' "${PHASE_RESULTS[@]}")""".splitlines():
    if not item.strip():
        continue
    phase, n, p, pct = item.split("|")
    n=int(n); p=int(p); pct=float(pct)
    results.append({"phase": phase, "trials": n, "pass": p, "passPct": pct})
    total_n += n; total_p += p
meta["phaseResults"] = results
suite.write_text(json.dumps(meta, indent=2) + "\n")
pct = 0 if total_n==0 else 100.0*total_p/total_n
lines = [
  "FEZD client soak suite summary",
  f"out-dir: $SUITE_DIR",
  f"clientVersion: $CLIENT_VERSION",
  f"serverVersion: $SERVER_VERSION",
  f"seed: $SEED",
  f"total: {total_p}/{total_n} pass ({pct:.1f}%)",
  "",
]
for r in results:
    lines.append(f"  {r['phase']}: {r['pass']}/{r['trials']} ({r['passPct']}%)")
Path("$SUITE_DIR/suite-summary.txt").write_text("\n".join(lines) + "\n")
print("\n".join(lines))
print(f"TOTAL|{total_n}|{total_p}")
PY

echo ""
echo "Suite finished: $SUITE_DIR"

# exit non-zero if any fail
TOTAL_LINE="$(python3 -c "
import json
from pathlib import Path
m=json.loads(Path('$SUITE_DIR/suite.json').read_text())
n=sum(r['trials'] for r in m.get('phaseResults',[]))
p=sum(r['pass'] for r in m.get('phaseResults',[]))
print(f'{n}|{p}')
")"
TOTAL_N="${TOTAL_LINE%%|*}"
TOTAL_P="${TOTAL_LINE##*|}"
if [[ "$TOTAL_P" -lt "$TOTAL_N" ]]; then
  exit 1
fi
exit 0
