using UnityEngine;

/// <summary>
/// No-op placeholder. The original <c>SartorialMirrorProto_BACKUP</c> pose pipeline does not disable
/// <see cref="FollowTransform"/>, sphere follow, or <see cref="AutoScaleSpheresToSmpl"/> — an earlier Cursor
/// version did, which broke camera → spheres → small body movement. This component is kept only so scenes
/// that still reference it do not show a missing script.
/// </summary>
[DisallowMultipleComponent]
public sealed class CleanGarmentPipelineInstaller : MonoBehaviour
{
    void Awake()
    {
        if (Application.isPlaying)
            enabled = false;
    }
}
