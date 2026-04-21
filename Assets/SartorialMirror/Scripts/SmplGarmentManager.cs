using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class SmplGarmentManager : MonoBehaviour
{
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
    public string[] smplPelvisBoneNames = { "pelvis", "Hips", "hips", "Pelvis", "J00" };

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
            if (!smplBonesByName.ContainsKey(t.name))
                smplBonesByName.Add(t.name, t);
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
}

