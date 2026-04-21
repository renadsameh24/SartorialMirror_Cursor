using UnityEngine;

[DefaultExecutionOrder(150)]
public class SmplRootFollowPelvisSphere : MonoBehaviour
{
    [Header("What to move (recommended: CharacterRoot, not the SMPL rig itself)")]
    public Transform rigRootToMove;   // drag CharacterRoot here

    [Header("What to follow (pelvis sphere in world space)")]
    public Transform pelvisSphere;    // drag J_pelvis here

    [Header("Options")]
    public bool followRotation = false;
    [Range(0f, 1f)] public float lerp = 0.35f;

    private Vector3 posOffset;
    private Quaternion rotOffset;
    private bool initialized;

    void Start()
    {
        Initialize();
    }

    void OnEnable()
    {
        // In case enabled during play
        Initialize();
    }

    void Initialize()
    {
        if (initialized) return;
        if (!rigRootToMove || !pelvisSphere) return;

        // Preserve the initial offset so we don't teleport
        posOffset = rigRootToMove.position - pelvisSphere.position;
        rotOffset = Quaternion.Inverse(pelvisSphere.rotation) * rigRootToMove.rotation;

        initialized = true;
    }

    void LateUpdate()
    {
        if (!initialized) Initialize();
        if (!initialized) return;

        Vector3 targetPos = pelvisSphere.position + posOffset;
        rigRootToMove.position = Vector3.Lerp(rigRootToMove.position, targetPos, lerp);

        if (followRotation)
        {
            Quaternion targetRot = pelvisSphere.rotation * rotOffset;
            rigRootToMove.rotation = Quaternion.Slerp(rigRootToMove.rotation, targetRot, lerp);
        }
    }
}
