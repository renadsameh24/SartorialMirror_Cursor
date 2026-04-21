"""
One-shot Blender 4.x/5.x: import SMPL FBX + garment FBX, transfer weights from SMPL body -> shirt, export FBX.

Pipeline (revised):
  1) Import SMPL + garment; keep SMPL_Armature; remove duplicate armatures.
  2) Parent garment to SMPL armature.
  3) Pre-create vertex groups matching the SMPL body (and armature deform bones).
  4) Data Transfer (vertex group weights). Prefer POLYINTERP_NEAREST for broad coverage; optional env override.
  5) Validate: count vertex groups that receive any weight on any vert. If too few, fall back to
     KD-tree nearest-vertex weight copy (same idea as "transfer from closest body vertex") — fixes sparse shirts.
  6) Normalize, limit to 4 influences per vert (Unity), optional smooth.
  7) Export FBX with deform bones + mesh.

Usage (from repo root):
  export SMPL_FBX="/abs/path/SMPL_neutral_rig_GOLDEN.fbx"
  export GARMENT_FBX="/abs/path/Shirt.fbx"
  export EXPORT_FBX="/abs/path/out/Flannel_SMPL_Skinned.fbx"
  /Applications/Blender.app/Contents/MacOS/Blender --background --python "Tools/blender_golden_garment_from_fbx.py"

Optional env:
  ARMATURE_NAME=SMPL_Armature
  BODY_MESH_NAME=SMPL_neutral
  GARMENT_MESH_HINT=Shirt
  DT_VERT_MAPPING=POLYINTERP_NEAREST   # or POLYINTERP_VNORPROJ, NEAREST, ...
  MIN_INFLUENCING_VGROUPS=8            # below this, KD-tree fallback runs
  SKIP_KD_FALLBACK=0                   # set 1 to never use KD-tree
  FORCE_KD=1                           # always use KD-tree copy (best match to SMPL body; slower)
"""

from __future__ import annotations

import os
import sys

import bpy
from mathutils.kdtree import KDTree


def log(msg: str) -> None:
    print(f"[golden_from_fbx] {msg}", flush=True)


SMPL_FBX = os.environ.get("SMPL_FBX", "").strip()
GARMENT_FBX = os.environ.get("GARMENT_FBX", "").strip()
EXPORT_FBX = os.environ.get("EXPORT_FBX", "").strip()
ARMATURE_NAME = os.environ.get("ARMATURE_NAME") or None
BODY_MESH_NAME = os.environ.get("BODY_MESH_NAME") or None
GARMENT_MESH_HINT = os.environ.get("GARMENT_MESH_HINT") or None
DT_VERT_MAPPING = os.environ.get("DT_VERT_MAPPING", "").strip() or None
MIN_INFLUENCING_VGROUPS = int(os.environ.get("MIN_INFLUENCING_VGROUPS", "8"))
SKIP_KD_FALLBACK = os.environ.get("SKIP_KD_FALLBACK", "0").strip() in ("1", "true", "yes", "on")
FORCE_KD = os.environ.get("FORCE_KD", "0").strip() in ("1", "true", "yes", "on")


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


def deform_bone_names(arm: bpy.types.Object) -> list[str]:
    names: list[str] = []
    for b in arm.data.bones:
        if getattr(b, "use_deform", True):
            names.append(b.name)
    return names


def ensure_vertex_groups_for_armature_and_body(
    garment: bpy.types.Object, arm: bpy.types.Object, body: bpy.types.Object
) -> None:
    """Pre-create VGroups so Data Transfer and KD copy have matching names (J00, …)."""
    existing = {g.name for g in garment.vertex_groups}
    wanted: set[str] = set()

    for g in body.vertex_groups:
        wanted.add(g.name)
    for name in deform_bone_names(arm):
        wanted.add(name)

    for name in sorted(wanted):
        if name not in existing:
            garment.vertex_groups.new(name=name)
            existing.add(name)

    log(f"Pre-created vertex groups on garment: {len(garment.vertex_groups)} (body+armature names)")


