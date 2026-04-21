using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives garment bones from scene SMPL. Runs late in the frame so SMPL bones are final before we copy them.
/// This project’s working body setup uses only <see cref="SpheresToBones_FKDriver"/> on SMPL (see README);
/// other IK / alternate drivers in the repo are unused experiments—execution order here is still “after default”
/// so we follow whatever last wrote the rig (Animator + FK spheres pipeline).
/// </summary>
[DefaultExecutionOrder(1200)]
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

    [Tooltip("If true, keep snapping every frame. Can fight armature driving if alignment is already correct; try off first.")]
    public bool continuousPelvisSnap = false;

    [Tooltip("Candidate bone names for pelvis/hips on the SMPL rig.")]
    public string[] smplPelvisBoneNames = { "J00", "pelvis", "Hips", "hips", "Pelvis" };

    [Tooltip("Candidate bone names for pelvis/hips on the garment rig.")]
    public string[] garmentPelvisBoneNames = { "J00", "pelvis", "Hips", "hips", "Pelvis" };

    [Header("Deformation Strategy")]
    [Tooltip("Recommended. Keep garment skinned to its own armature and drive that armature from the scene SMPL bones each frame. Avoids bindpose mismatches that can cause 'exploding' sleeves.")]
    public bool driveGarmentArmatureFromSmpl = true;

    [Tooltip("If true, also copy bone positions (not just rotations). Usually NOT recommended; can stretch chains if bind poses differ.")]
    public bool drivePositions = false;

    [Tooltip("If true, wrist/hand bones keep spawn pose. If the mesh has weights on those bones, leaving them undriven can distort; disable for stable full-body drive.")]
    public bool skipHandsAndWrists = false;

    [Header("Stretch Guard")]
    [Tooltip("If true, clamp each driven garment bone so it cannot move farther from its SMPL bone than at spawn time. Can fight rotation drive; leave off while tuning.")]
    public bool clampBoneStretch = false;

    [Tooltip("Extra slack allowed beyond the initial bone offset (meters).")]
    public float clampSlackMeters = 0.02f;

    [Tooltip("If true, drive garment bones using world rotation (more stable than localRotation when bind poses differ).")]
    public bool driveWorldRotation = true;

    [Tooltip("If true, each frame uses a pre-sampled quaternion offset between garment and SMPL bone (see RecordBindPoseRotationOffsets). " +
             "If arms still look wrong, leave this off and use simple world copy + end-of-frame drive.")]
    public bool useBindPoseRotationOffset = false;

    [Tooltip("If true, copy bone rotations in a WaitForEndOfFrame coroutine so SpheresToBones_FKDriver + Animator finish first. " +
             "Off by default: if the coroutine fails to start, the garment would not move. Turn on only when debugging arm spikes.")]
    public bool applyGarmentDriveAtEndOfFrame = false;

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
    private Transform cachedSmplPelvis;
    private Dictionary<Transform, Transform> garmentToSmplBoneMap;
    private Transform garmentArmatureRoot;
    private readonly Dictionary<Transform, float> garmentBoneMaxDistance = new();
    /// <summary>Per bone: k = g.rotation * Inv(s.rotation) at bind sample, then g = k * s each frame.</summary>
    private readonly Dictionary<Transform, Quaternion> bindRotLeftMul = new();
    private bool pendingBindPoseSample;
    private Coroutine garmentDriveEndOfFrameRoutine;
    private readonly List<(Transform g, Transform s, int depth)> _driveBonesSorted = new(48);

    void Awake()
    {
        EnsureSmplRoot();
        EnsureBoneMap();
        EnsureGarmentsParent();
        cachedSmplPelvis = FindFirstByNames(smplRoot, smplPelvisBoneNames);
    }

    void OnEnable()
    {
        TryStartEndOfFrameGarmentDrive();
    }

    void Start()
    {
        // AddComponent during another Awake can skip OnEnable timing; ensure EOF drive still starts when enabled.
        TryStartEndOfFrameGarmentDrive();
    }

    void TryStartEndOfFrameGarmentDrive()
    {
        if (!applyGarmentDriveAtEndOfFrame || garmentDriveEndOfFrameRoutine != null)
            return;
        if (!isActiveAndEnabled)
            return;
        garmentDriveEndOfFrameRoutine = StartCoroutine(CoApplyGarmentDriveEndOfFrame());
    }

    void OnDisable()
    {
        if (garmentDriveEndOfFrameRoutine != null)
        {
            StopCoroutine(garmentDriveEndOfFrameRoutine);
            garmentDriveEndOfFrameRoutine = null;
        }
    }

    IEnumerator CoApplyGarmentDriveEndOfFrame()
    {
        var wait = new WaitForEndOfFrame();
        while (isActiveAndEnabled)
        {
            yield return wait;
            if (!applyGarmentDriveAtEndOfFrame)
                continue;
            try
            {
                if (driveGarmentArmatureFromSmpl && ActiveGarmentInstance != null)
                    ApplyGarmentArmatureDrive();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }
    }

    // NOTE: LateUpdate is implemented at bottom of file (armature drive + pelvis snap).

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

        // Align to SMPL *before* building drive maps / stretch limits. Doing snap after the map
        // used wrong world-space bone pairs and made clamp distances invalid (classic "exploding" mesh).
        if (snapGarmentToSmplPelvis)
            SnapGarmentRootToSmplPelvis(ActiveGarmentInstance);

        if (driveGarmentArmatureFromSmpl)
        {
            BuildGarmentArmatureDriveMap(ActiveGarmentInstance);
        }
        else
        {
            RemapAllSkinnedMeshesToSmpl(ActiveGarmentInstance);
        }

        pendingBindPoseSample = driveGarmentArmatureFromSmpl && useBindPoseRotationOffset && garmentToSmplBoneMap != null;

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
        garmentToSmplBoneMap = null;
        garmentArmatureRoot = null;
        garmentBoneMaxDistance.Clear();
        bindRotLeftMul.Clear();
        pendingBindPoseSample = false;
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

    void BuildGarmentArmatureDriveMap(GameObject garmentRoot)
    {
        garmentToSmplBoneMap = null;
        garmentArmatureRoot = null;
        garmentBoneMaxDistance.Clear();
        if (garmentRoot == null || smplRoot == null) return;
        if (smplBonesByName == null || smplBonesByName.Count == 0) EnsureBoneMap();

        // Find garment armature root by looking at any SkinnedMeshRenderer rootBone.
        var smr = garmentRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || smr.rootBone == null) return;

        garmentArmatureRoot = FindArmatureRoot(smr.rootBone);
        if (garmentArmatureRoot == null) garmentArmatureRoot = smr.rootBone;

        var map = new Dictionary<Transform, Transform>(1024);
        foreach (var gt in garmentArmatureRoot.GetComponentsInChildren<Transform>(true))
        {
            if (gt == null) continue;
            var key = ResolveSmplKey(gt.name);
            if (string.IsNullOrEmpty(key)) continue;
            if (skipHandsAndWrists && IsHandOrWrist(key)) continue;
            if (smplBonesByName.TryGetValue(key, out var smplT) && smplT != null)
                map[gt] = smplT;
        }

        AugmentGarmentDriveMapWithSkinWeights(smr, map);

        garmentToSmplBoneMap = map.Count > 0 ? map : null;
        if (garmentToSmplBoneMap == null)
            Debug.LogWarning("Garment armature drive: no matching bones found between garment armature and SMPL rig.", garmentRoot);

        if (clampBoneStretch && garmentToSmplBoneMap != null)
        {
            foreach (var kv in garmentToSmplBoneMap)
            {
                var g = kv.Key;
                var s = kv.Value;
                if (g == null || s == null) continue;
                float d = Vector3.Distance(g.position, s.position) + Mathf.Max(0f, clampSlackMeters);
                garmentBoneMaxDistance[g] = d;
            }
        }

    }

    void RecordBindPoseRotationOffsets()
    {
        bindRotLeftMul.Clear();
        foreach (var kv in garmentToSmplBoneMap)
        {
            var g = kv.Key;
            var s = kv.Value;
            if (g == null || s == null) continue;
            bindRotLeftMul[g] = g.rotation * Quaternion.Inverse(s.rotation);
        }
    }

    void AugmentGarmentDriveMapWithSkinWeights(SkinnedMeshRenderer smr, Dictionary<Transform, Transform> map)
    {
        var mesh = smr.sharedMesh;
        var bones = smr.bones;
        if (mesh == null || bones == null || bones.Length == 0) return;
        var bws = mesh.boneWeights;
        if (bws == null || bws.Length == 0) return;

        var weightedBones = new HashSet<Transform>();
        foreach (var bw in bws)
        {
            void consider(int idx, float w)
            {
                if (w <= 1e-4f || idx < 0 || idx >= bones.Length) return;
                var t = bones[idx];
                if (t != null) weightedBones.Add(t);
            }

            consider(bw.boneIndex0, bw.weight0);
            consider(bw.boneIndex1, bw.weight1);
            consider(bw.boneIndex2, bw.weight2);
            consider(bw.boneIndex3, bw.weight3);
        }

        foreach (var gBone in weightedBones)
        {
            if (map.ContainsKey(gBone)) continue;
            var key = ResolveSmplKey(gBone.name);
            if (string.IsNullOrEmpty(key)) continue;
            if (skipHandsAndWrists && IsHandOrWrist(key)) continue;
            if (smplBonesByName.TryGetValue(key, out var smplT) && smplT != null)
                map[gBone] = smplT;
        }
    }

    static Transform FindArmatureRoot(Transform bone)
    {
        if (bone == null) return null;
        Transform t = bone;
        // Walk up until parent is null or name suggests it's no longer part of skeleton.
        // In practice, the armature root is a direct child of the garment prefab root.
        while (t.parent != null)
        {
            // Heuristic: stop if parent has a SkinnedMeshRenderer (we're leaving skeleton space).
            if (t.parent.GetComponent<SkinnedMeshRenderer>() != null) break;
            t = t.parent;
        }
        return t;
    }

    void ApplyGarmentArmatureDrive()
    {
        if (!driveGarmentArmatureFromSmpl) return;
        if (garmentToSmplBoneMap == null || garmentToSmplBoneMap.Count == 0) return;

        if (pendingBindPoseSample && useBindPoseRotationOffset)
        {
            RecordBindPoseRotationOffsets();
            pendingBindPoseSample = false;
        }

        // Apply parents before children. Random dictionary order can set shoulder/arm world rotation
        // before spine/clavicle chain is updated → wrong FK and visibly "stretched" arms.
        _driveBonesSorted.Clear();
        Transform subtreeRoot = garmentArmatureRoot != null ? garmentArmatureRoot : ActiveGarmentInstance != null ? ActiveGarmentInstance.transform : null;
        foreach (var kv in garmentToSmplBoneMap)
        {
            var g = kv.Key;
            var s = kv.Value;
            if (g == null || s == null) continue;
            int depth = BoneDepthFromSubtreeRoot(g, subtreeRoot);
            _driveBonesSorted.Add((g, s, depth));
        }

        _driveBonesSorted.Sort(static (a, b) => a.depth.CompareTo(b.depth));

        foreach (var item in _driveBonesSorted)
        {
            var g = item.g;
            var s = item.s;
            if (drivePositions) g.position = s.position;

            if (driveWorldRotation)
            {
                if (useBindPoseRotationOffset && bindRotLeftMul.TryGetValue(g, out var k))
                    g.rotation = k * s.rotation;
                else
                    g.rotation = s.rotation;
            }
            else
            {
                g.localRotation = s.localRotation;
            }

            if (clampBoneStretch && garmentBoneMaxDistance.TryGetValue(g, out var maxD))
            {
                var offset = g.position - s.position;
                float len = offset.magnitude;
                if (len > maxD && len > 1e-6f)
                    g.position = s.position + offset * (maxD / len);
            }
        }
    }

    static int BoneDepthFromSubtreeRoot(Transform bone, Transform subtreeRoot)
    {
        if (bone == null) return 0;
        int d = 0;
        var t = bone;
        while (t != null && t != subtreeRoot)
        {
            d++;
            t = t.parent;
            if (d > 128) break;
        }

        return d;
    }

    static bool IsHandOrWrist(string j)
    {
        // J20/J21 wrists, J22/J23 hands in this rig family.
        return string.Equals(j, "J20", StringComparison.OrdinalIgnoreCase)
            || string.Equals(j, "J21", StringComparison.OrdinalIgnoreCase)
            || string.Equals(j, "J22", StringComparison.OrdinalIgnoreCase)
            || string.Equals(j, "J23", StringComparison.OrdinalIgnoreCase);
    }

    void SnapGarmentRootToSmplPelvis(GameObject garmentRoot)
    {
        if (garmentRoot == null || smplRoot == null) return;

        var smplPelvis = cachedSmplPelvis != null ? cachedSmplPelvis : FindFirstByNames(smplRoot, smplPelvisBoneNames);
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

    void LateUpdate()
    {
        if (!applyGarmentDriveAtEndOfFrame)
            ApplyGarmentArmatureDrive();

        if (!continuousPelvisSnap) return;
        if (!snapGarmentToSmplPelvis) return;
        if (ActiveGarmentInstance == null) return;
        if (smplRoot == null) return;

        if (cachedSmplPelvis == null)
            cachedSmplPelvis = FindFirstByNames(smplRoot, smplPelvisBoneNames);

        if (cachedSmplPelvis == null) return;

        SnapGarmentRootToSmplPelvis(ActiveGarmentInstance);
    }
}

