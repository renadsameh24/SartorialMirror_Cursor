using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Which transform defines mesh space when rebuilding inverse bind matrices after bone remap.</summary>
public enum GarmentBindPoseReference
{
    /// <summary>Vertices are in SkinnedMeshRenderer.local space (Unity default for skinned meshes).</summary>
    SkinnedMeshRendererLocal,
    /// <summary>Use rootBone.localToWorldMatrix instead of the renderer transform (try if sleeves still stretch).</summary>
    RootBoneWorld
}

/// <summary>
/// Drives garment bones from scene SMPL. Runs late in the frame so SMPL bones are final before we copy them.
/// This project’s working body setup uses only <see cref="SpheresToBones_FKDriver"/> on SMPL (see README);
/// other IK / alternate drivers in the repo are unused experiments—execution order here is late (3200)
/// so we run after <see cref="SpheresToBones_FKDriver"/> (2500), <see cref="FollowTransform"/> (1000), etc.
/// </summary>
[DefaultExecutionOrder(3200)]
public sealed class SmplGarmentManager : MonoBehaviour
{
    public enum TuningPreset
    {
        RemapOnly,
        Remap_RecalcBindPoses_RootBoneWorld,
        Drive_Stable_NoTwistFix,
        Drive_TwistFix_180,
        Drive_MaxStability_TwistFix_Clamp
    }

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

    private static readonly string[] ArmChainSmplKeys =
    {
        "J16", "J17", "J18", "J19", "J20", "J21", "J22", "J23"
    };

    [Header("SMPL Target")]
    [Tooltip("If empty, finds GameObject by name at runtime.")]
    public Transform smplRoot;

    [Tooltip("Fallback: name of the SMPL root in the scene.")]
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";

    [Tooltip("Optional parent under SMPL root to keep garments organized.")]
    public string garmentsParentName = "_Garments";

    [Tooltip("Direct child (or descendant) name of the driven SMPL skeleton — NOT the garment copy. " +
             "Bone lookups use ONLY this subtree so duplicate J-bones under _Garments never win (frozen shirt).")]
    public string smplArmatureRootName = "SMPL_Armature";

    [Tooltip("Optional: drag the driven SMPL armature root (the parent of J00 under SMPL_neutral_rig_GOLDEN). " +
             "When set, skips name search — use when multiple Armature/SMPL objects exist and the shirt stays frozen.")]
    public Transform smplArmatureRootOverride;

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
    [Tooltip("If OFF: point each SkinnedMeshRenderer bone at the scene SMPL bones by name. If ON: duplicate garment armature + copy transforms each frame.")]
    public bool driveGarmentArmatureFromSmpl = true;

    [Tooltip("After remap, rebuild Mesh.bindposes from SMPL bones at spawn. " +
             "OFF by default: remap-only usually follows pose (Blender export uses the same SMPL rig). " +
             "Turn ON only if you see stretched sleeves / wrong arm length; requires readable mesh import for best results.")]
    public bool recalculateBindPosesAfterRemap = false;

    [Tooltip("Bind-pose math: use SkinnedMeshRenderer local space (default), or SMPL rootBone world matrix if stretch persists.")]
    public GarmentBindPoseReference bindPoseReference = GarmentBindPoseReference.SkinnedMeshRendererLocal;

    [Header("Remap: head vs arms (shirt meshes)")]
    [Tooltip("Many garment FBXs weight the collar to J15 (head) while sleeves use arm bones — same verts then tear between head and arms. " +
             "When ON, verts with both head + arm influence move most J15 weight to J12 (neck), or J09 if neck is absent.")]
    public bool decoupleHeadFromArmWeightsAfterRemap = false;

    [Tooltip("Vertex must have at least this much total weight on arm chain (J16–J23) before we shift head weight.")]
    [Range(0.01f, 0.5f)]
    public float minArmWeightToDecoupleHead = 0.06f;

    [Tooltip("Fraction of J15 weight moved to neck on qualifying verts (rest stays on head for pure collar verts).")]
    [Range(0f, 1f)]
    public float headWeightShiftToNeck = 0.88f;

    [Tooltip("If true, also copy bone positions (not just rotations). Usually NOT recommended; can stretch chains if bind poses differ.")]
    public bool drivePositions = false;

    [Tooltip("If true, skip wrist/hand in the initial bone sweep only. Any bone with skin weights is still driven from SMPL (required or verts stretch). " +
             "Leave false to drive the full chain including hands.")]
    public bool skipHandsAndWrists = false;

    [Header("Stretch Guard")]
    [Tooltip("If true, clamp each driven garment bone so it cannot move farther from its SMPL bone than at spawn time. Can fight rotation drive; leave off while tuning.")]
    public bool clampBoneStretch = true;

    [Tooltip("Extra slack allowed beyond the initial bone offset (meters).")]
    public float clampSlackMeters = 0.02f;

    [Tooltip("Used when Match Driven Bones To SMPL World is off. If true, copy world rotation; if false, copy localRotation (parent chain must be sane).")]
    public bool driveWorldRotation = true;

    [Tooltip("Only when Drive Garment Armature From SMPL is ON. Matches world position+rotation per bone. Ignored in remap mode.")]
    public bool matchDrivenBonesToSmplWorld = true;

    [Header("Twist Fix (Drive mode)")]
    [Tooltip("Only affects Drive Garment Armature From SMPL mode. Applies an extra roll (typically 180°) around the forearm/wrist axis to counter bone-roll mismatches that cause 180° twists.")]
    public bool applyArmTwistFix = true;

    public enum TwistAxisMode
    {
        SmplBoneToChild,
        SmplForward,
        SmplUp,
        SmplRight,
        GarmentBoneToChild,
        GarmentForward,
        GarmentUp,
        GarmentRight,
    }

    [Tooltip("Axis used for twist rotation. If 180° seems to twist 'vertically', try SmplForward/Up/Right until it behaves like roll.")]
    public TwistAxisMode twistAxisMode = TwistAxisMode.SmplBoneToChild;

