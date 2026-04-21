using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SmplArmBoneDriver : MonoBehaviour
{
    [Header("Find SMPL mesh (SkinnedMeshRenderer)")]
    public Transform smplRoot;
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";
    public string skinnedMeshObjectName = "smpl_neutral";   // child name in Hierarchy

    [Header("Bone names (must exist in SkinnedMeshRenderer.bones)")]
    public string leftUpperArmBone = "J16";
    public string leftForearmBone  = "J18";
    public string rightUpperArmBone = "J17";
    public string rightForearmBone  = "J19";

    [Header("Joint sphere keys (from your JSON)")]
    public string lShoulder = "l_shoulder";
    public string lElbow    = "l_elbow";
    public string lWrist    = "l_wrist";
    public string rShoulder = "r_shoulder";
    public string rElbow    = "r_elbow";
    public string rWrist    = "r_wrist";

    [Header("Mirroring fix (if needed)")]
    public bool swapArmTargets = false;

    [Header("Rotation")]
    [Range(0f, 1f)] public float rotationLerp = 1.0f;
    public Vector3 worldUp = new Vector3(0, 1, 0);

    [Header("Debug")]
    public bool debugLogs = false;

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
            Debug.LogWarning("[ArmDriver] smplRoot not found.");
            yield break;
        }

        // Disable Animator so it can’t overwrite rotations
        var animator = smplRoot.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.enabled = false;
            Debug.Log("[ArmDriver] Animator disabled to prevent bone overrides.");
        }

        // Find SkinnedMeshRenderer on child "smpl_neutral"
        Transform meshT = FindDeepChild(smplRoot, skinnedMeshObjectName);
        if (meshT == null)
        {
            Debug.LogWarning("[ArmDriver] Could not find mesh object: " + skinnedMeshObjectName);
            yield break;
        }

        _smr = meshT.GetComponent<SkinnedMeshRenderer>();
        if (_smr == null)
        {
            Debug.LogWarning("[ArmDriver] No SkinnedMeshRenderer found on: " + skinnedMeshObjectName);
            yield break;
        }

        // Build bone dictionary from SkinnedMeshRenderer.bones
        _bonesByName.Clear();
        foreach (var b in _smr.bones)
        {
            if (b != null && !_bonesByName.ContainsKey(b.name))
                _bonesByName[b.name] = b;
        }

        Debug.Log($"[ArmDriver] SkinnedMeshRenderer bones count = {_smr.bones.Length}");

        // Make sure needed bones exist
        if (!HasBone(leftUpperArmBone) || !HasBone(leftForearmBone) || !HasBone(rightUpperArmBone) || !HasBone(rightForearmBone))
        {
            Debug.LogWarning(
                "[ArmDriver] Missing arm bones (J16/J18/J17/J19) in SkinnedMeshRenderer.bones. " +
                "If your mesh doesn't deform when rotating bones manually, re-export from Blender."
            );
            yield break;
        }

        // Wait until joint spheres exist
        yield return new WaitUntil(() =>
            Joint(lShoulder) && Joint(lElbow) && Joint(lWrist) &&
            Joint(rShoulder) && Joint(rElbow) && Joint(rWrist)
        );

        // Compute bind offsets once using current pose
        ComputeOffset(leftUpperArmBone,  lShoulder, lElbow);
        ComputeOffset(leftForearmBone,   lElbow,    lWrist);
        ComputeOffset(rightUpperArmBone, rShoulder, rElbow);
        ComputeOffset(rightForearmBone,  rElbow,    rWrist);

        Debug.Log("✅ [ArmDriver] Ready. Move J_l_wrist / J_r_wrist spheres in Scene view to bend arms.");
    }

    void LateUpdate()
    {
        if (_smr == null) return;

        ApplyBone(leftUpperArmBone,  lShoulder, lElbow);
        ApplyBone(leftForearmBone,   lElbow,    lWrist);
        ApplyBone(rightUpperArmBone, rShoulder, rElbow);
        ApplyBone(rightForearmBone,  rElbow,    rWrist);
    }

    bool HasBone(string boneName) => _bonesByName.ContainsKey(boneName) && _bonesByName[boneName] != null;

    void ApplyBone(string boneName, string startKey, string endKey)
    {
        if (!HasBone(boneName)) return;
        if (!_boneOffsets.TryGetValue(boneName, out var offset)) return;

        string s = startKey;
        string e = endKey;

        if (swapArmTargets)
        {
            s = SwapLR(s);
            e = SwapLR(e);
        }

        Transform a = Joint(s);
        Transform b = Joint(e);
        if (a == null || b == null) return;

        Vector3 dirWorld = b.position - a.position;
        if (dirWorld.sqrMagnitude < 1e-8f) return;
        dirWorld.Normalize();

        // Desired world rotation for this bone
        Quaternion desiredWorld = Quaternion.LookRotation(dirWorld, worldUp) * offset;

        Transform bone = _bonesByName[boneName];

        // Apply as LOCAL rotation (more robust in hierarchies)
        Quaternion desiredLocal = (bone.parent != null)
            ? Quaternion.Inverse(bone.parent.rotation) * desiredWorld
            : desiredWorld;

        if (rotationLerp >= 0.999f)
            bone.localRotation = desiredLocal;
        else
            bone.localRotation = Quaternion.Slerp(bone.localRotation, desiredLocal, rotationLerp);

        if (debugLogs && boneName == leftForearmBone)
            Debug.Log($"[ArmDriver] driving {boneName}, dir={dirWorld}");
    }

    void ComputeOffset(string boneName, string startKey, string endKey)
    {
        if (!HasBone(boneName)) return;

        string s = startKey;
        string e = endKey;

        if (swapArmTargets)
        {
            s = SwapLR(s);
            e = SwapLR(e);
        }

        Transform a = Joint(s);
        Transform b = Joint(e);
        if (a == null || b == null) return;

        Vector3 dirWorld = b.position - a.position;
        if (dirWorld.sqrMagnitude < 1e-8f) return;
        dirWorld.Normalize();

        Quaternion look = Quaternion.LookRotation(dirWorld, worldUp);
        Transform bone = _bonesByName[boneName];

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
