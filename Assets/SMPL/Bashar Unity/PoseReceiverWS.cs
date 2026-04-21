using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using NativeWebSocket;

[Serializable]
public class PoseMsg
{
    public bool ok;
    public string error;
    public List<Lm> landmarks;
}

[Serializable]
public class Lm
{
    public float x, y, z, v;
}

public class PoseReceiverWS : MonoBehaviour
{
    [Header("WebSocket")]
    public string wsUrl = "ws://127.0.0.1:8000/ws";
    public bool logStatus = true;
    public bool logPythonErrors = true;

    [Header("Optional: Send Webcam Frames")]
    public bool sendWebcamFrames = true;
    public int reqWidth = 640;
    public int reqHeight = 480;
    public int reqFps = 30;
    public int sendFps = 15;
    [Range(20, 90)] public int jpegQuality = 60;

    public PoseMsg Latest { get; private set; }
    public bool HasPose => Latest != null && Latest.ok && Latest.landmarks != null && Latest.landmarks.Count >= 33;

    private WebSocket ws;
    private WebCamTexture cam;
    private Texture2D frameTex;
    private float sendTimer;
    private bool isSending;

    public WebCamTexture WebcamTexture => cam;

    async void Start()
    {
        if (sendWebcamFrames)
        {
            cam = new WebCamTexture(reqWidth, reqHeight, reqFps);
            cam.Play();
        }

        ws = new WebSocket(wsUrl);

        ws.OnOpen += () =>
        {
            if (logStatus) Debug.Log("WS Open: " + wsUrl);
        };

        ws.OnMessage += (bytes) =>
        {
            try
            {
                string json = Encoding.UTF8.GetString(bytes);
                Latest = JsonUtility.FromJson<PoseMsg>(json);

                if (Latest != null && !Latest.ok && logPythonErrors)
                    Debug.LogWarning("Python error: " + Latest.error);
            }
            catch (Exception e)
            {
                Debug.LogError("WS Parse error: " + e.Message);
            }
        };

        ws.OnError += (e) =>
        {
            Debug.LogError("WS Error: " + e);
        };

        ws.OnClose += (e) =>
        {
            if (logStatus) Debug.Log("WS Closed: " + e);
        };

        await ws.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
        if (!sendWebcamFrames) return;

        if (cam == null || !cam.isPlaying) return;

        if (frameTex == null && cam.width > 16 && cam.height > 16)
            frameTex = new Texture2D(cam.width, cam.height, TextureFormat.RGB24, false);

        sendTimer += Time.deltaTime;
        float interval = 1f / Mathf.Max(1, sendFps);

        if (sendTimer >= interval)
        {
            sendTimer = 0f;
            TrySendFrame();
        }
    }

    async void TrySendFrame()
    {
        if (isSending) return;
        if (ws == null || ws.State != WebSocketState.Open) return;
        if (cam == null || !cam.didUpdateThisFrame) return;
        if (frameTex == null) return;

        isSending = true;

        try
        {
            frameTex.SetPixels32(cam.GetPixels32());
            frameTex.Apply(false);

            byte[] jpg = frameTex.EncodeToJPG(jpegQuality);
            if (jpg != null && jpg.Length > 0)
                await ws.Send(jpg);
        }
        catch (Exception ex)
        {
            Debug.LogError("SendFrame error: " + ex.Message);
        }
        finally
        {
            isSending = false;
        }
    }

    async void OnDisable()
    {
        try
        {
            if (ws != null && ws.State == WebSocketState.Open)
                await ws.Close();
        }
        catch { }
    }
}
