using System.Collections;
using UnityEngine;

public class FollowTransformEndOfFrame : MonoBehaviour
{
    public Transform source;
    public bool followPosition = true;
    public bool followRotation = false;

    void OnEnable()
    {
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        var wait = new WaitForEndOfFrame();
        while (enabled)
        {
            yield return wait; // runs after rigs/animation updates
            if (!source) continue;

            if (followPosition) transform.position = source.position;
            if (followRotation) transform.rotation = source.rotation;
        }
    }
}