    [Tooltip("Forearm roll correction (degrees). Typical values: 180 or -180.")]
    [Range(-180f, 180f)]
    public float forearmTwistFixDegrees = 180f;

    [Tooltip("Wrist roll correction (degrees). Often the opposite sign from forearm.")]
    [Range(-180f, 180f)]
    public float wristTwistFixDegrees = -180f;

    [Tooltip("Which SMPL keys count as forearms for twist fix.")]
    public string[] forearmTwistFixKeys = { "J18", "J19" };

    [Tooltip("Which SMPL keys count as wrists for twist fix.")]
    public string[] wristTwistFixKeys = { "J20", "J21" };

    [Tooltip("If true, each frame uses a pre-sampled quaternion offset between garment and SMPL bone (see RecordBindPoseRotationOffsets). " +
             "If arms still look wrong, leave this off and use simple world copy + end-of-frame drive.")]
    public bool useBindPoseRotationOffset = true;

    [Tooltip("If true, copy bone rotations in a WaitForEndOfFrame coroutine so SpheresToBones_FKDriver + Animator finish first. " +
             "Off by default: if the coroutine fails to start, the garment would not move. Turn on only when debugging arm spikes.")]
    public bool applyGarmentDriveAtEndOfFrame = true;

    [Header("Presets (quality of life)")]
    [Tooltip("Pick a preset, then click Apply Preset (context menu) or enable applyPresetOnEnable for Play Mode iteration.")]
    public TuningPreset tuningPreset = TuningPreset.Drive_MaxStability_TwistFix_Clamp;

    [Tooltip("If true, applies the selected preset in OnEnable (useful since bootstrap adds components at runtime).")]
    public bool applyPresetOnEnable = true;

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

    [Tooltip("After a garment spawns, log one structured checklist: SMPL, FK driver, playback, skin weights, pelvis match — and what to fix next.")]
    public bool logPipelineDiagnosis = true;

    [Tooltip("Below this count, pipeline diagnosis logs a warning (≤1 bone is an error). A shirt can move with ~4 bones but sleeves/arms may look wrong until weights cover more SMPL bones.")]
    [Range(2, 32)]
    public int diagnosisMinHealthyInfluencingBones = 8;

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
    private readonly List<Mesh> runtimeGarmentMeshCopies = new();

    void Awake()
    {
        EnsureSmplRoot();
        EnsureBoneMapExcludingGarments();
        cachedSmplPelvis = FindFirstByNames(smplRoot, smplPelvisBoneNames);
    }

    void OnEnable()
    {
        if (applyPresetOnEnable)
            ApplyPreset(tuningPreset);
        TryStartEndOfFrameGarmentDrive();
    }

    void Start()
    {
        // AddComponent during another Awake can skip OnEnable timing; ensure EOF drive still starts when enabled.
        TryStartEndOfFrameGarmentDrive();
    }

    [ContextMenu("Apply tuning preset")]
    public void ApplyTuningPresetFromContextMenu()
    {
        ApplyPreset(tuningPreset);
    }

    public void ApplyPreset(TuningPreset preset)
    {
        tuningPreset = preset;

        switch (preset)
        {
            case TuningPreset.RemapOnly:
                driveGarmentArmatureFromSmpl = false;
                recalculateBindPosesAfterRemap = false;
                bindPoseReference = GarmentBindPoseReference.SkinnedMeshRendererLocal;
                applyArmTwistFix = false;
                useBindPoseRotationOffset = false;
                applyGarmentDriveAtEndOfFrame = false;
                clampBoneStretch = false;
                drivePositions = false;
                matchDrivenBonesToSmplWorld = true;
                break;

            case TuningPreset.Remap_RecalcBindPoses_RootBoneWorld:
                driveGarmentArmatureFromSmpl = false;
                recalculateBindPosesAfterRemap = true;
                bindPoseReference = GarmentBindPoseReference.RootBoneWorld;
                applyArmTwistFix = false;
                useBindPoseRotationOffset = false;
                applyGarmentDriveAtEndOfFrame = false;
                clampBoneStretch = false;
                break;

            case TuningPreset.Drive_Stable_NoTwistFix:
                driveGarmentArmatureFromSmpl = true;
                recalculateBindPosesAfterRemap = false;
                applyArmTwistFix = false;
                useBindPoseRotationOffset = true;
                applyGarmentDriveAtEndOfFrame = true;
                matchDrivenBonesToSmplWorld = true;
                drivePositions = false;
                clampBoneStretch = false;
                break;

            case TuningPreset.Drive_TwistFix_180:
                driveGarmentArmatureFromSmpl = true;
                recalculateBindPosesAfterRemap = false;
                applyArmTwistFix = true;
                forearmTwistFixDegrees = 180f;
                wristTwistFixDegrees = -180f;
                useBindPoseRotationOffset = true;
                applyGarmentDriveAtEndOfFrame = true;
                matchDrivenBonesToSmplWorld = true;
                drivePositions = false;
                clampBoneStretch = false;
                break;

            case TuningPreset.Drive_MaxStability_TwistFix_Clamp:
                driveGarmentArmatureFromSmpl = true;
                recalculateBindPosesAfterRemap = false;
                applyArmTwistFix = true;
                forearmTwistFixDegrees = 180f;
                wristTwistFixDegrees = -180f;
                useBindPoseRotationOffset = true;
                applyGarmentDriveAtEndOfFrame = true;
                matchDrivenBonesToSmplWorld = true;
                drivePositions = false;
                clampBoneStretch = true;
                clampSlackMeters = Mathf.Clamp(clampSlackMeters, 0.005f, 0.03f);
                break;
        }

        // Make sure remap updates are visible.
        if (!driveGarmentArmatureFromSmpl && ActiveGarmentInstance != null)
            RemapAllSkinnedMeshesToSmpl(ActiveGarmentInstance);

        // Drive mode needs a map.
        if (driveGarmentArmatureFromSmpl && ActiveGarmentInstance != null)
            BuildGarmentArmatureDriveMap(ActiveGarmentInstance);
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
        if (go == null)
            go = FindSceneObjectByNameIncludingInactive(smplRootName);
        if (go == null) return false;
        smplRoot = go.transform;
        return true;
    }

