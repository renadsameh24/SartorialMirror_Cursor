using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SmplFullBodyBoneDriverV2 : MonoBehaviour
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
    // Torso bones (you can change these after you print bones)
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
    // Torso keys (from your spheres)
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
            Debug.LogWarning("[FullBodyV2] smplRoot not found.");
            yield break;
        }

        // Disable Animator
        var animator = smplRoot.GetComponentInChildren<Animator>();
        if (animator != null) animator.enabled = false;

        Transform meshT = FindDeepChild(smplRoot, skinnedMeshObjectName);
        _smr = meshT ? meshT.GetComponent<SkinnedMeshRenderer>() : null;
        if (_smr == null)
        {
            Debug.LogWarning("[FullBodyV2] SkinnedMeshRenderer not found.");
            yield break;
        }

        _bonesByName.Clear();
        foreach (var b in _smr.bones)
            if (b != null && !_bonesByName.ContainsKey(b.name))
                _bonesByName[b.name] = b;

        Debug.Log($"[FullBodyV2] bones count = {_smr.bones.Length}");

        // Wait for required spheres
        yield return new WaitUntil(() =>
            Joint(pelvis) && Joint(spine) && Joint(neck) && Joint(head) &&
            Joint(lShoulder) && Joint(lElbow) && Joint(lWrist) &&
            Joint(rShoulder) && Joint(rElbow) && Joint(rWrist) &&
            Joint(lHip) && Joint(lKnee) && Joint(lAnkle) &&
            Joint(rHip) && Joint(rKnee) && Joint(rAnkle)
        );

        // Compute offsets once
        if (driveTorso)
        {
            ComputeOffset(pelvisBone, pelvis, spine, false, fallbackUp);
            ComputeOffset(spineBone,  spine,  neck,  false, fallbackUp);
            ComputeOffset(neckBone,   neck,   head,  false, fallbackUp);
            // headBone optional (often tiny), you can skip driving head if you want
        }

        if (driveArms)
        {
            ComputeOffset(leftUpperArmBone,  lShoulder, lElbow, swapArmTargets, fallbackUp);
            ComputeOffset(leftForearmBone,   lElbow,    lWrist, swapArmTargets, fallbackUp);
            ComputeOffset(rightUpperArmBone, rShoulder, rElbow, swapArmTargets, fallbackUp);
            ComputeOffset(rightForearmBone,  rElbow,    rWrist, swapArmTargets, fallbackUp);
        }

        if (driveLegs)
        {
            ComputeOffset(leftThighBone,  lHip,  lKnee,  false, fallbackUp);
            ComputeOffset(leftShinBone,   lKnee, lAnkle, false, fallbackUp);
            ComputeOffset(rightThighBone, rHip,  rKnee,  false, fallbackUp);
            ComputeOffset(rightShinBone,  rKnee, rAnkle, false, fallbackUp);
        }

        Debug.Log("✅ [FullBodyV2] Ready (torso+arms+legs).");
    }

    void LateUpdate()
    {
        if (_smr == null) return;

        if (driveTorso)
        {
            ApplyBone(pelvisBone, pelvis, spine, false, fallbackUp);
            ApplyBone(spineBone,  spine,  neck,  false, fallbackUp);
            ApplyBone(neckBone,   neck,   head,  false, fallbackUp);
        }

        if (driveArms)
        {
            ApplyBone(leftUpperArmBone,  lShoulder, lElbow, swapArmTargets, fallbackUp);
            ApplyBone(leftForearmBone,   lElbow,    lWrist, swapArmTargets, fallbackUp);
            ApplyBone(rightUpperArmBone, rShoulder, rElbow, swapArmTargets, fallbackUp);
            ApplyBone(rightForearmBone,  rElbow,    rWrist, swapArmTargets, fallbackUp);
        }

        if (driveLegs)
        {
            ApplyBone(leftThighBone,  lHip,  lKnee,  false, fallbackUp);
            ApplyBone(leftShinBone,   lKnee, lAnkle, false, fallbackUp);
            ApplyBone(rightThighBone, rHip,  rKnee,  false, fallbackUp);
            ApplyBone(rightShinBone,  rKnee, rAnkle, false, fallbackUp);
        }
    }

    void ApplyBone(string boneName, string startKey, string endKey, bool doSwapLR, Vector3 up)
    {
        if (!_bonesByName.TryGetValue(boneName, out var bone) || bone == null) return;
        if (!_boneOffsets.TryGetValue(boneName, out var offset)) return;

        string s = doSwapLR ? SwapLR(startKey) : startKey;
        string e = doSwapLR ? SwapLR(endKey) : endKey;

        Transform a = Joint(s);
        Transform b = Joint(e);
        if (a == null || b == null) return;

        Vector3 dirWorld = (b.position - a.position);
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
