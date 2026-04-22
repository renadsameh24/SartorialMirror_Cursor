#!/usr/bin/env bash
# Scale-match an already SMPL-skinned garment FBX in Blender (no weight re-transfer), then overwrite
# EXPORT_FBX so Unity can reimport the same asset path (e.g. Assets/garments_prepared/Flannel_SMPL_Skinned.fbx).
#
# Often exits without exporting: shirt-only prepared FBXs already match Unity scale; armature-only math
# can suggest a tiny factor (e.g. 0.003) which we skip on purpose. After a successful golden pipeline run,
# you usually do NOT need this script — tune size in Unity (SmplGarmentManager) instead.
#
# Usage (from repo root):
#   ./Tools/run_blender_scale_match_to_unity.sh
#
# Optional: SCALE_MATCH_GARMENT_FBX=/path/to/prepared.fbx overrides defaults.
# Do not point SCALE_MATCH at the raw VR4D shirt FBX (no SMPL bones).

set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO"

export SCALE_MATCH_ONLY=1

# Stale GARMENT_FBX often points at the *raw* retail shirt (only "Armature", no SMPL J-bones).
# SCALE_MATCH_ONLY needs the *prepared* SMPL-skinned FBX (SMPL_Armature + shirt).
if [[ -n "${GARMENT_FBX:-}" ]]; then
  case "$GARMENT_FBX" in
    *VR4D030312_ShirtFlannelBWWoman_Normal*)
      echo "[run_blender_scale_match] Ignoring GARMENT_FBX (raw shirt): $GARMENT_FBX" >&2
      echo "[run_blender_scale_match] Use prepared: $REPO/Assets/garments_prepared/Flannel_SMPL_Skinned.fbx (or SCALE_MATCH_GARMENT_FBX=...)" >&2
      unset GARMENT_FBX
      ;;
  esac
fi
export GARMENT_FBX="${SCALE_MATCH_GARMENT_FBX:-${GARMENT_FBX:-$REPO/Assets/garments_prepared/Flannel_SMPL_Skinned.fbx}}"
export EXPORT_FBX="${EXPORT_FBX:-$REPO/Assets/garments_prepared/Flannel_SMPL_Skinned.fbx}"
if [[ -n "${EXPORT_FBX:-}" && "$EXPORT_FBX" != "$REPO/"* ]]; then
  echo "[run_blender_scale_match] WARNING: EXPORT_FBX is outside this repo: $EXPORT_FBX" >&2
fi

if [[ ! -f "$GARMENT_FBX" ]]; then
  echo "Missing FBX: $GARMENT_FBX" >&2
  exit 1
fi

BLENDER="${BLENDER:-}"
if [[ -z "$BLENDER" ]]; then
  if command -v blender &>/dev/null; then
    BLENDER="$(command -v blender)"
  elif [[ -x "/Applications/Blender.app/Contents/MacOS/Blender" ]]; then
    BLENDER="/Applications/Blender.app/Contents/MacOS/Blender"
  else
    echo "Blender not found. Install Blender or set BLENDER=/path/to/blender" >&2
    exit 1
  fi
fi

export AUTO_SCALE="${AUTO_SCALE:-1}"
export AUTO_SCALE_MODE="${AUTO_SCALE_MODE:-combined}"
export AUTO_SCALE_S_MIN="${AUTO_SCALE_S_MIN:-0.001}"
export AUTO_SCALE_S_MAX="${AUTO_SCALE_S_MAX:-500}"
# Shirt-only FBX: if Blender skips scale (median outside 0.2–5), set ALLOW_EXTREME_ARMATURE_SCALE=1 once you know you need it.
# export ALLOW_EXTREME_ARMATURE_SCALE=1

echo "SCALE_MATCH_ONLY=1"
echo "GARMENT_FBX=$GARMENT_FBX"
echo "EXPORT_FBX=$EXPORT_FBX"
echo "Blender: $BLENDER"

exec "$BLENDER" --background --python "$REPO/Tools/blender_golden_garment_from_fbx.py"
