using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SartorialMirror.EditorTools
{
    public static class GarmentWeightAudit
    {
        private const string PreparedFbxPath = "Assets/garments_prepared/Flannel_SMPL_Skinned.fbx";

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            // If the user doesn't see the menu item, the usual cause is: opened the wrong Unity project,
            // or compilation errors prevented Editor scripts from loading. This message confirms load.
            Debug.Log("[GarmentWeightAudit] Loaded. Menu: Tools/SartorialMirror/Audit/Prepared Garment Weights (Flannel_SMPL_Skinned)");
        }

        [MenuItem("Tools/SartorialMirror/Audit/Prepared Garment Weights (Flannel_SMPL_Skinned)")]
        public static void AuditPreparedGarmentWeights()
        {
            var modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PreparedFbxPath);
            if (modelRoot == null)
            {
                Debug.LogError($"[GarmentWeightAudit] Missing asset at '{PreparedFbxPath}'.");
                return;
            }

            var smrs = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs == null || smrs.Length == 0)
            {
                Debug.LogError($"[GarmentWeightAudit] No SkinnedMeshRenderer found under '{PreparedFbxPath}'.", modelRoot);
                return;
            }

            Debug.Log($"──────── [GarmentWeightAudit] {PreparedFbxPath} ────────", modelRoot);

            foreach (var smr in smrs)
            {
                if (smr == null) continue;
                var mesh = smr.sharedMesh;
                if (mesh == null)
                {
                    Debug.LogWarning($"[GarmentWeightAudit] SMR '{smr.name}': sharedMesh is null.", smr);
                    continue;
                }

                var bones = smr.bones ?? Array.Empty<Transform>();
                var bws = mesh.boneWeights;

                if (bws == null || bws.Length == 0)
                {
                    Debug.LogError(
                        $"[GarmentWeightAudit] SMR '{smr.name}' mesh '{mesh.name}': has 0 boneWeights. Unity imported this as effectively rigid.",
                        smr);
                    continue;
                }

                var totals = new float[bones.Length];
                float sum = 0f;
                for (int i = 0; i < bws.Length; i++)
                {
                    var bw = bws[i];
                    Acc(totals, bw.boneIndex0, bw.weight0, ref sum);
                    Acc(totals, bw.boneIndex1, bw.weight1, ref sum);
                    Acc(totals, bw.boneIndex2, bw.weight2, ref sum);
                    Acc(totals, bw.boneIndex3, bw.weight3, ref sum);
                }

                int influencingBones = 0;
                var top = new List<(int idx, float w)>(bones.Length);
                for (int bi = 0; bi < totals.Length; bi++)
                {
                    if (totals[bi] > 1e-6f)
                    {
                        influencingBones++;
                        top.Add((bi, totals[bi]));
                    }
                }

                top.Sort((a, b) => b.w.CompareTo(a.w));
                int nTop = Mathf.Min(8, top.Count);

                string TopList()
                {
                    if (sum <= 1e-8f || nTop == 0) return "(none)";
                    var parts = new string[nTop];
                    for (int i = 0; i < nTop; i++)
                    {
                        int idx = top[i].idx;
                        string bn = (idx >= 0 && idx < bones.Length && bones[idx] != null) ? bones[idx].name : $"boneIndex{idx}";
                        float pct = (top[i].w / sum) * 100f;
                        parts[i] = $"{bn}={pct:F1}%";
                    }
                    return string.Join(", ", parts);
                }

                Debug.Log(
                    $"[GarmentWeightAudit] SMR '{smr.name}' mesh '{mesh.name}': " +
                    $"verts={mesh.vertexCount}, bonesArray={bones.Length}, bindPoses={mesh.bindposes?.Length ?? 0}, " +
                    $"boneWeights={bws.Length}, influencingBones={influencingBones}, top={TopList()}",
                    smr);

                if (influencingBones <= 1)
                {
                    Debug.LogError(
                        "[GarmentWeightAudit] FIX BLOCKER: Unity imported this mesh with weights on only one bone. " +
                        "Do not tune drive/remap/twist until this is resolved (export/import or wrong asset being referenced).",
                        smr);
                }
            }
        }

        private static void Acc(float[] totals, int idx, float w, ref float sum)
        {
            if (totals == null) return;
            if (idx < 0 || idx >= totals.Length) return;
            if (w <= 0f) return;
            totals[idx] += w;
            sum += w;
        }
    }
}

