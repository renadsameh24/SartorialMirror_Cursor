using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Clean baseline runtime pipeline:
/// - Disables experimental/overlapping motion scripts that can cause NaN/Inf or mirrored motion.
/// - Ensures exactly one SmplGarmentManager spawns exactly one garment.
/// - Forces Remap mode for SMPL-skinned garments.
/// Attach to the same GameObject as SartorialMirrorBootstrap (auto-installed by bootstrap in Play Mode).
/// </summary>
[DefaultExecutionOrder(-20000)]
[DisallowMultipleComponent]
public sealed class CleanGarmentPipelineInstaller : MonoBehaviour
{
    [Header("Targets")]
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";

    [Header("Behavior")]
    public bool disableConflictingPoseScripts = true;
    public bool forceRemapMode = true;
    public bool forceMirrorAcrossRootX = false;
    public int spawnGarmentIndex = 0;

    private bool _ran = false;

    void Awake()
    {
        if (!Application.isPlaying) return;
        RunOnce();
    }

    void RunOnce()
    {
        if (_ran) return;
        _ran = true;

        var smplRoot = FindSmplRoot();
        if (smplRoot == null)
        {
            Debug.LogWarning($"[CleanPipeline] SMPL root '{smplRootName}' not found.", this);
            return;
        }

        if (disableConflictingPoseScripts)
            DisableConflictingScriptsGlobal(smplRoot);

        var fk = FindObjectOfType<SpheresToBones_FKDriver>(true);
        if (fk != null)
            fk.mirrorAcrossRootX = forceMirrorAcrossRootX;

        var mgr = FindObjectOfType<SmplGarmentManager>(true);
        if (mgr == null)
        {
            Debug.LogWarning("[CleanPipeline] SmplGarmentManager not found (Bootstrap should add it).", this);
            return;
        }

        if (forceRemapMode)
        {
            mgr.driveGarmentArmatureFromSmpl = false;
            mgr.recalculateBindPosesAfterRemap = false;
            mgr.decoupleHeadFromArmWeightsAfterRemap = false;
            mgr.applyArmTwistFix = false;
            mgr.useBindPoseRotationOffset = false;
            mgr.clampBoneStretch = false;
            mgr.drivePositions = false;
            mgr.matchDrivenBonesToSmplWorld = true;
        }

        // Spawn one garment as baseline.
        if (spawnGarmentIndex >= 0 && mgr.HasCatalog)
        {
            mgr.TrySetActive(spawnGarmentIndex);
        }
    }

    Transform FindSmplRoot()
    {
        var go = GameObject.Find(smplRootName);
        if (go != null) return go.transform;
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in all)
        {
            if (t == null) continue;
            if (t.name != smplRootName) continue;
            if (t.gameObject != null && t.gameObject.scene.IsValid())
                return t;
        }
        return null;
    }

    static void DisableConflictingScriptsGlobal(Transform smplRoot)
    {
        // These scripts overlap in responsibility (root follow / sphere scaling / IK experiments)
        // and are the most common sources of NaN assertions when fed bad joint data.
        var typeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "AutoScaleSpheresToSmpl",
            "HardFollowPelvisRoot",
            "SmplRootFollowPelvisSphere",
            "SmplRootFollowPelvisSphereOffset",
            "CharacterRootFollowPelvisSphereSafe",
            "FollowTransform",
            "FollowTransformEndOfFrame",
            "SmplPoseFromSpheres",
            "IKDriver_ExactUpdate",
            "IKDriver_Safe",
            "IKDriver_Stable",
            "IKTargetsFromSpheres",
            "IKTargetsAndHintsAuto",
            "IKTargetsAndHintsDriver_BonePlane",
            "IKTargetsAndHintsFromSpheresStable",
            "IKTargetsAndHints_FromSpheres_LengthScaled",
            "SmplFkDriverPro",
            "SmplFkDriver_FromSpheresPro",
            "SmplFullBodyBoneDriver",
            "SmplFullBodyBoneDriverV2",
            "SmplFullBodyBoneDriverV3",
            "SmplFullBodyBoneDriverV4",
            "SmplArmBoneDriver",
            "SmplRootFollowHipsMidpoint",
            "SmplRootFollowPelvisSphere",
            "CharacterRootFollowPelvisSphere",
        };

        int disabled = 0;
        foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (mb == null || !mb.enabled) continue;
            var n = mb.GetType().Name;
            if (!typeNames.Contains(n)) continue;
            // Keep the FK driver; it's the intended pose driver.
            if (n == nameof(SpheresToBones_FKDriver)) continue;
            mb.enabled = false;
            disabled++;
        }

        if (disabled > 0)
            Debug.Log($"[CleanPipeline] Disabled {disabled} conflicting pose scripts (global scan).", smplRoot);
    }
}

