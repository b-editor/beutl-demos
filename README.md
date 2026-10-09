# Beutl demos

Reproducible landing-page recordings of the real Beutl editor. The hero scenario
creates **Create. Animate. Make it yours.** through Beutl's production MCP tools,
then refines it with native Headless mouse and keyboard events. It does not
capture or change an installed desktop Beutl session.

## Requirements

- macOS for the included macOS window presentation; a working Vulkan/MoltenVK device.
- .NET 10 SDK (the pinned Beutl checkout supplies `global.json`).
- Python 3.10+, Git, FFmpeg and FFprobe on `PATH`.
- FFmpeg encoders `libx264` and `libvpx-vp9` for MP4 and WebM respectively.

The Inter 28pt Black typeface is bundled and registered only in the capture process.
No system font installation, desktop automation permission, MCP token or API key is required.
The first run downloads the pinned Beutl source and submodules and restores its
.NET dependencies.

## Preview and record

```bash
python3 scripts/demo.py check
python3 scripts/demo.py preview
python3 scripts/demo.py record
```

`preview` replays the complete workflow at 2× DPI, saves full-resolution checkpoints
and four composed 1080p stills, and never starts a video encoder. Review these before
running `record`. Both use `config/camera.json` and the same compositor.

`record` creates a unique directory under `captures/` containing:

- `editor-demo-camera.mp4`: the final 1920×1080, 30 fps, approximately two-minute LP video.
- `editor-demo.mp4`: the 3840×2160 source UI capture, retained for camera adjustments.
- `Hero Editorial Wipe.bep` and its scene files: the editable result.
- `hero-final.mp4`: the nine-second finished composition.
- PNG checkpoints, camera settings, pointer/timing metadata and `workflow.json`.
- `composition-verification.json`: matching hashes after comparing all 270 rendered
  composition frames with the original MCP composition. A mismatch fails the run.

The scenario uses English UI, Enter to commit fields, property-label dragging,
effects inside a FilterEffectGroup, generated element colors, and the graph tree's
keyframe button. Create's curve is shaped in **Symmetry** mode: dragging the
midpoint handle also moves its opposite handle, without Alt. Every drag frame
checks that coupling. Easing is edited by hand rather than dropped from a library.
The graph remains fully visible during curve editing. The dark wallpaper, native
title row and editor move together under the restrained camera; no operation
captions are drawn. The UI is sampled at 2× DPI before the final 1080p composition.

Useful options:

```bash
# Fetch from a local clone instead of downloading Beutl from GitHub.
python3 scripts/demo.py prepare --source /path/to/beutl

# Reuse an already-built checkout when only changing camera settings.
python3 scripts/demo.py camera --no-build --capture captures/hero-...

# Independently compare a saved project with the original MCP composition.
python3 scripts/demo.py verify --no-build --capture captures/hero-...

# Use another configuration/output location.
python3 scripts/demo.py preview --settings /path/to/camera.json --output /path/to/captures
```

Use `--no-build` only after building the current sources. `BEUTL_DEMO_FFMPEG` can
point at another FFmpeg executable. Camera passes write uniquely named outputs;
existing captures are preserved.

## Update beutl-web

```bash
python3 scripts/demo.py web-assets \
  --video captures/hero-.../editor-demo-camera.mp4 \
  --web /path/to/beutl-web
```

This prepares the H.264 MP4, VP9 WebM and a PNG of the video's first frame, checks
duration and asset size, and decodes both videos before replacing
`apps/web/public/img/showcase.{mp4,webm}` and `showcase-poster.png`.
The landing page's media dimensions must be 1920×1080. The helper does not commit,
push or deploy the website; review those three assets together.

## Updating the workflow or Beutl UI

| File | Responsibility |
| --- | --- |
| `src/DemoWorkflow.cs` | Editing sequence, adjustment targets and pauses |
| `src/DemoEditor.cs` | Resolve real controls and their current bounds; simulate input |
| `assets/hero-editorial.json` | Original MCP composition and render-verification reference |
| `config/camera.json` | Render DPI, camera timing, zoom and presentation assets |
| `src/DemoFrameComposer.cs` | Shared still/video presentation |
| `beutl.lock.json` | Exact tested Beutl source revision |
| `patches/` | Capture initialization and graph-framing fixes for that revision |

`prepare` creates a generated checkout under `.cache/`, applies the reviewed
patches, and copies this repository's scenario into Beutl's existing Headless test
project. It never patches your source clone. The capture initialization patch
sets culture before the UI/library load and embeds the scene and font. The graph
patch fixes fitting a larger vertical range and permits fitting subsecond curves;
it also carries their regression tests.

To try an updated UI, pass a full commit SHA with `--revision`. Each revision gets
its own checkout. Update `beutl.lock.json` after checking the new render. If a patch
no longer applies, review it against that revision; remove fixes that have landed
upstream. Do not edit `.cache/` as the source of truth. The input adapter uses
current visual-tree bounds rather than recorded desktop coordinates.

## Assets and license

Code is under the MIT license, preserving Beutl's copyright notice. Inter is
distributed under the [SIL Open Font License](assets/fonts/OFL.txt); its authors
and upstream are [the Inter project](https://github.com/rsms/inter). The bundled
static `Inter_28pt-Black.ttf` is the typeface used by the reference composition.
The wallpaper was edited with the built-in image_gen tool; its exact prompt is
in `assets/wallpaper-generation.json`. The title-bar image is a seed asset;
recording refreshes it from Beutl's current production title row. Native traffic
lights are composited because AppKit chrome is not in the Headless framebuffer.

Generated captures, build caches and machine-local configuration are not tracked.