    /// <summary>GameObject.Find only sees active objects; SMPL may be inactive on first frame.</summary>
    static GameObject FindSceneObjectByNameIncludingInactive(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return null;
        var transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null) continue;
            if (t.name != objectName) continue;
            var go = t.gameObject;
            if (go != null && go.scene.IsValid())
                return go;
        }
        return null;
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
        EnsureBoneMapInternal(excludeTransformsUnderGarmentsParent: false);
    }

    /// <summary>
    /// Prefer the real SMPL armature subtree only (see <see cref="smplArmatureRootName"/>).
    /// That guarantees duplicate J-bones on the spawned garment FBX never shadow the driven SMPL bones (classic “shirt frozen”).
    /// </summary>
    void EnsureBoneMapExcludingGarments()
    {
        EnsureGarmentsParent();
        smplBonesByName = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        if (smplRoot == null) return;

        var armatureRoot = FindSmplDrivenArmatureRoot();
        if (armatureRoot != null)
        {
            foreach (var t in armatureRoot.GetComponentsInChildren<Transform>(true))
                AddBoneNameEntry(t);

            if (logMissingBoneNames)
                Debug.Log(
                    $"[SmplGarmentManager] SMPL bone map built from '{armatureRoot.name}' only: {smplBonesByName.Count} name keys.",
                    this);

            if (smplBonesByName.Count < 12 && logSpawnFailures)
                Debug.LogWarning(
                    $"[SmplGarmentManager] Few bones mapped ({smplBonesByName.Count}). Check that '{smplArmatureRootName}' exists under '{smplRoot.name}'.",
                    this);
            return;
        }

        if (logSpawnFailures)
            Debug.LogWarning(
                $"[SmplGarmentManager] Armature '{smplArmatureRootName}' not found under '{smplRoot.name}'. " +
                "Falling back to full SMPL root minus _Garments (may mis-resolve if duplicate J-bones exist).",
                this);

        EnsureBoneMapInternal(excludeTransformsUnderGarmentsParent: true);
    }

    Transform FindSmplDrivenArmatureRoot()
    {
        if (smplRoot == null) return null;

        if (smplArmatureRootOverride != null)
        {
            if (IsTransformUnderGarments(smplArmatureRootOverride))
            {
                if (logSpawnFailures)
                    Debug.LogWarning(
                        "[SmplGarmentManager] smplArmatureRootOverride is under _Garments — ignoring (would freeze the shirt).",
                        this);
            }
            else if (!smplArmatureRootOverride.IsChildOf(smplRoot) && smplArmatureRootOverride != smplRoot)
            {
                if (logSpawnFailures)
                    Debug.LogWarning("[SmplGarmentManager] smplArmatureRootOverride is not under smplRoot — ignoring.", this);
            }
            else
            {
                if (logMissingBoneNames)
                    Debug.Log($"[SmplGarmentManager] Using smplArmatureRootOverride '{smplArmatureRootOverride.name}'.", this);
                return smplArmatureRootOverride;
            }
        }

        // Prefer the subtree that SpheresToBones_FKDriver actually drives (avoids picking a duplicate static armature).
        var fk = smplRoot.GetComponentInChildren<SpheresToBones_FKDriver>(true);
        if (fk != null)
        {
            var anchor = fk.rootBone;
            if (anchor == null && fk.segments != null)
            {
                foreach (var seg in fk.segments)
                {
                    if (seg != null && seg.bone != null)
                    {
                        anchor = seg.bone;
                        break;
                    }
                }
            }

            if (anchor != null)
            {
                var resolved = FindDirectChildOfSmplRootOnPathToDescendant(smplRoot, anchor);
                if (resolved != null && !IsTransformUnderGarments(resolved))
                {
                    if (logMissingBoneNames)
                        Debug.Log(
                            $"[SmplGarmentManager] Armature root from SpheresToBones_FKDriver (anchor '{anchor.name}'): '{resolved.name}'.",
                            this);
                    return resolved;
                }
            }
        }

        // Try user name first, then common FBX import names (Unity may rename SMPL_Armature → Armature).
        var candidates = new List<string>(4);
        if (!string.IsNullOrEmpty(smplArmatureRootName))
            candidates.Add(smplArmatureRootName);
        foreach (var n in new[] { "SMPL_Armature", "Armature", "SMPL" })
        {
            if (!candidates.Contains(n))
                candidates.Add(n);
        }

        foreach (var name in candidates)
        {
            var direct = smplRoot.Find(name);
            if (direct != null && !IsTransformUnderGarments(direct))
                return direct;

            foreach (var t in smplRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == smplRoot) continue;
                if (IsTransformUnderGarments(t)) continue;
                if (string.Equals(t.name, name, StringComparison.Ordinal))
                    return t;
            }
        }

        return null;
    }

    /// <summary>Returns the transform that is a direct child of <paramref name="smplRoot"/> on the path to <paramref name="descendant"/>.</summary>
    static Transform FindDirectChildOfSmplRootOnPathToDescendant(Transform smplRoot, Transform descendant)
    {
        if (smplRoot == null || descendant == null) return null;
        if (descendant == smplRoot) return null;
        if (!descendant.IsChildOf(smplRoot)) return null;
        var t = descendant;
        while (t.parent != null && t.parent != smplRoot)
            t = t.parent;
        return t;
    }

    static string TransformHierarchyPath(Transform t)
    {
        if (t == null) return "";
        var parts = new List<string>(8);
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    bool IsTransformUnderGarments(Transform t)
    {
        if (t == null || garmentsParent == null) return false;
        return t == garmentsParent || t.IsChildOf(garmentsParent);
    }

    void AddBoneNameEntry(Transform t)
    {
        if (!t) return;
        var raw = t.name;
        var norm = NormalizeBoneName(raw);
        if (!smplBonesByName.ContainsKey(raw))
            smplBonesByName.Add(raw, t);
        if (!string.IsNullOrEmpty(norm) && !smplBonesByName.ContainsKey(norm))
            smplBonesByName.Add(norm, t);
    }

    void EnsureBoneMapInternal(bool excludeTransformsUnderGarmentsParent)
    {
        smplBonesByName = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        if (smplRoot == null) return;

        foreach (var t in smplRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!t) continue;
            if (excludeTransformsUnderGarmentsParent && garmentsParent != null && t != garmentsParent && t.IsChildOf(garmentsParent))
                continue;
            AddBoneNameEntry(t);
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
        EnsureBoneMapExcludingGarments();

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

        // Drive mode with an empty map leaves bones on the prefab armature while we disable Animators → frozen mesh.
        bool useArmatureDrive = driveGarmentArmatureFromSmpl;
        if (useArmatureDrive)
        {
            BuildGarmentArmatureDriveMap(ActiveGarmentInstance);
            if (garmentToSmplBoneMap == null || garmentToSmplBoneMap.Count == 0)
            {
                useArmatureDrive = false;
                if (logSpawnFailures)
                    Debug.LogWarning(
                        "SmplGarmentManager: Drive Garment Armature From SMPL is ON but no SMPL bone pairs were found " +
                        "(names must match scene SMPL). Falling back to remap mode so the garment can move. " +
                        "Turn Drive OFF in the Inspector or fix bone names.",
                        ActiveGarmentInstance);
            }
        }

        if (!useArmatureDrive)
            RemapAllSkinnedMeshesToSmpl(ActiveGarmentInstance);

        pendingBindPoseSample = useArmatureDrive && useBindPoseRotationOffset && garmentToSmplBoneMap != null;

        // Set active index before applying variants (ApplyActiveColorVariant reads activeIndex).
        activeIndex = index;

        activeColorVariantIndex = Mathf.Max(0, entry.defaultColorVariantIndex);
        ApplyActiveColorVariant();
        LogPipelineDiagnosisAfterSpawn();
        return true;
    }

    [ContextMenu("Log pipeline diagnosis (Play Mode)")]
    public void LogPipelineDiagnosisFromContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[SmplGarmentManager] Enter Play Mode, then run this again.", this);
            return;
        }

        LogPipelineDiagnosis();
    }

    void LogPipelineDiagnosisAfterSpawn()
    {
        if (!logPipelineDiagnosis) return;
        LogPipelineDiagnosis();
    }

    /// <summary>
    /// One console block: what works, what blocks motion, and the next fix (body pose vs garment mesh).
    /// </summary>
    public void LogPipelineDiagnosis()
    {
        EnsureSmplRoot();
        EnsureBoneMapExcludingGarments();

        Debug.Log("──────── [SmplGarmentManager] PIPELINE DIAGNOSIS ────────", this);

        if (smplRoot == null)
        {
            Debug.LogError(
                "[1/5] SMPL root: MISSING. Assign Smpl Root on bootstrap/manager or set smplRootName to the rig in Hierarchy.",
                this);
        }
        else
            Debug.Log($"[1/5] SMPL root: OK → '{smplRoot.name}'", this);

        if (smplBonesByName == null || smplBonesByName.Count == 0)
            Debug.LogError("[2/5] SMPL bone map: EMPTY. Check Smpl Armature Root Name / override.", this);
        else
            Debug.Log($"[2/5] SMPL bone map: OK ({smplBonesByName.Count} keys)", this);

        var fk = FindSpheresToBonesFkInScene();
        if (fk == null)
        {
            Debug.LogError(
                "[3/5] SpheresToBones_FKDriver: NOT FOUND in scene. Without it, SMPL bones are not driven from spheres → no pose → garment follows nothing. Add/configure it on the SMPL rig (see README).",
                this);
        }
        else
        {
            int validSeg = CountValidFkSegments(fk);
            Debug.Log(
                $"[3/5] SpheresToBones_FKDriver: OK on '{fk.gameObject.name}' | rootBone={(fk.rootBone != null ? fk.rootBone.name : "NULL")} | wiredSegments={validSeg}",
                fk);
            if (validSeg == 0)
                Debug.LogError(
                    "[3/5] FK segments not wired: assign bone/boneChild/sphere/sphereChild on each segment (or body stays in bind pose).",
                    fk);
        }

        LogPlaybackOptionalDiagnosis();

        if (ActiveGarmentInstance == null)
        {
            Debug.Log("[5/5] Garment: none spawned yet. Call TrySetActive or use catalog auto-select.", this);
        }
        else
        {
            var skinned = ActiveGarmentInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned == null || skinned.Length == 0)
                Debug.LogError("[5/5] Garment: no SkinnedMeshRenderer — prefab must be a skinned mesh.", ActiveGarmentInstance);
            else
            {
                foreach (var smr in skinned)
                {
                    if (!smr) continue;
                    int infl = CountBonesInfluencingMesh(smr);
                    Debug.Log($"[5/5] Garment SMR '{smr.name}': bones influencing mesh = {infl}", smr);
                    if (infl <= 1)
                        Debug.LogError(
                            "[5/5] FIX MESH: weights use only one bone (often J00). Re-export from Blender with weights transferred from SMPL body — Tools/blender_golden_garment_from_fbx.py, then GarmentCatalog → prepared FBX.",
                            smr);
                    else if (infl < Mathf.Max(2, diagnosisMinHealthyInfluencingBones))
                        Debug.LogWarning(
                            $"[5/5] Garment has only {infl} bones with any mesh weight (counts distinct bones used mesh-wide; a sleeve-heavy shirt can be OK). " +
                            "If motion looks wrong on lower arms, re-export with Blender WEIGHT_COPY_METHOD=BVH (default) or try Recalculate Bind Poses on SmplGarmentManager.",
                            smr);
                }

                if (fk != null && fk.rootBone != null)
                {
                    var smr0 = ActiveGarmentInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (smr0 != null && smr0.bones != null)
                    {
                        foreach (var b in smr0.bones)
                        {
                            if (b == null) continue;
                            if (!string.Equals(ResolveSmplKey(b.name), "J00", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (b != fk.rootBone)
                                Debug.LogError(
                                    "[5/5] Shirt J00 after remap ≠ FK rootBone (wrong armature in bone map). Set Smpl Armature Root Override to the driven armature parent of J00.",
                                    smr0);
                            break;
                        }
                    }
                }
            }
        }

        Debug.Log(
            $"── Next: If [3] fails → fix SMPL/spheres first. If [5] warns on low bone count → often OK; if arms/sleeves bad → Blender Tools/blender_golden_garment_from_fbx.py (BVH). If [5] pelvis error → armature override. ──",
            this);
    }

    static SpheresToBones_FKDriver FindSpheresToBonesFkInScene()
    {
        var all = UnityEngine.Object.FindObjectsOfType<SpheresToBones_FKDriver>(true);
        return all != null && all.Length > 0 ? all[0] : null;
    }

    static int CountValidFkSegments(SpheresToBones_FKDriver fk)
    {
        if (fk?.segments == null) return 0;
        int n = 0;
        foreach (var s in fk.segments)
        {
            if (s == null) continue;
            if (s.bone && s.boneChild && s.sphere && s.sphereChild) n++;
        }

        return n;
    }

    void LogPlaybackOptionalDiagnosis()
    {
        var jpv1 = UnityEngine.Object.FindObjectsOfType<JointPlaybackStream>(true);
        var jpv2 = UnityEngine.Object.FindObjectsOfType<JointPlaybackStreamV2>(true);

        if ((jpv1 == null || jpv1.Length == 0) && (jpv2 == null || jpv2.Length == 0))
        {
            Debug.Log(
                "[4/5] Playback (JointPlaybackStream): none in scene — OK if another system moves JointSpheresRoot (e.g. websocket).",
                this);
            return;
        }

        if (jpv1 != null && jpv1.Length > 0)
        {
            var p = jpv1[0];
            bool json = p.sequenceJson != null && !string.IsNullOrWhiteSpace(p.sequenceJson.text);
            Debug.Log(
                $"[4/5] JointPlaybackStream on '{p.gameObject.name}': sequenceJson={(json ? "OK" : "EMPTY — assign TextAsset or set playOnStart=false")}",
                p);
        }

        if (jpv2 != null && jpv2.Length > 0)
        {
            var p = jpv2[0];
            bool seq = p.mode == JointPlaybackStreamV2.Mode.SequencePlayback
                && p.sequenceJson != null
                && !string.IsNullOrWhiteSpace(p.sequenceJson.text);
            Debug.Log($"[4/5] JointPlaybackStreamV2 on '{p.gameObject.name}': mode={p.mode} sequenceOk={seq}", p);
        }
    }

    static int CountBonesInfluencingMesh(SkinnedMeshRenderer smr)
    {
        var mesh = smr.sharedMesh;
        if (mesh == null) return 0;
        var bws = mesh.boneWeights;
        if (bws == null || bws.Length == 0) return 0;
        var bones = smr.bones;
        int n = bones != null ? bones.Length : 0;
        if (n <= 0) return 0;
        var totals = new float[n];
        foreach (var bw in bws)
        {
            void Acc(int i, float w)
            {
                if (i < 0 || i >= n || w <= 1e-8f) return;
                totals[i] += w;
            }

            Acc(bw.boneIndex0, bw.weight0);
            Acc(bw.boneIndex1, bw.weight1);
            Acc(bw.boneIndex2, bw.weight2);
            Acc(bw.boneIndex3, bw.weight3);
        }

        int c = 0;
        for (int i = 0; i < n; i++)
        {
            if (totals[i] > 1e-6f) c++;
        }

        return c;
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

        foreach (var m in runtimeGarmentMeshCopies)
        {
            if (m != null) Destroy(m);
        }
        runtimeGarmentMeshCopies.Clear();
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
        if (garmentRoot == null) return;
        EnsureBoneMapExcludingGarments();
        if (smplBonesByName == null || smplBonesByName.Count == 0)
        {
            Debug.LogError(
                "[SmplGarmentManager] SMPL bone map is empty — cannot remap garment. Check smplRoot and smplArmatureRootName (e.g. SMPL_Armature).",
                this);
            return;
        }

        var skinned = garmentRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in skinned)
        {
            if (!smr) continue;
            RemapSkinnedMeshToSmpl(smr);
            smr.updateWhenOffscreen = true;
            LogTopWeightInfluences(smr);
        }

        foreach (var anim in garmentRoot.GetComponentsInChildren<Animator>(true))
        {
            if (anim) anim.enabled = false;
        }
    }

    void BuildGarmentArmatureDriveMap(GameObject garmentRoot)
    {
        garmentToSmplBoneMap = null;
        garmentArmatureRoot = null;
        garmentBoneMaxDistance.Clear();
        if (garmentRoot == null || smplRoot == null) return;
        if (smplBonesByName == null || smplBonesByName.Count == 0) EnsureBoneMapExcludingGarments();

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

        var skinned = garmentRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var sm in skinned)
            AugmentGarmentDriveMapWithSkinWeights(sm, map);

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
            // Never skip weighted wrist/hand: undriven bones + non-zero weights = huge stretch / "long tube" artifacts.
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

            Quaternion rot;
            if (useBindPoseRotationOffset && bindRotLeftMul.TryGetValue(g, out var k))
                rot = k * s.rotation;
            else
                rot = s.rotation;

            if (applyArmTwistFix)
            {
                var j = ResolveSmplKey(g != null ? g.name : "");
                if (!string.IsNullOrEmpty(j) && TryGetTwistFixDegreesForKey(j, out var deg))
                {
                    Vector3 axis = GetAxisForTwist(j, garmentBone: g, smplBone: s);
                    if (axis.sqrMagnitude > 1e-8f)
                        rot = Quaternion.AngleAxis(deg, axis) * rot;
                }
            }

            if (matchDrivenBonesToSmplWorld)
            {
                g.SetPositionAndRotation(s.position, rot);
            }
            else
            {
                if (drivePositions) g.position = s.position;

                if (driveWorldRotation)
                    g.rotation = rot;
                else
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

    bool TryGetTwistFixDegreesForKey(string j, out float degrees)
    {
        degrees = 0f;
        if (string.IsNullOrEmpty(j)) return false;

        if (forearmTwistFixKeys != null)
        {
            for (int i = 0; i < forearmTwistFixKeys.Length; i++)
            {
                if (string.Equals(forearmTwistFixKeys[i], j, StringComparison.OrdinalIgnoreCase))
                {
                    degrees = forearmTwistFixDegrees;
                    return true;
                }
            }
        }

        if (wristTwistFixKeys != null)
        {
            for (int i = 0; i < wristTwistFixKeys.Length; i++)
            {
                if (string.Equals(wristTwistFixKeys[i], j, StringComparison.OrdinalIgnoreCase))
                {
                    degrees = wristTwistFixDegrees;
                    return true;
                }
            }
        }

        return false;
    }

    Vector3 GetAxisForTwist(string j, Transform garmentBone, Transform smplBone)
    {
        // Use SMPL bone transform as default axis source.
        if (smplBonesByName == null || smplBonesByName.Count == 0)
            EnsureBoneMapExcludingGarments();

        string childKey = j switch
        {
            "J18" => "J20",
            "J19" => "J21",
            "J20" => "J22",
            "J21" => "J23",
            _ => null
        };

        Vector3 AxisBoneToChild(Transform b, string child)
        {
            if (b == null) return Vector3.zero;
            if (!string.IsNullOrEmpty(child) && smplBonesByName != null && smplBonesByName.TryGetValue(child, out var c) && c != null)
                return (c.position - b.position).normalized;
            if (b.childCount > 0)
                return (b.GetChild(0).position - b.position).normalized;
            return Vector3.zero;
        }

        switch (twistAxisMode)
        {
            case TwistAxisMode.SmplBoneToChild:
            {
                var a = AxisBoneToChild(smplBone, childKey);
                if (a.sqrMagnitude > 1e-8f) return a;
                return smplBone != null ? smplBone.forward.normalized : Vector3.forward;
            }
            case TwistAxisMode.SmplForward:
                return smplBone != null ? smplBone.forward.normalized : Vector3.forward;
            case TwistAxisMode.SmplUp:
                return smplBone != null ? smplBone.up.normalized : Vector3.up;
            case TwistAxisMode.SmplRight:
                return smplBone != null ? smplBone.right.normalized : Vector3.right;

            case TwistAxisMode.GarmentBoneToChild:
            {
                if (garmentBone == null) return Vector3.forward;
                if (garmentBone.childCount > 0) return (garmentBone.GetChild(0).position - garmentBone.position).normalized;
                return garmentBone.forward.normalized;
            }
            case TwistAxisMode.GarmentForward:
                return garmentBone != null ? garmentBone.forward.normalized : Vector3.forward;
            case TwistAxisMode.GarmentUp:
                return garmentBone != null ? garmentBone.up.normalized : Vector3.up;
            case TwistAxisMode.GarmentRight:
                return garmentBone != null ? garmentBone.right.normalized : Vector3.right;
        }

        return Vector3.forward;
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

    bool TryGetSmplBoneTransform(string rawBoneName, out Transform smplBone)
    {
        smplBone = null;
        if (smplBonesByName == null || string.IsNullOrEmpty(rawBoneName)) return false;

        if (smplBonesByName.TryGetValue(rawBoneName, out smplBone) && smplBone != null)
            return true;

        var stripped = NormalizeBoneName(rawBoneName);
        if (!string.IsNullOrEmpty(stripped) && smplBonesByName.TryGetValue(stripped, out smplBone) && smplBone != null)
            return true;

        var key = ResolveSmplKey(rawBoneName);
        if (!string.IsNullOrEmpty(key) && smplBonesByName.TryGetValue(key, out smplBone) && smplBone != null)
            return true;

        if (!string.IsNullOrEmpty(stripped))
        {
            key = ResolveSmplKey(stripped);
            if (!string.IsNullOrEmpty(key) && smplBonesByName.TryGetValue(key, out smplBone) && smplBone != null)
                return true;
        }

        return false;
    }

    void RemapSkinnedMeshToSmpl(SkinnedMeshRenderer smr)
    {
        if (smr == null || smplBonesByName == null) return;

        int mappedCount = 0;
        int totalCount = 0;

        if (smr.rootBone != null && TryGetSmplBoneTransform(smr.rootBone.name, out var smplRootBone))
            smr.rootBone = smplRootBone;

        var bones = smr.bones;
        if (bones == null || bones.Length == 0) return;

        bool anyMissing = false;
        HashSet<string> missingNames = null;
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            if (b == null)
            {
                anyMissing = true;
                continue;
            }

            totalCount++;
            if (TryGetSmplBoneTransform(b.name, out var smplBone))
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

        if (mappedCount > 0 && (recalculateBindPosesAfterRemap || decoupleHeadFromArmWeightsAfterRemap))
            PrepareRuntimeGarmentMeshAfterRemap(smr);

        if (mappedCount == 0)
        {
            Debug.LogWarning(
                $"Garment bone remap: {smr.name} mapped 0/{Mathf.Max(1, totalCount)} bones onto SMPL. " +
                $"Bone names must match the scene rig (J00… or prefixed). " +
                $"Example garment bone: {GetFirstNonNullName(smr.bones)}.",
                smr);
        }

        if (anyMissing && logMissingBoneNames && missingNames != null && missingNames.Count > 0)
        {
            Debug.LogWarning(
                $"Garment bone remap: {smr.name} is missing {missingNames.Count} SMPL bone(s). Example: {GetFirst(missingNames)}.",
                smr);
        }

        if (mappedCount > 0)
        {
            ValidateRemappedBonesAreSceneSmpl(smr);
            ValidateRemappedPelvisMatchesFkDriver(smr);
            RefreshSkinnedRendererAfterBoneRemap(smr);
            ExpandLocalBoundsForExternalSkinnedBones(smr);
        }
    }

    void ValidateRemappedBonesAreSceneSmpl(SkinnedMeshRenderer smr)
    {
        if (smr == null || smr.bones == null || garmentsParent == null) return;
        foreach (var b in smr.bones)
        {
            if (b == null) continue;
            if (b == garmentsParent || b.IsChildOf(garmentsParent))
            {
                Debug.LogError(
                    $"[SmplGarmentManager] Skinned mesh still references bones under '{garmentsParentName}' ({b.name}). " +
                    "Remap resolved to the duplicate garment armature, not scene SMPL — shirt will not move. " +
                    "Confirm Smpl Armature Root Name matches the driven rig (default: SMPL_Armature).",
                    smr);
                return;
            }
        }
    }

    /// <summary>
    /// If two armatures exist under smplRoot (not only under _Garments), remap can still bind to the wrong J00.
    /// Compare shirt pelvis bone to <see cref="SpheresToBones_FKDriver.rootBone"/>.
    /// </summary>
    void ValidateRemappedPelvisMatchesFkDriver(SkinnedMeshRenderer smr)
    {
        if (smr == null || smr.bones == null || smplRoot == null) return;
        var fk = smplRoot.GetComponentInChildren<SpheresToBones_FKDriver>(true);
        if (fk == null || fk.rootBone == null) return;

        foreach (var b in smr.bones)
        {
            if (b == null) continue;
            if (!string.Equals(ResolveSmplKey(b.name), "J00", StringComparison.OrdinalIgnoreCase))
                continue;

            if (b != fk.rootBone)
            {
                Debug.LogError(
                    "[SmplGarmentManager] Shirt pelvis bone after remap is NOT the same Transform as SpheresToBones_FKDriver.rootBone " +
                    $"(shirt→'{TransformHierarchyPath(b)}', FK→'{TransformHierarchyPath(fk.rootBone)}'). " +
                    "The bone map likely used a duplicate static armature — assign Smpl Armature Root Override to the driven armature root, " +
                    "or fix hierarchy so only one SMPL skeleton exists under the rig root.",
                    smr);
            }

            return;
        }
    }

    /// <summary>
    /// Some Unity versions / platforms delay picking up new bone references until the renderer is toggled.
    /// </summary>
    static void RefreshSkinnedRendererAfterBoneRemap(SkinnedMeshRenderer smr)
    {
        if (smr == null) return;
        bool e = smr.enabled;
        smr.enabled = false;
        smr.enabled = e;
    }

    /// <summary>
    /// After remap, <see cref="SkinnedMeshRenderer.bones"/> point at SMPL (not under the garment renderer).
    /// Unity may compute tiny <see cref="SkinnedMeshRenderer.localBounds"/>, which breaks culling / skin updates in some cases.
    /// </summary>
    static void ExpandLocalBoundsForExternalSkinnedBones(SkinnedMeshRenderer smr)
    {
        if (smr == null || smr.rootBone == null) return;
        if (smr.rootBone.IsChildOf(smr.transform)) return;

        var b = smr.localBounds;
        const float minHalfAxis = 0.65f;
        var ext = b.extents;
        if (ext.x < minHalfAxis || ext.y < minHalfAxis || ext.z < minHalfAxis)
        {
            ext.x = Mathf.Max(ext.x, minHalfAxis);
            ext.y = Mathf.Max(ext.y, minHalfAxis * 2.2f);
            ext.z = Mathf.Max(ext.z, minHalfAxis);
            smr.localBounds = new Bounds(b.center, ext * 2f);
        }
    }

    /// <summary>
    /// Single mesh copy after remap: optional head/arm weight fix, then bindpose rebuild.
    /// </summary>
    void PrepareRuntimeGarmentMeshAfterRemap(SkinnedMeshRenderer smr)
    {
        if (smr == null) return;
        var src = smr.sharedMesh;
        if (src == null) return;
        var bones = smr.bones;
        if (bones == null || bones.Length == 0) return;

        if (recalculateBindPosesAfterRemap)
        {
            var oldBind = src.bindposes;
            if (oldBind == null || oldBind.Length != bones.Length)
            {
                if (logMissingBoneNames)
                    Debug.LogWarning(
                        $"Garment bindpose rebuild skipped for '{smr.name}': mesh bindposes ({oldBind?.Length ?? 0}) vs bones ({bones.Length}).",
                        smr);
                if (!decoupleHeadFromArmWeightsAfterRemap)
                    return;
            }
        }

        var mesh = Instantiate(src);
        mesh.name = src.name + "_garmentRuntime";
        runtimeGarmentMeshCopies.Add(mesh);
        smr.sharedMesh = mesh;

        if (decoupleHeadFromArmWeightsAfterRemap)
            SanitizeHeadVersusArmWeights(smr, mesh);

        if (recalculateBindPosesAfterRemap && mesh.bindposes != null && mesh.bindposes.Length == bones.Length)
            ApplyRecalculatedBindPosesToMesh(smr, mesh);
    }

    void ApplyRecalculatedBindPosesToMesh(SkinnedMeshRenderer smr, Mesh mesh)
    {
        var bones = smr.bones;
        if (bones == null || bones.Length == 0 || mesh == null) return;

        Matrix4x4 meshToWorld = smr.transform.localToWorldMatrix;
        if (bindPoseReference == GarmentBindPoseReference.RootBoneWorld && smr.rootBone != null)
            meshToWorld = smr.rootBone.localToWorldMatrix;

        var newBind = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            newBind[i] = b != null ? b.worldToLocalMatrix * meshToWorld : Matrix4x4.identity;
        }
        mesh.bindposes = newBind;
        // Do not call mesh.RecalculateBounds() here: runtime meshes cloned from imported FBX are often
        // non-readable and Unity throws "Not allowed to call RecalculateBounds() on mesh".
    }

    static int FindBoneIndexBySmplKey(Transform[] bones, string jKey)
    {
        if (bones == null || string.IsNullOrEmpty(jKey)) return -1;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            if (string.Equals(ResolveSmplKey(bones[i].name), jKey, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    static void AccumulateBoneInfl(Dictionary<int, float> d, int boneIndex, float w)
    {
        if (boneIndex < 0 || w <= 1e-8f) return;
        if (d.TryGetValue(boneIndex, out var o)) d[boneIndex] = o + w;
        else d[boneIndex] = w;
    }

    static void BoneWeightToDict(in BoneWeight bw, Dictionary<int, float> d)
    {
        d.Clear();
        AccumulateBoneInfl(d, bw.boneIndex0, bw.weight0);
        AccumulateBoneInfl(d, bw.boneIndex1, bw.weight1);
        AccumulateBoneInfl(d, bw.boneIndex2, bw.weight2);
        AccumulateBoneInfl(d, bw.boneIndex3, bw.weight3);
    }

    static BoneWeight PackTop4BoneWeights(Dictionary<int, float> d)
    {
        var pairs = new List<(int idx, float w)>(8);
        foreach (var kv in d)
        {
            if (kv.Value > 1e-8f) pairs.Add((kv.Key, kv.Value));
        }
        pairs.Sort(static (a, b) => b.w.CompareTo(a.w));
        int n = Mathf.Min(4, pairs.Count);
        float s = 0f;
        for (int i = 0; i < n; i++) s += pairs[i].w;
        if (s < 1e-8f) return default;

        int b0 = pairs[0].idx;
        float w0 = pairs[0].w / s;
        var r = new BoneWeight { boneIndex0 = b0, weight0 = w0 };
        if (n > 1)
        {
            r.boneIndex1 = pairs[1].idx;
            r.weight1 = pairs[1].w / s;
        }
        if (n > 2)
        {
            r.boneIndex2 = pairs[2].idx;
            r.weight2 = pairs[2].w / s;
        }
        if (n > 3)
        {
            r.boneIndex3 = pairs[3].idx;
            r.weight3 = pairs[3].w / s;
        }
        return r;
    }

    void SanitizeHeadVersusArmWeights(SkinnedMeshRenderer smr, Mesh mesh)
    {
        var bones = smr.bones;
        if (bones == null || bones.Length == 0) return;
        var bws = mesh.boneWeights;
        if (bws == null || bws.Length == 0) return;

        int jHead = FindBoneIndexBySmplKey(bones, "J15");
        int jNeck = FindBoneIndexBySmplKey(bones, "J12");
        int jSpine2 = FindBoneIndexBySmplKey(bones, "J09");
        int neckTarget = jNeck >= 0 ? jNeck : jSpine2;
        if (jHead < 0 || neckTarget < 0) return;

        var isArmBone = new bool[bones.Length];
        for (int bi = 0; bi < bones.Length; bi++)
        {
            if (bones[bi] == null) continue;
            var key = ResolveSmplKey(bones[bi].name);
            for (int a = 0; a < ArmChainSmplKeys.Length; a++)
            {
                if (string.Equals(key, ArmChainSmplKeys[a], StringComparison.OrdinalIgnoreCase))
                {
                    isArmBone[bi] = true;
                    break;
                }
            }
        }

        var dict = new Dictionary<int, float>(12);
        var newBws = new BoneWeight[bws.Length];
        float shift = Mathf.Clamp01(headWeightShiftToNeck);
        float minArm = minArmWeightToDecoupleHead;

        for (int vi = 0; vi < bws.Length; vi++)
        {
            BoneWeightToDict(in bws[vi], dict);
            if (dict.Count == 0)
            {
                newBws[vi] = bws[vi];
                continue;
            }

            if (!dict.TryGetValue(jHead, out var wHead) || wHead <= 1e-8f)
            {
                newBws[vi] = PackTop4BoneWeights(dict);
                continue;
            }

            float wArm = 0f;
            foreach (var kv in dict)
            {
                if (kv.Key >= 0 && kv.Key < isArmBone.Length && isArmBone[kv.Key])
                    wArm += kv.Value;
            }
            if (wArm < minArm)
            {
                newBws[vi] = PackTop4BoneWeights(dict);
                continue;
            }

            float xfer = wHead * shift;
            dict[jHead] = wHead - xfer;
            dict.TryGetValue(neckTarget, out var wNeck);
            dict[neckTarget] = wNeck + xfer;

            float sum = 0f;
            foreach (var kv in dict)
                sum += kv.Value;
            if (sum > 1e-8f)
            {
                var keys = new List<int>(dict.Count);
                foreach (var kv in dict) keys.Add(kv.Key);
                foreach (var k in keys)
                    dict[k] /= sum;
            }
            newBws[vi] = PackTop4BoneWeights(dict);
        }

        mesh.boneWeights = newBws;
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
            int bonesWithWeight = 0;
            for (int i = 0; i < totals.Length; i++)
            {
                float w = totals[i];
                if (w <= 0f) continue;
                bonesWithWeight++;
                top.Add((i, w));
            }
            top.Sort((a, b) => b.w.CompareTo(a.w));

            if (bonesWithWeight <= 1)
            {
                string bn = top.Count > 0 && bones != null && top[0].idx < bones.Length && bones[top[0].idx] != null
                    ? bones[top[0].idx].name
                    : "?";
                Debug.LogError(
                    $"[SmplGarmentManager] Garment mesh '{smr.name}' has skin weights on only ONE bone ({bn}). " +
                    "The shirt cannot follow arms/torso — it is rigidly glued to that bone. " +
                    "Re-bind / transfer weights from the SMPL body in Blender (see README: Tools/blender_golden_garment_from_fbx.py), " +
                    "then re-export the FBX with full J00–J23 influence.",
                    smr);
            }

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

