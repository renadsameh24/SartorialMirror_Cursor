# Sartorial Mirror Prototype

## Overview
Sartorial Mirror is a real-time virtual try-on prototype built in Unity.
The current stable version focuses on live body tracking using a local Python pose server and an SMPL body inside Unity.

At this stage, the body tracking pipeline is the part that works reliably:
- a local FastAPI WebSocket server runs on a laptop
- Unity connects to the server
- MediaPipe pose landmarks are received in Unity
- pose spheres and mapping update correctly
- the SMPL avatar follows the user in real time

Garment overlay is now wired as a **swappable garment system** that follows the SMPL rig (FK-driven) and renders over a live webcam background. Garment assets still need to be **prepared for the SMPL armature** (see “Garments (Current Workflow)” below).

---

## Repository Structure

This repository currently contains:
- the Unity project
- the local Python pose server
- the MediaPipe pose model file required by the server

Important server-side files:
- python_server/server_fastapi.py
- python_server/models/pose_landmarker_full.task
- python_server/requirements.txt

The Python virtual environment is not included in the repository and must be created locally.

---

## Current Working Status

The current working setup is:
- Python pose server runs locally on a laptop
- Unity connects through WebSocket
- MediaPipe pose landmarks are received in Unity
- pose spheres / pose rig update correctly
- the SMPL body follows the user in real time

This is the stable part of the project.

---

## Main Unity Scene to Open

Open this scene first:

Assets/SMPL_Unity_Checkpoint_FullBody_March.unity

This is the main working scene for the current full-body pose pipeline.

It now also contains a runtime bootstrap object:

- `SartorialMirrorRuntime` (added to the scene roots)

This object:

- Shows the **live webcam background** (UGUI)
- Hides the SMPL body mesh (rig stays active)
- Creates a **garment selection UI**
- Instantiates the selected garment and **remaps its skinned bones by bone-name** onto the SMPL rig

---

## Garments (Current Workflow)

### Add garments to the project

1. Import your garment as a **Prefab** containing one or more `SkinnedMeshRenderer`s.
2. Ensure the garment is skinned to an armature whose **bone names match the SMPL bone names** in the Unity rig (recommended: bind in Blender using the exported SMPL armature).

### Register garments in the catalog

Edit this asset in Unity:

- `Assets/SartorialMirror/GarmentCatalog.asset`

Add entries under `garments`:

- `displayName`: what shows in the UI
- `garmentPrefab`: the garment prefab to instantiate
- `thumbnail`: optional

At runtime, click a garment button to swap garments.

---

## Main Root Object

The main root object is:

SMPL_neutral_rig_GOLDEN

This is the character setup used for the working live pose system.

---

## Important Working Configuration

For the main working setup on SMPL_neutral_rig_GOLDEN:

Keep enabled:
- Animator
- SpheresToBones_FKDriver

These are the important components for the current working setup.

Keep the MediaPipe and pose pipeline the same.
The current Unity-side body tracking flow should remain unchanged.
Do not change the existing pose receiving and mapping pipeline unless necessary.

This includes the current working scripts and logic such as:
- PoseReceiverWS.cs
- PoseRig3DSpheres.cs
- PoseRigHierarchy3D.cs
- MediaPipe33_To_J17Mapper1.cs
- SpheresToBones_FKDriver.cs

These are part of the working body-tracking system.

What failed and should stay off:
All other experimental script combinations and IK-based attempts should be treated as failed experiments unless intentionally revisited later.

In particular:
- IK constraints
- alternate IK drivers
- alternate hints/targets systems
- other experimental root follow / IK combinations

These produced unstable or incorrect motion, including:
- unnatural bending
- twisting
- collapsing limbs
- incorrect alignment

So for the main scene:

Use FK, not IK.

---

## How to Open the Unity Project

1. Open Unity Hub
2. Add this project if it is not already listed
3. Open the project folder
4. Open the scene:
   Assets/SMPL_Unity_Checkpoint_FullBody_March.unity
5. Make sure the server is running before pressing Play

---

## How to Run the Pose Server Locally (Mac)

1) Open Terminal and go to Desktop
cd ~/Desktop

2) Go into the server folder inside the repo
Example:
cd SartorialMirrorProto_BACKUP/Unity_Golden/SMPL_Unity_Test/python_server

Adjust the path if your local folder name is different.

3) Create a virtual environment if needed
If you do not already have one:
python3 -m venv venv

4) Activate the virtual environment
The venv folder is named venv:
source venv/bin/activate

5) Install the required packages
If not installed yet:
pip install -r requirements.txt

6) Make sure the model file exists
ls models/pose_landmarker_full.task

7) Start the FastAPI WebSocket pose server
python3 server_fastapi.py

You should see something like:
WebSocket server running on ws://0.0.0.0:8000/ws

8) Set the Unity WebSocket URL

If Unity is running on the same laptop:
ws://localhost:8000/ws

If Unity is running on another machine:
ws://<YOUR_LAPTOP_IP>:8000/ws

Stop the server:
Ctrl + C

---

## Expected Runtime Behavior

When the Python server is running correctly and Unity is configured properly:
- Unity connects to the WebSocket server
- pose data is received
- pose spheres update
- the pose rig updates
- the SMPL body follows the user live

If this does not happen, first check:
- the server is running
- the correct WebSocket URL is set in Unity
- the correct scene is open
- Animator and SpheresToBones_FKDriver are enabled on SMPL_neutral_rig_GOLDEN

---

## Garment Work So Far

Garment work was started, but it is not finalized in this repository.

What was attempted:
We tried importing garment assets and making them follow the SMPL body.

What went wrong:
The main issues were:

