using System.Collections.Generic;
using UnityEngine;

public class SkeletonLines3D : MonoBehaviour
{
    public Transform rigRoot;         // drag PoseRig3D here
    public float width = 0.02f;

    private LineRenderer lr;

    // MediaPipe Pose indices
    const int L_SHOULDER = 11, R_SHOULDER = 12;
    const int L_ELBOW = 13, R_ELBOW = 14;
    const int L_WRIST = 15, R_WRIST = 16;
    const int L_HIP = 23, R_HIP = 24;
    const int L_KNEE = 25, R_KNEE = 26;
    const int L_ANKLE = 27, R_ANKLE = 28;

    void Start()
    {
        lr = gameObject.AddComponent<LineRenderer>();
        lr.startWidth = width;
        lr.endWidth = width;
        lr.useWorldSpace = true;
        lr.positionCount = 0;
    }

    void Update()
    {
        if (rigRoot == null) return;
        if (rigRoot.childCount < 33) return;

        Vector3 LS = J(L_SHOULDER), RS = J(R_SHOULDER);
        Vector3 LH = J(L_HIP), RH = J(R_HIP);

        Vector3 shoulderMid = (LS + RS) * 0.5f;
        Vector3 hipMid = (LH + RH) * 0.5f;

        var pts = new List<Vector3>();

        // Left arm
        pts.Add(LS); pts.Add(J(L_ELBOW)); pts.Add(J(L_WRIST));
        pts.Add(J(L_WRIST)); // break

        // Right arm
        pts.Add(RS); pts.Add(J(R_ELBOW)); pts.Add(J(R_WRIST));
        pts.Add(J(R_WRIST)); // break

        // Torso
        pts.Add(shoulderMid); pts.Add(hipMid);
        pts.Add(hipMid); // break

        // Left leg
        pts.Add(LH); pts.Add(J(L_KNEE)); pts.Add(J(L_ANKLE));
        pts.Add(J(L_ANKLE)); // break

        // Right leg
        pts.Add(RH); pts.Add(J(R_KNEE)); pts.Add(J(R_ANKLE));

        lr.positionCount = pts.Count;
        lr.SetPositions(pts.ToArray());
    }

    Vector3 J(int idx)
    {
        return rigRoot.GetChild(idx).position;
    }
}
