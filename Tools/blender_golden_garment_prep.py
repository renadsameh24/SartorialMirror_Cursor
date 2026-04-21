"""
Golden garment prep for SMPL shirt: Data Transfer weights -> apply -> Armature -> cleanup -> FBX.

From this repo (script in Tools/):
  Blender --background "/path/to/scene.blend" --python "Tools/blender_golden_garment_prep.py"

Optional env vars:
  MODE=INSPECT   — only list objects/modifiers (no changes)
  GARMENT_NAME=MyShirt
  ARMATURE_NAME=SMPL_Armature
  BODY_MESH_NAME=SMPL_neutral
  EXPORT_FBX=/full/path/out.fbx

Note: Applying the Data Transfer modifier removes that modifier by design (weights are baked).
"""

from __future__ import annotations

import os
import sys

import bpy


MODE = os.environ.get("MODE", "RUN").upper()
GARMENT_NAME = os.environ.get("GARMENT_NAME") or None
ARMATURE_NAME = os.environ.get("ARMATURE_NAME") or None
BODY_MESH_NAME = os.environ.get("BODY_MESH_NAME") or None

_script_dir = os.path.dirname(os.path.abspath(__file__))
if os.path.basename(_script_dir).lower() == "tools" and os.path.isdir(
    os.path.join(os.path.dirname(_script_dir), "Assets")
):
    _repo_root = os.path.dirname(_script_dir)
    _unity_default = os.path.join(_repo_root, "Assets", "garments_prepared", "Flannel_SMPL_Skinned.fbx")
else:
    _unity_default = os.path.join(
        _script_dir, "SartorialMirror_Cursor", "Assets", "garments_prepared", "Flannel_SMPL_Skinned.fbx"
    )

EXPORT_FBX = os.environ.get("EXPORT_FBX", _unity_default)

BODY_HINTS = ("smpl", "neutral", "body", "skin", "avatar", "male", "female")


def log(msg: str) -> None:
    print(f"[golden_prep] {msg}", flush=True)


def objs_by_type(t: str):
    return [o for o in bpy.data.objects if o.type == t]


def pick_armature() -> bpy.types.Object | None:
    arms = objs_by_type("ARMATURE")
    if ARMATURE_NAME:
        for o in arms:
            if o.name == ARMATURE_NAME:
                return o
        log(f"ARMATURE_NAME={ARMATURE_NAME!r} not found; falling back to heuristics.")
    for key in ("SMPL", "smpl", "Armature", "Rig"):
        for o in arms:
            if key.lower() in o.name.lower():
                return o
    if len(arms) == 1:
        return arms[0]
    return None


def mesh_score(o: bpy.types.Object) -> int:
    n = o.name.lower()
    s = len(o.data.vertices)
    if any(h in n for h in BODY_HINTS):
        return -10_000_000 + s  # deprioritize body-like names
    return s


def pick_body_mesh() -> bpy.types.Object | None:
    meshes = objs_by_type("MESH")
    if BODY_MESH_NAME:
        for o in meshes:
            if o.name == BODY_MESH_NAME:
                return o
        log(f"BODY_MESH_NAME={BODY_MESH_NAME!r} not found; falling back to heuristics.")
    meshes_sorted = sorted(meshes, key=mesh_score, reverse=True)
    for o in meshes_sorted:
        if any(h in o.name.lower() for h in ("smpl", "neutral", "body")):
            return o
    return meshes_sorted[0] if meshes_sorted else None


def pick_garment(body: bpy.types.Object | None) -> bpy.types.Object | None:
    meshes = objs_by_type("MESH")
    if GARMENT_NAME:
        for o in meshes:
            if o.name == GARMENT_NAME:
                return o
        log(f"GARMENT_NAME={GARMENT_NAME!r} not found; falling back to heuristics.")
    cands = [o for o in meshes if body is None or o != body]
    if not cands:
        return None
    cands.sort(key=mesh_score, reverse=True)
    return cands[0]


def inspect() -> None:
    log("=== INSPECT ===")
    for obj in bpy.data.objects:
        t = obj.type
        extra = ""
        if t == "MESH":
            mods = [(m.name, m.type) for m in obj.modifiers]
            extra = f" verts={len(obj.data.vertices)} vgroups={len(obj.vertex_groups)} mods={mods}"
        elif t == "ARMATURE":
            extra = f" bones={len(obj.data.bones)}"
        print(f"  [{t}] {obj.name}{extra}", flush=True)
    log("=== END INSPECT ===")


