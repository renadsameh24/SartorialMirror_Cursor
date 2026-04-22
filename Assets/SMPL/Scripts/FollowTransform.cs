using UnityEngine;

[DefaultExecutionOrder(1000)] // run late
public class FollowTransform : MonoBehaviour
{
    public Transform source;
    public bool followPosition = true;
    public bool followRotation = false;
    public Vector3 positionOffset;
    public Vector3 rotationOffsetEuler;

    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w);

    void LateUpdate()
    {
        if (!source) return;

        if (followPosition)
        {
            var p = source.position + positionOffset;
            if (Finite(p))
                transform.position = p;
        }

        if (followRotation)
        {
            var r = source.rotation * Quaternion.Euler(rotationOffsetEuler);
            if (Finite(r))
                transform.rotation = r;
        }
    }
}
