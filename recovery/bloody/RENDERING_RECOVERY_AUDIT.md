# Original rendering recovery audit

Source: `Bloody-Bastards-v5.0.3-mod-apkvision.apk`, SHA-256
`c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba`.
The editable source is in `recovery/bloody`; generated imported assets and native
evidence are under `output/bloody-full-port-20261028`. No replacement character,
environment, texture, animation or sound art was generated for this recovery.

## Source recovery

* `export_bloody_visual_data.py` preserves source file/path IDs, renderer component
  indices and hierarchy paths. The current catalog includes 1,112 armor rows,
  three source character graphs, scene components and 8,238 referenced identities.
  An identity without an imported graphical asset can be an original script or
  component; catalog row count is not an asset-completeness test.
* `export_bloody_shader_programs.py` reads Unity 6 `m_PlayerSubPrograms` and their
  executable GLES program blobs. The AssetRipper shaders contained a dummy white
  fragment shader. Extracting the old top-level shader blob alone does not recover
  the game's executable shader variants.
* `recover_bloody_base_shaders.py` translates the original arithmetic, sampler
  access and pass states. World shaders include the original URP 2D shape-light
  variants. TopRim, FlipView, PaintCore replacement and the actual point/multiply
  decal program are included. `Shaders/translation-status.json` records remaining
  variant limitations. GPU SpriteSkin, stereo, DOTS and debug variants have not
  been certified. Compile success does not establish pixel equivalence.
* `export_bloody_native_strings.py` recovers 3,896 obfuscated string getters from
  original `global-metadata.dat` bytes and ARM64 getter offsets. The decrypted
  129,991-byte pool starts at metadata offset 10,258,248. Dummy DLL FieldRVA data
  for this nested field points at generated CIL bytes and must not be used.

## Runtime paths recovered

* CharacterConfigApplier: original skin/hair/beard gradients, face textures,
  body/face blendshapes, gender meshes, physical body proportions and joint
  anchors, armor layers, hair/beard tucking/erasure, hat spring settings and
  source per-part defense/material references. TopL1 tucked sleeves use L2/L3
  `CoversL1Arms`; the independent `TuckTopL1` flag sets
  `_Erases_Bottom_of_L1`. Original eyes/mouth animation remains on the original
  Animator through the expressions port.
* PaintCore: original per-renderer material clones; source RT dimensions,
  filtering/wrapping/mipmap and color-space behavior; source replacement shader;
  point decal projection, source group/layer selection, original random-angle
  modifier, normal-front/back ranges and skinned-mesh scale compensation.
  All blits/readbacks preserve `RenderTexture.active`.
* CwMeshModel uses source `useMesh == 0` for all 154 current catalog models.
  Native seam repair only runs for `useMesh == 1`; it is deliberately not applied
  globally. A general `useMesh == 1` implementation remains unported.
* Ground blood: original hidden paint quad and 2,048-by-1 texture feed the source
  SpriteShape fill readback. DamageDecalPainter uses original cycle count,
  interval and first-seven-cycles big blood behavior.
* Weapon blood: original per-renderer offsets and shader IDs, initial 0.35
  visual amount, source accumulation table and shield divisor/caps.
* Damage presentation: source material/surface classification, hit-tier selection
  at publish time, point/volume quantization, eight-slot byte-cursor presentation
  feed, original particles/decals/audio and original expression playback.
* ImpactSFX: primary/secondary sources, native randomized clip/pitch, native gain,
  armor-dependent bodyfall choices and separate kick branch. Mode-change sounds
  follow source bashing/severing events. An additional generic bodyfall trigger
  has not been invented without a source callsite.
* FootstepsSFX: original continuously looping source clips, native armor-material
  mapping, delayed random phase with its original shared-RNG side effect, and
  volume from original locomotion speed.
* SkirtBonesAnimator: eight original transforms, original left/right thigh ranges,
  flip handling, frame-based 0.75 Slerp and original thigh damage stop conditions.
