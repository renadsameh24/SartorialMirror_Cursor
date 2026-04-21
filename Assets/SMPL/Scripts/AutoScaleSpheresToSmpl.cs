using UnityEngine;

public class AutoScaleSpheresToSmpl : MonoBehaviour
{
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

        // WORLD space heights
        float smplHeight = Vector3.Distance(
            smplHeadBone.position,
            Midpoint(smplLeftAnkleBone.position, smplRightAnkleBone.position)
        );

        float spheresHeight = Vector3.Distance(
            sphereHead.position,
            Midpoint(sphereLeftAnkle.position, sphereRightAnkle.position)
        );

        if (spheresHeight < 1e-6f || smplHeight < 1e-6f)
        {
            Debug.LogWarning("[AutoScaleSpheresToSmpl] Height too small; check assignments.");
            return;
        }

        float ratio = (smplHeight / spheresHeight) * extraScale;

        // IMPORTANT: set localScale directly (don’t multiply repeatedly each frame)
        jointSpheresRoot.localScale = jointSpheresRoot.localScale * ratio;

        Debug.Log($"[AutoScaleSpheresToSmpl] smplHeight={smplHeight:F3}, spheresHeight={spheresHeight:F3}, ratio={ratio:F3}, newScale={jointSpheresRoot.localScale}");
    }

    static Vector3 Midpoint(Vector3 a, Vector3 b) => (a + b) * 0.5f;
}
