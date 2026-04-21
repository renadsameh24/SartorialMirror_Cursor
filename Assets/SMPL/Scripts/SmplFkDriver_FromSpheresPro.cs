using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stable FK retarget: drives SMPL bones directly from joint spheres (positions).
/// No IK targets/hints needed.
/// Works best with Animator OFF (or no controller) and RigBuilder OFF.
/// </summary>
public class SmplFkDriver_FromSpheresPro : MonoBehaviour
{
    [Header("Rig Root (the SMPL skeleton root)")]
    public Transform rigRoot; // SMPL_neutral_rig_GOLDEN

    [Header("Spheres (world positions)")]
    public Transform S_Pelvis;
    public Transform S_LHip, S_RHip;
    public Transform S_LKnee, S_RKnee;
    public Transform S_LAnkle, S_RAnkle;

    public Transform S_Spine;     // use spine3-ish (your "spine" sphere)
    public Transform S_Neck;      // neck sphere
    public Transform S_Head;      // head sphere (can be same as neck if needed)

    public Transform S_LShoulder, S_RShoulder;
    public Transform S_LElbow, S_RElbow;
    public Transform S_LWrist, S_RWrist;

    [Header("SMPL Bones")]
    public Transform B_Pelvis;    // J00
    public Transform B_LHip;      // J01
    public Transform B_RHip;      // J02
    public Transform B_Spine;     // J09 (or your torso driver bone)
    public Transform B_Neck;      // J12
    public Transform B_Head;      // J15 (or J12 if no head)

    public Transform B_LShoulder; // J16
    public Transform B_LElbow;    // J18
    public Transform B_LWrist;    // J20
    public Transform B_RShoulder; // J17
    public Transform B_RElbow;    // J19
    public Transform B_RWrist;    // J21

    public Transform B_LKnee;     // J04
    public Transform B_RKnee;     // J05
    public Transform B_LAnkle;    // J07
    public Transform B_RAnkle;    // J08

    [Header("Root / Torso behavior")]
    public bool applyRootPosition = true;
    public bool applyRootRotation = true;

    [Range(0f, 1f)] public float pelvisYawWeight = 1.0f;   // 1 = fully follow hips yaw
    [Range(0f, 1f)] public float spineTwistWeight = 0.25f; // small twist helps “alive” torso
    [Range(0f, 1f)] public float neckTwistWeight  = 0.20f;

    [Header("Smoothing")]
    [Range(0.0f, 1.0f)] public float rotSmoothing = 0.45f; // higher = smoother (more lag)
    [Range(0.0f, 1.0f)] public float posSmoothing = 0.25f;

    [Header("Debug")]
    public bool logMissingOnce = true;
    private bool _loggedMissing;

    struct BoneLink
    {
        public Transform bone;
        public Transform sphereA; // start joint
        public Transform sphereB; // end joint
        public Quaternion restWorldRot;
        public Vector3 restWorldDir;
    }

    private readonly List<BoneLink> _links = new();

    void Start()
    {
        BuildLinks();
        CacheRestPose();
    }

    void LateUpdate()
    {
        if (!rigRoot) { LogMissing("RigRoot missing."); return; }

        // Root position follow (optional)
        if (applyRootPosition && B_Pelvis && S_Pelvis)
        {
            Vector3 targetPos = S_Pelvis.position;
            B_Pelvis.position = Vector3.Lerp(B_Pelvis.position, targetPos, 1f - posSmoothing);
        }

        // Root rotation from hips + spine (optional)
        if (applyRootRotation)
        {
            ApplyPelvisRotationFromHips();
        }

        // Drive limb/torso bone rotations from segment directions
        for (int i = 0; i < _links.Count; i++)
        {
            var link = _links[i];
            if (!link.bone || !link.sphereA || !link.sphereB) continue;

            Vector3 dir = (link.sphereB.position - link.sphereA.position);
            if (dir.sqrMagnitude < 1e-8f) continue;
            dir.Normalize();

            Quaternion delta = Quaternion.FromToRotation(link.restWorldDir, dir);
            Quaternion targetWorldRot = delta * link.restWorldRot;

            link.bone.rotation = Quaternion.Slerp(link.bone.rotation, targetWorldRot, 1f - rotSmoothing);
        }

        // Optional: add a tiny torso/neck twist so it doesn’t look “robot stiff”
        ApplyTorsoTwist();
        ApplyNeckTwist();
    }

