using UnityEngine;

public class AutoAlignSphereRootToRigPelvis : MonoBehaviour
{
    [Header("Move this (parent of all spheres)")]
    public Transform jointsRoot;          // JointSpheresRoot

    [Header("References")]
    public Transform spherePelvis;        // J_pelvis sphere (child of jointsRoot)
    public Transform rigPelvisBone;       // J00 bone in the rig

    [Header("Options")]
    public bool calibrateOnPlay = true;
    public bool keepUpdating = false;     // keep OFF
    public Vector3 additionalOffset = Vector3.zero;

    private Vector3 _rootStartPos;
    private Vector3 _calibratedOffset;
    private bool _hasCalibrated;

    void Start()
    {
        if (!jointsRoot) jointsRoot = transform;
        _rootStartPos = jointsRoot.position;

        if (calibrateOnPlay) Calibrate();
    }

    [ContextMenu("Calibrate Now")]
    public void Calibrate()
    {
        if (!jointsRoot || !spherePelvis || !rigPelvisBone)
        {
            Debug.LogWarning("[AutoAlign] Assign jointsRoot, spherePelvis, rigPelvisBone.");
            return;
        }

        // Compute offset in WORLD space
        _calibratedOffset = rigPelvisBone.position - spherePelvis.position;
        _hasCalibrated = true;

        Apply();
        Debug.Log($"✅ [AutoAlign] Calibrated offset = {_calibratedOffset}");
    }

    void LateUpdate()
    {
        if (_hasCalibrated && keepUpdating) Apply();
    }

    void Apply()
    {
        // Set root based on its start pos + calibrated offset (no accumulation)
        jointsRoot.position = _rootStartPos + _calibratedOffset + additionalOffset;
    }
}
