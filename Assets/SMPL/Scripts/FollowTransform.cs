using UnityEngine;

[DefaultExecutionOrder(1000)] // run late
public class FollowTransform : MonoBehaviour
{
    public Transform source;
    public bool followPosition = true;
    public bool followRotation = false;
    public Vector3 positionOffset;
    public Vector3 rotationOffsetEuler;

    void LateUpdate()
    {
        if (!source) return;

        if (followPosition)
            transform.position = source.position + positionOffset;

        if (followRotation)
            transform.rotation = source.rotation * Quaternion.Euler(rotationOffsetEuler);
    }
}
