using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class SmplGarmentManager : MonoBehaviour
{
    // Maps common SMPL/humanoid bone names to this project's SMPL rig joint names (Jxx).
    // This project’s rig uses J00.. naming (see existing scripts referencing J16/J18/etc).
    private static readonly Dictionary<string, string> SmplAliasToJ = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core
        ["pelvis"] = "J00",
        ["hips"] = "J00",
        ["hip"] = "J00",
        ["spine"] = "J03",
        ["spine1"] = "J06",
        ["spine2"] = "J09",
        ["neck"] = "J12",
        ["head"] = "J15",

        // Legs
        ["l_hip"] = "J01",
        ["left_hip"] = "J01",
        ["r_hip"] = "J02",
        ["right_hip"] = "J02",
        ["l_knee"] = "J04",
        ["left_knee"] = "J04",
        ["r_knee"] = "J05",
        ["right_knee"] = "J05",
        ["l_ankle"] = "J07",
        ["left_ankle"] = "J07",
        ["r_ankle"] = "J08",
        ["right_ankle"] = "J08",

        // Arms (matches existing pipeline scripts)
        ["l_shoulder"] = "J16",
        ["left_shoulder"] = "J16",
        ["r_shoulder"] = "J17",
        ["right_shoulder"] = "J17",
        ["l_elbow"] = "J18",
        ["left_elbow"] = "J18",
        ["r_elbow"] = "J19",
        ["right_elbow"] = "J19",
        ["l_wrist"] = "J20",
        ["left_wrist"] = "J20",
        ["r_wrist"] = "J21",
        ["right_wrist"] = "J21",
    };
    [Header("SMPL Target")]
    [Tooltip("If empty, finds GameObject by name at runtime.")]
    public Transform smplRoot;

    [Tooltip("Fallback: name of the SMPL root in the scene.")]
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";

    [Tooltip("Optional parent under SMPL root to keep garments organized.")]
    public string garmentsParentName = "_Garments";

    [Header("Alignment")]
    [Tooltip("If true, after spawning we snap the garment to the SMPL pelvis/hips to correct FBX root offsets.")]
    public bool snapGarmentToSmplPelvis = true;

    [Tooltip("Candidate bone names for pelvis/hips on the SMPL rig.")]
    public string[] smplPelvisBoneNames = { "J00", "pelvis", "Hips", "hips", "Pelvis" };

    [Tooltip("Candidate bone names for pelvis/hips on the garment rig.")]
    public string[] garmentPelvisBoneNames = { "pelvis", "Hips", "hips", "Pelvis" };

    [Header("Catalog")]
    public GarmentCatalog catalog;

    [Header("Runtime")]
    [SerializeField] private int activeIndex = -1;
    public int ActiveIndex => activeIndex;
    public GameObject ActiveGarmentInstance { get; private set; }

    [SerializeField] private int activeColorVariantIndex = 0;
    public int ActiveColorVariantIndex => activeColorVariantIndex;

    [Header("Diagnostics")]
    public bool logMissingBoneNames = true;
    public bool logSpawnFailures = true;

    private Transform garmentsParent;
    private Dictionary<string, Transform> smplBonesByName;

    void Awake()
    {
        EnsureSmplRoot();
        EnsureBoneMap();
        EnsureGarmentsParent();
    }

    public bool EnsureSmplRoot()
    {
        if (smplRoot != null) return true;
        var go = GameObject.Find(smplRootName);
        if (go == null) return false;
        smplRoot = go.transform;
        return true;
    }

    void EnsureGarmentsParent()
    {
        if (smplRoot == null) return;

        if (garmentsParent != null) return;

        var existing = smplRoot.Find(garmentsParentName);
        if (existing != null)
        {
            garmentsParent = existing;
            return;
        }

        var go = new GameObject(garmentsParentName);
        garmentsParent = go.transform;
        garmentsParent.SetParent(smplRoot, false);
        garmentsParent.localPosition = Vector3.zero;
        garmentsParent.localRotation = Quaternion.identity;
        garmentsParent.localScale = Vector3.one;
    }

    void EnsureBoneMap()
    {
        smplBonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        if (smplRoot == null) return;

        foreach (var t in smplRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!t) continue;
            // Add both raw and normalized keys to tolerate FBX importer prefixes.
            var raw = t.name;
            var norm = NormalizeBoneName(raw);
            if (!smplBonesByName.ContainsKey(raw))
                smplBonesByName.Add(raw, t);
            if (!string.IsNullOrEmpty(norm) && !smplBonesByName.ContainsKey(norm))
                smplBonesByName.Add(norm, t);
        }
    }

    public bool HasCatalog => catalog != null && catalog.garments != null && catalog.garments.Count > 0;

    public bool TrySetActive(int index)
    {
        if (!EnsureSmplRoot())
        {
            if (logSpawnFailures)
                Debug.LogWarning($"Garment spawn failed: SMPL root '{smplRootName}' not found in scene.", this);
            return false;
        }
        EnsureBoneMap();
        EnsureGarmentsParent();

        if (catalog == null || catalog.garments == null)
        {
            if (logSpawnFailures)
                Debug.LogWarning("Garment spawn failed: GarmentCatalog is not assigned (or garments list is null).", this);
            return false;
        }
        if (catalog.garments.Count == 0)
        {
            if (logSpawnFailures)
                Debug.LogWarning("Garment spawn failed: GarmentCatalog has 0 entries.", this);
            return false;
        }
        if (index < 0 || index >= catalog.garments.Count)
        {
            if (logSpawnFailures)
                Debug.LogWarning($"Garment spawn failed: index {index} is out of range (0..{catalog.garments.Count - 1}).", this);
            return false;
        }

        var entry = catalog.garments[index];
        if (entry == null)
        {
            if (logSpawnFailures)
                Debug.LogWarning($"Garment spawn failed: catalog entry {index} is null.", this);
            return false;
        }
        if (entry.garmentPrefab == null)
        {
            if (logSpawnFailures)
                Debug.LogWarning($"Garment spawn failed: catalog entry {index} '{entry.displayName}' has no prefab assigned.", this);
            return false;
        }

        ClearActive();

        ActiveGarmentInstance = Instantiate(entry.garmentPrefab, garmentsParent);
        ActiveGarmentInstance.name = $"Garment_{index}_{entry.garmentPrefab.name}";
        ActiveGarmentInstance.AddComponent<GarmentInstanceTag>();
        ActiveGarmentInstance.transform.localPosition = Vector3.zero;
        ActiveGarmentInstance.transform.localRotation = Quaternion.identity;
        ActiveGarmentInstance.transform.localScale = Vector3.one;

        RemapAllSkinnedMeshesToSmpl(ActiveGarmentInstance);
        if (snapGarmentToSmplPelvis)
            SnapGarmentRootToSmplPelvis(ActiveGarmentInstance);

        // Set active index before applying variants (ApplyActiveColorVariant reads activeIndex).
        activeIndex = index;

        activeColorVariantIndex = Mathf.Max(0, entry.defaultColorVariantIndex);
        ApplyActiveColorVariant();
        return true;
    }

    public void ClearActive()
    {
        activeIndex = -1;
        activeColorVariantIndex = 0;
        if (ActiveGarmentInstance != null)
        {
            Destroy(ActiveGarmentInstance);
            ActiveGarmentInstance = null;
        }
    }

    public bool TrySetColorVariant(int variantIndex)
    {
        if (activeIndex < 0) return false;
        if (catalog == null || catalog.garments == null) return false;
        if (activeIndex >= catalog.garments.Count) return false;

        var entry = catalog.garments[activeIndex];
        if (entry == null || entry.colorVariants == null || entry.colorVariants.Count == 0) return false;
        if (variantIndex < 0 || variantIndex >= entry.colorVariants.Count) return false;

        activeColorVariantIndex = variantIndex;
        ApplyActiveColorVariant();
        return true;
    }

    public void CycleColorVariant(int delta)
    {
        if (activeIndex < 0) return;
        var entry = catalog?.garments?[activeIndex];
        if (entry == null || entry.colorVariants == null || entry.colorVariants.Count == 0) return;

        int n = entry.colorVariants.Count;
        activeColorVariantIndex = (activeColorVariantIndex + delta) % n;
        if (activeColorVariantIndex < 0) activeColorVariantIndex += n;
        ApplyActiveColorVariant();
    }

    void ApplyActiveColorVariant()
    {
        if (ActiveGarmentInstance == null) return;
        if (activeIndex < 0) return;
        var entry = catalog?.garments?[activeIndex];
        if (entry == null || entry.colorVariants == null || entry.colorVariants.Count == 0) return;

        int idx = Mathf.Clamp(activeColorVariantIndex, 0, entry.colorVariants.Count - 1);
        GarmentMaterialTint.Apply(ActiveGarmentInstance, entry.colorVariants[idx]);
    }

    void RemapAllSkinnedMeshesToSmpl(GameObject garmentRoot)
    {
        if (garmentRoot == null || smplBonesByName == null) return;

        var skinned = garmentRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in skinned)
        {
            if (!smr) continue;
            RemapSkinnedMeshToSmpl(smr);
            LogTopWeightInfluences(smr);
        }
    }

    void SnapGarmentRootToSmplPelvis(GameObject garmentRoot)
    {
        if (garmentRoot == null || smplRoot == null) return;

        var smplPelvis = FindFirstByNames(smplRoot, smplPelvisBoneNames);
        var garmentPelvis = FindFirstByNames(garmentRoot.transform, garmentPelvisBoneNames);

        // If garment doesn't expose pelvis/hips as a Transform, try the rootBone of any SkinnedMeshRenderer.
        if (garmentPelvis == null)
        {
            var smr = garmentRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.rootBone != null)
                garmentPelvis = smr.rootBone;
        }

        if (smplPelvis == null || garmentPelvis == null) return;

        // Move the whole instance so garment pelvis coincides with SMPL pelvis.
        Vector3 delta = smplPelvis.position - garmentPelvis.position;
        garmentRoot.transform.position += delta;
    }

    void RemapSkinnedMeshToSmpl(SkinnedMeshRenderer smr)
    {
        // 1) Remap root bone if present
        int mappedCount = 0;
        int totalCount = 0;

        if (smr.rootBone != null && smplBonesByName.TryGetValue(smr.rootBone.name, out var smplRootBone))
        {
            smr.rootBone = smplRootBone;
            mappedCount++;
        }
        else if (smr.rootBone != null)
        {
            var key = ResolveSmplKey(smr.rootBone.name);
            if (!string.IsNullOrEmpty(key) && smplBonesByName.TryGetValue(key, out smplRootBone))
            {
                smr.rootBone = smplRootBone;
                mappedCount++;
            }
        }

        // 2) Remap all bones by name
        var bones = smr.bones;
        if (bones == null || bones.Length == 0) return;

        bool anyMissing = false;
        HashSet<string> missingNames = null;
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            if (b == null) { anyMissing = true; continue; }
            totalCount++;

            if (smplBonesByName.TryGetValue(b.name, out var smplBone))
            {
                bones[i] = smplBone;
                mappedCount++;
            }
            else
            {
                var key = ResolveSmplKey(b.name);
                if (!string.IsNullOrEmpty(key) && smplBonesByName.TryGetValue(key, out smplBone))
                {
                    bones[i] = smplBone;
                    mappedCount++;
                    continue;
                }
                anyMissing = true;
                if (logMissingBoneNames)
                {
                    missingNames ??= new HashSet<string>(StringComparer.Ordinal);
                    missingNames.Add(b.name);
                }
            }
        }

        smr.bones = bones;

        if (mappedCount == 0)
        {
            Debug.LogWarning(
                $"Garment bone remap: {smr.name} mapped 0/{Mathf.Max(1, totalCount)} bones onto SMPL. " +
                $"This usually means bone names don't match between the garment FBX and the Unity SMPL rig. " +
                $"Example garment bone: {GetFirstNonNullName(smr.bones)}; example SMPL bone: {GetFirstKey(smplBonesByName)}.",
                smr);
        }

        // 3) If the garment isn't authored in SMPL space, you may still need a one-time local offset.
        // We intentionally don't apply offsets here to keep the pipeline deterministic.
        if (anyMissing)
        {
            if (logMissingBoneNames && missingNames != null && missingNames.Count > 0)
            {
                Debug.LogWarning(
                    $"Garment bone remap: {smr.name} is missing {missingNames.Count} SMPL bone(s). " +
                    $"Example: {GetFirst(missingNames)}. (Garment must be skinned to SMPL bone names for best results.)",
                    smr);
            }
        }
    }

    static string GetFirst(HashSet<string> set)
    {
        foreach (var s in set) return s;
        return "";
    }

    static string GetFirstNonNullName(Transform[] bones)
    {
        if (bones == null) return "";
        foreach (var b in bones)
        {
            if (b != null) return b.name;
        }
        return "";
    }

    static string GetFirstKey(Dictionary<string, Transform> dict)
    {
        if (dict == null) return "";
        foreach (var kv in dict) return kv.Key;
        return "";
    }

    static Transform FindFirstByNames(Transform root, string[] names)
    {
        if (root == null || names == null || names.Length == 0) return null;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            for (int i = 0; i < names.Length; i++)
            {
                if (t.name == names[i]) return t;
            }
        }

        return null;
    }

    static string NormalizeBoneName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        // Unity FBX importer sometimes prefixes names like "Armature|pelvis" or "mixamorig:Hips".
        int pipe = name.LastIndexOf('|');
        if (pipe >= 0 && pipe + 1 < name.Length) name = name[(pipe + 1)..];
        int colon = name.LastIndexOf(':');
        if (colon >= 0 && colon + 1 < name.Length) name = name[(colon + 1)..];
        return name;
    }

    static string ResolveSmplKey(string garmentBoneName)
    {
        // Try raw + normalized.
        var norm = NormalizeBoneName(garmentBoneName);
        if (string.IsNullOrEmpty(norm)) return "";

        // If user already uses Jxx naming, return as-is.
        if (norm.Length == 3 && (norm[0] == 'J' || norm[0] == 'j') && char.IsDigit(norm[1]) && char.IsDigit(norm[2]))
            return norm.ToUpperInvariant();

        // Alias common names → Jxx.
        if (SmplAliasToJ.TryGetValue(norm, out var j))
            return j;

        return norm;
    }

    void LogTopWeightInfluences(SkinnedMeshRenderer smr)
    {
        if (smr == null) return;
        var mesh = smr.sharedMesh;
        if (mesh == null) return;

        // Avoid spamming: only log once per renderer instance.
        // Use instanceID so Play Mode re-runs still log (helpful for debugging).
        int id = smr.GetInstanceID();
        if (_loggedWeightStats.Contains(id)) return;
        _loggedWeightStats.Add(id);

        try
        {
            var boneWeights = mesh.boneWeights;
            if (boneWeights == null || boneWeights.Length == 0) return;

            var bones = smr.bones;
            int boneCount = bones != null ? bones.Length : 0;
            if (boneCount <= 0) return;

            // Accumulate absolute weight per bone index.
            var totals = new float[boneCount];
            float sum = 0f;
            foreach (var bw in boneWeights)
            {
                Add(totals, bw.boneIndex0, bw.weight0, ref sum);
                Add(totals, bw.boneIndex1, bw.weight1, ref sum);
                Add(totals, bw.boneIndex2, bw.weight2, ref sum);
                Add(totals, bw.boneIndex3, bw.weight3, ref sum);
            }

            if (sum <= 1e-6f) return;

            // Find top 5 influences.
            var top = new List<(int idx, float w)>(8);
            for (int i = 0; i < totals.Length; i++)
            {
                float w = totals[i];
                if (w <= 0f) continue;
                top.Add((i, w));
            }
            top.Sort((a, b) => b.w.CompareTo(a.w));
            int n = Mathf.Min(5, top.Count);

            string msg = $"Garment weights (top {n}) for '{smr.name}': ";
            for (int i = 0; i < n; i++)
            {
                int bi = top[i].idx;
                float pct = (top[i].w / sum) * 100f;
                string bn = (bones != null && bi >= 0 && bi < bones.Length && bones[bi] != null) ? bones[bi].name : $"boneIndex{bi}";
                msg += $"{bn}={pct:F1}%";
                if (i != n - 1) msg += ", ";
            }

            Debug.Log(msg, smr);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Garment weight diagnostic failed: {ex.Message}", smr);
        }
    }

    static void Add(float[] totals, int idx, float w, ref float sum)
    {
        if (idx < 0 || idx >= totals.Length) return;
        if (w <= 0f) return;
        totals[idx] += w;
        sum += w;
    }

    private readonly HashSet<int> _loggedWeightStats = new();
}

