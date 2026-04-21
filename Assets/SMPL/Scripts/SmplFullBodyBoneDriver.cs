using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SmplFullBodyBoneDriver : MonoBehaviour
{
    [Header("Find SMPL mesh (SkinnedMeshRenderer)")]
    public Transform smplRoot;
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";
    public string skinnedMeshObjectName = "smpl_neutral";

    [Header("Bone names (SMPL bones)")]
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
    // Arms
    public string lShoulder = "l_shoulder";
    public string lElbow    = "l_elbow";
    public string lWrist    = "l_wrist";
    public string rShoulder = "r_shoulder";
    public string rElbow    = "r_elbow";
    public string rWrist    = "r_wrist";

    // Legs
    public string lHip   = "l_hip";
    public string lKnee  = "l_knee";
    public string lAnkle = "l_ankle";
    public string rHip   = "r_hip";
    public string rKnee  = "r_knee";
    public string rAnkle = "r_ankle";

    [Header("Extra joints for a stable up vector")]
    public string pelvis = "pelvis";
    public string spine = "spine";

    [Header("Mirroring fix (if needed for arms)")]
    public bool swapArmTargets = false;

    [Header("Rotation")]
    [Range(0f, 1f)] public float rotationLerp = 0.75f; // try 0.75 at 60fps
    public Vector3 fallbackUp = new Vector3(0, 1, 0);

    [Header("Leg stability")]
    [Tooltip("Small forward hint added to knee/ankle direction to avoid snapping.")]
    public float legForwardHint = 0.03f; // meters in world forward direction (after pelvis-forward calc)

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
            Debug.LogWarning("[FullBodyDriver] smplRoot not found.");
            yield break;
        }

        // Disable Animator
        var animator = smplRoot.GetComponentInChildren<Animator>();
        if (animator != null) animator.enabled = false;

        Transform meshT = FindDeepChild(smplRoot, skinnedMeshObjectName);
        _smr = meshT ? meshT.GetComponent<SkinnedMeshRenderer>() : null;
        if (_smr == null)
        {
            Debug.LogWarning("[FullBodyDriver] SkinnedMeshRenderer not found.");
            yield break;
        }

        _bonesByName.Clear();
        foreach (var b in _smr.bones)
            if (b != null && !_bonesByName.ContainsKey(b.name))
                _bonesByName[b.name] = b;

        Debug.Log($"[FullBodyDriver] bones count = {_smr.bones.Length}");

        // Wait until spheres exist
        yield return new WaitUntil(() =>
            Joint(lShoulder) && Joint(lElbow) && Joint(lWrist) &&
            Joint(rShoulder) && Joint(rElbow) && Joint(rWrist) &&
            Joint(lHip) && Joint(lKnee) && Joint(lAnkle) &&
            Joint(rHip) && Joint(rKnee) && Joint(rAnkle) &&
            Joint(pelvis) && Joint(spine)
        );

        // Compute offsets once (calibration pose) using fallbackUp
        ComputeOffset(leftUpperArmBone,  lShoulder, lElbow, swapArmTargets, fallbackUp);
        ComputeOffset(leftForearmBone,   lElbow,    lWrist, swapArmTargets, fallbackUp);
        ComputeOffset(rightUpperArmBone, rShoulder, rElbow, swapArmTargets, fallbackUp);
        ComputeOffset(rightForearmBone,  rElbow,    rWrist, swapArmTargets, fallbackUp);

        ComputeOffset(leftThighBone,  lHip,  lKnee, false, fallbackUp);
        ComputeOffset(leftShinBone,   lKnee, lAnkle, false, fallbackUp);
        ComputeOffset(rightThighBone, rHip,  rKnee, false, fallbackUp);
        ComputeOffset(rightShinBone,  rKnee, rAnkle, false, fallbackUp);

        Debug.Log("✅ [FullBodyDriver] Ready (stable legs).");
    }

    void LateUpdate()
    {
        if (_smr == null) return;

        // Dynamic up derived from pelvis->spine
        Vector3 dynamicUp = GetDynamicUp(out Vector3 pelvisForward);

        // Arms (can use fallbackUp)
        ApplyBone(leftUpperArmBone,  lShoulder, lElbow, swapArmTargets, fallbackUp, Vector3.zero);
        ApplyBone(leftForearmBone,   lElbow,    lWrist, swapArmTargets, fallbackUp, Vector3.zero);
        ApplyBone(rightUpperArmBone, rShoulder, rElbow, swapArmTargets, fallbackUp, Vector3.zero);
        ApplyBone(rightForearmBone,  rElbow,    rWrist, swapArmTargets, fallbackUp, Vector3.zero);

        // Legs (use dynamicUp + forward hint)
        Vector3 legHint = pelvisForward * legForwardHint;

        ApplyBone(leftThighBone,  lHip,  lKnee, false, dynamicUp, legHint);
        ApplyBone(leftShinBone,   lKnee, lAnkle, false, dynamicUp, legHint);
        ApplyBone(rightThighBone, rHip,  rKnee, false, dynamicUp, legHint);
        ApplyBone(rightShinBone,  rKnee, rAnkle, false, dynamicUp, legHint);
    }

    Vector3 GetDynamicUp(out Vector3 pelvisForward)
    {
        var p = Joint(pelvis);
        var s = Joint(spine);

        Vector3 up = fallbackUp;
        pelvisForward = Vector3.forward;

        if (p != null && s != null)
        {
            Vector3 spineDir = (s.position - p.position);
            if (spineDir.sqrMagnitude > 1e-8f)
                up = spineDir.normalized;

            // estimate forward as perpendicular to left-right (hips) and up
            var lh = Joint(lHip);
            var rh = Joint(rHip);
            if (lh != null && rh != null)
            {
                Vector3 rightDir = (rh.position - lh.position);
                if (rightDir.sqrMagnitude > 1e-8f)
                {
                    rightDir.Normalize();
                    pelvisForward = Vector3.Cross(up, rightDir).normalized;
                    if (pelvisForward.sqrMagnitude < 1e-8f) pelvisForward = Vector3.forward;
                }
            }
        }

        return up;
    }

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

        Vector3 dirWorld = b.position - a.position;
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