1. Random rigged garments from online sources were not compatible enough
   - different skeletons
   - different bind poses
   - different scaling assumptions
   - different shoulder / sleeve structure

2. Unity-side remapping was not reliable
   - garments did not follow the SMPL body correctly
   - sleeves and shoulders behaved badly
   - alignment was unstable

3. IK-based approaches failed
   - unnatural bending
   - unstable constraints
   - difficult to maintain as a repeatable workflow

4. The workflow was not scalable for multiple garments
   - one garment might partially work
   - but the method was too messy to repeat for a full garment library

Current conclusion about garments:
Do not continue using random pre-rigged garments as the main workflow.

Instead, use:
- one fixed SMPL body
- simple upper-body garment meshes
- garments prepared specifically for this body

---

## Current Recommendation for Garments

For the next garment phase:

Remove current garment experiments from the main working scene.
The main live-tracking setup should stay clean and focused on the working body pipeline.

Recommended garment type:
Use:
- simple upper-body garments
- preferably non-rigged meshes
- fitted or semi-fitted shirts / tops
- not oversized
- not layered
- not complex jackets or coats

Why non-rigged is recommended:
A non-rigged garment mesh is better for this project because:
- there is no old incompatible skeleton
- there are no hidden scale assumptions from another rig
- it can be fitted directly to the project’s fixed SMPL body
- it can later be bound specifically to the SMPL armature

This is much more practical for a project that needs:
- multiple garments
- garment selection
- later size options

---

## Recommended Next Garment Pipeline

The recommended workflow is:

Step 1
Keep the current Unity project focused on the working body pipeline only.

Step 2
Prepare each garment specifically for the fixed SMPL body.

Step 3
Use Blender to:
- import the garment mesh
- fit it to the SMPL body
- bind it to the SMPL armature
- test deformation
- export the prepared garment back into Unity

Step 4
In Unity, use prepared garments as swappable assets later.

This is more scalable than trying to force random online rigged garments into the live system.

---

## Important Note About Blender

A Blender-ready FBX/source file for the SMPL body was not originally included in the repo.

So before garment work in Blender, export the current SMPL body from Unity.

---

## How to Export the SMPL Model from Unity to Blender

Since the Blender-ready source file is not included, export the current SMPL body from Unity as FBX.

Option A: Using Unity FBX Exporter
If Unity FBX Exporter is installed:

1. Open the main scene:
   Assets/SMPL_Unity_Checkpoint_FullBody_March.unity

2. In the Hierarchy, select:
   SMPL_neutral_rig_GOLDEN

3. Use the FBX Exporter from Unity:
   - GameObject > Export To FBX
   - or the FBX Exporter menu if available

4. Export the selected object as an FBX file

5. Save it in a separate working folder for Blender

Option B: If FBX Exporter is not installed
Install Unity’s FBX Exporter package through Package Manager, then export the root object.

After exporting:
Open Blender and:
- import the FBX
- confirm the body mesh and armature are present
- use that as the base file for garment preparation

### New garment FBX must match SMPL in Unity (scale and rig)

If you only revert C# but keep the same garment FBX, **nothing visible will change** — the mesh, weights, and bind pose are the asset.

After importing a new garment FBX, its **Model Import** settings must match the SMPL reference (`Assets/SMPL/Models/SMPL_neutral_rig_GOLDEN.fbx`):

- **Scale Factor** (Meshes): **100** — same as SMPL in this project. Wrong scale = shirt looks short/tiny/wrong height next to the body.
- **Optimize Bones**: **0** (disabled) — matches SMPL; avoids bone-index surprises with skinning.
- **Rig**: use the imported armature from the garment FBX, or remap in Unity only if bone names are identical to `J00`…`J23` style used on scene SMPL.

Recommended automated prep (weights copied from SMPL body mesh, then export):

- Run `Tools/blender_golden_garment_prep.py` (see script header for `MODE=INSPECT` and env vars). Point `GarmentCatalog` at the new FBX/prefab under e.g. `Assets/garments_prepared/`.

In Blender, avoid relying on **IK/constraints** for the final bind — Unity uses the exported rest pose and skin weights; parent the shirt to the SMPL armature with **Armature Deform** after weights are baked.

---

## Recommended Blender Garment Base Workflow

Once the SMPL body is exported to Blender:

1. Create a base Blender file containing only:
   - SMPL body
   - SMPL armature

2. For each garment:
   - import the garment mesh
   - fit it to the body
   - bind it to the SMPL armature
   - fix deformation around sleeves / shoulders if needed
   - export it back to Unity

This should become the standard garment workflow.

---

## Current Development Recommendation

For anyone continuing this project:

Keep the current working body pipeline untouched.
That means:
- use Assets/SMPL_Unity_Checkpoint_FullBody_March.unity
- keep SMPL_neutral_rig_GOLDEN as the main root
- keep only the known stable FK + Animator setup enabled
- do not reactivate failed IK experiments in the main scene

Continue garment work separately.
The next stage should focus on:
- a clean garment-preparation workflow
- simple non-rigged garments
- Blender-based fitting and rebinding
- later returning finished garments to Unity

---

## Summary

Working now:
- local FastAPI pose server
- Unity WebSocket connection
- MediaPipe landmark pipeline
- pose spheres / pose rig
- FK-driven SMPL full body motion

Not finalized:
- garment overlay
- garment fitting
- garment size variation
- garment UI selection

Recommended next step:
Continue with:
- clean garment preparation using non-rigged garment meshes
- Blender fitting to exported SMPL FBX
- then bring finished garment assets back into Unity

---

## Main Entry Point

Open this scene first:

Assets/SMPL_Unity_Checkpoint_FullBody_March.unity

This is the main working entry point for the current prototype.
