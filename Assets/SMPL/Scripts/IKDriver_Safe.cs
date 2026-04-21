using UnityEngine;

[DefaultExecutionOrder(200)]
public class IKDriver_Safe : MonoBehaviour
{
    [Header("Rig reference (world axes)")]
    public Transform rigRoot; // usually SMPL_neutral_rig_GOLDEN or CharacterRoot (not scaled)

    [Header("Sphere joints (world)")]
    public Transform L_WristSphere, R_WristSphere;
    public Transform L_AnkleSphere, R_AnkleSphere;
    public Transform L_ElbowSphere, R_ElbowSphere;   // optional
    public Transform L_KneeSphere,  R_KneeSphere;    // optional

    [Header("IK Targets (empties)")]
    public Transform L_HandTarget, R_HandTarget;
    public Transform L_FootTarget, R_FootTarget;

    [Header("IK Hints (empties)")]
    public Transform L_ElbowHint, R_ElbowHint;
    public Transform L_KneeHint,  R_KneeHint;

    [Header("Tuning")]
    public float elbowSide = 0.20f;
    public float elbowForward = 0.10f;
    public float kneeSide = 0.10f;
    public float kneeForward = 0.15f;
    public float hintUp = 0.05f;
    public float footLift = 0.00f;

    [Range(0f, 1f)] public float lerp = 1f;

    // ---------- SAFETY ----------
    static bool IsFinite(Vector3 v)
    {
        return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }

    static Vector3 SafeLerp(Vector3 a, Vector3 b, float t)
    {
        if (!IsFinite(b)) return a;
        return Vector3.Lerp(a, b, Mathf.Clamp01(t));
    }

    void LateUpdate()
    {
        if (!rigRoot) return;

        // If any required sphere/target missing -> do nothing (prevents NaN)
        if (!L_WristSphere || !R_WristSphere || !L_AnkleSphere || !R_AnkleSphere) return;
        if (!L_HandTarget || !R_HandTarget || !L_FootTarget || !R_FootTarget) return;

        Vector3 F = rigRoot.forward;
        Vector3 R = rigRoot.right;
        Vector3 U = rigRoot.up;

        // Targets follow spheres (world space)
        Follow(L_HandTarget, L_WristSphere.position);
        Follow(R_HandTarget, R_WristSphere.position);
        Follow(L_FootTarget, L_AnkleSphere.position + U * footLift);
        Follow(R_FootTarget, R_AnkleSphere.position + U * footLift);

        // Hints: use elbow/knee spheres if available; otherwise midpoints
        if (L_ElbowHint)
        {
            Vector3 basePos = L_ElbowSphere ? L_ElbowSphere.position : Mid(rigRoot.position, L_HandTarget.position);
            SetHint(L_ElbowHint, basePos + (-R * elbowSide) + (F * elbowForward) + (U * hintUp));
        }
        if (R_ElbowHint)
        {
            Vector3 basePos = R_ElbowSphere ? R_ElbowSphere.position : Mid(rigRoot.position, R_HandTarget.position);
            SetHint(R_ElbowHint, basePos + ( R * elbowSide) + (F * elbowForward) + (U * hintUp));
        }

        if (L_KneeHint)
        {
            Vector3 basePos = L_KneeSphere ? L_KneeSphere.position : Mid(rigRoot.position, L_FootTarget.position);
            SetHint(L_KneeHint, basePos + (-R * kneeSide) + (F * kneeForward) + (U * hintUp));
        }
        if (R_KneeHint)
        {
            Vector3 basePos = R_KneeSphere ? R_KneeSphere.position : Mid(rigRoot.position, R_FootTarget.position);
            SetHint(R_KneeHint, basePos + ( R * kneeSide) + (F * kneeForward) + (U * hintUp));
        }
    }

    void Follow(Transform t, Vector3 goal)
    {
        if (!t) return;
        if (!IsFinite(goal)) return;
        t.position = SafeLerp(t.position, goal, lerp);
    }

    void SetHint(Transform t, Vector3 goal)
    {
        if (!t) return;
        if (!IsFinite(goal)) return;
        t.position = SafeLerp(t.position, goal, lerp);
    }

    static Vector3 Mid(Vector3 a, Vector3 b) => (a + b) * 0.5f;
}
