using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SmplFullBodyBoneDriverV4 : MonoBehaviour
{
    [Header("Find SMPL mesh (SkinnedMeshRenderer)")]
    public Transform smplRoot;
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";
    public string skinnedMeshObjectName = "smpl_neutral";

    [Header("Enable Parts")]
    public bool driveTorso = true;
    public bool driveArms = true;
    public bool driveLegs = true;

    [Header("Bones")]
    // Torso (optional)
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

    [Header("Joint sphere keys (your J_* spheres)")]
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

    [Header("If your arm input is mirrored, toggle this")]
    public bool swapArmTargets = false;

    [Header("Rotation smoothing")]
    [Range(0f, 1f)] public float rotationLerp = 0.85f; // 0.75-0.95 is good at 60fps

    [Header("Fallback up (used if arm plane degenerates)")]
    public Vector3 fallbackUp = new Vector3(0, 1, 0);

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
            Debug.LogWarning("[FullBodyV4] smplRoot not found.");
            yield break;
        }

        // Disable Animator
        var animator = smplRoot.GetComponentInChildren<Animator>();
        if (animator != null) animator.enabled = false;

        Transform meshT = FindDeepChild(smplRoot, skinnedMeshObjectName);
        _smr = meshT ? meshT.GetComponent<SkinnedMeshRenderer>() : null;
        if (_smr == null)
        {
            Debug.LogWarning("[FullBodyV4] SkinnedMeshRenderer not found.");
            yield break;
        }

        _bonesByName.Clear();
        foreach (var b in _smr.bones)
            if (b != null && !_bonesByName.ContainsKey(b.name))
                _bonesByName[b.name] = b;

        Debug.Log($"[FullBodyV4] bones count = {_smr.bones.Length}");

        // wait for spheres
        yield return new WaitUntil(() =>
            Joint(lShoulder) && Joint(lElbow) && Joint(lWrist) &&
            Joint(rShoulder) && Joint(rElbow) && Joint(rWrist) &&
            Joint(lHip) && Joint(lKnee) && Joint(lAnkle) &&
            Joint(rHip) && Joint(rKnee) && Joint(rAnkle)
        );

        // ---- OFFSETS (calibration pose) ----
        // Arms: use plane-normal up
        ComputeArmOffset(leftUpperArmBone,  lShoulder, lElbow, lWrist, swapArmTargets);
        ComputeArmOffset(leftForearmBone,   lElbow,    lWrist, lShoulder, swapArmTargets); // helper third point is shoulder
        ComputeArmOffset(rightUpperArmBone, rShoulder, rElbow, rWrist, swapArmTargets);
        ComputeArmOffset(rightForearmBone,  rElbow,    rWrist, rShoulder, swapArmTargets);

        // Legs: simple up (works fine)
        ComputeOffsetLook(leftThighBone,  lHip,  lKnee,  false, fallbackUp);
        ComputeOffsetLook(leftShinBone,   lKnee, lAnkle, false, fallbackUp);
        ComputeOffsetLook(rightThighBone, rHip,  rKnee,  false, fallbackUp);
        ComputeOffsetLook(rightShinBone,  rKnee, rAnkle, false, fallbackUp);

        // Torso (optional)
        if (driveTorso && Joint(pelvis) && Joint(spine))
        {
            ComputeOffsetLook(pelvisBone, pelvis, spine, false, fallbackUp);
            if (Joint(neck)) ComputeOffsetLook(spineBone, spine, neck, false, fallbackUp);
            if (Joint(head)) ComputeOffsetLook(neckBone, neck, head, false, fallbackUp);
        }

        Debug.Log("✅ [FullBodyV4] Ready (plane-normal arms).");
    }

    void LateUpdate()
    {
        if (_smr == null) return;

        // Torso
        if (driveTorso && Joint(pelvis) && Joint(spine))
        {
            ApplyLook(pelvisBone, pelvis, spine, false, fallbackUp);
            if (Joint(neck)) ApplyLook(spineBone, spine, neck, false, fallbackUp);
            if (Joint(head)) ApplyLook(neckBone, neck, head, false, fallbackUp);
        }

        // Arms (plane-normal)
        if (driveArms)
        {
            ApplyArm(leftUpperArmBone,  lShoulder, lElbow, lWrist, swapArmTargets);
            ApplyArm(leftForearmBone,   lElbow,    lWrist, lShoulder, swapArmTargets);

            ApplyArm(rightUpperArmBone, rShoulder, rElbow, rWrist, swapArmTargets);
            ApplyArm(rightForearmBone,  rElbow,    rWrist, rShoulder, swapArmTargets);
        }

        // Legs
        if (driveLegs)
        {
            ApplyLook(leftThighBone,  lHip,  lKnee,  false, fallbackUp);
            ApplyLook(leftShinBone,   lKnee, lAnkle, false, fallbackUp);
            ApplyLook(rightThighBone, rHip,  rKnee,  false, fallbackUp);
            ApplyLook(rightShinBone,  rKnee, rAnkle, false, fallbackUp);
        }
    }

    // ---------------- Arms with plane-normal up ----------------
    void ApplyArm(string boneName, string startKey, string endKey, string thirdKey, bool doSwapLR)
    {
        if (!_bonesByName.TryGetValue(boneName, out var bone) || bone == null) return;
        if (!_boneOffsets.TryGetValue(boneName, out var offset)) return;

        string aKey = doSwapLR ? SwapLR(startKey) : startKey;
        string bKey = doSwapLR ? SwapLR(endKey) : endKey;
        string cKey = doSwapLR ? SwapLR(thirdKey) : thirdKey;

        Transform A = Joint(aKey);
        Transform B = Joint(bKey);
        Transform C = Joint(cKey);
        if (A == null || B == null || C == null) return;

        Vector3 dir = (B.position - A.position);
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();

        // plane normal: cross(upperArmDir, forearmDir) style
        // here we use cross(dir, (C-A)) to define plane around this segment
        Vector3 v2 = (C.position - A.position);
        Vector3 up = Vector3.Cross(dir, v2);
        if (up.sqrMagnitude < 1e-8f) up = fallbackUp;
        else up.Normalize();

        Quaternion desiredWorld = Quaternion.LookRotation(dir, up) * offset;
        Quaternion desiredLocal = bone.parent ? Quaternion.Inverse(bone.parent.rotation) * desiredWorld : desiredWorld;

        if (rotationLerp >= 0.999f)
            bone.localRotation = desiredLocal;
        else
            bone.localRotation = Quaternion.Slerp(bone.localRotation, desiredLocal, rotationLerp);
    }

    void ComputeArmOffset(string boneName, string startKey, string endKey, string thirdKey, bool doSwapLR)
    {
        if (!_bonesByName.TryGetValue(boneName, out var bone) || bone == null) return;

        string aKey = doSwapLR ? SwapLR(startKey) : startKey;
        string bKey = doSwapLR ? SwapLR(endKey) : endKey;
        string cKey = doSwapLR ? SwapLR(thirdKey) : thirdKey;

        Transform A = Joint(aKey);
        Transform B = Joint(bKey);
        Transform C = Joint(cKey);
        if (A == null || B == null || C == null) return;

        Vector3 dir = (B.position - A.position);
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();

        Vector3 v2 = (C.position - A.position);
        Vector3 up = Vector3.Cross(dir, v2);
        if (up.sqrMagnitude < 1e-8f) up = fallbackUp;
        else up.Normalize();

        Quaternion look = Quaternion.LookRotation(dir, up);
        _boneOffsets[boneName] = Quaternion.Inverse(look) * bone.rotation;
    }

    // ---------------- Generic look driver (legs/torso) ----------------
    void ApplyLook(string boneName, string startKey, string endKey, bool doSwapLR, Vector3 up)
    {
        if (!_bonesByName.TryGetValue(boneName, out var bone) || bone == null) return;
        if (!_boneOffsets.TryGetValue(boneName, out var offset)) return;

        string s = doSwapLR ? SwapLR(startKey) : startKey;
        string e = doSwapLR ? SwapLR(endKey) : endKey;

        Transform A = Joint(s);
        Transform B = Joint(e);
        if (A == null || B == null) return;

        Vector3 dir = (B.position - A.position);
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();

        Quaternion desiredWorld = Quaternion.LookRotation(dir, up) * offset;
        Quaternion desiredLocal = bone.parent ? Quaternion.Inverse(bone.parent.rotation) * desiredWorld : desiredWorld;

        if (rotationLerp >= 0.999f)
            bone.localRotation = desiredLocal;
        else
            bone.localRotation = Quaternion.Slerp(bone.localRotation, desiredLocal, rotationLerp);
    }

    void ComputeOffsetLook(string boneName, string startKey, string endKey, bool doSwapLR, Vector3 up)
    {
        if (!_bonesByName.TryGetValue(boneName, out var bone) || bone == null) return;

        string s = doSwapLR ? SwapLR(startKey) : startKey;
        string e = doSwapLR ? SwapLR(endKey) : endKey;

        Transform A = Joint(s);
        Transform B = Joint(e);
        if (A == null || B == null) return;

        Vector3 dir = (B.position - A.position);
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();

        Quaternion look = Quaternion.LookRotation(dir, up);
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
