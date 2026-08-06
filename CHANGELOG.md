# Changelog

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
