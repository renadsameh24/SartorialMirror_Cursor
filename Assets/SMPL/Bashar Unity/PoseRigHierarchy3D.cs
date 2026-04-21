using UnityEngine;

public class PoseRigHierarchy3D : MonoBehaviour
{
    [Header("Source joints (your PoseRig3D object that contains J00..J32)")]
    public Transform poseRigRoot;   // drag PoseRig3D here

    [Header("Options")]
    public bool mirrorX = true;
    [Range(0f, 1f)] public float smoothing = 0.25f;

    // MediaPipe indices we care about
    const int L_SHOULDER = 11, R_SHOULDER = 12;
    const int L_ELBOW = 13, R_ELBOW = 14;
    const int L_WRIST = 15, R_WRIST = 16;
    const int L_HIP = 23, R_HIP = 24;
    const int L_KNEE = 25, R_KNEE = 26;
    const int L_ANKLE = 27, R_ANKLE = 28;
    const int NOSE = 0;

    // Bones (Transforms)
    Transform pelvis, spine, chest, neck, head;
    Transform lUpperArm, lLowerArm, lHand;
    Transform rUpperArm, rLowerArm, rHand;
    Transform lUpperLeg, lLowerLeg, lFoot;
    Transform rUpperLeg, rLowerLeg, rFoot;

    // Smoothed positions
    Vector3 spPelvis, spChest, spNeck, spHead;
    Vector3 spLS, spRS, spLH, spRH;

    void Start()
    {
        // Build hierarchy under this GameObject
        pelvis = Make("Pelvis", transform);
        spine = Make("Spine", pelvis);
        chest = Make("Chest", spine);
        neck = Make("Neck", chest);
        head = Make("Head", neck);

        lUpperArm = Make("L_UpperArm", chest);
        lLowerArm = Make("L_LowerArm", lUpperArm);
        lHand = Make("L_Hand", lLowerArm);

        rUpperArm = Make("R_UpperArm", chest);
        rLowerArm = Make("R_LowerArm", rUpperArm);
        rHand = Make("R_Hand", rLowerArm);

        lUpperLeg = Make("L_UpperLeg", pelvis);
        lLowerLeg = Make("L_LowerLeg", lUpperLeg);
        lFoot = Make("L_Foot", lLowerLeg);

        rUpperLeg = Make("R_UpperLeg", pelvis);
        rLowerLeg = Make("R_LowerLeg", rUpperLeg);
        rFoot = Make("R_Foot", rLowerLeg);
    }

    void Update()
    {
        if (poseRigRoot == null || poseRigRoot.childCount < 33) return;

        // Get key joint positions
        Vector3 LS = J(L_SHOULDER);
        Vector3 RS = J(R_SHOULDER);
        Vector3 LH = J(L_HIP);
        Vector3 RH = J(R_HIP);

        Vector3 shoulderMid = (LS + RS) * 0.5f;
        Vector3 hipMid = (LH + RH) * 0.5f;

        Vector3 nose = J(NOSE);
        Vector3 neckPos = shoulderMid;                 // good approx
        Vector3 headPos = Vector3.Lerp(neckPos, nose, 0.7f);

        // Smooth core anchors
        spPelvis = Vector3.Lerp(spPelvis, hipMid, smoothing);
        spChest = Vector3.Lerp(spChest, shoulderMid, smoothing);
        spNeck = Vector3.Lerp(spNeck, neckPos, smoothing);
        spHead = Vector3.Lerp(spHead, headPos, smoothing);

        pelvis.position = spPelvis;
        spine.position = Vector3.Lerp(spPelvis, spChest, 0.35f);
        chest.position = spChest;
        neck.position = spNeck;
        head.position = spHead;

        // Arms positions
        lUpperArm.position = LS;
        lLowerArm.position = J(L_ELBOW);
        lHand.position = J(L_WRIST);

        rUpperArm.position = RS;
        rLowerArm.position = J(R_ELBOW);
        rHand.position = J(R_WRIST);

        // Legs positions
        lUpperLeg.position = LH;
        lLowerLeg.position = J(L_KNEE);
        lFoot.position = J(L_ANKLE);

        rUpperLeg.position = RH;
        rLowerLeg.position = J(R_KNEE);
        rFoot.position = J(R_ANKLE);

        // Now compute rotations (next step)
        SolveRotations();
    }

    void SolveRotations()
    {
        // Torso orientation
        Vector3 up = (chest.position - pelvis.position).normalized;
        Vector3 right = (rUpperArm.position - lUpperArm.position).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;

        if (forward.sqrMagnitude < 1e-6f) forward = transform.forward;

        // Pelvis/chest face forward
        pelvis.rotation = SmoothLook(pelvis.rotation, forward, up);
        chest.rotation = SmoothLook(chest.rotation, forward, up);
        spine.rotation = SmoothLook(spine.rotation, forward, up);
        neck.rotation = SmoothLook(neck.rotation, forward, up);

        // Arms (upper->lower, lower->hand)
        AimBone(lUpperArm, lLowerArm.position - lUpperArm.position, up);
        AimBone(lLowerArm, lHand.position - lLowerArm.position, up);

        AimBone(rUpperArm, rLowerArm.position - rUpperArm.position, up);
        AimBone(rLowerArm, rHand.position - rLowerArm.position, up);

        // Legs (upper->lower, lower->foot)
        AimBone(lUpperLeg, lLowerLeg.position - lUpperLeg.position, up);
        AimBone(lLowerLeg, lFoot.position - lLowerLeg.position, up);

        AimBone(rUpperLeg, rLowerLeg.position - rUpperLeg.position, up);
        AimBone(rLowerLeg, rFoot.position - rLowerLeg.position, up);
    }

    void AimBone(Transform bone, Vector3 dir, Vector3 up)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        Quaternion target = Quaternion.LookRotation(dir.normalized, up);
        bone.rotation = Quaternion.Slerp(bone.rotation, target, 1f - Mathf.Pow(1f - smoothing, 2f));
    }

    Quaternion SmoothLook(Quaternion current, Vector3 forward, Vector3 up)
    {
        Quaternion target = Quaternion.LookRotation(forward, up);
        return Quaternion.Slerp(current, target, 1f - Mathf.Pow(1f - smoothing, 2f));
    }

    Transform Make(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    Vector3 J(int idx)
    {
        return poseRigRoot.GetChild(idx).position;
    }
}
