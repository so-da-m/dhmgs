# Changelog

## [0.6.0] - 2026-08-10

### Added

- Included a package-local Humanoid Y Bot target in the Full Demo without Git LFS.
- Added regression coverage for raw Humanoid FBX baking and Y Bot package dependencies.

### Changed

- Changed the lower RenderTexture from a duplicate Generic SMPL-X preview to a baked Humanoid clip retargeted onto Y Bot through Unity Playables.
- Switched the bundled Neutral SMPL-X FBX import rig to Humanoid so it can author both Humanoid muscle clips and direct SMPL-X playback without a duplicate 30 MB FBX.
- Made raw Humanoid FBX assets usable directly as bake and preview inputs by resolving their imported Avatar subasset and adding the transient Animator automatically.
- Exposed both saved Humanoid in-place and root-motion clips from `DhmgsAssetWriteResult`.
- Removed the custom Humanoid preview layer requirement; isolated Editor previews no longer mutate project layer settings.
- Moved the Humanoid preview component out of Unity's special `Editor/` folder so it remains build-excluded but can be attached to the transient preview host.

## [0.5.2] - 2026-08-06

### Fixed

- Replaced the Full Demo's raw FBX instances with the original package-local rig prefabs so skin materials and SMPL-X setup cannot drift.
- Restored the lower panel's original baked-preview-only role and cleared it until a Fetch bake succeeds.
- Added renderer-bounds auto-framing to both RenderTexture views so direct and baked subjects remain visible at the current panel size.
- Kept auto-framed cameras centered on travelling motion and cleared stale frames when playback stops.
- Corrected `Dhmgs_ClothTransparent` to reference the clothes shader.
- Made skin and clothes shaders remain neutrally visible without scene lighting and stop writing depth at zero opacity.
- Made saved motion object names match their `.asset` filenames.

### Added

- Included the original chat-garment clothes rig as a compact standalone prefab and native Mesh asset, without Git LFS.
- Added the garment to the Full Demo clothes selector.

### Changed

- Switched opacity control to per-renderer `MaterialPropertyBlock` values so shared materials are no longer cloned or mutated.

## [0.5.1] - 2026-08-06

### Fixed

- Restored the original white UI tint on both RenderTexture panels so the wide and detail camera outputs are visible at full brightness.
- Restored the package's original `Dhmgs_SkinTransparent` material and legacy directional light for Female, Male, and Neutral avatars.
- Applied the same original skin material to the freshly baked Generic preview instead of relying on the raw FBX material.
- Relocked both preview cameras when playback or the active avatar changes.

### Removed

- Removed the sample-only green, pink, and blue avatar materials and their replacement shader.

## [0.5.0] - 2026-08-06

### Added

- Restored the two interactive upper-right and lower-right RenderTexture camera views with orbit, pan, and zoom.
- Restored avatar/clothes dropdowns plus avatar, clothes, video, and playback-speed sliders.
- Added automatic Generic clip baking with the bundled Neutral SMPL-X rig to the sample Fetch workflow.
- Added Editor-only synchronized video preview when an imported attempt contains a video.

### Changed

- Returned the Full Demo to the compact legacy Attempt ID + Fetch workflow while retaining the current Canvas visual style.
- Made Fetch download, save, bake, and immediately play in one operation again.
- Replaced the old external X Bot/Humanoid lower preview dependency with a package-local Generic bake preview.
- Kept all downloading, baking, and video code out of Player builds; existing motion playback and both RenderTexture views remain build-safe.
- Exposed the saved Generic clip from `DhmgsAssetWriteResult` so Editor integrations can preview the exact asset they wrote.
- Removed global camera/player/name searches from the sample preview path; every Editor operation now uses references owned by that Full Demo controller.
- Reduced Generic clips to the rotations actually driven by DHMGS instead of recording every Transform property.

## [0.4.0] - 2026-08-06

### Added

- Added a Canvas-based Full Demo interface with Base URL and Attempt ID input.
- Added an Editor-only bridge that downloads, imports, saves, and immediately plays an Attempt ID.
- Added progress and status reporting for the sample import flow.

### Changed

- Replaced the sample's IMGUI overlay with a UGUI Canvas.
- Kept runtime playback isolated from the Editor importer so already imported assets remain Player-build safe.
- Restored the Input System dependency for Canvas input handling in the Full Demo sample.

## [0.3.0] - 2026-08-06

### Added

- Added an importable `Full Demo` sample under `Samples~`.
- Bundled a small, build-safe `DhmgsMotionAsset` for immediate playback.
- Added a self-contained scene demonstrating play, stop, speed control, and Female/Male/Neutral avatar switching.
- Added sample-owned cross-pipeline materials so no project-local materials are required.

### Changed

- Documented the boundary between the runtime sample and the Editor-only importer/baker.

## [0.2.0] - 2026-08-06

### Changed

- Split the package into a build-safe runtime player, optional runtime UI, and Editor-only import/bake pipeline.
- Replaced the coupled download-and-play flow with `DhmgsImportPipeline`, `DhmgsAssetWriter`, `DhmgsMotionPlayer`, and `DhmgsRigDriver`.
- Moved archive parsing, HTTP download, video overlay, preview, and AnimationClip baking out of the Player assembly.
- Preserved the serialized `DhmgsMotionAsset` layout so previously generated assets remain playable.
- Removed automatic project mutation; validation and HTTP/layer setup are now explicit menu actions.
- Added EditMode tests for archive parsing and motion conversion.
- Removed the unused Input System dependency.

## [0.1.0] - 2026-08-06

### Added

- Added Unity Package Manager metadata for Git URL installation.
- Added explicit UGUI, URP, and required Unity module dependencies.
- Added standard Git binary tracking rules and internal release documentation.
- Separated package code from optional project-specific rigs and sample content.
- Bundled the Female, Male, and Neutral SMPL-X base FBX files.
- Consolidated the package under `Assets/Mnnh.Dhmgs/Package` and local content under `Assets/Mnnh.Dhmgs/Content`.
- Changed the default imported result path to `Assets/Mnnh.Dhmgs/Content/Generated`.
