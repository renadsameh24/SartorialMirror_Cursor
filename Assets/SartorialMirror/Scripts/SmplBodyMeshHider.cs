using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hides the SMPL skinned mesh renderers (body) while keeping the rig/bones active.
/// Garments (instances tagged with <see cref="GarmentInstanceTag"/>) are not affected.
/// </summary>
public sealed class SmplBodyMeshHider : MonoBehaviour
{
    [Tooltip("Optional. If empty, will use this object's hierarchy.")]
    public Transform smplRoot;

    [Tooltip("Renderer GameObject names to hide. If empty, hides all SMPL skinned meshes under root (excluding garments).")]
    public List<string> skinnedMeshObjectNamesToHide = new() { "smpl_neutral", "SMPL_neutral", "Body", "body" };

    [Tooltip("Hide immediately on Start.")]
    public bool hideOnStart = true;

    void Start()
    {
        if (hideOnStart) ApplyHidden(true);
    }

    public void ApplyHidden(bool hidden)
    {
        var root = smplRoot != null ? smplRoot : transform;
        var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in skinned)
        {
            if (!smr) continue;
            if (smr.GetComponentInParent<GarmentInstanceTag>() != null) continue;

            if (skinnedMeshObjectNamesToHide == null || skinnedMeshObjectNamesToHide.Count == 0)
            {
                smr.enabled = !hidden ? true : false;
                continue;
            }

            if (skinnedMeshObjectNamesToHide.Contains(smr.gameObject.name))
                smr.enabled = !hidden ? true : false;
        }
    }
}

