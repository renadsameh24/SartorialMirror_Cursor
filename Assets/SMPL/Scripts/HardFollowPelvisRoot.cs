using UnityEngine;

[DefaultExecutionOrder(-10000)]
public class HardFollowPelvisRoot : MonoBehaviour
{
    [Header("This MUST be the moving pelvis sphere (J_pelvis)")]
    public Transform pelvisSphere;

    [Header("Optional: assign your mesh root to verify parenting")]
    public Transform smplRoot;

    [Tooltip("If true, also copy pelvis rotation")]
    public bool followRotation = false;

    [Tooltip("Freeze offset on first frame so it doesn't teleport")]
    public bool keepInitialOffset = true;

    Vector3 posOffset;
    Quaternion rotOffset;
    bool inited;

    void LateUpdate()
    {
        if (!pelvisSphere) return;

        if (!inited)
        {
            if (keepInitialOffset)
            {
                posOffset = transform.position - pelvisSphere.position;
                rotOffset = transform.rotation * Quaternion.Inverse(pelvisSphere.rotation);
            }
            else
            {
                posOffset = Vector3.zero;
                rotOffset = Quaternion.identity;
            }
            inited = true;
        }

        transform.position = pelvisSphere.position + posOffset;

        if (followRotation)
            transform.rotation = rotOffset * pelvisSphere.rotation;

        // DEBUG: if this prints changing pelvis but root not moving, script isn't running.
        // If root moves but mesh doesn't, parenting is wrong or mesh is being overridden.
        // Comment out once fixed.
        // Debug.Log($"Pelvis: {pelvisSphere.position}  Root: {transform.position}");
    }
}
