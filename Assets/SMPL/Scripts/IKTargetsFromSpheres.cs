using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads the moving debug spheres (J_l_wrist, etc.) and drives IK targets/hints.
/// Designed for Unity Animation Rigging TwoBoneIK.
/// - LateUpdate so it runs after other updates.
/// - Optional manual control for hints (so you can drag them).
/// - Optional auto hint placement with stable left/right offsets (prevents crossing).
/// </summary>
public class IKTargetsFromSpheres : MonoBehaviour
{
    [Header("Sphere Root (contains J_* spheres)")]
    public Transform JointSpheresRoot;

    [Header("Targets/Hints (drag these)")]
    public Transform L_HandTarget;
    public Transform L_ElbowHint;

    public Transform R_HandTarget;
    public Transform R_ElbowHint;

    public Transform L_FootTarget;
    public Transform L_KneeHint;

    public Transform R_FootTarget;
    public Transform R_KneeHint;

    [Header("Driving toggles")]
    public bool DriveHandTargets = true;
    public bool DriveFootTargets = true;

    [Tooltip("If ON, script will also position elbow/knee hints automatically.")]
    public bool DriveHints = false;

    [Tooltip("If ON, you can move Hint objects manually in Play mode and the script will NOT overwrite them.")]
    public bool ManualHintOverride = true;

    [Header("Hint offsets (LOCAL to pelvis-ish frame)")]
    [Tooltip("How far to the side the elbow hint sits (+ = right, - = left)")]
    public float ElbowHintSide = 0.25f;

    [Tooltip("How far forward the elbow hint sits (+ forward)")]
    public float ElbowHintForward = 0.15f;

    [Tooltip("How far down/up the elbow hint sits (+ up)")]
    public float ElbowHintUp = 0.00f;

    [Tooltip("How far to the side the knee hint sits (+ = right, - = left)")]
    public float KneeHintSide = 0.20f;

    [Tooltip("How far forward the knee hint sits (+ forward)")]
    public float KneeHintForward = 0.10f;

    [Tooltip("How far down/up the knee hint sits (+ up)")]
    public float KneeHintUp = 0.00f;

    [Header("Axis mapping (IMPORTANT)")]
    [Tooltip("Which axis is 'Up' in your JSON world. If you used SwapYZ in playback, set UpAxis = Z.")]
    public Axis UpAxis = Axis.Z;

    [Tooltip("Which axis is 'Forward' in your scene (walk direction). Often +Y or +Z depending on your setup.")]
    public Axis ForwardAxis = Axis.Y;

    [Tooltip("Which axis is 'Right' in your scene. Usually +X.")]
    public Axis RightAxis = Axis.X;

    [Header("Smoothing")]
    [Range(0f, 1f)] public float TargetLerp = 0.35f;
    [Range(0f, 1f)] public float HintLerp = 0.35f;

    [Header("Debug")]
    public bool DebugLogs = true;
    public float LogEverySeconds = 2f;

    // Sphere transforms
    Transform LW, RW, LA, RA;
    Transform LE, RE, LK, RK;

    Dictionary<string, Transform> sphereMap = new Dictionary<string, Transform>();

    float nextLogTime;

    public enum Axis { X, Y, Z }

    void Awake()
    {
        RebuildMap();
    }

    void OnValidate()
    {
        // Keep it safe in editor
        if (LogEverySeconds < 0.1f) LogEverySeconds = 0.1f;
    }

    public void RebuildMap()
    {
        sphereMap.Clear();
        LW = RW = LA = RA = null;
        LE = RE = LK = RK = null;

        if (!JointSpheresRoot) return;

        var all = JointSpheresRoot.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (!sphereMap.ContainsKey(t.name))
                sphereMap.Add(t.name, t);
        }

        // wrists/ankles are what we MUST have
        sphereMap.TryGetValue("J_l_wrist", out LW);
        sphereMap.TryGetValue("J_r_wrist", out RW);
        sphereMap.TryGetValue("J_l_ankle", out LA);
        sphereMap.TryGetValue("J_r_ankle", out RA);