* Environment: original region/stage graphics, source props, sprites, transforms,
  randomization, SpriteShape profiles and native serialized Light2D settings.
  Scene camera is the original perspective camera. Runtime phase framing uses
  the source cameras, target offsets, lenses, aspect correction and damping.
* TopRim renderer feature: source renderer lists, rendering-layer masks, RG8
  combined mask, optional blur, R8 field and both original composite draws.
  FlipView uses its original shader and source injection point. These helpers
  require `RestoreOriginalRendererFeatures` from the editor rendering partial.
* `OriginalTimeOfDay` restores original region/holiday gradients, local-clock
  initialization, window colors/power, stars, scene lights, rim curves and weather
  light-intensity table. Add it to the original TimeOfDayController scene root.
  Native transition/customization/weather events have public entry points;
  complete weather/holiday mode lifecycle wiring is not yet certified.
* `OriginalWeather` restores the native .25-second initial invocation, frame-count
  Unity RNG reseed, region-specific weather transitions, holiday remapping,
  weather containers, independent seeded cloud positions, precipitation and
  lightning sorting layers, the independent lightning coroutine and light
  intensity event. `OriginalLightningFlash` retains the native first-activation
  .01-second disable and the serialized five-second lifetime. It requires the
  Light2D animation-script binding remap from the original DLL reference to the
  installed URP Light2D source script. These components have compiled; actual
  weather/lightning rendering has not yet been validated.
  Native `_WindSpeed`/`_SnowAmount` global writes are preserved, but no extracted
  GLES program references those names: Vegetation directly contains the source
  constant 0.25. The recovered shader is not changed to invent a global coupling.

## Runtime evidence and unresolved checks

Capture 8 (`integration-capture-8/original-camera-render.png`) first confirms the
source environment, translucent texture cutouts, clothing and weapons in the
explicit URP camera render. Both original Head paint RTs have alpha 0 through 255;
the empty armor RT is fully transparent. The earlier cream polygons came from a
duplicate shader name: Resources/cwreplace.shader still contained a dummy shader.
Paint initialization now binds the recovered shader asset explicitly, and the
preparation pipeline synchronizes source shader aliases while retaining metas.

Capture 8 `rendering-diagnostics.json` resolves and loads all 78 unique ImpactSFX
clips from 30 AudioObjects, and both unique Footsteps clips from four AudioObjects.
This establishes binding and load state; actual audible desktop playback is a
separate check.

The batch-mode main ScreenCapture is black while an explicit
`SingleCameraRequest` renders the world. That difference has not been represented
as successful interactive desktop display. The diagnostic captures world/HUD
separately and restores temporary Canvas modes, camera masks and RT state.

Capture 9 confirms the original HUD in `original-camera-with-hud.png`, and active
TopRim plus local-clock night lighting/stars in `original-camera-render.png`.
Both Head renderers are active, enabled, in the camera layer/frustum and use a
supported shader with original White/Tint values and no vertex colors. Both have
ten clockwise triangles. The source `Head COM` is a force point 0.687 units above
Bone_Head, rather than the visible head center. Player Bone_Head is at y1.408 in
Capture 9; its head projects into the raised hands, which are closer to camera.
These facts support pose occlusion; a direct heads-only camera render is prepared
for the next capture to establish this visually. No source culling or colors were
changed. Original Idle has no Head transform curves. The diagnostic also projects
the native CwMeshModel baked coordinates with inverse lossy-scale compensation,
avoiding double application of the original Smeg z scale of 2.45.

All current Runtime sources compile with the installed Unity 6000.4 engine,
URP and UGUI references through Roslyn. The unified editor/player build and actual
screenshots belong to the parent integration task. A synchronized original APK
versus desktop frame comparison, active FlipView rendering, visible
interactive UI/backbuffer, damage-paint progression and all environmental modes
still require runtime validation. This audit does not claim a complete one-to-one
rendering or gameplay port.
