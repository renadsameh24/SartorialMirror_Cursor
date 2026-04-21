using UnityEngine;

[DefaultExecutionOrder(-50)] // run early (before rig evaluation)
public class IKDriver_ExactUpdate : MonoBehaviour
{
    [Header("Rig reference (axes only)")]
    public Transform rigRoot; // use SMPL_neutral_rig_GOLDEN (NOT the scaled container)

    [Header("Spheres (joint positions)")]
    public Transform J_pelvis;

    public Transform J_l_wrist, J_r_wrist;
    public Transform J_l_elbow, J_r_elbow;
    public Transform J_l_ankle, J_r_ankle;
    public Transform J_l_knee,  J_r_knee;

    [Header("IK Targets (empties used by TwoBoneIKConstraint)")]
    public Transform L_HandTarget, R_HandTarget;
    public Transform L_FootTarget, R_FootTarget;

    [Header("IK Hints (empties used by TwoBoneIKConstraint)")]
    public Transform L_ElbowHint, R_ElbowHint;
    public Transform L_KneeHint,  R_KneeHint;

    [Header("Hint Offsets (in rig axes)")]
    public float elbowSide = 0.20f;
    public float elbowForward = 0.08f;
    public float kneeSide = 0.10f;
    public float kneeForward = 0.12f;
    public float hintUp = 0.05f;

    [Header("Feet")]
    public float footLift = 0.00f;

    [Header("Debug")]
    public bool logOnceOnPlay = true;

    bool _logged = false;

    void Update()
    {
        if (!rigRoot) return;

        // Required
        if (!J_l_wrist || !J_r_wrist || !J_l_ankle || !J_r_ankle) return;
        if (!L_HandTarget || !R_HandTarget || !L_FootTarget || !R_FootTarget) return;

        Vector3 F = rigRoot.forward;
        Vector3 R = rigRoot.right;
        Vector3 U = rigRoot.up;

        // --- Targets: EXACT follow spheres (world) ---
        L_HandTarget.position = J_l_wrist.position;
        R_HandTarget.position = J_r_wrist.position;

        L_FootTarget.position = J_l_ankle.position + U * footLift;
        R_FootTarget.position = J_r_ankle.position + U * footLift;

        // --- Hints ---
        // Base at elbow/knee spheres if provided; otherwise midpoint pelvis->target
        Vector3 pelvisPos = J_pelvis ? J_pelvis.position : rigRoot.position;

        Vector3 lElbowBase = J_l_elbow ? J_l_elbow.position : (pelvisPos + L_HandTarget.position) * 0.5f;
        Vector3 rElbowBase = J_r_elbow ? J_r_elbow.position : (pelvisPos + R_HandTarget.position) * 0.5f;

        Vector3 lKneeBase  = J_l_knee  ? J_l_knee.position  : (pelvisPos + L_FootTarget.position) * 0.5f;
        Vector3 rKneeBase  = J_r_knee  ? J_r_knee.position  : (pelvisPos + R_FootTarget.position) * 0.5f;

        if (L_ElbowHint) L_ElbowHint.position = lElbowBase + (-R * elbowSide) + (F * elbowForward) + (U * hintUp);
        if (R_ElbowHint) R_ElbowHint.position = rElbowBase + ( R * elbowSide) + (F * elbowForward) + (U * hintUp);

        if (L_KneeHint)  L_KneeHint.position  = lKneeBase  + (-R * kneeSide)  + (F * kneeForward)  + (U * hintUp);
        if (R_KneeHint)  R_KneeHint.position  = rKneeBase  + ( R * kneeSide)  + (F * kneeForward)  + (U * hintUp);

        // --- Debug (prints once) ---
        if (logOnceOnPlay && !_logged)
        {
            _logged = true;
            Debug.Log($"[IKDriver_ExactUpdate] LW dist={(L_HandTarget.position - J_l_wrist.position).magnitude:F6}, " +
                      $"RW dist={(R_HandTarget.position - J_r_wrist.position).magnitude:F6}");
        }
    }
}
