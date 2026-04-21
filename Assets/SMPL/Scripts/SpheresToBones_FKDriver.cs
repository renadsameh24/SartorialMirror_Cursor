using System;
using UnityEngine;

public class SpheresToBones_FKDriver : MonoBehaviour
{
    [Serializable]
    public class Segment
    {
        [Header("Bone chain")]
        public Transform bone;       // e.g. SMPL upperarm bone
        public Transform boneChild;  // e.g. SMPL forearm bone (child in chain)

        [Header("Sphere chain (same joint points)")]
        public Transform sphere;      // e.g. J_l_shoulder sphere
        public Transform sphereChild; // e.g. J_l_elbow sphere

        [Header("Options")]
        public bool applyPositionToBone = false; // keep false for most bones
    }

    [Header("Root follow (optional but recommended)")]
    public Transform rootBone;     // e.g. pelvis / hips bone in SMPL
    public Transform rootSphere;   // J_pelvis sphere
    public bool followRootPosition = true;
    public bool followRootRotation = false; // usually false unless you have pelvis rotation data

    [Header("Segments (order doesn’t matter, but must be correct pairs)")]
    public Segment[] segments;

    [Header("Smoothing")]
    [Range(0f, 1f)] public float rotLerp = 1f; // 1 = exact, 0.3 = smoother

    void LateUpdate()
    {
        // 1) Root follow (position only is safest)
        if (followRootPosition && rootBone && rootSphere)
            rootBone.position = rootSphere.position;

        if (followRootRotation && rootBone && rootSphere)
            rootBone.rotation = rootSphere.rotation;

        // 2) Drive each bone rotation to match sphere direction
        if (segments == null) return;

        foreach (var s in segments)
        {
            if (!s.bone || !s.boneChild || !s.sphere || !s.sphereChild) continue;

            Vector3 boneDir = (s.boneChild.position - s.bone.position);
            Vector3 sphereDir = (s.sphereChild.position - s.sphere.position);

            if (boneDir.sqrMagnitude < 1e-10f || sphereDir.sqrMagnitude < 1e-10f) continue;

            // rotation that turns current bone direction into desired sphere direction
            Quaternion delta = Quaternion.FromToRotation(boneDir.normalized, sphereDir.normalized);
            Quaternion targetRot = delta * s.bone.rotation;

            s.bone.rotation = (rotLerp >= 0.999f)
                ? targetRot
                : Quaternion.Slerp(s.bone.rotation, targetRot, rotLerp);

            // Only if you REALLY want positional snapping (usually keep false)
            if (s.applyPositionToBone)
                s.bone.position = s.sphere.position;
        }
    }
}
