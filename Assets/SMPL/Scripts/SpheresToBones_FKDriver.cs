using System;
using UnityEngine;

/// <summary>
/// Runs after default Animators (0) and after sphere-follow (≈1000) so sphere-driven rotations win.
/// Must finish before <see cref="SmplGarmentManager"/> (3200) so SMPL bones are final for garment skinning.
/// </summary>
[DefaultExecutionOrder(2500)]
public class SpheresToBones_FKDriver : MonoBehaviour
{
    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    private bool _loggedNonFiniteOnce = false;

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

    public enum MirrorAxis
    {
        None,
        RootX,
        RootY,
        RootZ
    }

    [Header("Mirror / coordinate fix")]
    [Tooltip("Mirror the incoming sphere vectors/positions across the SMPL root local axis. " +
             "Use this when left/right looks swapped or arms move 'the opposite way' relative to the camera.")]
    public MirrorAxis mirrorAxis = MirrorAxis.None;

    [Tooltip("Legacy toggle (kept for existing scenes). If enabled, forces Mirror Axis = RootX at runtime.")]
    public bool mirrorAcrossRootX = false;

    void LateUpdate()
    {
        Transform root = rootBone != null ? rootBone.root : null;
        if (rootBone != null) root = rootBone; // mirror in rootBone space by default (stable even if rig is nested)

        // Back-compat: old scenes used mirrorAcrossRootX.
        if (mirrorAcrossRootX && mirrorAxis == MirrorAxis.None)
            mirrorAxis = MirrorAxis.RootX;

        Vector3 MirrorDir(Vector3 worldDir)
        {
            if (mirrorAxis == MirrorAxis.None || root == null) return worldDir;
            var local = root.InverseTransformDirection(worldDir);
            switch (mirrorAxis)
            {
                case MirrorAxis.RootX: local.x = -local.x; break;
                case MirrorAxis.RootY: local.y = -local.y; break;
                case MirrorAxis.RootZ: local.z = -local.z; break;
            }
            return root.TransformDirection(local);
        }

        Vector3 MirrorPos(Vector3 worldPos)
        {
            if (mirrorAxis == MirrorAxis.None || root == null) return worldPos;
            var local = root.InverseTransformPoint(worldPos);
            switch (mirrorAxis)
            {
                case MirrorAxis.RootX: local.x = -local.x; break;
                case MirrorAxis.RootY: local.y = -local.y; break;
                case MirrorAxis.RootZ: local.z = -local.z; break;
            }
            return root.TransformPoint(local);
        }

        // 1) Root follow (position only is safest)
        if (followRootPosition && rootBone && rootSphere)
        {
            var p = MirrorPos(rootSphere.position);
            if (Finite(p))
                rootBone.position = p;
            else if (!_loggedNonFiniteOnce)
            {
                _loggedNonFiniteOnce = true;
                Debug.LogError("[SpheresToBones_FKDriver] rootSphere position is non-finite (NaN/Inf). Skipping root follow to prevent SMPL exploding.", this);
            }
        }

        if (followRootRotation && rootBone && rootSphere)
            rootBone.rotation = rootSphere.rotation;

        // 2) Drive each bone rotation to match sphere direction
        if (segments == null) return;

        foreach (var s in segments)
        {
            if (!s.bone || !s.boneChild || !s.sphere || !s.sphereChild) continue;

            Vector3 boneDir = (s.boneChild.position - s.bone.position);
            Vector3 sphereDir = (s.sphereChild.position - s.sphere.position);
            sphereDir = MirrorDir(sphereDir);

            if (!Finite(boneDir) || !Finite(sphereDir))
            {
                if (!_loggedNonFiniteOnce)
                {
                    _loggedNonFiniteOnce = true;
                    Debug.LogError("[SpheresToBones_FKDriver] Non-finite bone/sphere direction detected (NaN/Inf). Skipping this frame to prevent SMPL exploding.", this);
                }
                continue;
            }

            if (boneDir.sqrMagnitude < 1e-10f || sphereDir.sqrMagnitude < 1e-10f) continue;

            // rotation that turns current bone direction into desired sphere direction
            // Note: only s.bone is written; boneChild follows by hierarchy. Wrists may stay near bind
            // unless you add a segment whose .bone is that joint.
            Quaternion delta = Quaternion.FromToRotation(boneDir.normalized, sphereDir.normalized);
            Quaternion targetRot = delta * s.bone.rotation;

            s.bone.rotation = (rotLerp >= 0.999f)
                ? targetRot
                : Quaternion.Slerp(s.bone.rotation, targetRot, rotLerp);

            // Only if you REALLY want positional snapping (usually keep false)
            if (s.applyPositionToBone)
            {
                var bp = MirrorPos(s.sphere.position);
                if (Finite(bp))
                    s.bone.position = bp;
                else if (!_loggedNonFiniteOnce)
                {
                    _loggedNonFiniteOnce = true;
                    Debug.LogError("[SpheresToBones_FKDriver] sphere position is non-finite (NaN/Inf). Skipping bone position snap.", this);
                }
            }
        }
    }
}
