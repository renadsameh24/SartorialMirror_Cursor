using UnityEngine;

public class IKTargetsAndHints_FromSpheres_LengthScaled : MonoBehaviour
{
    [Header("Rig reference (optional)")]
    public Transform rigRoot; // can be CharacterRoot, used only if plane normal degenerates

    [Header("Spheres (joint positions)")]
    public Transform S_LShoulder, S_LElbow, S_LWrist;
    public Transform S_RShoulder, S_RElbow, S_RWrist;

    public Transform S_LHip, S_LKnee, S_LAnkle;
    public Transform S_RHip, S_RKnee, S_RAnkle;

    [Header("SMPL Bones (for limb length + fallback plane)")]
    public Transform B_LShoulder, B_LElbow, B_LWrist;
    public Transform B_RShoulder, B_RElbow, B_RWrist;

    public Transform B_LHip, B_LKnee, B_LAnkle;
    public Transform B_RHip, B_RKnee, B_RAnkle;

    [Header("IK Targets (assigned in TwoBoneIKConstraint)")]
    public Transform T_LHand, T_RHand;
    public Transform T_LFoot, T_RFoot;

    [Header("IK Hints (assigned in TwoBoneIKConstraint)")]
    public Transform H_LElbow, H_RElbow;
    public Transform H_LKnee,  H_RKnee;

    [Header("Hint tuning (fraction of SMPL limb length)")]
    [Range(0.05f, 1.0f)] public float hintSideFracArm = 0.25f;
    [Range(0.05f, 1.0f)] public float hintSideFracLeg = 0.15f;

    [Header("Follow")]
    public bool exactFollow = true;
    [Range(0.01f, 1f)] public float smooth = 0.35f;

    void LateUpdate()
    {
        // 1) Targets (RETARGETED lengths)
        RetargetEndEffector(T_LHand, S_LShoulder, S_LWrist, B_LShoulder, B_LWrist);
        RetargetEndEffector(T_RHand, S_RShoulder, S_RWrist, B_RShoulder, B_RWrist);

        RetargetEndEffector(T_LFoot, S_LHip, S_LAnkle, B_LHip, B_LAnkle);
        RetargetEndEffector(T_RFoot, S_RHip, S_RAnkle, B_RHip, B_RAnkle);

        // 2) Hints (based on limb plane normal)
        PlaceHintFromPlane(H_LElbow, S_LShoulder, S_LElbow, S_LWrist, B_LShoulder, B_LElbow, B_LWrist, hintSideFracArm);
        PlaceHintFromPlane(H_RElbow, S_RShoulder, S_RElbow, S_RWrist, B_RShoulder, B_RElbow, B_RWrist, hintSideFracArm);

        PlaceHintFromPlane(H_LKnee,  S_LHip, S_LKnee, S_LAnkle, B_LHip, B_LKnee, B_LAnkle, hintSideFracLeg);
        PlaceHintFromPlane(H_RKnee,  S_RHip, S_RKnee, S_RAnkle, B_RHip, B_RKnee, B_RAnkle, hintSideFracLeg);
    }

    void RetargetEndEffector(Transform target,
                             Transform sRoot, Transform sEnd,
                             Transform bRoot, Transform bEnd)
    {
        if (!target || !sRoot || !sEnd || !bRoot || !bEnd) return;

        Vector3 dir = (sEnd.position - sRoot.position);
        float srcLen = dir.magnitude;
        if (srcLen < 1e-6f) return;
        dir /= srcLen;

        float smplLen = Vector3.Distance(bRoot.position, bEnd.position);

        Vector3 desired = sRoot.position + dir * smplLen;

        if (exactFollow) target.position = desired;
        else target.position = Vector3.Lerp(target.position, desired, smooth);
    }

    void PlaceHintFromPlane(Transform hint,
                            Transform sRoot, Transform sMid, Transform sEnd,
                            Transform bRoot, Transform bMid, Transform bEnd,
                            float sideFrac)
    {
        if (!hint || !sRoot || !sEnd || !bRoot || !bEnd) return;

        Vector3 rootPos = sRoot.position;
        Vector3 endPos  = sEnd.position;

        // Limb direction (root->end)
        Vector3 axis = endPos - rootPos;
        float axisLen = axis.magnitude;
        if (axisLen < 1e-6f) return;
        axis /= axisLen;

        // Determine plane normal from spheres if possible, otherwise from SMPL bones
        Vector3 normal = Vector3.zero;

        if (sMid)
        {
            Vector3 a = (sMid.position - rootPos);
            Vector3 b = (endPos - rootPos);
            normal = Vector3.Cross(a, b);
        }

        if (normal.sqrMagnitude < 1e-8f && bMid)
        {
            Vector3 a = (bMid.position - bRoot.position);
            Vector3 b = (bEnd.position - bRoot.position);
            normal = Vector3.Cross(a, b);
        }

        if (normal.sqrMagnitude < 1e-8f && rigRoot)
        {
            // last resort
            normal = rigRoot.forward;
        }

        normal.Normalize();

        // Mid reference (use sphere mid if present; else midpoint)
        Vector3 basePos = sMid ? sMid.position : (rootPos + endPos) * 0.5f;

        // Use SMPL limb length for consistent offset
        float smplLen = Vector3.Distance(bRoot.position, bEnd.position);

        Vector3 desired = basePos + normal * (smplLen * sideFrac);

        if (exactFollow) hint.position = desired;
        else hint.position = Vector3.Lerp(hint.position, desired, smooth);
    }
}
