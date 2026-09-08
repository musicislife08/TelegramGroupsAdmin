# Replace SixLabors.ImageSharp with SkiaSharp

Closes #523.

## Problem

`SixLabors.ImageSharp` changed licensing at v4.0.0: the build target hard-fails without a paid
Six Labors license key or `sixlabors.lic` file for any project holding a direct dependency. OSS
projects can apply for a free community license, but it is application-based, not an automatic
opt-out.

We are pinned to 3.1.12 — the last release under the free Split License — behind a version-hold
comment in `Directory.Packages.props:99`. That pin blocks routine dependency bumps and parks us on
a line that will eventually accrue unpatched CVEs. 3.1.12 has none flagged today, which makes this
the cheap moment to move rather than the expensive one.

### The dependency is diffuse

Six production files call ImageSharp directly, plus a seventh reference that is already dead:

| File | Operations |
|---|---|
| `TelegramGroupsAdmin.Core/Services/PhotoHashService.cs` | `Image.LoadAsync<L8>` → `Resize(8,8)` → `ProcessPixelRows` → aHash |
| `TelegramGroupsAdmin.Telegram/Services/ThumbnailService.cs` | load `Rgba32`, `Frames.CloneFrame(0)`, `Resize(Max)`, `SaveAsPngAsync` |
| `TelegramGroupsAdmin.Telegram/Services/TelegramPhotoService.cs:249` | load, `Resize(Crop)` to 64×64, `SaveAsJpegAsync` |
| `TelegramGroupsAdmin.Telegram/Services/Bot/BotMediaService.cs:242` | load, `Resize(Crop)` to 64×64, `SaveAsJpegAsync` |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs:663` | load, `GaussianBlur(sigma)`, `SaveAsJpegAsync` (in place) |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs:792` | load, `Resize(Max)`, `SaveAsJpegAsync(Quality = 85)` |
| `TelegramGroupsAdmin.Telegram/Handlers/ImageProcessingHandler.cs:122` | load, `Resize(Max)`, `SaveAsJpegAsync` |

`TelegramGroupsAdmin/TelegramGroupsAdmin.csproj:51` carries a `SixLabors.ImageSharp`
`PackageReference` with zero `using` directives anywhere in that project — a dead reference to
delete.

