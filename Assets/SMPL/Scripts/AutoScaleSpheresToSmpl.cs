using UnityEngine;

public class AutoScaleSpheresToSmpl : MonoBehaviour
{
    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    static bool Finite(float v) => float.IsFinite(v);
    [Header("Scale THIS (parent of all spheres)")]
    public Transform jointSpheresRoot;   // JointDebug/JointSpheresRoot

    [Header("Spheres used to measure height (WORLD)")]
    public Transform sphereHead;         // J_head
    public Transform sphereLeftAnkle;    // J_l_ankle
    public Transform sphereRightAnkle;   // J_r_ankle

    [Header("SMPL bones used to measure height (WORLD)")]
    public Transform smplHeadBone;       // SMPL head bone
    public Transform smplLeftAnkleBone;  // SMPL left ankle/foot bone
    public Transform smplRightAnkleBone; // SMPL right ankle/foot bone

    [Header("Settings")]
    public bool applyOnStart = true;
    public bool keepUpdating = false; // set true only if scales change at runtime
    public float extraScale = 1f;     // tweak if you want spheres slightly smaller/bigger

    private Vector3 _baselineScale = Vector3.zero;

    void Start()
    {
        if (applyOnStart) ApplyScaleOnce();
    }

    void LateUpdate()
    {
        if (keepUpdating) ApplyScaleOnce();
    }

    [ContextMenu("Apply Scale Once")]
    public void ApplyScaleOnce()
    {
        if (!jointSpheresRoot || !sphereHead || !sphereLeftAnkle || !sphereRightAnkle ||
            !smplHeadBone || !smplLeftAnkleBone || !smplRightAnkleBone)
        {
            Debug.LogWarning("[AutoScaleSpheresToSmpl] Missing references.");
            return;
        }

        if (!Finite(smplHeadBone.position) || !Finite(smplLeftAnkleBone.position) || !Finite(smplRightAnkleBone.position) ||
            !Finite(sphereHead.position) || !Finite(sphereLeftAnkle.position) || !Finite(sphereRightAnkle.position))
        {
            Debug.LogError("[AutoScaleSpheresToSmpl] Non-finite (NaN/Inf) positions detected; skipping scale to prevent NaN propagation.", this);
            return;
        }

        // WORLD space heights
        float smplHeight = Vector3.Distance(
            smplHeadBone.position,
            Midpoint(smplLeftAnkleBone.position, smplRightAnkleBone.position)
        );

        float spheresHeight = Vector3.Distance(
            sphereHead.position,
            Midpoint(sphereLeftAnkle.position, sphereRightAnkle.position)
        );

        if (!Finite(smplHeight) || !Finite(spheresHeight) || spheresHeight < 1e-6f || smplHeight < 1e-6f)
        {
            Debug.LogWarning("[AutoScaleSpheresToSmpl] Height too small; check assignments.");
            return;
        }

        float ratio = (smplHeight / spheresHeight) * extraScale;
        if (!Finite(ratio) || ratio <= 0f)
        {
            Debug.LogError($"[AutoScaleSpheresToSmpl] Invalid ratio={ratio}; skipping scale.", this);
            return;
        }

        // IMPORTANT: avoid multiplying repeatedly each frame (explodes to Inf/NaN and triggers GUIUtility IsFinite assertions).
        if (_baselineScale == Vector3.zero) _baselineScale = jointSpheresRoot.localScale;
        jointSpheresRoot.localScale = _baselineScale * ratio;
        if (!Finite(jointSpheresRoot.localScale))
        {
            Debug.LogError("[AutoScaleSpheresToSmpl] Resulting localScale is non-finite; reverting and skipping.", this);
            jointSpheresRoot.localScale = Vector3.one;
            return;
        }

        Debug.Log($"[AutoScaleSpheresToSmpl] smplHeight={smplHeight:F3}, spheresHeight={spheresHeight:F3}, ratio={ratio:F3}, newScale={jointSpheresRoot.localScale}");
    }

    static Vector3 Midpoint(Vector3 a, Vector3 b) => (a + b) * 0.5f;
}
