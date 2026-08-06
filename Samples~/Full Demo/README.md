# DHMGS Full Demo

Open `Scenes/DhmgsFullDemo.unity` and enter Play Mode.

The Canvas panel in the upper-left lets you:

- enter a DHMGS Base URL and Attempt ID, then select **Import & Play**;
- play and stop the bundled motion;
- switch between the Female, Male, and Neutral base rigs;
- change playback speed from 0.25x to 2.0x.

This sample references only assets in this sample folder and in the MNNH DHMGS package. It does not reference `Assets/Mnnh.Dhmgs/Content`, `PromptGenTest`, `DhTest`, or any project-specific scene, prefab, material, font, or input actions.

**Import & Play** is available only while running the scene in the Unity Editor. It downloads the Attempt ID, saves a persistent `DhmgsMotionAsset` under `Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/`, and starts playback. In a Player build, the import controls are disabled while bundled or serialized motion assets remain playable.

Use `DHMGS > Importer` when you also need Generic/Humanoid AnimationClip baking or preview-video options.