def ensure_active(obj: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def clear_vertex_groups(obj: bpy.types.Object) -> None:
    ensure_active(obj)
    for g in list(obj.vertex_groups):
        obj.vertex_groups.remove(g)


def ensure_vertex_groups_from_source_names(garment: bpy.types.Object, source: bpy.types.Object) -> None:
    """Data Transfer writes into existing groups; empty groups with matching names are filled."""
    existing = {g.name for g in garment.vertex_groups}
    for g in source.vertex_groups:
        if g.name not in existing:
            garment.vertex_groups.new(name=g.name)
            existing.add(g.name)
    log(f"Garment vertex groups (names from body): {len(garment.vertex_groups)}")


def strip_armature_modifiers(obj: bpy.types.Object) -> None:
    for m in list(obj.modifiers):
        if m.type == "ARMATURE":
            obj.modifiers.remove(m)


def remove_modifiers_named(obj: bpy.types.Object, *names: str) -> None:
    for m in list(obj.modifiers):
        if m.name in names:
            obj.modifiers.remove(m)


def add_data_transfer(garment: bpy.types.Object, source_mesh: bpy.types.Object) -> bpy.types.Modifier:
    remove_modifiers_named(garment, "DT_SMPL", "DT")
    mod = garment.modifiers.new(name="DT_SMPL", type="DATA_TRANSFER")
    mod.object = source_mesh
    mod.use_object_transform = True
    mod.use_vert_data = True
    mod.data_types_verts = {"VGROUP_WEIGHTS"}
    try:
        mod.vert_mapping = "POLYINTERP_NEAREST"
    except TypeError:
        mod.vert_mapping = "NEAREST"
    mod.mix_mode = "REPLACE"
    mod.mix_factor = 1.0
    return mod


def apply_modifier(obj: bpy.types.Object, mod_name: str) -> None:
    ensure_active(obj)
    bpy.ops.object.modifier_apply(modifier=mod_name)


def ensure_armature_modifier(mesh_obj: bpy.types.Object, arm_obj: bpy.types.Object) -> None:
    strip_armature_modifiers(mesh_obj)
    mod = mesh_obj.modifiers.new(name="Armature", type="ARMATURE")
    mod.object = arm_obj
    mod.use_vertex_groups = True


def normalize_weights(mesh_obj: bpy.types.Object) -> None:
    if not mesh_obj.vertex_groups:
        log("vertex_group_normalize_all skipped: no vertex groups")
        return
    ensure_active(mesh_obj)
    try:
        bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    except Exception as e:
        log(f"vertex_group_normalize_all skipped: {e}")


def export_fbx(arm_obj: bpy.types.Object, mesh_objs: list[bpy.types.Object]) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    arm_obj.select_set(True)
    for o in mesh_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj

    os.makedirs(os.path.dirname(EXPORT_FBX) or ".", exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=EXPORT_FBX,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        bake_anim=False,
        path_mode="AUTO",
        apply_scale_options="FBX_SCALE_ALL",
        global_scale=1.0,
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        use_armature_deform_only=True,
    )
    log(f"Exported: {EXPORT_FBX}")


def run() -> int:
    if MODE == "INSPECT":
        inspect()
        return 0

    arm = pick_armature()
    if not arm:
        log("No armature found. Set ARMATURE_NAME or add a single armature.")
        inspect()
        return 1

    body = pick_body_mesh()
    garment = pick_garment(body)
    if not garment or not body:
        log("Could not resolve body mesh and garment mesh.")
        inspect()
        return 1

    log(f"Armature: {arm.name}")
    log(f"Body (transfer source): {body.name}")
    log(f"Garment (transfer target): {garment.name}")

    if garment.parent != arm:
        ensure_active(garment)
        garment.parent = arm
        garment.matrix_parent_inverse = arm.matrix_world.inverted()

    strip_armature_modifiers(garment)
    clear_vertex_groups(garment)
    ensure_vertex_groups_from_source_names(garment, body)

    dt = add_data_transfer(garment, body)
    bpy.context.view_layer.depsgraph.update()
    apply_modifier(garment, dt.name)

    log(f"Garment vertex groups after DT apply: {len(garment.vertex_groups)}")

    ensure_armature_modifier(garment, arm)
    normalize_weights(garment)

    export_fbx(arm, [garment])

    log("Done.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(run())
    except Exception:
        import traceback

        traceback.print_exc()
        sys.exit(2)
