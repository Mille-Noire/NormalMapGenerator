# NormalMapGenerator Backlog

## 3D Preview Lighting Controls

Status: Todo

Add user-facing controls for the 3D preview lighting setup so materials can be
judged under different preview conditions.

Ideas:
- Add a `Preview Light Intensity` slider for the overall 3D preview light rig.
- Add lighting presets, for example `Studio`, `Neutral`, and `High Contrast`.
- Keep the current warm key light plus cool fill-light setup as the default
  `Studio` preset.
- Preserve enough contrast so normal-map detail remains readable.

Acceptance criteria:
- Changing light intensity updates the 3D preview without regenerating the
  normal map.
- Preset changes update only the 3D preview scene lighting.
- The cube preview keeps visible detail on front, side, back, and lower faces.
- The plane preview remains useful for judging subtle normal-map detail.
