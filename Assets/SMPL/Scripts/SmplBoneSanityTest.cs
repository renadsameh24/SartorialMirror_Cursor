using System.Collections.Generic;
using UnityEngine;

public class SmplBoneSanityTest : MonoBehaviour
{
    [Header("Find mesh")]
    public Transform smplRoot;
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";
    public string skinnedMeshObjectName = "smpl_neutral";

    [Header("Test bone")]
    public string boneName = "J16";         // try J16 first, then J01
    public Vector3 axis = Vector3.up;       // axis to rotate around
    public float degrees = 30f;             // rotation amount

    [Header("Control")]
    public bool animate = true;             // if true, oscillates
    public bool applyOnce = false;          // if true, applies one-time and stops
    public bool resetOnDisable = true;

    private SkinnedMeshRenderer _smr;
    private Transform _bone;
    private Quaternion _initialLocalRot;
    private readonly Dictionary<string, Transform> _bonesByName = new();

    void Start()
    {
        AutoFindRoot();

        var meshT = FindDeepChild(smplRoot, skinnedMeshObjectName);
        _smr = meshT ? meshT.GetComponent<SkinnedMeshRenderer>() : null;

        if (_smr == null)
        {
            Debug.LogWarning("[SanityTest] SkinnedMeshRenderer not found.");
            return;
        }

        _bonesByName.Clear();
        foreach (var b in _smr.bones)
            if (b != null && !_bonesByName.ContainsKey(b.name))
                _bonesByName[b.name] = b;

        Debug.Log($"[SanityTest] bones count = {_smr.bones.Length}");

        if (!_bonesByName.TryGetValue(boneName, out _bone) || _bone == null)
        {
            Debug.LogWarning("[SanityTest] Bone not found by name: " + boneName);
            return;
        }

        _initialLocalRot = _bone.localRotation;
        Debug.Log("[SanityTest] Found bone: " + boneName + ". Rotate will show if mesh is weighted to it.");
    }

    void Update()
    {
        if (_bone == null) return;

        if (applyOnce)
        {
            _bone.localRotation = _initialLocalRot * Quaternion.AngleAxis(degrees, axis.normalized);
            applyOnce = false;
            animate = false;
            return;
        }

        if (!animate) return;

        float t = Mathf.Sin(Time.time * 2f) * 0.5f + 0.5f; // 0..1
        float a = Mathf.Lerp(-degrees, degrees, t);
        _bone.localRotation = _initialLocalRot * Quaternion.AngleAxis(a, axis.normalized);
    }

    void OnDisable()
    {
        if (resetOnDisable && _bone != null)
            _bone.localRotation = _initialLocalRot;
    }

    void AutoFindRoot()
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
