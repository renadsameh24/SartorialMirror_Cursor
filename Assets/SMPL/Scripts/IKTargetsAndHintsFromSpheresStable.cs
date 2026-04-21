using UnityEngine;

[DefaultExecutionOrder(200)] // after spheres + root follow
public class IKTargetsAndHintsFromSpheresStable : MonoBehaviour
{
    [Header("If TRUE, script does NOTHING (so you can move gizmos manually)")]
    public bool manualOverride = false;

    [Header("Rig reference (defines forward/right/up)")]
    public Transform rigRoot;

    [Header("Sphere joints (from JointSpheresRoot)")]
    public Transform L_WristSphere, R_WristSphere;
    public Transform L_AnkleSphere, R_AnkleSphere;
    public Transform L_ElbowSphere, R_ElbowSphere;
    public Transform L_KneeSphere,  R_KneeSphere;

    [Header("IK Targets (empties used by TwoBoneIKConstraint)")]
    public Transform L_HandTarget, R_HandTarget;
    public Transform L_FootTarget, R_FootTarget;

    [Header("IK Hints (empties used by TwoBoneIKConstraint)")]
    public Transform L_ElbowHint, R_ElbowHint;
    public Transform L_KneeHint,  R_KneeHint;

    [Header("Tuning")]
    public float elbowSide = 0.25f;
    public float elbowForward = 0.12f;
    public float kneeSide = 0.20f;
    public float kneeForward = 0.18f;
    public float hintUp = 0.05f;
    public float footLift = 0.02f;

    [Range(0f, 1f)] public float lerp = 0.35f;

    void LateUpdate()
    {
        if (manualOverride) return;
        if (!rigRoot) return;

        Vector3 F = rigRoot.forward;
        Vector3 R = rigRoot.right;
        Vector3 U = rigRoot.up;

        FollowTarget(L_HandTarget, L_WristSphere, Vector3.zero);
        FollowTarget(R_HandTarget, R_WristSphere, Vector3.zero);

        FollowTarget(L_FootTarget, L_AnkleSphere, U * footLift);
        FollowTarget(R_FootTarget, R_AnkleSphere, U * footLift);

        // Use elbow/knee spheres if available
        Vector3 lElbowBase = L_ElbowSphere ? L_ElbowSphere.position : (L_HandTarget.position + rigRoot.position) * 0.5f;
        Vector3 rElbowBase = R_ElbowSphere ? R_ElbowSphere.position : (R_HandTarget.position + rigRoot.position) * 0.5f;

        Vector3 lKneeBase  = L_KneeSphere ? L_KneeSphere.position : (L_FootTarget.position + rigRoot.position) * 0.5f;
        Vector3 rKneeBase  = R_KneeSphere ? R_KneeSphere.position : (R_FootTarget.position + rigRoot.position) * 0.5f;

        // Outward = -R for left, +R for right
        SetHint(L_ElbowHint, lElbowBase + (-R * elbowSide) + (F * elbowForward) + (U * hintUp));
        SetHint(R_ElbowHint, rElbowBase + ( R * elbowSide) + (F * elbowForward) + (U * hintUp));

        SetHint(L_KneeHint,  lKneeBase  + (-R * kneeSide)  + (F * kneeForward)  + (U * hintUp));
        SetHint(R_KneeHint,  rKneeBase  + ( R * kneeSide)  + (F * kneeForward)  + (U * hintUp));
    }

    void FollowTarget(Transform target, Transform sphere, Vector3 offset)
    {
        if (!target || !sphere) return;
        Vector3 goal = sphere.position + offset;
        target.position = Vector3.Lerp(target.position, goal, Mathf.Clamp01(lerp));
    }

    void SetHint(Transform hint, Vector3 goal)
    {
        if (!hint) return;
        hint.position = Vector3.Lerp(hint.position, goal, Mathf.Clamp01(lerp));
    }
}