        // elbows/knees spheres are OPTIONAL (if you have them)
        sphereMap.TryGetValue("J_l_elbow", out LE);
        sphereMap.TryGetValue("J_r_elbow", out RE);
        sphereMap.TryGetValue("J_l_knee", out LK);
        sphereMap.TryGetValue("J_r_knee", out RK);
    }

    void LateUpdate()
    {
        // If JointSpheresRoot moved/changed, still safe (but not heavy)
        if (sphereMap.Count == 0 || !LW || !RW || !LA || !RA)
            RebuildMap();

        // Drive targets (wrist/ankle)
        if (DriveHandTargets)
        {
            DriveTarget(L_HandTarget, LW);
            DriveTarget(R_HandTarget, RW);
        }

        if (DriveFootTargets)
        {
            DriveTarget(L_FootTarget, LA);
            DriveTarget(R_FootTarget, RA);
        }

        // Drive hints only if enabled AND not manually overriding
        if (DriveHints && !(ManualHintOverride && Application.isPlaying))
        {
            DriveAutoHints();
        }

        // Logs
        if (DebugLogs && Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + LogEverySeconds;
            string lw = LW ? LW.name : "None";
            string rw = RW ? RW.name : "None";
            string la = LA ? LA.name : "None";
            string ra = RA ? RA.name : "None";
            Debug.Log($"[IKTargetsFromSpheres] LW={lw} RW={rw} LA={la} RA={ra} | DriveHints={DriveHints} ManualOverride={ManualHintOverride}");
        }
    }

    void DriveTarget(Transform target, Transform sphere)
    {
        if (!target || !sphere) return;
        Vector3 p = sphere.position;
        target.position = Vector3.Lerp(target.position, p, 1f - Mathf.Pow(1f - TargetLerp, 60f * Time.deltaTime));
    }

    void DriveAutoHints()
    {
        // We need some "frame" to define right/forward/up.
        // If you want, you can later set this to pelvis bone transform instead.
        // For now: use this object's transform as a stable reference.
        Transform frame = transform;

        Vector3 right = AxisVector(frame, RightAxis);
        Vector3 forward = AxisVector(frame, ForwardAxis);
        Vector3 up = AxisVector(frame, UpAxis);

        // LEFT elbow hint: push to LEFT side = negative right
        if (L_ElbowHint)
        {
            Vector3 elbowBase = LE ? LE.position : (L_HandTarget ? L_HandTarget.position : L_ElbowHint.position);
            Vector3 want = elbowBase + (-right * ElbowHintSide) + (forward * ElbowHintForward) + (up * ElbowHintUp);
            L_ElbowHint.position = Vector3.Lerp(L_ElbowHint.position, want, 1f - Mathf.Pow(1f - HintLerp, 60f * Time.deltaTime));
        }

        // RIGHT elbow hint: push to RIGHT side = positive right
        if (R_ElbowHint)
        {
            Vector3 elbowBase = RE ? RE.position : (R_HandTarget ? R_HandTarget.position : R_ElbowHint.position);
            Vector3 want = elbowBase + (right * ElbowHintSide) + (forward * ElbowHintForward) + (up * ElbowHintUp);
            R_ElbowHint.position = Vector3.Lerp(R_ElbowHint.position, want, 1f - Mathf.Pow(1f - HintLerp, 60f * Time.deltaTime));
        }

        // LEFT knee hint: usually goes slightly forward + to the side
        if (L_KneeHint)
        {
            Vector3 kneeBase = LK ? LK.position : (L_FootTarget ? L_FootTarget.position : L_KneeHint.position);
            Vector3 want = kneeBase + (-right * KneeHintSide) + (forward * KneeHintForward) + (up * KneeHintUp);
            L_KneeHint.position = Vector3.Lerp(L_KneeHint.position, want, 1f - Mathf.Pow(1f - HintLerp, 60f * Time.deltaTime));
        }

        // RIGHT knee hint
        if (R_KneeHint)
        {
            Vector3 kneeBase = RK ? RK.position : (R_FootTarget ? R_FootTarget.position : R_KneeHint.position);
            Vector3 want = kneeBase + (right * KneeHintSide) + (forward * KneeHintForward) + (up * KneeHintUp);
            R_KneeHint.position = Vector3.Lerp(R_KneeHint.position, want, 1f - Mathf.Pow(1f - HintLerp, 60f * Time.deltaTime));
        }
    }

    Vector3 AxisVector(Transform t, Axis a)
    {
        switch (a)
        {
            case Axis.X: return t.right;
            case Axis.Y: return t.up;      // NOTE: Y here means transform.up (not world y)
            case Axis.Z: return t.forward;
            default: return t.forward;
        }
    }
}
