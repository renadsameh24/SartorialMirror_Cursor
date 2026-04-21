using UnityEngine;

/// <summary>
/// Drives Animation Rigging IK targets from JointDebug spheres (wrist/ankle)
/// and generates stable elbow/knee hints. Supports ManualOverride.
/// Attach to: IK_Driver
/// </summary>
[DefaultExecutionOrder(-100)] // run before RigBuilder typically evaluates
public class IKTargetsAndHintsDriver : MonoBehaviour
{
    [Header("Rig reference (defines forward/right/up)")]
    public Transform rigRoot; // e.g. SMPL_neutral_rig_GOLDEN root

    [Header("Sphere joints (from JointSpheresRoot)")]
    public Transform lWristSphere;
    public Transform rWristSphere;
    public Transform lAnkleSphere;
    public Transform rAnkleSphere;

    [Header("IK Targets (empties used by TwoBoneIKConstraint)")]
    public Transform lHandTarget;
    public Transform rHandTarget;
    public Transform lFootTarget;
    public Transform rFootTarget;

    [Header("IK Hints (empties used by TwoBoneIKConstraint)")]
    public Transform lElbowHint;
    public Transform rElbowHint;
    public Transform lKneeHint;
    public Transform rKneeHint;

    [Header("Optional bones (for better hint placement)")]
    public Transform lShoulderBone; // J_l_shoulder or your arm root parent
    public Transform rShoulderBone;
    public Transform lElbowBone;    // J_l_elbow (mid)
    public Transform rElbowBone;
    public Transform lHipBone;      // J_l_hip
    public Transform rHipBone;
    public Transform lKneeBone;     // J_l_knee
    public Transform rKneeBone;

    [Header("Hint tuning")]
    [Tooltip("How far to push elbows sideways (meters in rig space scale).")]
    public float elbowSide = 0.20f;
    [Tooltip("How far to push elbows forward.")]
    public float elbowForward = 0.12f;

    [Tooltip("How far to push knees sideways.")]
    public float kneeSide = 0.10f;
    [Tooltip("How far to push knees forward.")]
    public float kneeForward = 0.18f;

    [Tooltip("Extra upward push for hints (usually 0 for elbows, small for knees if needed).")]
    public float hintUp = 0.0f;

    [Header("Update mode")]
    [Tooltip("If true, runs in OnAnimatorIK (only if Animator is active). Otherwise runs in Update.")]
    public bool useOnAnimatorIK = false;

    [Header("Manual control")]
    [Tooltip("If true, script will NOT move hint objects (you can drag them manually). Targets still follow spheres.")]
    public bool manualOverride = false;

    [Header("Debug")]
    public bool debugLogs = false;
    public float logEverySeconds = 2f;

    float _t;

    void Update()
    {
        if (useOnAnimatorIK) return;
        StepDriver();
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!useOnAnimatorIK) return;
        StepDriver();
    }

    void StepDriver()
    {
        if (!rigRoot) return;

        // 1) Targets follow spheres (world space)
        CopyPos(lWristSphere, lHandTarget);
        CopyPos(rWristSphere, rHandTarget);
        CopyPos(lAnkleSphere, lFootTarget);
        CopyPos(rAnkleSphere, rFootTarget);

        // 2) Hints auto-placement unless manual override
        if (!manualOverride)
        {
            PlaceElbowHint(isLeft:true);
            PlaceElbowHint(isLeft:false);
            PlaceKneeHint(isLeft:true);
            PlaceKneeHint(isLeft:false);
        }

        // 3) Debug logging
        if (debugLogs)
        {
            _t += Time.deltaTime;
            if (_t >= logEverySeconds)
            {
                _t = 0;
                Debug.Log($"[IKTargetsAndHintsDriver] LW={NameOrNone(lWristSphere)} RW={NameOrNone(rWristSphere)} LA={NameOrNone(lAnkleSphere)} RA={NameOrNone(rAnkleSphere)} Manual={manualOverride} Mode={(useOnAnimatorIK ? "OnAnimatorIK" : "Update")}");
            }
        }
    }

    void PlaceElbowHint(bool isLeft)
    {
        Transform shoulder = isLeft ? lShoulderBone : rShoulderBone;
        Transform elbow    = isLeft ? lElbowBone    : rElbowBone;
        Transform wristTgt = isLeft ? lHandTarget   : rHandTarget;
        Transform hint     = isLeft ? lElbowHint    : rElbowHint;

        if (!wristTgt || !hint) return;

        // If bones exist, compute a better base point (near elbow). Otherwise fall back midpoint.
        Vector3 basePoint;
        if (elbow) basePoint = elbow.position;
        else if (shoulder) basePoint = Vector3.Lerp(shoulder.position, wristTgt.position, 0.45f);
        else basePoint = wristTgt.position; // worst-case fallback

        // Rig axes
        Vector3 rigRight = rigRoot.right;
        Vector3 rigFwd   = rigRoot.forward;
        Vector3 rigUp    = rigRoot.up;

        // Left vs right side direction
        float sideSign = isLeft ? -1f : 1f;

        // Build hint offset in world using rig basis
        Vector3 offset = (rigRight * (elbowSide * sideSign)) + (rigFwd * elbowForward) + (rigUp * hintUp);

        hint.position = basePoint + offset;
    }

    void PlaceKneeHint(bool isLeft)
    {
        Transform hip      = isLeft ? lHipBone    : rHipBone;
        Transform knee     = isLeft ? lKneeBone   : rKneeBone;
        Transform ankleTgt = isLeft ? lFootTarget : rFootTarget;
        Transform hint     = isLeft ? lKneeHint   : rKneeHint;

        if (!ankleTgt || !hint) return;

        Vector3 basePoint;
        if (knee) basePoint = knee.position;
        else if (hip) basePoint = Vector3.Lerp(hip.position, ankleTgt.position, 0.55f);
        else basePoint = ankleTgt.position;

        Vector3 rigRight = rigRoot.right;
        Vector3 rigFwd   = rigRoot.forward;
        Vector3 rigUp    = rigRoot.up;

        float sideSign = isLeft ? -1f : 1f;
        Vector3 offset = (rigRight * (kneeSide * sideSign)) + (rigFwd * kneeForward) + (rigUp * hintUp);

        hint.position = basePoint + offset;
    }

    static void CopyPos(Transform from, Transform to)
    {
        if (!from || !to) return;
        to.position = from.position;
    }

    static string NameOrNone(Transform t) => t ? t.name : "None";
}