def strip_armature_modifiers(obj: bpy.types.Object) -> None:
    for m in list(obj.modifiers):
        if m.type == "ARMATURE":
            obj.modifiers.remove(m)


def remove_modifiers_named(obj: bpy.types.Object, *names: str) -> None:
    for m in list(obj.modifiers):
        if m.name in names:
            obj.modifiers.remove(m)


def add_data_transfer(
    garment: bpy.types.Object, source_mesh: bpy.types.Object, vert_mapping: str | None
) -> bpy.types.Modifier:
    remove_modifiers_named(garment, "DT_SMPL", "DT", "DT_SMPL_2")
    mod = garment.modifiers.new(name="DT_SMPL", type="DATA_TRANSFER")
    mod.object = source_mesh
    mod.use_object_transform = True
    mod.use_vert_data = True
    mod.data_types_verts = {"VGROUP_WEIGHTS"}

    mappings: tuple[str, ...]
    if vert_mapping:
        mappings = (vert_mapping,)
    else:
        # NEAREST first: best coverage when shirt mesh is offset from body (common for retail FBX).
        mappings = (
            "POLYINTERP_NEAREST",
            "POLYINTERP_VNORPROJ",
            "NEAREST",
        )

    applied = None
    for mapping in mappings:
        try:
            mod.vert_mapping = mapping
            applied = mapping
            break
        except (TypeError, ValueError):
            continue
    if applied is None:
        mod.vert_mapping = "NEAREST"
        applied = "NEAREST"

    log(f"Data Transfer vert_mapping={applied}")
    mod.mix_mode = "REPLACE"
    mod.mix_factor = 1.0
    return mod


def apply_modifier(obj: bpy.types.Object, mod_name: str) -> None:
    ensure_active(obj)
    bpy.ops.object.modifier_apply(modifier=mod_name)


def count_influencing_vertex_groups(obj: bpy.types.Object) -> int:
    """How many vertex groups receive non-zero weight on at least one vertex."""
    mesh = obj.data
    touched: set[int] = set()
    for v in mesh.vertices:
        for ge in v.groups:
            if ge.weight > 1e-6:
                touched.add(ge.group)
    return len(touched)


def copy_weights_nearest_body_vertex(
    garment: bpy.types.Object, body: bpy.types.Object, arm: bpy.types.Object
) -> None:
    """
    For each garment vertex, copy all vertex weights from the nearest body vertex (world space).
    Robust when Data Transfer leaves most weights on only 1–2 bones.
    """
    log("KD-tree fallback: copying weights from nearest body vertex per garment vertex…")

    clear_vertex_groups(garment)
    ensure_vertex_groups_for_armature_and_body(garment, arm, body)

    body_mesh = body.data
    garment_mesh = garment.data
    mw_body = body.matrix_world
    mw_g = garment.matrix_world

    kd = KDTree(len(body_mesh.vertices))
    for i, v in enumerate(body_mesh.vertices):
        kd.insert(mw_body @ v.co, i)
    kd.balance()

    idx_to_name = {g.index: g.name for g in body.vertex_groups}
    name_to_garment_vg = {g.name: g for g in garment.vertex_groups}

    for vi, gv in enumerate(garment_mesh.vertices):
        co_w = mw_g @ gv.co
        _co, bi, _dist = kd.find(co_w)
        bv = body_mesh.vertices[bi]
        for ge in bv.groups:
            gname = idx_to_name.get(ge.group)
            if not gname or ge.weight <= 1e-8:
                continue
            vg = name_to_garment_vg.get(gname)
            if vg is None:
                vg = garment.vertex_groups.new(name=gname)
                name_to_garment_vg[gname] = vg
            vg.add([vi], float(ge.weight), "REPLACE")

    garment_mesh.update()


