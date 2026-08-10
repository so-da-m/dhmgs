# DHMGS Full Demo

Open `Scenes/DhmgsFullDemo.unity` and enter Play Mode.

The compact Canvas panel in the upper-left restores the original workflow:

- enter an Attempt ID and select **Fetch** to download, save, bake, and play;
- choose `None`, Female, Male, or Neutral from the avatar dropdown;
- choose the included `SmplxChatGarmentClothes` rig or `None`;
- adjust avatar, clothes, and Editor video opacity;
- change playback speed from 0.25x to 2.0x;
- read the newest-first operation log.

The upper-right direct view and lower-right Humanoid retarget view are live RenderTextures. Each target is automatically fitted when it changes. Drag with the left mouse button to orbit, drag with another mouse button to pan, and use the wheel to zoom.

Female, Male, and Neutral reuse the package's original `Dhmgs_SkinTransparent` material. The included garment uses `Dhmgs_ClothTransparent`. YBot keeps its own embedded Mixamo material so the lower view represents an ordinary target avatar instead of another SMPL-X material override.

This sample references only assets in this sample folder and in the MNNH DHMGS package. It does not reference `Assets/Mnnh.Dhmgs/Content`, `PromptGenTest`, `DhTest`, or any project-specific scene, prefab, material, font, or input actions.

**Fetch** is available only while running the scene in the Unity Editor. It downloads the Attempt ID and saves a persistent `DhmgsMotionAsset`, Humanoid in-place clip, and Humanoid root-motion clip under `Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/`. The package-owned Neutral SMPL-X is the Humanoid bake source; the included YBot is only the retarget playback target. If a preview video is present, the Editor displays it in sync and the Video Opacity slider controls it.

Before Fetch, the upper view shows the active SMPL-X player and the lower view stays empty. After Fetch, the lower view plays the freshly baked Humanoid in-place clip on YBot through a standard Unity Animator/Playable path. In a Player build, Fetch, baking, the Editor comparison preview, and video controls are disabled; already generated clips and motion assets remain build-safe and can be assigned to runtime Animators or players.

Use `DHMGS > Importer` when you need to choose a different Generic or Humanoid bake source.
