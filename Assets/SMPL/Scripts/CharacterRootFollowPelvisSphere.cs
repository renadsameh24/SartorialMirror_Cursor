using UnityEngine;

[DefaultExecutionOrder(50)]
public class CharacterRootFollowPelvisSphereSafe : MonoBehaviour
{
    [Header("Follow this sphere (world space)")]
    public Transform pelvisSphere;   // JointSpheresRoot/J_pelvis

    [Header("Move this root (usually CharacterRoot)")]
    public Transform characterRoot;

    [Header("Tuning")]
    [Range(0f, 1f)] public float lerp = 1f;

    [Tooltip("If spheres are in different units, scale their motion. 1 = normal.")]
    public float motionScale = 1f;

    [Tooltip("Hard safety: if pelvis target is farther than this from current root, ignore the update.")]
    public float maxAllowedJump = 5f;

    [Tooltip("Optional extra offset in world space.")]
    public Vector3 extraOffset;

    private Vector3 initialOffset;   // characterRoot - pelvisSphere at start
    private Vector3 pelvisStart;
    private Vector3 rootStart;
    private bool initialized;

    void Reset()
    {
        characterRoot = transform;
    }

    void Start()
    {
        Init();
    }

    void Init()
    {
        if (!pelvisSphere) return;
        if (!characterRoot) characterRoot = transform;

        pelvisStart = pelvisSphere.position;
        rootStart = characterRoot.position;

        // Preserve the initial relationship so we don't snap away.
        initialOffset = rootStart - pelvisStart;

        initialized = true;
    }

    void LateUpdate()
    {
        if (!initialized)
        {
            Init();
            if (!initialized) return;
        }

        // Scale motion relative to pelvis start so we don't blow up absolute coords.
        Vector3 pelvisDelta = (pelvisSphere.position - pelvisStart) * motionScale;
        Vector3 targetPos = rootStart + pelvisDelta + extraOffset;

        // Safety clamp: ignore insane jumps.
        float dist = Vector3.Distance(characterRoot.position, targetPos);
        if (dist > maxAllowedJump)
        {
            // Uncomment if you want a visible warning:
            // Debug.LogWarning($"[RootFollow] Ignored jump {dist:F2}m. Pelvis might be in wrong space/scale.");
            return;
        }

        characterRoot.position = Vector3.Lerp(characterRoot.position, targetPos, lerp);
    }
}
