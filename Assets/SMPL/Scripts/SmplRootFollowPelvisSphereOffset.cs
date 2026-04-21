using UnityEngine;

[DefaultExecutionOrder(150)]
public class SmplRootFollowPelvisSphereOffset : MonoBehaviour
{
    [Header("Move this (recommended: CharacterRoot)")]
    public Transform rootToMove;

    [Header("Follow this (pelvis sphere in world space)")]
    public Transform pelvisSphere;

    [Header("Options")]
    public bool followRotation = false;
    [Range(0f, 1f)] public float lerp = 1f;

    Vector3 posOffset;
    Quaternion rotOffset;
    bool hasInit;

    void Start()
    {
        InitOffsets();
    }

    void OnEnable()
    {
        InitOffsets();
    }

    void InitOffsets()
    {
        if (!rootToMove || !pelvisSphere) return;

        // Store the initial relationship so we don't teleport
        posOffset = rootToMove.position - pelvisSphere.position;
        rotOffset = Quaternion.Inverse(pelvisSphere.rotation) * rootToMove.rotation;
        hasInit = true;
    }

    void LateUpdate()
    {
        if (!hasInit || !rootToMove || !pelvisSphere) return;

        Vector3 targetPos = pelvisSphere.position + posOffset;
        rootToMove.position = Vector3.Lerp(rootToMove.position, targetPos, Mathf.Clamp01(lerp));

        if (followRotation)
        {
            Quaternion targetRot = pelvisSphere.rotation * rotOffset;
            rootToMove.rotation = Quaternion.Slerp(rootToMove.rotation, targetRot, Mathf.Clamp01(lerp));
        }
    }
}
