using System.Collections.Generic;
using UnityEngine;

public class SmplBoneNamePrinter : MonoBehaviour
{
    public Transform smplRoot;
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";
    public string skinnedMeshObjectName = "smpl_neutral";
    public bool printOnStart = true;

    void Start()
    {
        if (!printOnStart) return;

        if (smplRoot == null)
        {
            var go = GameObject.Find(smplRootName);
            if (go != null) smplRoot = go.transform;
        }

        var meshT = FindDeepChild(smplRoot, skinnedMeshObjectName);
        var smr = meshT ? meshT.GetComponent<SkinnedMeshRenderer>() : null;

        if (smr == null)
        {
            Debug.LogWarning("[BonePrinter] SkinnedMeshRenderer not found.");
            return;
        }

        var names = new List<string>();
        foreach (var b in smr.bones)
            if (b != null) names.Add(b.name);

        names.Sort();

        Debug.Log($"[BonePrinter] bones count = {names.Count}");
        for (int i = 0; i < names.Count; i++)
            Debug.Log("[BonePrinter] " + names[i]);
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

