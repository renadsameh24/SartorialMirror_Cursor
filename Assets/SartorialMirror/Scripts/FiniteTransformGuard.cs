using UnityEngine;

/// <summary>
/// No-op placeholder. A previous version disabled <see cref="JointPlaybackStreamV2"/>,
/// <see cref="SpheresToBones_FKDriver"/>, and <see cref="FollowTransform"/> when any transform was non-finite,
/// which could kill the pose chain. Original BACKUP pipeline did not use this. Kept for serialized scenes.
/// </summary>
[DisallowMultipleComponent]
public sealed class FiniteTransformGuard : MonoBehaviour
{
    void Awake()
    {
        if (Application.isPlaying)
            enabled = false;
    }
}
