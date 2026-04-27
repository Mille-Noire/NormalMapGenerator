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
