# DHMGS Full Demo

Open `Scenes/DhmgsFullDemo.unity` and enter Play Mode.

The compact Canvas panel in the upper-left restores the original workflow:

- enter an Attempt ID and select **Fetch** to download, save, bake, and play;
- choose `None`, Female, Male, or Neutral from the avatar dropdown;
- choose optional clothes when the player has clothes rigs;
- adjust avatar, clothes, and Editor video opacity;
- change playback speed from 0.25x to 2.0x;
- read the newest-first operation log.

The upper-right wide view and lower-right detail view are live RenderTextures. Drag with the left mouse button to orbit, drag with another mouse button to pan, and use the wheel to zoom.

Female, Male, Neutral, and the freshly baked Generic preview all reuse the package's original `Dhmgs_SkinTransparent` material. The scene also carries the original directional-light setup.

This sample references only assets in this sample folder and in the MNNH DHMGS package. It does not reference `Assets/Mnnh.Dhmgs/Content`, `PromptGenTest`, `DhTest`, or any project-specific scene, prefab, material, font, or input actions.

**Fetch** is available only while running the scene in the Unity Editor. It downloads the Attempt ID, saves a persistent `DhmgsMotionAsset` and Generic AnimationClip under `Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/`, and starts playback. The Generic clip uses the package-owned Neutral SMPL-X rig. If a preview video is present, the Editor displays it in sync and the Video Opacity slider controls it.

Before Fetch, both right-side views show the active SMPL-X player. After Fetch, the lower view shows the freshly baked Generic clip. In a Player build, Fetch and video controls are disabled while bundled or serialized motion assets, playback controls, and both views remain available.

Use `DHMGS > Importer` when you need Humanoid clips with a separately supplied Humanoid rig.
