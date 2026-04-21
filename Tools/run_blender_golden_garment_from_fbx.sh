#!/usr/bin/env bash
# Regenerate a SMPL-skinned garment FBX (Data Transfer weights from SMPL body mesh).
#
# Usage:
#   cd /path/to/SartorialMirror_Cursor
#   ./Tools/run_blender_golden_garment_from_fbx.sh /absolute/path/to/YourShirt.fbx
#
# Or set GARMENT_FBX yourself and omit the argument (defaults shown below).
#
# After export: open Unity, let it reimport, then:
#   Menu → SartorialMirror → Link prepared Flannel FBX to GarmentCatalog
# (or drag Assets/garments_prepared/Flannel_SMPL_Skinned.fbx into GarmentCatalog entry 0).

set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO"

export SMPL_FBX="${SMPL_FBX:-$REPO/Assets/SMPL/Models/SMPL_neutral_rig_GOLDEN.fbx}"
export EXPORT_FBX="${EXPORT_FBX:-$REPO/Assets/garments_prepared/Flannel_SMPL_Skinned.fbx}"

# First CLI argument wins over a stale GARMENT_FBX in the environment.
if [[ $# -ge 1 ]]; then
  G1="$1"
  export GARMENT_FBX="$(cd "$(dirname "$G1")" && pwd)/$(basename "$G1")"
else
  export GARMENT_FBX="${GARMENT_FBX:-}"
fi

if [[ ! -f "$SMPL_FBX" ]]; then
  echo "Missing SMPL FBX: $SMPL_FBX" >&2
  exit 1
fi
if [[ -z "$GARMENT_FBX" ]] || [[ ! -f "$GARMENT_FBX" ]]; then
  echo "Usage: $0 /absolute/path/to/garment.fbx" >&2
  echo "Or:    GARMENT_FBX=/path/to/shirt.fbx $0   (no argument)" >&2
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

echo "SMPL_FBX=$SMPL_FBX"
echo "GARMENT_FBX=$GARMENT_FBX"
echo "EXPORT_FBX=$EXPORT_FBX"
echo "Blender: $BLENDER"
# Nearest-body-vertex weight copy (recommended): set FORCE_KD=0 to skip and rely on Data Transfer only.
export FORCE_KD="${FORCE_KD:-1}"

exec "$BLENDER" --background --python "$REPO/Tools/blender_golden_garment_from_fbx.py"
