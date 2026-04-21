import json
import time
from pathlib import Path

import cv2
import numpy as np
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
import mediapipe as mp

APP = FastAPI()

# Expects: ./models/pose_landmarker_full.task (relative to this file)
MODEL_PATH = Path(__file__).parent / "models" / "pose_landmarker_full.task"


def create_landmarker():
    # MediaPipe Tasks API
    BaseOptions = mp.tasks.BaseOptions
    PoseLandmarker = mp.tasks.vision.PoseLandmarker
    PoseLandmarkerOptions = mp.tasks.vision.PoseLandmarkerOptions
    RunningMode = mp.tasks.vision.RunningMode

    options = PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=str(MODEL_PATH)),
        running_mode=RunningMode.VIDEO,
        num_poses=1,
        output_segmentation_masks=False,
    )
    return PoseLandmarker.create_from_options(options)


@APP.websocket("/ws")
async def ws_pose(websocket: WebSocket):
    await websocket.accept()

    if not MODEL_PATH.exists():
        await websocket.send_text(json.dumps({
            "ok": False,
            "error": f"Model not found at: {MODEL_PATH}"
        }))
        await websocket.close()
        return

    landmarker = create_landmarker()

    try:
        while True:
            # Receive JPEG frame bytes from Unity
            try:
                frame_bytes = await websocket.receive_bytes()
            except WebSocketDisconnect:
                break
            except Exception:
                break

            if not frame_bytes:
                continue

            # Decode JPEG -> BGR image
            nparr = np.frombuffer(frame_bytes, np.uint8)
            bgr = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
            if bgr is None:
                await websocket.send_text(json.dumps({"ok": False, "error": "decode_failed"}))
                continue

            # Convert to RGB for MediaPipe
            rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)

            # Timestamp in ms (must increase for VIDEO mode)
            ts_ms = int(time.time() * 1000)

            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

            try:
                result = landmarker.detect_for_video(mp_image, ts_ms)
            except Exception as e:
                await websocket.send_text(json.dumps({"ok": False, "error": str(e)}))
                continue

            payload = {"ok": True, "landmarks": []}

            # 33 landmarks if a person is detected
            if result.pose_landmarks and len(result.pose_landmarks) > 0:
                for lm in result.pose_landmarks[0]:
                    payload["landmarks"].append({
                        "x": float(lm.x),
                        "y": float(lm.y),
                        "z": float(lm.z),
                        "v": float(lm.visibility) if lm.visibility is not None else 0.0
                    })

            await websocket.send_text(json.dumps(payload))

    finally:
        landmarker.close()


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(APP, host="0.0.0.0", port=8000)
