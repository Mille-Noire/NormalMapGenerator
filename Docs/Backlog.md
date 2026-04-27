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

## Custom 3D Preview Shape Import

Status: Todo

Allow users to import their own mesh for the 3D preview so generated normal maps
can be judged on project-specific geometry.

Ideas:
- Support at least one common static mesh format, for example OBJ or glTF.
- Keep built-in preview shapes available as fallback presets.
- Reuse the generated normal map and optional heightmap albedo on imported
  meshes.
- Show a simple error message if the mesh cannot be imported or lacks usable UVs.

Acceptance criteria:
- Imported meshes appear in the `Shape` selection without replacing built-in
  shapes.
- Meshes with valid UVs display the current normal map correctly.
- Failed imports do not clear the currently loaded heightmap or normal map.
- Export behavior remains unchanged and still exports only the normal map PNG.

## Maximizable Preview Panels

Status: Todo

Allow users to temporarily maximize individual preview areas so details can be
inspected without changing the global window layout.

Ideas:
- Add a small maximize button to `Source Heightmap`, `Generated Map`, and
  `3D Preview`.
- Show the selected preview in a larger modal, overlay, or dedicated expanded
  layout state.
- Keep the current image stretch behavior so source and generated maps remain
  undistorted.
- Preserve 3D preview controls and camera state when entering or leaving the
  maximized view.

Acceptance criteria:
- Each preview area can be maximized and restored independently.
- Maximizing one preview does not regenerate normal or displacement maps.
- Export behavior remains unchanged.
- Keyboard escape or a visible close/restore button returns to the normal
  three-column preview layout.

## Additional Normal Map Generation Controls

Status: Todo

Add more source-height shaping controls after the current channel source and
edge-mode options have proven useful in practice.

Ideas:
- Add `Black Point` and `White Point` controls to remap a useful source height
  range before normal generation.
- Add a `Gamma / Curve` control for non-linear height shaping.
- Add `Height Offset` to shift the interpreted height before level/curve
  processing.
- Add `Detail Normal Blend` for mixing in a secondary fine-detail normal map.
- Add `Batch Export` once single-texture settings feel stable.

Acceptance criteria:
- New controls update the preview through the existing async normal-generation
  flow.
- Reset buttons return every new control to its documented default.
- Controls that affect height interpretation also affect future displacement
  workflows consistently.
- Export always matches the currently visible generated normal map.

## Theme Mode

Status: Todo

Add theme handling so the app can be used comfortably in light and dark desktop
environments.

Ideas:
- Add a theme mode setting with `System`, `Light`, and `Dark`.
- Use `System` as the default so Windows decides the app appearance.
- Define shared brushes/resources instead of hard-coded colors in individual
  controls.
- Make the image and 3D preview backgrounds stay useful in both light and dark
  mode.

Acceptance criteria:
- `System` follows the current Windows app theme where available.
- `Light` and `Dark` override the system choice.
- Theme changes update the visible UI without restarting the app.
- Preview contrast, borders, labels, and tooltips remain readable in both
  themes.
