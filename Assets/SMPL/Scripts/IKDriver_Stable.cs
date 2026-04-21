using UnityEngine;

[DefaultExecutionOrder(300)] // after sphere playback updates, before rig solves (rig solves late)
public class IKDriver_Stable : MonoBehaviour
{
    [Header("Main Root (moves whole character + spheres together)")]
    public Transform characterRoot;

    [Header("Sphere joints (world positions)")]
    public Transform S_Pelvis;
    public Transform S_Spine;      // used for body up/forward
    public Transform S_LHip;
    public Transform S_RHip;

    public Transform S_LShoulder;
    public Transform S_LElbow;
    public Transform S_LWrist;

    public Transform S_RShoulder;
    public Transform S_RElbow;
    public Transform S_RWrist;

    public Transform S_LKnee;
    public Transform S_LAnkle;

    public Transform S_RKnee;
    public Transform S_RAnkle;

    [Header("IK Targets (empties used by TwoBoneIKConstraint)")]
    public Transform T_LHand;
    public Transform T_RHand;
    public Transform T_LFoot;
    public Transform T_RFoot;

    [Header("IK Hints (empties used by TwoBoneIKConstraint)")]
    public Transform H_LElbow;
    public Transform H_RElbow;
    public Transform H_LKnee;
    public Transform H_RKnee;

    [Header("Tuning")]
    [Range(0f, 1f)] public float positionLerp = 1f;

    [Tooltip("How far hints are pushed away from the limb plane (in meters).")]
    public float hintDistance = 0.25f;

    [Tooltip("Extra lift to prevent knees/elbows collapsing.")]
    public float hintUp = 0.03f;

    [Tooltip("Lift feet slightly above ankle spheres (optional).")]
    public float footLift = 0.00f;

    [Header("Calibration")]
    public bool autoCalibrateOnPlay = true;

    // Offset so CharacterRoot follows pelvis sphere without teleporting to wrong global space
    private Vector3 rootToPelvisOffsetWS;
    private bool calibrated;

    void Start()
    {
        if (autoCalibrateOnPlay)
            Calibrate();
    }

    [ContextMenu("Calibrate Now")]
    public void Calibrate()
    {
        if (!characterRoot || !S_Pelvis) return;

        // Keep current visual alignment: root stays where it is, compute offset to pelvis sphere
        rootToPelvisOffsetWS = characterRoot.position - S_Pelvis.position;
        calibrated = true;
    }

    void LateUpdate()
    {
        if (!characterRoot || !S_Pelvis) return;

        if (!calibrated)
            Calibrate();

        // ----- 1) Move whole character root to follow pelvis sphere (with offset) -----
        Vector3 desiredRootPos = S_Pelvis.position + rootToPelvisOffsetWS;
        characterRoot.position = Vector3.Lerp(characterRoot.position, desiredRootPos, positionLerp);

        // ----- 2) Targets follow wrist/ankle spheres -----
        Follow(T_LHand, S_LWrist, Vector3.zero);
        Follow(T_RHand, S_RWrist, Vector3.zero);

        Follow(T_LFoot, S_LAnkle, Vector3.up * footLift);
        Follow(T_RFoot, S_RAnkle, Vector3.up * footLift);

        // ----- 3) Compute a stable "body forward" for hint direction fallback -----
        Vector3 bodyUp = SafeDir(S_Spine ? (S_Spine.position - S_Pelvis.position) : Vector3.up, Vector3.up);
        Vector3 hipsRight = (S_LHip && S_RHip) ? SafeDir(S_RHip.position - S_LHip.position, Vector3.right) : Vector3.right;
        Vector3 bodyForward = Vector3.Cross(hipsRight, bodyUp).normalized;
        if (bodyForward.sqrMagnitude < 1e-6f) bodyForward = characterRoot.forward;

        // ----- 4) Hints: push perpendicular to limb plane (shoulder-elbow-wrist / hip-knee-ankle) -----
        PlaceElbowHint(H_LElbow, S_LShoulder, S_LElbow, S_LWrist, bodyForward, bodyUp, isLeft: true);
        PlaceElbowHint(H_RElbow, S_RShoulder, S_RElbow, S_RWrist, bodyForward, bodyUp, isLeft: false);

        PlaceKneeHint(H_LKnee, S_Pelvis, S_LKnee, S_LAnkle, bodyForward, bodyUp, isLeft: true);
        PlaceKneeHint(H_RKnee, S_Pelvis, S_RKnee, S_RAnkle, bodyForward, bodyUp, isLeft: false);
    }

    void Follow(Transform target, Transform sphere, Vector3 offset)
    {
        if (!target || !sphere) return;
        Vector3 goal = sphere.position + offset;
        target.position = Vector3.Lerp(target.position, goal, positionLerp);
    }

    void PlaceElbowHint(Transform hint, Transform shoulder, Transform elbow, Transform wrist,
                        Vector3 bodyForward, Vector3 bodyUp, bool isLeft)
    {
        if (!hint || !shoulder || !elbow || !wrist) return;

        Vector3 upper = SafeDir(elbow.position - shoulder.position, isLeft ? -Vector3.right : Vector3.right);
        Vector3 fore  = SafeDir(wrist.position - elbow.position, isLeft ? -Vector3.right : Vector3.right);

        // Plane normal for bend direction:
        Vector3 n = Vector3.Cross(upper, fore).normalized;

        // If limb is almost straight, cross becomes tiny -> use bodyForward as stable normal
        if (n.sqrMagnitude < 1e-6f) n = bodyForward;

        // Choose consistent side: flip normal so left/right behave consistently
        // (helps prevent elbows bending backwards)
        float sideSign = isLeft ? -1f : 1f;
        Vector3 goal = elbow.position + (n * hintDistance * sideSign) + (bodyUp * hintUp);

        hint.position = Vector3.Lerp(hint.position, goal, positionLerp);
    }

    void PlaceKneeHint(Transform hint, Transform hipRef, Transform knee, Transform ankle,
                       Vector3 bodyForward, Vector3 bodyUp, bool isLeft)
    {
        if (!hint || !knee || !ankle) return;

        // For knee plane, use hipRef->knee as "upper" if hipRef exists, else knee direction fallback
        Vector3 upper = hipRef ? SafeDir(knee.position - hipRef.position, Vector3.down) : Vector3.down;
        Vector3 lower = SafeDir(ankle.position - knee.position, Vector3.down);

        Vector3 n = Vector3.Cross(upper, lower).normalized;
        if (n.sqrMagnitude < 1e-6f) n = bodyForward;

        float sideSign = isLeft ? -1f : 1f;
        Vector3 goal = knee.position + (n * hintDistance * sideSign) + (bodyUp * hintUp);

        hint.position = Vector3.Lerp(hint.position, goal, positionLerp);
    }

    Vector3 SafeDir(Vector3 v, Vector3 fallback)
    {
        if (v.sqrMagnitude < 1e-8f) return fallback.normalized;
        return v.normalized;
    }
}

