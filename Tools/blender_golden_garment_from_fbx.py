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
  FORCE_KD=1                           # always run weight copy (BVH or VERTEX)
  WEIGHT_COPY_METHOD=BVH               # BVH = nearest triangle on body surface (better sleeves); VERTEX = nearest body vertex
  DISALLOW_BONES=J15,J22,J23           # comma-separated vertex-group names to zero out after transfer (default: head + hands)
  AUTO_ALIGN=1                         # translate garment to body bbox center before weight transfer (recommended)
  ALLOW_BONES=J00,J03,J06,J09,J12,J16,J17,J18,J19,J20,J21  # upper-body only; prevents leg weights on shirts
  REGION_REWEIGHT=1                    # post-process weights: torso vs sleeves by nearest key bones
  MAX_WRIST_WEIGHT=0.25                # cap wrist weight on sleeve verts; excess moves to elbow/shoulder
  SLEEVES_ARM_ONLY=1                   # if 1, sleeves keep only arm-chain bones (no spine/neck bleed)
  PRESERVE_GARMENT_REST=1              # keep garment transform exactly; temporarily move BODY for transfer instead
  KEEP_ORIGINAL_RIG=0                  # if 1, skip weight transfer and export garment as-is (preserves rest/bindposes). Use Unity Drive mode to follow SMPL.
"""

from __future__ import annotations

import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
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
WEIGHT_COPY_METHOD = os.environ.get("WEIGHT_COPY_METHOD", "BVH").strip().upper()
DISALLOW_BONES = tuple(
    s.strip() for s in (os.environ.get("DISALLOW_BONES", "J15,J22,J23") or "").split(",") if s.strip()
)
AUTO_ALIGN = os.environ.get("AUTO_ALIGN", "1").strip() in ("1", "true", "yes", "on")
ALLOW_BONES = tuple(
    s.strip()
    for s in (
        os.environ.get(
            "ALLOW_BONES",
            "J00,J03,J06,J09,J12,J16,J17,J18,J19,J20,J21",
        )
        or ""
    ).split(",")
    if s.strip()
)
REGION_REWEIGHT = os.environ.get("REGION_REWEIGHT", "1").strip() in ("1", "true", "yes", "on")
MAX_WRIST_WEIGHT = float(os.environ.get("MAX_WRIST_WEIGHT", "0.25").strip() or "0.25")
SLEEVES_ARM_ONLY = os.environ.get("SLEEVES_ARM_ONLY", "1").strip() in ("1", "true", "yes", "on")
PRESERVE_GARMENT_REST = os.environ.get("PRESERVE_GARMENT_REST", "1").strip() in ("1", "true", "yes", "on")
KEEP_ORIGINAL_RIG = os.environ.get("KEEP_ORIGINAL_RIG", "0").strip() in ("1", "true", "yes", "on")


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


def world_bbox_center(obj: bpy.types.Object) -> Vector:
    """World-space center of the object bound_box."""
    mw = obj.matrix_world
    pts = [mw @ Vector(corner) for corner in obj.bound_box]
    c = Vector((0.0, 0.0, 0.0))
    for p in pts:
        c += p
    return c * (1.0 / max(1, len(pts)))


def auto_align_garment_to_body(garment: bpy.types.Object, body: bpy.types.Object) -> None:
    """Translate garment so its bbox center matches the body's bbox center (world space)."""
    if not AUTO_ALIGN or PRESERVE_GARMENT_REST:
        return
    try:
        gc = world_bbox_center(garment)
        bc = world_bbox_center(body)
        delta = bc - gc
        garment.location = garment.location + delta
        log(f"AUTO_ALIGN=1: moved garment by {tuple(round(x, 4) for x in delta)}")
    except Exception as e:
        log(f"AUTO_ALIGN failed: {e}")


def align_body_to_garment_temporarily(body: bpy.types.Object, garment: bpy.types.Object) -> Vector | None:
    """
    If PRESERVE_GARMENT_REST is enabled, we keep garment exactly where it imported (rest pose stays identical),
    and temporarily translate the BODY to overlap it for weight transfer. Returns the delta applied to body.
    """
    if not PRESERVE_GARMENT_REST:
        return None
    try:
        bc = world_bbox_center(body)
        gc = world_bbox_center(garment)
        delta = gc - bc
        body.location = body.location + delta
        log(f"PRESERVE_GARMENT_REST=1: moved BODY by {tuple(round(x, 4) for x in delta)} for transfer")
        return delta
    except Exception as e:
        log(f"PRESERVE_GARMENT_REST body-align failed: {e}")
        return None


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


