# Replacement art

Images packed into the dats as-is, replacing retail interface art.

| File | Replaces | Notes |
|---|---|---|
| `06007576-character-select.jpg` | `0x06007576`, character select background (portal) | Repainted from the retail background, cropped to 4:3 inside its frame and darkened (gamma 1.4, gain 0.9); 800x600 baseline JPEG, 4:4:4, quality 95 |

UI images must keep the retail pixel size. Clients draw them at that size, so a larger image changes the screen
layout instead of sharpening it. A raw JPEG surface (format 500) stores width and height as 0, so only the bytes
change. See [docs/RELEASE.md](../docs/RELEASE.md) for the build command.
