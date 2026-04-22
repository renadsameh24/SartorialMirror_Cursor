using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SartorialMirror.EditorTools
{
    public static class GarmentBindPoseAudit
    {
        private const string SmplFbxPath = "Assets/SMPL/Models/SMPL_neutral_rig_GOLDEN.fbx";
        private const string GarmentFbxPath = "Assets/garments_prepared/Flannel_SMPL_Skinned.fbx";

        [MenuItem("Tools/SartorialMirror/Audit/Bindposes vs SMPL (Flannel_SMPL_Skinned)")]
        public static void Audit()
        {
            var smplRoot = AssetDatabase.LoadAssetAtPath<GameObject>(SmplFbxPath);
            var garmentRoot = AssetDatabase.LoadAssetAtPath<GameObject>(GarmentFbxPath);

            if (smplRoot == null)
            {
                Debug.LogError($"[BindPoseAudit] Missing SMPL FBX at '{SmplFbxPath}'.");
                return;
            }
            if (garmentRoot == null)
            {
                Debug.LogError($"[BindPoseAudit] Missing garment FBX at '{GarmentFbxPath}'.");
                return;
            }

            var smplSmr = FindFirstSmr(smplRoot);
            var garmentSmr = FindFirstSmr(garmentRoot);

            if (smplSmr == null || smplSmr.sharedMesh == null)
            {
                Debug.LogError($"[BindPoseAudit] No SMPL SkinnedMeshRenderer found in '{SmplFbxPath}'.", smplRoot);
                return;
            }
            if (garmentSmr == null || garmentSmr.sharedMesh == null)
            {
                Debug.LogError($"[BindPoseAudit] No garment SkinnedMeshRenderer found in '{GarmentFbxPath}'.", garmentRoot);
                return;
            }

            var smplMesh = smplSmr.sharedMesh;
            var garmentMesh = garmentSmr.sharedMesh;

            Debug.Log($"──────── [BindPoseAudit] START ────────");
            Debug.Log($"[BindPoseAudit] SMPL: path='{SmplFbxPath}', mesh='{smplMesh.name}', bones={smplSmr.bones?.Length ?? 0}, bindposes={smplMesh.bindposes?.Length ?? 0}");
            Debug.Log($"[BindPoseAudit] Garment: path='{GarmentFbxPath}', mesh='{garmentMesh.name}', bones={garmentSmr.bones?.Length ?? 0}, bindposes={garmentMesh.bindposes?.Length ?? 0}");

            // Quick structural checks.
            if ((garmentSmr.bones?.Length ?? 0) != (garmentMesh.bindposes?.Length ?? 0))
            {
                Debug.LogError("[BindPoseAudit] Garment bones[] length != mesh.bindposes length. Unity skinning will be unstable. Re-export FBX with a proper armature + skin cluster.", garmentRoot);
            }

            // Check for mirrored/negative scale in garment bone hierarchy (common cause of 'flipped' limbs).
            var negScaleBones = CountNegativeDeterminantBones(garmentSmr.bones);
            if (negScaleBones > 0)
            {
                Debug.LogError($"[BindPoseAudit] Garment has {negScaleBones} bone transforms with negative determinant (mirrored). This often causes flipped arms/twists. Fix in Blender: apply transforms, ensure armature/bones have positive scale, re-export.", garmentRoot);
            }

            // Compare key SMPL bones by name if present.
            var smplByName = IndexBonesByName(smplSmr.bones);
            var garmentByName = IndexBonesByName(garmentSmr.bones);
            string[] keyBones = { "J00", "J16", "J17", "J18", "J19", "J20", "J21" };

            foreach (var k in keyBones)
            {
                bool hasSmpl = TryGetBone(smplByName, k, out var sb);
                bool hasGarment = TryGetBone(garmentByName, k, out var gb);
                if (!hasSmpl || !hasGarment)
                {
                    string side = !hasSmpl && !hasGarment ? "SMPL & garment" : (!hasSmpl ? "SMPL" : "garment");
                    Debug.LogWarning($"[BindPoseAudit] Missing key bone '{k}' on {side}.");
                    continue;
                }

                // In FBX assets, bones are in local space under their respective roots.
                // We compare local axes similarity to detect 180° roll mismatches.
                float fwd = Vector3.Angle(sb.localRotation * Vector3.forward, gb.localRotation * Vector3.forward);
                float up = Vector3.Angle(sb.localRotation * Vector3.up, gb.localRotation * Vector3.up);
                float right = Vector3.Angle(sb.localRotation * Vector3.right, gb.localRotation * Vector3.right);

                if (fwd > 45f || up > 45f || right > 45f)
                {
                    Debug.LogWarning($"[BindPoseAudit] Bone '{k}' local axes differ сильно (fwd={fwd:F1}°, up={up:F1}°, right={right:F1}°). This indicates bone-roll mismatch in the exported garment rig. Prefer SMPL-skinned remap (no garment rig), or re-export with matching bone orientations.", garmentRoot);
                }
            }

            // Compare mesh bounds magnitude (imported) as a proxy for unit scale mismatch.
            float smplMag = smplMesh.bounds.size.magnitude;
            float garmentMag = garmentMesh.bounds.size.magnitude;
            if (smplMag > 1e-6f && garmentMag > 1e-6f)
            {
                float ratio = smplMag / garmentMag;
                Debug.Log($"[BindPoseAudit] Imported bounds magnitude ratio (SMPL/garment) = {ratio:F4} (smpl={smplMag:F4}, garment={garmentMag:F4})");
                if (ratio < 0.2f || ratio > 5f)
                    Debug.LogWarning("[BindPoseAudit] Large bounds ratio suggests unit scale mismatch between SMPL FBX and garment FBX. Fix by applying scale in Blender or exporting both with consistent units (meters) and global scale=1.", garmentRoot);
            }

            Debug.Log($"──────── [BindPoseAudit] END ────────");
        }

        private static SkinnedMeshRenderer FindFirstSmr(GameObject root)
        {
            if (root == null) return null;
            return root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        private static Dictionary<string, Transform> IndexBonesByName(Transform[] bones)
        {
            var d = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            if (bones == null) return d;
            foreach (var b in bones)
            {
                if (b == null) continue;
                if (!d.ContainsKey(b.name)) d.Add(b.name, b);
            }
            return d;
        }

        private static bool TryGetBone(Dictionary<string, Transform> d, string name, out Transform t)
        {
            t = null;
            if (d == null || string.IsNullOrEmpty(name)) return false;
            return d.TryGetValue(name, out t) && t != null;
        }

        private static int CountNegativeDeterminantBones(Transform[] bones)
        {
            if (bones == null) return 0;
            int c = 0;
            foreach (var b in bones)
            {
                if (b == null) continue;
                var s = b.localScale;
                // Negative determinant if odd number of negative scale axes.
                int neg = (s.x < 0f ? 1 : 0) + (s.y < 0f ? 1 : 0) + (s.z < 0f ? 1 : 0);
                if ((neg % 2) == 1) c++;
            }
            return c;
        }
    }
}

