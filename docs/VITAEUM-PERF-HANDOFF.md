# Handoff: AC-HD texture pack performance in Vitaeum

From the ac-texture-upscale session, 2026-10-15. Vitaeum paths are relative to `/home/seth/projects/Vitaeum`
(main at 86b74237). Items marked *verified* were read directly; the rest come from a read-only code survey
and should be confirmed before acting on them.

## What the pack changes (measured from the dats)

Retail `~/Games/Vitaeum/dats` vs `~/Games/Vitaeum/AC-HD-Textures-2x-v1`, portal only:

- 7,206 RenderSurfaces changed (2x), adding 759 MiB of source data. HD total is 1,093 M texels.
- If all were resident as RGBA8 with mips, that is about 5.4 GiB. Players never have all of them loaded at
  once, but this is the scale of the problem.
- By texels: DXT1 40%, INDEX16 33%, R8G8B8->DXT1 15%, other formats 12%.
- The 570 textures that were 512 px or larger in retail (now 1024+) account for 46% of all texels.
- Nothing exceeds 2048 after 2x, so the 16 MiB decode cap (`render-codec/src/ui_assets/raster.rs:16`) is
  never hit.
- Terrain: `Region.TexMerge.BaseTexSize` is 2048 (retail 1024). The 32 ground textures are uncompressed
  A8R8G8B8.

## Findings, ranked by likely frame cost

1. **DXT is decompressed to RGBA8 on the CPU for every model texture** (*verified*:
   `render-codec/src/texture_asset/direct.rs:132` -> `rgba_face` at `:195`). With 2x textures this costs
   4-8x the VRAM and bandwidth of keeping them BC1/BC3. Native BC upload already exists for authored
   RenderTextures (`texture_asset.rs:571-578`, `render-wgpu/src/texture_upload.rs:277-292`). Extending it to
   direct DXT surfaces, with BC mips built on the GPU or by block-averaging, is probably the biggest single
   win. It also means the pack's 964 textures converted to DXT start saving VRAM, not just disk.
2. **Terrain composite VRAM has no budget.** Composite size is `BaseTexSize >> land_scale`
   (`smartbox_viewport/outdoor.rs:610-621`): about 21 MiB per recipe at High/VeryHigh with the pack, with
   118-184 recipes per 5x5 area (`render/port.md:412`). Options: a VRAM budget with LRU eviction,
   BC1-compressing finished composites on the GPU, or capping composite size by distance or LOD.
3. **Main-thread publication has no byte or time cap.** Only TexMerge composites are budgeted (4 per frame,
   `terrain_composite_budget.rs:6`). Also on the main thread:
   - Stand-ins SHA-256 hash a whole 4 MiB HD ground texture (`terrain_stand_in.rs:203-211`).
   - The CPU composite fallback runs on the render thread (`texmerge_gpu_runtime.rs:168`, 156-185 ms at
     2048²).
4. **Few preparation workers** (*verified*): cores/4, between 1 and 4 (`client/src/app/execution.rs:33-41`).
   A 4-7 core machine gets 1 worker. Something like `cores - 2` would let decode keep up with 4x the texels.
5. **Repeated CPU work per load:**
   - Every mip level is SHA-256 hashed (`texture_asset.rs:381-399`). A cheaper key, such as source identity
     plus xxh3, would do.
   - The default Environment Texture Detail (Low) decodes at full size and then reloads trimmed
     (`texture_mip_residency.rs:326-342`). Decoding straight to the target base mip avoids that.
   - TexMerge re-decodes the same ground sources for every recipe (`render-codec/src/terrain.rs:484-512`).
6. **Palette-variant cache churn.** The `TextureDecodeCache` key includes the full palette array, with a
   512 KiB key cap (`texture_decode_cache.rs:11-12`). That is about 64 variants of a 2048-colour palette.

## Worth confirming

**Update 2026-10-15:** the highres switch below is done in Vitaeum (PR #77, `b1101608`): at Very High Environment
Texture Detail with `client_highres.dat` present it draws entry [0] from highres, otherwise entry [1], and changing the
setting applies at once.

- `client_highres.dat` is never read for textures: 0x06/0x08 DIDs always route to Portal (*verified*:
  `resources/src/source_classification.rs:40`).
- The pack's highres dat holds 2,294 RenderSurfaces, none of which share an id with a portal entry.
- SurfaceTexture resolution picks the *last* non-null RenderSurface (`ui_assets/resolve.rs:69-83`). The
  2,284 two-entry SurfaceTextures are `[highres id, portal id]`, so Vitaeum always shows the low-detail portal
  version.
- **Retail rule, from the decomp** (`retail_client_source/ImgTex.cpp:972` `GetSurfaceDID`,
  `Render.cpp:1126` `ShouldDropHighDetail`):
  - 1 entry: use it.
  - 2 entries: use entry [0] if the highres dat is present **and** Environment Texture Detail is at max (0).
    Otherwise use entry [1].
  - More than 2 entries is a data error.

  - The presence test is the `HiFi` (0x69466948) dat tag via the AsyncCache, i.e. whether the highres dat is
    mounted. It isn't a check that a particular entry exists.
  - **Terrain goes through the same gate.** TexMerge loads its textures via `ImgTex::Get` and
    `GetSurfaceDID`, so the **Environment** setting (not Landscape) picks between the 8 terrain blend masks
    that have highres versions (SurfaceTextures `0x05001371`, `0x0500143E`-`0x05001441` and `0x0500168C`-`0x0500168E`;
    highres `0x060073A7`-`0x060073AE`). In the pack, both
    copies are 1024² LSCAPE_ALPHA, so either choice is safe. No ground texture has a highres version.
  - Measured on the v1 pack: Very High is never smaller than High for any pair, and 11 pairs are the same
    size. One highres texture (`0x0600628F`, DXT1) replaces a palettized portal texture (`0x060045B4`) in 2
    SurfaceTextures, so palette swaps don't apply to it at Very High. It's the placeholder model for invisible
    generators, so this is harmless.

  Vitaeum should match this rule. The pack side plans to use this gate as its quality tier (HD in highres
  [0], cheaper texture in portal [1]), so the Environment Texture Detail setting becomes the HD on/off
  switch in both clients. Doing that means mounting highres and routing entry [0] lookups to it.
- Live frame time with the pack has never been measured, per the port notes. Before/after captures with
  `VITAEUM4_FRAME_TIMING`, `VITAEUM4_GPU_TIMING`, the hitch recorder and the dev-harness stage split, at the
  starter dungeon, Town Network and outdoors on Medium and High landscape detail, would rank the items above.

## What the pack side is doing

The ac-texture-upscale session is working on dat-level levers: a texel budget (keep low-value textures at
retail size), possibly moving palettized textures that are never recolored to DXT1, and a lighter pack tier.
Any change to texture sizes or formats will show up in Vitaeum without code changes.