def _barycentric_on_triangle(p: Vector, a: Vector, b: Vector, c: Vector) -> tuple[float, float, float]:
    """Barycentric coords of p for triangle abc (closest point from BVHTree lies on or near this triangle)."""
    v0 = b - a
    v1 = c - a
    v2 = p - a
    d00 = v0.dot(v0)
    d01 = v0.dot(v1)
    d02 = v0.dot(v2)
    d11 = v1.dot(v1)
    d12 = v1.dot(v2)
    denom = d00 * d11 - d01 * d01
    if abs(denom) < 1e-24:
        return (1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0)
    inv = 1.0 / denom
    v = inv * (d11 * d02 - d01 * d12)
    w = inv * (d00 * d12 - d01 * d02)
    u = 1.0 - v - w
    u = max(0.0, min(1.0, u))
    v = max(0.0, min(1.0, v))
    w = max(0.0, min(1.0, w))
    s = u + v + w
    if s < 1e-12:
        return (1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0)
    return (u / s, v / s, w / s)


def _vert_weight_dict(mesh: bpy.types.Mesh, vi: int, idx_to_name: dict[int, str]) -> dict[str, float]:
    out: dict[str, float] = {}
    for ge in mesh.vertices[vi].groups:
        gn = idx_to_name.get(ge.group)
        if gn and ge.weight > 1e-12:
            out[gn] = float(ge.weight)
    return out


def _merge_weight_dicts(parts: list[tuple[dict[str, float], float]]) -> dict[str, float]:
    merged: dict[str, float] = {}
    for d, bw in parts:
        for k, val in d.items():
            merged[k] = merged.get(k, 0.0) + val * bw
    return merged


def copy_weights_bvh_nearest_triangle(
    garment: bpy.types.Object, body: bpy.types.Object, arm: bpy.types.Object
) -> None:
    """
    For each garment vertex, find closest point on SMPL body mesh surface, take the hit triangle,
    blend vertex weights from the three corners (barycentric). Better for sleeves than nearest *vertex*.
    """
    log("BVH: copying weights from nearest body triangle (surface / barycentric)…")

    clear_vertex_groups(garment)
    ensure_vertex_groups_for_armature_and_body(garment, arm, body)

    body_mesh = body.data
    garment_mesh = garment.data
    mw = body.matrix_world
    mw_g = garment.matrix_world

    body_mesh.calc_loop_triangles()
    verts = [mw @ body_mesh.vertices[i].co for i in range(len(body_mesh.vertices))]
    polygons: list[list[int]] = [list(lt.vertices) for lt in body_mesh.loop_triangles]

    if not polygons:
        log("BVH: no loop triangles — falling back to nearest-vertex copy.")
        copy_weights_nearest_body_vertex(garment, body, arm)
        return

    try:
        bvhtree = BVHTree.FromPolygons(verts, polygons)
    except Exception as e:
        log(f"BVH: FromPolygons failed ({e}) — falling back to nearest-vertex copy.")
        copy_weights_nearest_body_vertex(garment, body, arm)
        return

    idx_to_name = {g.index: g.name for g in body.vertex_groups}
    name_to_garment_vg = {g.name: g for g in garment.vertex_groups}

    for vi, gv in enumerate(garment_mesh.vertices):
        co_w = mw_g @ gv.co
        nearest = bvhtree.find_nearest(co_w)
        if nearest is None:
            continue
        loc, _normal, fi, _dist = nearest
        if fi is None or fi < 0 or fi >= len(polygons):
            continue
        tri = polygons[fi]
        if len(tri) < 3:
            continue
        i0, i1, i2 = tri[0], tri[1], tri[2]
        a, b, c = verts[i0], verts[i1], verts[i2]
        u, v, w = _barycentric_on_triangle(loc, a, b, c)

        d0 = _vert_weight_dict(body_mesh, i0, idx_to_name)
        d1 = _vert_weight_dict(body_mesh, i1, idx_to_name)
        d2 = _vert_weight_dict(body_mesh, i2, idx_to_name)
        merged = _merge_weight_dicts([(d0, u), (d1, v), (d2, w)])

        for gname, wt in merged.items():
            if wt <= 1e-12:
                continue
            vg = name_to_garment_vg.get(gname)
            if vg is None:
                vg = garment.vertex_groups.new(name=gname)
                name_to_garment_vg[gname] = vg
            vg.add([vi], wt, "REPLACE")

    garment_mesh.update()