    void BuildLinks()
    {
        _links.Clear();

        AddLink(B_Spine, S_Pelvis, S_Spine);
        AddLink(B_Neck,  S_Spine,  S_Neck);
        AddLink(B_Head,  S_Neck,   S_Head);

        // Arms
        AddLink(B_LShoulder, S_LShoulder, S_LElbow);
        AddLink(B_LElbow,    S_LElbow,    S_LWrist);
        AddLink(B_LWrist,    S_LWrist,    S_LWrist); // wrist "stabilizer" (dir ignored later)

        AddLink(B_RShoulder, S_RShoulder, S_RElbow);
        AddLink(B_RElbow,    S_RElbow,    S_RWrist);
        AddLink(B_RWrist,    S_RWrist,    S_RWrist);

        // Legs
        AddLink(B_LHip,   S_LHip,   S_LKnee);
        AddLink(B_LKnee,  S_LKnee,  S_LAnkle);
        AddLink(B_LAnkle, S_LAnkle, S_LAnkle);

        AddLink(B_RHip,   S_RHip,   S_RKnee);
        AddLink(B_RKnee,  S_RKnee,  S_RAnkle);
        AddLink(B_RAnkle, S_RAnkle, S_RAnkle);
    }

    void AddLink(Transform bone, Transform a, Transform b)
    {
        if (!bone || !a || !b) return;
        _links.Add(new BoneLink { bone = bone, sphereA = a, sphereB = b });
    }

    void CacheRestPose()
    {
        for (int i = 0; i < _links.Count; i++)
        {
            var link = _links[i];

            // Default rest direction: from sphereA->sphereB at Start time (your calibration pose)
            Vector3 d = (link.sphereB.position - link.sphereA.position);
            if (d.sqrMagnitude < 1e-8f)
                d = link.bone.forward; // fallback
            else
                d.Normalize();

            link.restWorldDir = d;
            link.restWorldRot = link.bone.rotation;

            _links[i] = link;
        }
    }

    void ApplyPelvisRotationFromHips()
    {
        if (!B_Pelvis || !S_LHip || !S_RHip || !S_Spine) return;

        Vector3 hipsRight = (S_RHip.position - S_LHip.position);
        if (hipsRight.sqrMagnitude < 1e-8f) return;
        hipsRight.Normalize();

        Vector3 up = (S_Spine.position - S_Pelvis.position);
        if (up.sqrMagnitude < 1e-8f) up = rigRoot.up;
        else up.Normalize();

        // Forward = Up x Right (right-handed)
        Vector3 fwd = Vector3.Cross(up, hipsRight);
        if (fwd.sqrMagnitude < 1e-8f) fwd = rigRoot.forward;
        else fwd.Normalize();

        Quaternion target = Quaternion.LookRotation(fwd, up);

        // Blend only yaw if you want (helps avoid weird roll/pitch from noisy input)
        if (pelvisYawWeight < 1f)
        {
            Vector3 e = target.eulerAngles;
            Vector3 curr = B_Pelvis.rotation.eulerAngles;
            target = Quaternion.Euler(curr.x, Mathf.LerpAngle(curr.y, e.y, pelvisYawWeight), curr.z);
        }

        B_Pelvis.rotation = Quaternion.Slerp(B_Pelvis.rotation, target, 1f - rotSmoothing);
    }

    void ApplyTorsoTwist()
    {
        if (!B_Spine || spineTwistWeight <= 0f || !S_LShoulder || !S_RShoulder || !S_Spine) return;

        Vector3 shouldersRight = (S_RShoulder.position - S_LShoulder.position).normalized;
        Vector3 up = (S_Spine.position - S_Pelvis.position).normalized;
        if (up.sqrMagnitude < 1e-8f) up = rigRoot.up;

        Vector3 fwd = Vector3.Cross(up, shouldersRight).normalized;
        if (fwd.sqrMagnitude < 1e-8f) return;

        Quaternion torso = Quaternion.LookRotation(fwd, up);
        B_Spine.rotation = Quaternion.Slerp(B_Spine.rotation, torso, spineTwistWeight * (1f - rotSmoothing));
    }

    void ApplyNeckTwist()
    {
        if (!B_Neck || neckTwistWeight <= 0f || !S_Head || !S_Neck) return;

        Vector3 up = (S_Head.position - S_Neck.position).normalized;
        if (up.sqrMagnitude < 1e-8f) return;

        // Keep forward from rigRoot to avoid random roll from tiny head noise
        Quaternion neck = Quaternion.LookRotation(rigRoot.forward, up);
        B_Neck.rotation = Quaternion.Slerp(B_Neck.rotation, neck, neckTwistWeight * (1f - rotSmoothing));
    }

    void LogMissing(string msg)
    {
        if (!logMissingOnce) { Debug.LogWarning(msg); return; }
        if (_loggedMissing) return;
        _loggedMissing = true;
        Debug.LogWarning(msg);
    }
}

