using UnityEngine;

[DefaultExecutionOrder(500)] // after sphere playback updates
public class SmplPoseFromSpheres : MonoBehaviour
{
    [Header("Spheres (joint positions)")]
    public Transform S_Pelvis;
    public Transform S_Spine;
    public Transform S_Neck;
    public Transform S_Head;

    public Transform S_LShoulder;
    public Transform S_LElbow;
    public Transform S_LWrist;

    public Transform S_RShoulder;
    public Transform S_RElbow;
    public Transform S_RWrist;

    public Transform S_LHip;
    public Transform S_LKnee;
    public Transform S_LAnkle;

    public Transform S_RHip;
    public Transform S_RKnee;
    public Transform S_RAnkle;

    [Header("SMPL Bones (your rig bones)")]
    public Transform B_Pelvis;
    public Transform B_Spine;
    public Transform B_Neck;
    public Transform B_Head;

    public Transform B_LShoulder;
    public Transform B_LElbow;
    public Transform B_LWrist;

    public Transform B_RShoulder;
    public Transform B_RElbow;
    public Transform B_RWrist;

    public Transform B_LHip;
    public Transform B_LKnee;
    public Transform B_LAnkle;

    public Transform B_RHip;
    public Transform B_RKnee;
    public Transform B_RAnkle;

    [Header("Options")]
    [Range(0f, 1f)] public float lerp = 1f; // 1 = exact (no smoothing)

    struct BoneRest
    {
        public Quaternion restLocalRot;
        public Vector3 restDirLocal; // bone->child direction in bone local space at rest
        public bool valid;
    }

    BoneRest pelvis, spine, neck;
    BoneRest lShoulder, lElbow, rShoulder, rElbow;
    BoneRest lHip, lKnee, rHip, rKnee;

    void Start()
    {
        pelvis    = CacheRestLocal(B_Pelvis,   B_Spine);
        spine     = CacheRestLocal(B_Spine,    B_Neck);
        neck      = CacheRestLocal(B_Neck,     B_Head);

        lShoulder = CacheRestLocal(B_LShoulder, B_LElbow);
        lElbow    = CacheRestLocal(B_LElbow,    B_LWrist);

        rShoulder = CacheRestLocal(B_RShoulder, B_RElbow);
        rElbow    = CacheRestLocal(B_RElbow,    B_RWrist);

        lHip      = CacheRestLocal(B_LHip,      B_LKnee);
        lKnee     = CacheRestLocal(B_LKnee,     B_LAnkle);

        rHip      = CacheRestLocal(B_RHip,      B_RKnee);
        rKnee     = CacheRestLocal(B_RKnee,     B_RAnkle);
    }

    void LateUpdate()
    {
        // DO NOT MOVE ROOT HERE. Root is handled by SmplRootFollowPelvisSphere.

        AimBoneLocal(B_Pelvis, S_Pelvis, S_Spine, pelvis);
        AimBoneLocal(B_Spine,  S_Spine,  S_Neck,  spine);
        AimBoneLocal(B_Neck,   S_Neck,   S_Head,  neck);

        AimBoneLocal(B_LShoulder, S_LShoulder, S_LElbow, lShoulder);
        AimBoneLocal(B_LElbow,    S_LElbow,    S_LWrist, lElbow);

        AimBoneLocal(B_RShoulder, S_RShoulder, S_RElbow, rShoulder);
        AimBoneLocal(B_RElbow,    S_RElbow,    S_RWrist, rElbow);

        AimBoneLocal(B_LHip,   S_LHip,   S_LKnee,  lHip);
        AimBoneLocal(B_LKnee,  S_LKnee,  S_LAnkle, lKnee);

        AimBoneLocal(B_RHip,   S_RHip,   S_RKnee,  rHip);
        AimBoneLocal(B_RKnee,  S_RKnee,  S_RAnkle, rKnee);
    }

    BoneRest CacheRestLocal(Transform bone, Transform child)
    {
        BoneRest r = new BoneRest();
        if (!bone || !child) return r;

        Vector3 dirWorld = (child.position - bone.position);
        if (dirWorld.sqrMagnitude < 1e-8f) return r;

        r.restLocalRot = bone.localRotation;
        r.restDirLocal = bone.InverseTransformDirection(dirWorld.normalized); // rest direction in bone local space
        r.valid = true;
        return r;
    }

    void AimBoneLocal(Transform bone, Transform sphereA, Transform sphereB, BoneRest rest)
    {
        if (!bone || !sphereA || !sphereB || !rest.valid) return;

        Vector3 desiredWorld = (sphereB.position - sphereA.position);
        if (desiredWorld.sqrMagnitude < 1e-8f) return;

        // Convert desired direction into the bone's local space (current frame)
        Vector3 desiredDirLocal = bone.InverseTransformDirection(desiredWorld.normalized);

        Quaternion delta = Quaternion.FromToRotation(rest.restDirLocal, desiredDirLocal);
        Quaternion targetLocal = delta * rest.restLocalRot;

        bone.localRotation = Quaternion.Slerp(bone.localRotation, targetLocal, Mathf.Clamp01(lerp));
    }
}