def limit_vertex_weights_to_four(obj: bpy.types.Object) -> None:
    """Unity SkinnedMeshRenderer uses up to 4 bones per vertex."""
    ensure_active(obj)
    try:
        bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
        log("Applied vertex_group_limit_total(limit=4) for Unity.")
    except Exception as e:
        log(f"vertex_group_limit_total skipped: {e}")


def smooth_vertex_weights_light(obj: bpy.types.Object, iterations: int = 1) -> None:
    """Optional; often unavailable in Blender --background (no VIEW_3D context)."""
    if bpy.context.window is None:
        log("vertex_group_smooth skipped (no UI context; normal for --background).")
        return
    ensure_active(obj)
    try:
        if obj.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        for _ in range(max(1, iterations)):
            bpy.ops.object.vertex_group_smooth(
                group_select_mode="ALL",
                factor=0.5,
                repeat=1,
                expand=0.0,
            )
        log(f"vertex_group_smooth iterations≈{iterations}")
    except Exception as e:
        log(f"vertex_group_smooth skipped: {e}")


def normalize_weights(mesh_obj: bpy.types.Object) -> None:
    if not mesh_obj.vertex_groups:
        log("vertex_group_normalize_all skipped: no vertex groups")
        return
    ensure_active(mesh_obj)
    try:
        bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    except Exception as e:
        log(f"vertex_group_normalize_all skipped: {e}")


def remove_orphan_vertex_groups_not_on_armature(garment: bpy.types.Object, arm: bpy.types.Object) -> None:
    """Remove VGroups that don't match a deform bone name (Unity only needs armature-deform groups)."""
    allowed = set(deform_bone_names(arm))
    ensure_active(garment)
    for g in list(garment.vertex_groups):
        if g.name not in allowed:
            garment.vertex_groups.remove(g)
    log(f"Vertex groups after pruning non-armature names: {len(garment.vertex_groups)}")


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
    log(f"Body (weight source): {body.name} | body vgroups={len(body.vertex_groups)}")
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
    ensure_vertex_groups_for_armature_and_body(garment, arm, body)

    dt = add_data_transfer(garment, body, DT_VERT_MAPPING)
    bpy.context.view_layer.depsgraph.update()
    apply_modifier(garment, dt.name)

    infl = count_influencing_vertex_groups(garment)
    log(f"After Data Transfer: influencing vertex groups (any vert) = {infl}")

    use_kd = FORCE_KD or (not SKIP_KD_FALLBACK and infl < MIN_INFLUENCING_VGROUPS)
    if use_kd:
        if FORCE_KD:
            log("FORCE_KD=1 — running KD-tree nearest-vertex weight copy (full SMPL weight projection).")
        else:
            log(
                f"Influencing groups {infl} < {MIN_INFLUENCING_VGROUPS} — running KD-tree nearest-vertex weight copy."
            )
        copy_weights_nearest_body_vertex(garment, body, arm)
        infl2 = count_influencing_vertex_groups(garment)
        log(f"After KD-tree: influencing vertex groups = {infl2}")

    remove_orphan_vertex_groups_not_on_armature(garment, arm)
    normalize_weights(garment)
    smooth_vertex_weights_light(garment, iterations=1)
    normalize_weights(garment)
    limit_vertex_weights_to_four(garment)

    ensure_armature_modifier(garment, arm)

    final_infl = count_influencing_vertex_groups(garment)
    log(f"Final: influencing vertex groups = {final_infl}, total named groups = {len(garment.vertex_groups)}")

    export_fbx(arm, [garment])
    log("Done.")
    return 0


def ensure_armature_modifier(mesh_obj: bpy.types.Object, arm_obj: bpy.types.Object) -> None:
    strip_armature_modifiers(mesh_obj)
    mod = mesh_obj.modifiers.new(name="Armature", type="ARMATURE")
    mod.object = arm_obj
    mod.use_vertex_groups = True


if __name__ == "__main__":
    try:
        sys.exit(run())
    except Exception:
        import traceback

        traceback.print_exc()
        sys.exit(2)
