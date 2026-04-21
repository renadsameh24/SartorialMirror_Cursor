"""
One-shot Blender 4.x/5.x: import SMPL FBX + garment FBX, transfer weights from SMPL body -> shirt, export FBX.

Usage (from repo root):
  export SMPL_FBX="/abs/path/SMPL_neutral_rig_GOLDEN.fbx"
  export GARMENT_FBX="/abs/path/Shirt.fbx"
  export EXPORT_FBX="/abs/path/out/Flannel_SMPL_Skinned.fbx"
  /Applications/Blender.app/Contents/MacOS/Blender --background --python "Tools/blender_golden_garment_from_fbx.py"

Optional:
  ARMATURE_NAME=SMPL_Armature
  BODY_MESH_NAME=SMPL_neutral
  GARMENT_MESH_HINT=Shirt
"""

from __future__ import annotations

import os
import sys

import bpy


def log(msg: str) -> None:
    print(f"[golden_from_fbx] {msg}", flush=True)


SMPL_FBX = os.environ.get("SMPL_FBX", "").strip()
GARMENT_FBX = os.environ.get("GARMENT_FBX", "").strip()
EXPORT_FBX = os.environ.get("EXPORT_FBX", "").strip()
ARMATURE_NAME = os.environ.get("ARMATURE_NAME") or None
BODY_MESH_NAME = os.environ.get("BODY_MESH_NAME") or None
GARMENT_MESH_HINT = os.environ.get("GARMENT_MESH_HINT") or None


def objs_by_type(t: str):
    return [o for o in bpy.data.objects if o.type == t]


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path: str) -> None:
    if not os.path.isfile(path):
        raise FileNotFoundError(path)
    bpy.ops.import_scene.fbx(
        filepath=path,
        use_anim=False,
        ignore_leaf_bones=False,
        force_connect_children=False,
        automatic_bone_orientation=False,
    )


def pick_armature() -> bpy.types.Object | None:
    arms = objs_by_type("ARMATURE")
    if ARMATURE_NAME:
        for o in arms:
            if o.name == ARMATURE_NAME:
                return o
        log(f"ARMATURE_NAME={ARMATURE_NAME!r} not found; using heuristics.")
    for key in ("SMPL_Armature", "SMPL", "Armature", "Rig"):
        for o in arms:
            if key.lower() in o.name.lower():
                return o
    return arms[0] if len(arms) == 1 else None


BODY_HINTS = ("smpl", "neutral", "body", "skin")


def mesh_score(o: bpy.types.Object) -> int:
    n = o.name.lower()
    s = len(o.data.vertices)
    if any(h in n for h in BODY_HINTS):
        return -10_000_000 + s
    return s


def pick_body_mesh() -> bpy.types.Object | None:
    meshes = objs_by_type("MESH")
    if BODY_MESH_NAME:
        want = BODY_MESH_NAME.lower()
        for o in meshes:
            if o.name == BODY_MESH_NAME or o.name.lower() == want:
                return o
        log(f"BODY_MESH_NAME={BODY_MESH_NAME!r} not found; using heuristics.")
    meshes_sorted = sorted(meshes, key=mesh_score, reverse=True)
    for o in meshes_sorted:
        if any(h in o.name.lower() for h in ("smpl", "neutral", "body")):
            return o
    return meshes_sorted[0] if meshes_sorted else None


def pick_garment_mesh(body: bpy.types.Object | None) -> bpy.types.Object | None:
    meshes = [o for o in objs_by_type("MESH") if body is None or o != body]
    if not meshes:
        return None
    if GARMENT_MESH_HINT:
        for o in meshes:
            if GARMENT_MESH_HINT.lower() in o.name.lower():
                return o
        log(f"GARMENT_MESH_HINT={GARMENT_MESH_HINT!r} not found; using largest non-body mesh.")
    meshes.sort(key=lambda o: len(o.data.vertices), reverse=True)
    return meshes[0]


def ensure_active(obj: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def clear_vertex_groups(obj: bpy.types.Object) -> None:
    ensure_active(obj)
    for g in list(obj.vertex_groups):
        obj.vertex_groups.remove(g)


def ensure_vertex_groups_from_source_names(garment: bpy.types.Object, source: bpy.types.Object) -> None:
    existing = {g.name for g in garment.vertex_groups}
    for g in source.vertex_groups:
        if g.name not in existing:
            garment.vertex_groups.new(name=g.name)
            existing.add(g.name)


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


def delete_extra_armatures(keep: bpy.types.Object) -> None:
    for o in list(objs_by_type("ARMATURE")):
        if o != keep:
            log(f"Removing extra armature: {o.name}")
            bpy.data.objects.remove(o, do_unlink=True)


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
    if not SMPL_FBX or not GARMENT_FBX or not EXPORT_FBX:
        log("Set SMPL_FBX, GARMENT_FBX, EXPORT_FBX (absolute paths).")
        return 1

    clear_scene()
    log(f"Import SMPL: {SMPL_FBX}")
    import_fbx(SMPL_FBX)
    log(f"Import garment: {GARMENT_FBX}")
    import_fbx(GARMENT_FBX)

    arm = pick_armature()
    if not arm:
        log("No armature found after import.")
        return 1

    body = pick_body_mesh()
    garment = pick_garment_mesh(body)
    if not body or not garment:
        log("Could not resolve body mesh and garment mesh.")
        return 1

    log(f"Armature (keep): {arm.name}")
    log(f"Body (weight source): {body.name}")
    log(f"Garment (target): {garment.name}")

    delete_extra_armatures(arm)

    if garment.parent and garment.parent.type == "ARMATURE" and garment.parent != arm:
        ensure_active(garment)
        bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")

    ensure_active(garment)
    garment.parent = arm
    garment.matrix_parent_inverse = arm.matrix_world.inverted()

    strip_armature_modifiers(garment)
    clear_vertex_groups(garment)
    ensure_vertex_groups_from_source_names(garment, body)

    dt = add_data_transfer(garment, body)
    bpy.context.view_layer.depsgraph.update()
    apply_modifier(garment, dt.name)

    log(f"Garment vertex groups after DT: {len(garment.vertex_groups)}")

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