def copy_weights_post_dt(garment: bpy.types.Object, body: bpy.types.Object, arm: bpy.types.Object) -> None:
    """Dispatch by WEIGHT_COPY_METHOD (BVH default)."""
    if WEIGHT_COPY_METHOD == "VERTEX":
        copy_weights_nearest_body_vertex(garment, body, arm)
    else:
        copy_weights_bvh_nearest_triangle(garment, body, arm)


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


def disallow_vertex_groups_and_renormalize(garment: bpy.types.Object, fallback_group: str = "J09") -> None:
    """
    Zero out weights for DISALLOW_BONES (e.g. head/hands) then renormalize per-vertex.
    If a vertex would end up with no weights, assign it fully to fallback_group.
    """
    if not DISALLOW_BONES:
        return

    mesh = garment.data
    if mesh is None or len(mesh.vertices) == 0:
        return

    # Map group name -> index for fast filtering.
    name_to_idx: dict[str, int] = {g.name: g.index for g in garment.vertex_groups}
    disallow_idx = {name_to_idx[n] for n in DISALLOW_BONES if n in name_to_idx}
    if not disallow_idx:
        log(f"DISALLOW_BONES set but none matched existing groups: {DISALLOW_BONES}")
        return

    fb_idx = name_to_idx.get(fallback_group)
    if fb_idx is None:
        # Create fallback group if missing.
        vg = garment.vertex_groups.new(name=fallback_group)
        fb_idx = vg.index
        name_to_idx[fallback_group] = fb_idx

    # For each vertex: collect weights, drop disallowed, renormalize.
    for v in mesh.vertices:
        kept: list[tuple[int, float]] = []
        for ge in v.groups:
            if ge.group in disallow_idx:
                continue
            if ge.weight > 1e-12:
                kept.append((ge.group, float(ge.weight)))

        if not kept:
            # Set fallback group to 1.0.
            garment.vertex_groups[fb_idx].add([v.index], 1.0, "REPLACE")
            continue

        s = sum(w for _gi, w in kept)
        if s < 1e-12:
            garment.vertex_groups[fb_idx].add([v.index], 1.0, "REPLACE")
            continue

        inv = 1.0 / s
        for gi, w in kept:
            garment.vertex_groups[gi].add([v.index], w * inv, "REPLACE")

    # Finally: remove the disallowed groups entirely so Unity cannot ever pick them up.
    # (Some importers can keep stale weights even if they are near-zero.)
    removed = 0
    for n in DISALLOW_BONES:
        vg = garment.vertex_groups.get(n)
        if vg is not None:
            garment.vertex_groups.remove(vg)
            removed += 1

    log(f"Disallowed groups removed ({removed}) and weights renormalized: {DISALLOW_BONES}")