The union of operations is small: decode PNG/JPEG/GIF/**WebP** (Telegram stickers are WebP —
`TelegramMediaService.cs:134`), first-frame extraction from animated GIF/APNG, resize in fit and
fill modes, Gaussian blur, encode PNG and JPEG-with-quality, and an 8×8 grayscale downsample with
raw pixel access.

### The real risk is the stored hashes, not the API

`PhotoHashService` computes a 64-bit average hash (aHash). Its output is **persisted in five
places**, and every consumer compares a stored hash against a hash computed *fresh at read time*:

| Store | Nullability | Source file | Recoverable? |
|---|---|---|---|
| `telegram_users.photo_hash` | nullable | `/data/media/user_photos/{id}.jpg` | **Yes** — no cleanup job touches `user_photos/` |
| `linked_channels.photo_hash` | nullable | channel icon | **Yes**, and self-heals on the next `ChatHealthRefreshOrchestrator` pass |
| `ban_celebration_gifs.photo_hash` | nullable | curated GIF on disk | **Yes** |
| `image_training_samples.photo_hash` | **NOT NULL** | `message.MediaLocalPath` | **Sometimes** |
| `video_training_samples.keyframe_hashes` | JSON | source video, frames deleted after hashing | **Sometimes** |

Because Skia and ImageSharp use different resampling kernels — and their JPEG decoders round
differently — **every stored hash changes**. Reproducing 3.1.12's bicubic output byte-for-byte is
not realistically achievable, so byte-identical hashes are off the table and the design question
is what happens to hashes we cannot recompute.

Getting this wrong fails silently. Impersonation detection
(`ImpersonationDetectionService.cs:462`) and known-spam image matching
(`ImageContentCheckV2.cs:111`) would simply stop matching, with no error and no log line.

Two wrinkles make a naive "rehash everything" pass wrong:

**The blur trap.** `ProfileScanService.CensorProfilePhotoAsync` (`:663`) Gaussian-blurs a banned
user's profile photo **in place**, overwriting `user_photos/{id}.jpg`. Rehashing that file does not
recover the old hash — it computes a hash *of the blurred image*, which is worse than useless for
impersonation matching. Those rows must be nulled, not rehashed.

**Training samples can outlive their pixels.** `ImageTrainingSampleDto` and `VideoTrainingSampleDto`
cascade-delete with their message (`AppDbContext.cs:451`, `:464`), and `DataCleanupJob` deletes
message media files in the same pass (`DataCleanupJob.cs:130-145`). But the rows were written
**without ever setting `PhotoPath`** — it is `[Required]` yet never assigned in
`ImageTrainingSamplesRepository.cs:128-139`, so it holds `""`. Recovery has to go through the
message join, and any row whose media file is gone while the message survives is unrecoverable.
This is the known-spam corpus, so losses here are real detection capability.

## Approach

Four decisions, taken together.

### 1. SkiaSharp 4.151.1, behind a seam

**SkiaSharp** (MIT, Microsoft-maintained) covers every operation including WebP decode and Gaussian
blur, has first-class `linux-x64` and `linux-arm64` support — both of which we publish
(`build-and-publish.yml:267`) — and ships an ~8MB native payload per architecture.
`SkiaSharp.NativeAssets.Linux.NoDependencies` is the right variant: we render no text, so we skip
the fontconfig/freetype runtime dependencies.

Magick.NET was considered and rejected: broader format support we do not need, at ~30MB+ of native
payload per RID on a multi-arch image.

Note the version numbering — SkiaSharp's latest stable is **4.151.1**; there is no 3.x to target.
`SKFilterQuality` is `[Obsolete(error: true)]` in this line. The current APIs are:

```csharp
bitmap.Resize(SKImageInfo info, SKSamplingOptions sampling)   // SKCubicResampler.Mitchell for quality
paint.ImageFilter = SKImageFilter.CreateBlur(sigmaX, sigmaY)  // Skia sigma == Gaussian sigma
bitmap.Encode(stream, SKEncodedImageFormat.Jpeg, quality)
```

Rather than swapping ImageSharp calls for SkiaSharp calls in seven places, introduce
`IImageProcessor` in `TelegramGroupsAdmin.Core` exposing only the operations we actually use:

- `ResizeToFitAsync` — aspect-preserving, bounded by a max dimension (replaces `ResizeMode.Max`)
- `ResizeToFillAsync` — square crop-to-fill (replaces `ResizeMode.Crop`)
- `BlurInPlaceAsync` — Gaussian blur at a given sigma, rewritten to the same path
- `ExtractFirstFrameAsync` — collapse an animated GIF/APNG/WebP to a static first frame

The diffuseness of the current dependency is what makes this issue expensive; a seam means the next
library change touches one file. Every interface, implementation, and carrying record gets its own
file.

### 2. The perceptual hash stops depending on any image library

SkiaSharp decodes to a pixel buffer. Everything after that is managed code:

1. Rec.601 luminance per pixel (`0.299R + 0.587G + 0.114B`)
2. **Box average** each of the 64 destination cells over the source pixels that fall inside it
3. Threshold each cell at the mean of all 64
4. Pack to 8 bytes

A box average over the whole image washes out per-decoder rounding noise, so the hash becomes
near-immune to future library or codec changes. Measured on the probe, PNG, JPEG(85) and WebP(90)
re-encodes of the same source produce **byte-identical** hashes. We pay the one-time rehash now;
this is the last time we pay it.

The existing bit packing is preserved exactly — `hash[i / 8] |= (byte)(1 << (i % 8))` is correct
standard packing. It is written confusingly, reusing `HashingConstants.PhotoHashByteCount` for both
the byte-index divisor and the bit-index modulus because both happen to be 8. Add a distinct
`BitsPerByte` constant and use each in its own role. The wire format does not change: still 8
bytes, still compared by Hamming distance.

A **golden-hash test** pins a checked-in fixture image to its exact expected bytes, so the v2
algorithm can never drift silently the way v1 is about to.

### 3. Unrecoverable hashes become NULL

`NULL` already means "cannot compare" on the *computing* side — `ComputePhotoHashAsync` returns
`byte[]?`, and `ComparePhotoHashes` (`ImpersonationDetectionService.cs:544`) and
`ImageContentCheckV2.cs:96` both guard the freshly-computed hash before use. So nulling degrades
detection to **misses only, never false positives**.

The *stored* side is not guarded: `ImageContentCheckV2.cs:109` passes `sampleHash` straight into
`CompareHashes`, which throws on a null or wrong-length array. That is precisely why the
null-filtering in `GetRecentSamplesAsync` below is load-bearing rather than cosmetic.

That error direction is the correct one here: confirming spam deletes messages permanently, so a
missed match costs a spam message getting through, while a false match costs an irreversible
deletion. Nulling is also strictly better than leaving stale hashes in place, which produces the
same misses but with no way to tell a stale row from a current one.

Nulling rather than deleting preserves each training sample's `IsSpam` label and its
`width`/`height`/`file_size_bytes` metadata features, which feed the model beyond hash matching.

### 4. One-shot rehash at startup

**Rehashing is naturally idempotent** — recomputing an already-rehashed row yields the identical
value. That removes the usual argument for a `photo_hash_version` column (knowing which rows are
done, resumability): the job can simply be re-run wholesale.

It runs once per deployment, gated by a marker in the `configs` table (`chat_id=0` JSONB, the
existing convention), inserted at the point in `Program.cs` after `RunDatabaseMigrationsAsync()`
(`:198`) and after the `--migrate-only` early-exit (`:208-212`) — before ML training and before the
Telegram bot begins processing. Running it there rather than as a scheduled Quartz job closes the
window in which old and new hashes coexist and silently fail to match.

Per row: **file present → recompute; file missing → `NULL`; user banned → `NULL`** (the blur trap).

Per row: **file truncated → `NULL`** as well. Skia does not reject a truncated image the way a
missing file fails: the header is valid, so it commits to a full-size canvas and leaves the rows it
never received as solid black — and `SKBitmap.Decode` explicitly treats `SKCodecResult.IncompleteInput`
as success. Hashing that yields an identity derived from the failure, and because every truncated
image tends toward the same near-black grid, two unrelated partial downloads compare as a match.
That is a *false positive* on the irreversible path, so `ReadLuminanceGrid` decodes through
`SKCodec` and refuses anything short of `SKCodecResult.Success`. The thumbnail operations
deliberately take the opposite trade — a partly-black preview is cosmetic, and refusing it would
drop the thumbnail entirely.

Video keyframes need FFmpeg re-extraction at the same percent positions. `ExtractKeyframesAsync` is
deterministic given the same source and positions, so this works — it is simply the slowest part of
the job.

## Migrations

One migration: `image_training_samples.photo_hash` becomes nullable. Per the project rule, the
model and `AppDbContext` change first, then `dotnet ef migrations add`.

`ImageTrainingSamplesRepository.GetRecentSamplesAsync` (`:34-50`) then filters nulls out of the
query so `ImageContentCheckV2` never receives one. Its return type is
`List<(byte[] PhotoHash, bool IsSpam)>` — a tuple, which the house rule forbids; replace it with a
named carrying record while we are in the file.

## Testing

`ThumbnailServiceTests`, `PhotoHashServiceTests`, and `BotMediaServiceTests` currently **generate
their fixtures** with ImageSharp, so they must be rewritten against SkiaSharp for the package to
leave `TelegramGroupsAdmin.UnitTests.csproj` at all.

New coverage:

- **Golden hash** — checked-in fixture → exact expected bytes.
- **Hash stability** — the same image re-encoded as PNG, JPEG and WebP produces byte-identical
  hashes (measured 0-bit drift on the probe), proving the box average absorbs decoder differences.
- **Rehash job, three branches** — file present yields a new hash; file missing yields `NULL`;
  banned user yields `NULL` without reading the blurred file.
- **Format coverage** — PNG, JPEG, animated GIF, and WebP each decode and thumbnail correctly.

## Deployment on the chiseled runtime — verified, not assumed

The runtime image is `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`
(`TelegramGroupsAdmin/Dockerfile:173`). ImageSharp is pure managed, so SkiaSharp would be the first
native library a managed code path depends on — normally the largest risk in this change.

**It was tested rather than reasoned about.** A throwaway probe (a console app referencing
SkiaSharp 4.151.1 + `NativeAssets.Linux.NoDependencies`, published into a container replicating the
production `tesseract-env` and `final` stages) exercised every operation the design needs. All
checks passed on `linux/amd64`:

```
PASS  native libSkiaSharp loads              SkiaSharp 4.151.0.0
PASS  encode Png / Jpeg / Webp               7,304 / 5,311 / 3,800 bytes
PASS  decode Png / Jpeg / Webp               240x160 Bgra8888
PASS  ResizeToFit                            240x160 -> 100x67
PASS  ResizeToFill                           240x160 -> 64x64
PASS  Gaussian blur                          sigma 26.7, contrast 7.4 -> 2.2
PASS  animated GIF decodes to first frame    2 frames, first frame pixel #ffff0000
PASS  managed box-average aHash              FF49499292242400 (Png +0, Jpeg +0, Webp +0)
```

Three results worth carrying into implementation:

**No Dockerfile change is needed, and there is no hidden coupling.** The probe passes on *bare*
`chiseled-extra` with the Tesseract `COPY --from=tesseract-env` lines removed entirely. The
flattened `/usr/lib` payload (`Dockerfile:200`) is not load-bearing for SkiaSharp, so this design
does not quietly depend on the OCR stage continuing to exist.

**arm64 is verified by execution, not inference.** The full probe suite was re-run under
`qemu-aarch64` on a `linux/arm64` build of the chiseled runtime: all twelve checks pass, including
decode/encode of PNG, JPEG and WebP, both resize modes, Gaussian blur, and animated-GIF
first-frame handling. The production image built for `linux/arm64` carries
`/app/libSkiaSharp.so` at 11,596,392 bytes, `Machine: AArch64`, declaring only glibc core plus
`libstdc++.so.6`.

**The hash is architecture-independent.** The same fixture hashes to `FF49499292242400` on both
amd64 and arm64 — byte-identical. This matters beyond this change: stored hashes stay comparable
across a hardware migration, and a mixed-architecture deployment cannot silently split the corpus
into two incomparable generations.

The remaining caveat is narrow: this is qemu emulation of AArch64, not physical arm64 hardware.
Emulation executes real AArch64 instructions, so the result is strong, but a confirming run on the
real host after deployment costs nothing.

**The box-average hash is more decoder-stable than predicted.** The design argued the box average
would absorb per-decoder rounding to within a couple of bits. Measured drift across PNG, JPEG(85),
and WebP(90) re-encodes of the same source is **0 bits** — byte-identical hashes. The stability
claim in §2 is stronger than stated.

The probe's assertions are not thrown away: the format-coverage, animated-GIF-first-frame, and
hash-stability checks become real NUnit tests in the Testing section below.

## Bundled dependency bumps

Shipped in the same PR:

| Package | From | To |
|---|---|---|
| AngleSharp | 1.7.1 | 1.7.2 |
| MudBlazor | 9.8.0 | 9.9.0 |
| Telegram.Bot | 22.10.2.1 | 22.10.3 |
| Quartz (+ AspNetCore, Extensions.DependencyInjection, Extensions.Hosting, Serialization.SystemTextJson) | 3.19.1 | 3.20.0 |
| NUnit3TestAdapter | 6.2.0 | 6.3.0 |
| Anthropic | 12.42.0 | 12.44.0 |

**`OpenAI` stays at 2.12.0.** `dotnet list package --outdated` offers 2.13.0, but
`Directory.Packages.props:49-51` documents an existing hold: `Microsoft.Extensions.AI.OpenAI`
10.9.0 constrains OpenAI to `[2.12.0, 2.13.0)` and taking 2.13.0 trips NU1608 on restore.
`Microsoft.Extensions.AI` is already at its latest, so the constraint cannot be widened yet and the
hold stands. Do not "fix" this during the bump.

`OpenTelemetry.Exporter.Prometheus.AspNetCore` stays at `1.18.0-beta.1`. `dotnet list package
--outdated` reports it as "not found at the sources" because it is a prerelease; this is expected,
not an oversight.

MudBlazor 9.8.0 → 9.9.0 is the only bump with UI surface. Check the v9 breaking-change notes in
context-keep before assuming it is mechanical.

Also removed: the `SixLabors.ImageSharp` `PackageVersion` and its version-hold comment
(`Directory.Packages.props:98-103`), the four `PackageReference` entries (Core, Telegram,
TelegramGroupsAdmin, UnitTests), and the ImageSharp entry in `THIRD-PARTY-LICENSES.md` — replaced
with SkiaSharp (MIT) and Skia itself (BSD-3-Clause).

## Acceptance criteria

- [ ] No `SixLabors.*` package reference or `using` remains anywhere in the solution, tests included.
- [x] `libSkiaSharp.so` loads and operates in the `linux/amd64` chiseled image (probe, verified).
- [x] Same, verified on `linux/arm64` under qemu emulation; hash output byte-identical across
      both architectures. A confirming run on physical arm64 hardware post-deploy remains cheap
      and worthwhile.
- [ ] Thumbnail, profile-photo, and media handling preserve current behavior across PNG, JPEG,
      animated GIF, and WebP.
- [ ] `PhotoHashService` computes its 8×8 downsample in managed code; a golden test pins the output.
- [ ] The rehash job recomputes every recoverable hash and nulls the rest, including banned users'
      blur-censored photos.
- [ ] `image_training_samples.photo_hash` is nullable and null rows are excluded from sample queries.
- [ ] All bundled package bumps applied.
- [ ] Solution builds with 0 warnings and all tests pass.

## Out of scope

- Changing the aHash algorithm itself (still 64-bit average hash, still Hamming-compared).
- Backfilling `ImageTrainingSampleDto.PhotoPath`, which is `[Required]` but never written. It is a
  real bug and worth its own issue, but fixing it does not recover already-deleted pixels and so
  does not change this design.
- Upgrading `OpenTelemetry.Exporter.Prometheus.AspNetCore` off its prerelease pin.
