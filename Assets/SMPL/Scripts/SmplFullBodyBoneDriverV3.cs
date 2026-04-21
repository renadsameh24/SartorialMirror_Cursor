using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SmplFullBodyBoneDriverV3 : MonoBehaviour
{
    [Header("Find SMPL mesh (SkinnedMeshRenderer)")]
    public Transform smplRoot;
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";
    public string skinnedMeshObjectName = "smpl_neutral";

    [Header("Enable Parts")]
    public bool driveTorso = true;
    public bool driveArms = true;
    public bool driveLegs = true;

    [Header("Bones (edit if your rig differs)")]
    // Torso
    public string pelvisBone = "J00";
    public string spineBone  = "J03";
    public string neckBone   = "J12";
    public string headBone   = "J15";

    // Arms
    public string leftUpperArmBone = "J16";
    public string leftForearmBone  = "J18";
    public string rightUpperArmBone = "J17";
    public string rightForearmBone  = "J19";

    // Legs
    public string leftThighBone = "J01";
    public string leftShinBone  = "J04";
    public string rightThighBone = "J02";
    public string rightShinBone  = "J05";

    [Header("Joint sphere keys")]
    // Torso keys
    public string pelvis = "pelvis";
    public string spine  = "spine";
    public string neck   = "neck";
    public string head   = "head";

    // Arms keys
    public string lShoulder = "l_shoulder";
    public string lElbow    = "l_elbow";
    public string lWrist    = "l_wrist";
    public string rShoulder = "r_shoulder";
    public string rElbow    = "r_elbow";
    public string rWrist    = "r_wrist";

    // Legs keys
    public string lHip   = "l_hip";
    public string lKnee  = "l_knee";
    public string lAnkle = "l_ankle";
    public string rHip   = "r_hip";
    public string rKnee  = "r_knee";
    public string rAnkle = "r_ankle";

    [Header("Mirroring fix (arms only if needed)")]
    public bool swapArmTargets = false;

    [Header("Rotation")]
    [Range(0f, 1f)] public float rotationLerp = 0.75f; // good at 60fps
    public Vector3 fallbackUp = new Vector3(0, 1, 0);

    [Header("Anti-cross clamp (recommended ON)")]
    public bool preventArmCross = true;
    public float chestPlanePadding = 0.02f; // meters allowed to cross center
    public float clampStrength = 1.0f;      // 1 = hard clamp, 0.5 = softer

    private SkinnedMeshRenderer _smr;
    private readonly Dictionary<string, Transform> _bonesByName = new();
    private readonly Dictionary<string, Quaternion> _boneOffsets = new();

    Transform Joint(string key)
    {
        var go = GameObject.Find("J_" + key);
        return go ? go.transform : null;
    }

    IEnumerator Start()
    {
        yield return null;
        yield return null;

        AutoFindSmplRoot();
        if (smplRoot == null)
        {
            Debug.LogWarning("[FullBodyV3] smplRoot not found.");
            yield break;
        }

        // Disable Animator
        var animator = smplRoot.GetComponentInChildren<Animator>();
        if (animator != null) animator.enabled = false;

        Transform meshT = FindDeepChild(smplRoot, skinnedMeshObjectName);
        _smr = meshT ? meshT.GetComponent<SkinnedMeshRenderer>() : null;
        if (_smr == null)
        {
            Debug.LogWarning("[FullBodyV3] SkinnedMeshRenderer not found.");
            yield break;
        }

        _bonesByName.Clear();
        foreach (var b in _smr.bones)
            if (b != null && !_bonesByName.ContainsKey(b.name))
                _bonesByName[b.name] = b;

        Debug.Log($"[FullBodyV3] bones count = {_smr.bones.Length}");

        // Wait for required spheres
        yield return new WaitUntil(() =>
            Joint(pelvis) && Joint(spine) &&
            Joint(lShoulder) && Joint(lElbow) && Joint(lWrist) &&
            Joint(rShoulder) && Joint(rElbow) && Joint(rWrist) &&
            Joint(lHip) && Joint(lKnee) && Joint(lAnkle) &&
            Joint(rHip) && Joint(rKnee) && Joint(rAnkle)
        );

        // Compute offsets once in calibration pose
        if (driveTorso)
        {
            ComputeOffset(pelvisBone, pelvis, spine, false, fallbackUp);
            // Spine/neck/head offsets are optional; keep if you like
            if (Joint(neck) && Joint(head))
            {
                ComputeOffset(spineBone, spine, neck, false, fallbackUp);
                ComputeOffset(neckBone,  neck,  head, false, fallbackUp);
            }
        }

        // Arms offsets use body-relative up computed now
        Vector3 leftUp = ComputeArmUp(isLeft: true);
        Vector3 rightUp = ComputeArmUp(isLeft: false);

        if (driveArms)
        {
            ComputeOffset(leftUpperArmBone,  lShoulder, lElbow, swapArmTargets, leftUp);
            ComputeOffset(leftForearmBone,   lElbow,    lWrist, swapArmTargets, leftUp);
            ComputeOffset(rightUpperArmBone, rShoulder, rElbow, swapArmTargets, rightUp);
            ComputeOffset(rightForearmBone,  rElbow,    rWrist, swapArmTargets, rightUp);
        }

        if (driveLegs)
        {
            ComputeOffset(leftThighBone,  lHip,  lKnee,  false, fallbackUp);
            ComputeOffset(leftShinBone,   lKnee, lAnkle, false, fallbackUp);
            ComputeOffset(rightThighBone, rHip,  rKnee,  false, fallbackUp);
            ComputeOffset(rightShinBone,  rKnee, rAnkle, false, fallbackUp);
        }

        Debug.Log("✅ [FullBodyV3] Ready (stable arms, anti-cross).");
    }

    void LateUpdate()
    {
        if (_smr == null) return;

        // Torso (optional)
        if (driveTorso)
        {
            ApplyBone(pelvisBone, pelvis, spine, false, fallbackUp, Vector3.zero);
            if (Joint(neck) && Joint(head))
            {
                ApplyBone(spineBone, spine, neck, false, fallbackUp, Vector3.zero);
                ApplyBone(neckBone,  neck,  head, false, fallbackUp, Vector3.zero);
            }
        }

        // Arms (body-relative up vectors every frame)
        if (driveArms)
        {
            Vector3 leftUp = ComputeArmUp(true);
            Vector3 rightUp = ComputeArmUp(false);

            ApplyArmWithClamp(leftUpperArmBone,  lShoulder, lElbow, swapArmTargets, leftUp,  isLeft:true);
            ApplyArmWithClamp(leftForearmBone,   lElbow,    lWrist, swapArmTargets, leftUp,  isLeft:true);
            ApplyArmWithClamp(rightUpperArmBone, rShoulder, rElbow, swapArmTargets, rightUp, isLeft:false);
            ApplyArmWithClamp(rightForearmBone,  rElbow,    rWrist, swapArmTargets, rightUp, isLeft:false);
        }

        // Legs
        if (driveLegs)
        {
            ApplyBone(leftThighBone,  lHip,  lKnee,  false, fallbackUp, Vector3.zero);
            ApplyBone(leftShinBone,   lKnee, lAnkle, false, fallbackUp, Vector3.zero);
            ApplyBone(rightThighBone, rHip,  rKnee,  false, fallbackUp, Vector3.zero);
            ApplyBone(rightShinBone,  rKnee, rAnkle, false, fallbackUp, Vector3.zero);
        }
    }

    // ---- Arm helpers ----
    Vector3 ComputeArmUp(bool isLeft)
    {
        // Use spine as "up" and shoulder line to derive a stable forward
        var p = Joint(pelvis);
        var s = Joint(spine);
        var ls = Joint(lShoulder);
        var rs = Joint(rShoulder);

        Vector3 up = fallbackUp;
        Vector3 forward = Vector3.forward;

        if (p && s)
        {
            Vector3 spineDir = (s.position - p.position);
            if (spineDir.sqrMagnitude > 1e-8f) up = spineDir.normalized;
        }

        if (ls && rs)
        {
            Vector3 rightDir = (rs.position - ls.position);
            if (rightDir.sqrMagnitude > 1e-8f)
            {
                rightDir.Normalize();
                forward = Vector3.Cross(up, rightDir).normalized;
                if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
            }
        }

        // Arm up should be perpendicular to arm direction and forward-ish.
        // We'll return a vector leaning toward forward to stabilize twist.
        return (up + forward * 0.6f).normalized;
    }

    void ApplyArmWithClamp(string boneName, string startKey, string endKey, bool doSwapLR, Vector3 up, bool isLeft)
    {
        // Clamp the target joint positions slightly so left arm doesn't cross to right and vice versa
        if (!preventArmCross)
        {
            ApplyBone(boneName, startKey, endKey, doSwapLR, up, Vector3.zero);
            return;
        }

        string sKey = doSwapLR ? SwapLR(startKey) : startKey;
        string eKey = doSwapLR ? SwapLR(endKey) : endKey;

        var startT = Joint(sKey);
        var endT = Joint(eKey);
        if (startT == null || endT == null)
        {
            ApplyBone(boneName, startKey, endKey, doSwapLR, up, Vector3.zero);
            return;
        }

        // Center plane X based on pelvis
        var p = Joint(pelvis);
        float centerX = p ? p.position.x : 0f;

        // Hard clamp: left end joint should stay <= centerX + padding
        // right end joint should stay >= centerX - padding
        Vector3 clampedEnd = endT.position;
        if (isLeft)
        {
            float maxX = centerX + chestPlanePadding;
            if (clampedEnd.x > maxX)
                clampedEnd.x = Mathf.Lerp(clampedEnd.x, maxX, clampStrength);
        }
        else
        {
            float minX = centerX - chestPlanePadding;
            if (clampedEnd.x < minX)
                clampedEnd.x = Mathf.Lerp(clampedEnd.x, minX, clampStrength);
        }

        // Apply using a hint (difference between clamped and original end)
        Vector3 hint = (clampedEnd - endT.position);
        ApplyBone(boneName, startKey, endKey, doSwapLR, up, hint);
    }

    // ---- Core look rotation driver ----
    void ApplyBone(string boneName, string startKey, string endKey, bool doSwapLR, Vector3 up, Vector3 extraHint)
    {
        if (!_bonesByName.TryGetValue(boneName, out var bone) || bone == null) return;
        if (!_boneOffsets.TryGetValue(boneName, out var offset)) return;

        string s = doSwapLR ? SwapLR(startKey) : startKey;
        string e = doSwapLR ? SwapLR(endKey) : endKey;

        Transform a = Joint(s);
        Transform b = Joint(e);
        if (a == null || b == null) return;

        Vector3 dirWorld = (b.position - a.position) + extraHint;
        if (dirWorld.sqrMagnitude < 1e-8f) return;
        dirWorld.Normalize();

        Quaternion desiredWorld = Quaternion.LookRotation(dirWorld, up) * offset;

        Quaternion desiredLocal = bone.parent != null
            ? Quaternion.Inverse(bone.parent.rotation) * desiredWorld
            : desiredWorld;

        if (rotationLerp >= 0.999f)
            bone.localRotation = desiredLocal;
        else
            bone.localRotation = Quaternion.Slerp(bone.localRotation, desiredLocal, rotationLerp);
    }

    void ComputeOffset(string boneName, string startKey, string endKey, bool doSwapLR, Vector3 up)
    {
        if (!_bonesByName.TryGetValue(boneName, out var bone) || bone == null) return;

        string s = doSwapLR ? SwapLR(startKey) : startKey;
        string e = doSwapLR ? SwapLR(endKey) : endKey;

        Transform a = Joint(s);
        Transform b = Joint(e);
        if (a == null || b == null) return;

        Vector3 dirWorld = (b.position - a.position);
        if (dirWorld.sqrMagnitude < 1e-8f) return;
        dirWorld.Normalize();

        Quaternion look = Quaternion.LookRotation(dirWorld, up);
        _boneOffsets[boneName] = Quaternion.Inverse(look) * bone.rotation;
    }

    string SwapLR(string key)
    {
        if (key.StartsWith("l_")) return "r_" + key.Substring(2);
        if (key.StartsWith("r_")) return "l_" + key.Substring(2);
        return key;
    }

    void AutoFindSmplRoot()
    {
        if (smplRoot != null) return;
        var go = GameObject.Find(smplRootName);
        if (go != null) smplRoot = go.transform;
    }

    Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var r = FindDeepChild(child, name);
            if (r != null) return r;
        }
        return null;
    }
}

