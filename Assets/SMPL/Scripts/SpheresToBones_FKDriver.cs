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

        [Tooltip("If > 0, multiply effective rotation lerp for this segment only (e.g. 0.5 = half as snappy). 0 = use global / torso defaults.")]
        [Range(0f, 2f)] public float rotLerpScale = 0f;
    }

    [Header("Root follow (optional but recommended)")]
    public Transform rootBone;     // e.g. pelvis / hips bone in SMPL
    public Transform rootSphere;   // J_pelvis sphere
    public bool followRootPosition = true;
    public bool followRootRotation = false; // usually false unless you have pelvis rotation data

    [Tooltip("1 = pelvis snaps to sphere each frame; lower = softer pelvis follow (reduces lower-torso jitter).")]
    [Range(0f, 1f)] public float rootPositionLerp = 1f;

    [Header("Segments (order doesn’t matter, but must be correct pairs)")]
    public Segment[] segments;

    [Header("Smoothing (arms / default)")]
    [Range(0f, 1f)] public float rotLerp = 1f; // 1 = exact, 0.3 = smoother

    [Header("Lower trunk restraint (chest uses normal Rot Lerp)")]
    [Tooltip("When on, only segments whose bone name matches Lower Trunk Name Tokens use Torso Rot Lerp / degree cap. Chest, mid/upper spine, and arms use Rot Lerp.")]
    public bool autoRestrainTorsoByBoneName = true;

    [Tooltip("Slerp factor for lower-trunk segments only (pelvis / first spine). Chest & arms use Rot Lerp.")]
    [Range(0f, 1f)] public float torsoRotLerp = 0.4f;

    [Tooltip("If > 0, cap degrees per LateUpdate for lower-trunk segments only (0 = no cap).")]
    [Range(0f, 180f)] public float torsoMaxDegreesPerFrame = 18f;

    [Tooltip("Default: pelvis + first spine step only. Add tokens here if your rig names differ; do NOT add J06/J09/chest/neck if you want chest free.")]
    public string[] torsoNameTokens =
    {
        "pelvis", "Pelvis", "J00", "hips", "Hips",
        "J03"
    };

    void LateUpdate()
    {
        // 1) Root follow (position only is safest)
        if (followRootPosition && rootBone && rootSphere)
        {
            var target = rootSphere.position;
            if (rootPositionLerp >= 0.999f)
                rootBone.position = target;
            else
                rootBone.position = Vector3.Lerp(rootBone.position, target, rootPositionLerp);
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

            if (boneDir.sqrMagnitude < 1e-10f || sphereDir.sqrMagnitude < 1e-10f) continue;

            // rotation that turns current bone direction into desired sphere direction
            Quaternion delta = Quaternion.FromToRotation(boneDir.normalized, sphereDir.normalized);
            Quaternion targetRot = delta * s.bone.rotation;

            float t = EffectiveRotLerp(s);
            if (t >= 0.999f)
                t = 1f;

            Quaternion next = t >= 1f ? targetRot : Quaternion.Slerp(s.bone.rotation, targetRot, t);

            if (IsLowerTrunkSegment(s) && torsoMaxDegreesPerFrame > 0.1f)
                next = ClampRotationStep(s.bone.rotation, next, torsoMaxDegreesPerFrame);

            s.bone.rotation = next;

            // Only if you REALLY want positional snapping (usually keep false)
            if (s.applyPositionToBone)
                s.bone.position = s.sphere.position;
        }
    }

    bool IsLowerTrunkSegment(Segment s)
    {
        if (!autoRestrainTorsoByBoneName || s.bone == null) return false;
        return BoneNameMatchesTorsoTokens(s.bone.name);
    }

    bool BoneNameMatchesTorsoTokens(string boneName)
    {
        if (string.IsNullOrEmpty(boneName) || torsoNameTokens == null) return false;
        foreach (var raw in torsoNameTokens)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            if (boneName.IndexOf(raw, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    float EffectiveRotLerp(Segment s)
    {
        float baseLerp = IsLowerTrunkSegment(s) ? torsoRotLerp : rotLerp;
        if (s.rotLerpScale > 0f)
            baseLerp *= Mathf.Clamp01(s.rotLerpScale);
        return Mathf.Clamp01(baseLerp);
    }

    static Quaternion ClampRotationStep(Quaternion from, Quaternion to, float maxDegrees)
    {
        float ang = Quaternion.Angle(from, to);
        if (ang <= maxDegrees + 1e-4f) return to;
        float t = maxDegrees / Mathf.Max(1e-4f, ang);
        return Quaternion.Slerp(from, to, t);
    }
}