def allowlist_vertex_groups(garment: bpy.types.Object, fallback_group: str = "J09") -> None:
    """
    Keep weights only on ALLOW_BONES (if provided). Removes all other vertex groups.
    This is useful for shirts so they can't ever pick up leg weights.
    """
    if not ALLOW_BONES:
        return

    mesh = garment.data
    if mesh is None or len(mesh.vertices) == 0:
        return

    allowed = set(ALLOW_BONES)
    name_to_idx: dict[str, int] = {g.name: g.index for g in garment.vertex_groups}
    fb_idx = name_to_idx.get(fallback_group)
    if fb_idx is None and fallback_group in allowed:
        vg = garment.vertex_groups.new(name=fallback_group)
        fb_idx = vg.index
        name_to_idx[fallback_group] = fb_idx

    # For each vertex: keep only allowed weights, renormalize, fallback if empty.
    for v in mesh.vertices:
        kept: list[tuple[int, float]] = []
        for ge in v.groups:
            gn = garment.vertex_groups[ge.group].name
            if gn not in allowed:
                continue
            if ge.weight > 1e-12:
                kept.append((ge.group, float(ge.weight)))

        if not kept:
            if fb_idx is not None:
                garment.vertex_groups[fb_idx].add([v.index], 1.0, "REPLACE")
            continue

        s = sum(w for _gi, w in kept)
        if s < 1e-12:
            if fb_idx is not None:
                garment.vertex_groups[fb_idx].add([v.index], 1.0, "REPLACE")
            continue
        inv = 1.0 / s
        for gi, w in kept:
            garment.vertex_groups[gi].add([v.index], w * inv, "REPLACE")

    # Remove non-allowed groups entirely.
    removed = 0
    for g in list(garment.vertex_groups):
        if g.name not in allowed:
            garment.vertex_groups.remove(g)
            removed += 1
    log(f"ALLOW_BONES applied; removed {removed} non-allowed groups; kept={sorted(list(allowed))}")

def _bone_world_pos(arm: bpy.types.Object, bone_name: str) -> Vector | None:
    pb = arm.pose.bones.get(bone_name) if arm and arm.pose else None
    if pb is None:
        return None
    return arm.matrix_world @ pb.head


def _region_sets():
    # Minimal sets for shirts (torso + 3-bone arm chains). Add shoulders/spine as stabilizers.
    torso = {"J00", "J03", "J06", "J09", "J12"}
    left = {"J16", "J18", "J20"}
    right = {"J17", "J19", "J21"}
    if not SLEEVES_ARM_ONLY:
        # Optionally allow a little torso bleed for softer shoulder transitions.
        left |= {"J12", "J09"}
        right |= {"J12", "J09"}
    return torso, left, right


def region_reweight_shirt(garment: bpy.types.Object, arm: bpy.types.Object) -> None:
    """
    Heuristic post-pass:
    - classify each vertex by nearest key bone (torso vs left sleeve vs right sleeve)
    - keep only that region's bones
    - cap wrist weights and push excess up-chain
    This prevents the \"whole shirt follows wrists\" failure mode.
    """
    if not REGION_REWEIGHT:
        return

    mesh = garment.data
    if mesh is None or len(mesh.vertices) == 0:
        return

    torso_set, left_set, right_set = _region_sets()

    # Cache key bone positions (world).
    keys = {
        "torso": ["J09", "J12", "J03"],
        "left": ["J16", "J18", "J20"],
        "right": ["J17", "J19", "J21"],
    }
    key_pos: dict[str, list[Vector]] = {"torso": [], "left": [], "right": []}
    for k, names in keys.items():
        for n in names:
            p = _bone_world_pos(arm, n)
            if p is not None:
                key_pos[k].append(p)

    if not key_pos["torso"]:
        log("REGION_REWEIGHT skipped: could not resolve torso key bones in armature.")
        return

    name_to_idx = {g.name: g.index for g in garment.vertex_groups}

    def group_name_from_index(i: int) -> str:
        return garment.vertex_groups[i].name

    def nearest_region(p: Vector) -> str:
        # If arm keys missing, default to torso.
        best = ("torso", 1e30)
        for region in ("torso", "left", "right"):
            pts = key_pos.get(region) or []
            if not pts:
                continue
            d = min((p - q).length_squared for q in pts)
            if d < best[1]:
                best = (region, d)
        return best[0]

    mw = garment.matrix_world
    max_wrist = max(0.0, min(1.0, MAX_WRIST_WEIGHT))

    for v in mesh.vertices:
        p = mw @ v.co
        region = nearest_region(p)
        allowed = torso_set if region == "torso" else left_set if region == "left" else right_set

        # Read current weights for allowed groups only.
        kept: dict[str, float] = {}
        for ge in v.groups:
            gn = group_name_from_index(ge.group)
            if gn in allowed and ge.weight > 1e-12:
                kept[gn] = kept.get(gn, 0.0) + float(ge.weight)

        if not kept:
            # fallback: spine2
            kept["J09"] = 1.0

        # Cap wrist on sleeve verts.
        if region == "left":
            w = kept.get("J20", 0.0)
            if w > max_wrist:
                excess = w - max_wrist
                kept["J20"] = max_wrist
                kept["J18"] = kept.get("J18", 0.0) + excess * 0.7
                kept["J16"] = kept.get("J16", 0.0) + excess * 0.3
        elif region == "right":
            w = kept.get("J21", 0.0)
            if w > max_wrist:
                excess = w - max_wrist
                kept["J21"] = max_wrist
                kept["J19"] = kept.get("J19", 0.0) + excess * 0.7
                kept["J17"] = kept.get("J17", 0.0) + excess * 0.3

        # Normalize and write back (REPLACE).
        s = sum(kept.values())
        if s < 1e-12:
            kept = {"J09": 1.0}
            s = 1.0

        inv = 1.0 / s
        for gn, val in kept.items():
            if gn not in name_to_idx:
                garment.vertex_groups.new(name=gn)
                name_to_idx = {g.name: g.index for g in garment.vertex_groups}
            garment.vertex_groups[name_to_idx[gn]].add([v.index], val * inv, "REPLACE")

    log(f"REGION_REWEIGHT applied (MAX_WRIST_WEIGHT={max_wrist}).")


