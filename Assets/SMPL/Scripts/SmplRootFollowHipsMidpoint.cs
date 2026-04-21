using UnityEngine;

[DefaultExecutionOrder(-10000)] // run very early so rigging/IK uses updated root
public class SmplRootFollowHipsMidpoint : MonoBehaviour
{
    [Header("MOVE THIS (recommended: CharacterRoot)")]
    public Transform rootToMove;

    [Header("DRIVE FROM THESE spheres")]
    public Transform leftHipSphere;   // J_l_hip
    public Transform rightHipSphere;  // J_r_hip

    [Header("Options")]
    public bool keepInitialOffset = true;
    [Range(0f, 1f)] public float lerp = 1f;

    Vector3 offset;
    bool inited;

    void LateUpdate()
    {
        if (!rootToMove || !leftHipSphere || !rightHipSphere) return;

        Vector3 hipsMid = (leftHipSphere.position + rightHipSphere.position) * 0.5f;

        if (!inited)
        {
            offset = keepInitialOffset ? (rootToMove.position - hipsMid) : Vector3.zero;
            inited = true;
        }

        Vector3 goal = hipsMid + offset;
        rootToMove.position = Vector3.Lerp(rootToMove.position, goal, lerp);
    }
}

