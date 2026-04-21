using UnityEngine;

[DefaultExecutionOrder(200)] // after playback updates
public class IKTargetsAndHintsDriver_BonePlane : MonoBehaviour
{
    [Header("Rig root (only for fallback directions)")]
    public Transform rigRoot;

    [Header("Sphere joints (from JointSpheresRoot)")]
    public Transform L_WristSphere, R_WristSphere;
    public Transform L_AnkleSphere, R_AnkleSphere;
    public Transform L_ElbowSphere, R_ElbowSphere; // optional
    public Transform L_KneeSphere,  R_KneeSphere;  // optional

    [Header("IK Targets (empties used by TwoBoneIKConstraint)")]
    public Transform L_HandTarget, R_HandTarget;
    public Transform L_FootTarget, R_FootTarget;

    [Header("IK Hints (empties used by TwoBoneIKConstraint)")]
    public Transform L_ElbowHint, R_ElbowHint;
    public Transform L_KneeHint,  R_KneeHint;

    [Header("Bones (required for stable hint plane)")]
    public Transform L_ShoulderBone, L_ElbowBone, L_WristBone;
    public Transform R_ShoulderBone, R_ElbowBone, R_WristBone;

    public Transform L_HipBone, L_KneeBone, L_AnkleBone;
    public Transform R_HipBone, R_KneeBone, R_AnkleBone;

    [Header("Tuning")]
    [Tooltip("How far to push elbow hint away from the arm plane normal.")]
    public float elbowHintDistance = 0.25f;

    [Tooltip("How far to push knee hint away from the leg plane normal.")]
    public float kneeHintDistance = 0.25f;

    [Tooltip("Small lift to reduce collapsing.")]
    public float hintUp = 0.05f;

    [Tooltip("Lift feet slightly to avoid squatting below ground.")]
    public float footLift = 0.02f;

    [Range(0f, 1f)] public float lerp = 0.35f;

    [Header("Safety clamps")]
    [Tooltip("Minimum distance from hint to the limb line (prevents singular IK).")]
    public float minHintLineDistance = 0.12f;

    [Tooltip("If plane normal is near zero, fallback to this direction (rig forward).")]
    public float normalFallbackDot = 0.02f;

    void LateUpdate()
    {
        if (!rigRoot) return;

        // 1) Targets follow spheres
        Follow(L_HandTarget, L_WristSphere, Vector3.zero);
        Follow(R_HandTarget, R_WristSphere, Vector3.zero);

        Follow(L_FootTarget, L_AnkleSphere, rigRoot.up * footLift);
        Follow(R_FootTarget, R_AnkleSphere, rigRoot.up * footLift);

        // 2) Hints driven by bone-plane normals (robust)
        SolveArmHint(
            isLeft: true,
            shoulder: L_ShoulderBone, elbow: L_ElbowBone, wrist: L_WristBone,
            elbowSphere: L_ElbowSphere,
            hint: L_ElbowHint,
            hintDistance: elbowHintDistance
        );

        SolveArmHint(
            isLeft: false,
            shoulder: R_ShoulderBone, elbow: R_ElbowBone, wrist: R_WristBone,
            elbowSphere: R_ElbowSphere,
            hint: R_ElbowHint,
            hintDistance: elbowHintDistance
        );

        SolveLegHint(
            isLeft: true,
            hip: L_HipBone, knee: L_KneeBone, ankle: L_AnkleBone,
            kneeSphere: L_KneeSphere,
            hint: L_KneeHint,
            hintDistance: kneeHintDistance
        );

        SolveLegHint(
            isLeft: false,
            hip: R_HipBone, knee: R_KneeBone, ankle: R_AnkleBone,
            kneeSphere: R_KneeSphere,
            hint: R_KneeHint,
            hintDistance: kneeHintDistance
        );
    }

    void Follow(Transform target, Transform sphere, Vector3 offset)
    {
        if (!target || !sphere) return;
        Vector3 goal = sphere.position + offset;
        target.position = Vector3.Lerp(target.position, goal, Mathf.Clamp01(lerp));
    }

    void SolveArmHint(bool isLeft,
        Transform shoulder, Transform elbow, Transform wrist,
        Transform elbowSphere,
        Transform hint,
        float hintDistance)
    {
        if (!hint || !shoulder || !elbow || !wrist) return;

        Vector3 S = shoulder.position;
        Vector3 E = elbowSphere ? elbowSphere.position : elbow.position;
        Vector3 W = wrist.position;

        PlaceHintWithPlane(isLeft, S, E, W, hint, hintDistance);
    }

    void SolveLegHint(bool isLeft,
        Transform hip, Transform knee, Transform ankle,
        Transform kneeSphere,
        Transform hint,
        float hintDistance)
    {
        if (!hint || !hip || !knee || !ankle) return;

        Vector3 H = hip.position;
        Vector3 K = kneeSphere ? kneeSphere.position : knee.position;
        Vector3 A = ankle.position;

        PlaceHintWithPlane(isLeft, H, K, A, hint, hintDistance);
    }

    void PlaceHintWithPlane(bool isLeft,
        Vector3 rootPos, Vector3 midPos, Vector3 tipPos,
        Transform hint,
        float hintDistance)
    {
        // Limb directions
        Vector3 a = (midPos - rootPos);
        Vector3 b = (tipPos - midPos);

        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return;

        // Plane normal for current limb bend
        Vector3 n = Vector3.Cross(a, b);

        // If limb is nearly straight, cross product collapses -> fallback
        if (n.sqrMagnitude < normalFallbackDot)
        {
            // choose a stable normal from rigRoot
            // arms: outward roughly +/- rigRight, legs: outward +/- rigRight
            Vector3 fallback = rigRoot.forward;
            n = Vector3.Cross(a.normalized, fallback).normalized;
            if (n.sqrMagnitude < 1e-6f)
                n = rigRoot.up; // last fallback
        }
        else
        {
            n.Normalize();
        }

        // Ensure left/right consistency: push hint outward by choosing side sign
        // If this pushes inward, swap sign in inspector by making hintDistance negative.
        float sideSign = isLeft ? -1f : 1f;

        Vector3 goal = midPos + (n * hintDistance * sideSign) + (rigRoot.up * hintUp);

        // Safety: keep hint away from the limb line
        goal = PushAwayFromLine(rootPos, tipPos, goal, minHintLineDistance);

        hint.position = Vector3.Lerp(hint.position, goal, Mathf.Clamp01(lerp));
    }

    Vector3 PushAwayFromLine(Vector3 lineA, Vector3 lineB, Vector3 p, float minDist)
    {
        Vector3 ab = lineB - lineA;
        float abLen2 = ab.sqrMagnitude;
        if (abLen2 < 1e-8f) return p;

        float t = Mathf.Clamp01(Vector3.Dot(p - lineA, ab) / abLen2);
        Vector3 proj = lineA + ab * t;

        Vector3 away = p - proj;
        float d = away.magnitude;

        if (d >= minDist || d < 1e-6f) return p;

        return proj + away.normalized * minDist;
    }
}