def delete_extra_armatures(keep: bpy.types.Object) -> None:
    for o in list(objs_by_type("ARMATURE")):
        if o != keep:
            log(f"Removing extra armature: {o.name}")
            bpy.data.objects.remove(o, do_unlink=True)


def find_garment_armature_for_mesh(mesh_obj: bpy.types.Object) -> bpy.types.Object | None:
    """Best-effort: find the armature that actually skins this mesh."""
    if mesh_obj is None:
        return None

    # 1) Armature modifier target
    for mod in mesh_obj.modifiers:
        if mod.type == "ARMATURE" and getattr(mod, "object", None) is not None:
            return mod.object

    # 2) Parent chain
    p = mesh_obj.parent
    while p is not None:
        if p.type == "ARMATURE":
            return p
        p = p.parent

    return None


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

    if KEEP_ORIGINAL_RIG:
        garment_arm = find_garment_armature_for_mesh(garment)
        if garment_arm is None:
            log(
                "KEEP_ORIGINAL_RIG=1 requested, but garment has no armature modifier/parent. "
                "Cannot preserve rig; aborting."
            )
            return 2

        log(
            f"KEEP_ORIGINAL_RIG=1: exporting garment with its OWN rig '{garment_arm.name}' "
            "(no weight transfer; preserves rest/bindposes)."
        )
        export_fbx(garment_arm, [garment])
        log("Done.")
        return 0

    # Normal pipeline: keep SMPL armature, delete garment rigs, transfer weights to SMPL bone names.
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
    auto_align_garment_to_body(garment, body)

    body_delta = align_body_to_garment_temporarily(body, garment)

    dt = add_data_transfer(garment, body, DT_VERT_MAPPING)
    bpy.context.view_layer.depsgraph.update()
    apply_modifier(garment, dt.name)

    if body_delta is not None:
        body.location = body.location - body_delta
        log("PRESERVE_GARMENT_REST=1: restored BODY transform after transfer")

    infl = count_influencing_vertex_groups(garment)
    log(f"After Data Transfer: influencing vertex groups (any vert) = {infl}")

    use_kd = FORCE_KD or (not SKIP_KD_FALLBACK and infl < MIN_INFLUENCING_VGROUPS)
    if use_kd:
        if FORCE_KD:
            log(f"FORCE_KD=1 — weight copy WEIGHT_COPY_METHOD={WEIGHT_COPY_METHOD} (BVH=surface triangle, VERTEX=nearest body vert).")
        else:
            log(
                f"Influencing groups {infl} < {MIN_INFLUENCING_VGROUPS} — running weight copy ({WEIGHT_COPY_METHOD})."
            )
        copy_weights_post_dt(garment, body, arm)
        infl2 = count_influencing_vertex_groups(garment)
        log(f"After weight copy: influencing vertex groups = {infl2}")

    remove_orphan_vertex_groups_not_on_armature(garment, arm)
    disallow_vertex_groups_and_renormalize(garment, fallback_group="J09")
    allowlist_vertex_groups(garment, fallback_group="J09")
    region_reweight_shirt(garment, arm)
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
