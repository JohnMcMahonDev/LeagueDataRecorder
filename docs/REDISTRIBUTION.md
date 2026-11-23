# Redistribution review

Reviewed 23 November 2026 against the attached source, its download catalog, NuGet package metadata/license files, and the upstream license pages linked below. This is a technical license review, not a legal opinion or certification of binaries that were not supplied.

## What is ready in this update

- The app ships third-party notices and full license texts, accessible from Settings → Licenses and credits.
- FFmpeg is obtained automatically on first launch, using an exact upstream LGPL shared build, an 80,356,594-byte archive, and a verified SHA-256 digest. Its shared DLLs and license files are installed together.
- The app invokes the FFmpeg executable as a separate process for audio conversion. Screen recording uses ScreenRecorderLib and Windows Media Foundation. This code does not require libx264, so an LGPL FFmpeg build supports its existing FFmpeg work.
- Local models and standalone tools are not included in `dotnet publish` by default. The downloads still work with a standard publish command.
- The emotion model's non-commercial terms appear during downloads and in the notices. Original objective artwork provenance remains available.

## FFmpeg: what can be redistributed

FFmpeg's license depends on its build. An LGPL build is redistributable when its conditions are met. GPL builds can also be redistributed when their GPL conditions are met. An `--enable-nonfree` build is not redistributable; being free of charge does not change that.

Using a standalone executable over ordinary command-line/file interfaces does not by itself establish that this application's source must use FFmpeg's license. That assessment depends on how programs are combined. This application does not link directly against FFmpeg's libav libraries. FFmpeg and its compiled dependencies still have their own binary redistribution requirements.

The original upload contained no ffmpeg.exe or ffprobe.exe. Their exact versions, configure options, and corresponding sources therefore could not be checked. Do not treat the old executables as cleared for release.

The new default downloads this exact upstream package:

https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/autobuild-2026-10-31-12-59/ffmpeg-n8.1.3-27-g8dca1586f4-win64-lgpl-shared-8.1.zip

SHA-256: `5cef7bc48e513f3cee7c881e8b2999d835541edb1846c3e89443a67f8f8556ab`

The complete archive was fetched and its size/digest checked during this review. Its LICENSE.txt specifies LGPL version 3. The default app release does not contain or mirror that binary archive; each user obtains it from the upstream distributor. This changes who distributes those binary bytes, while preserving notices and the user's applicable license obligations. Automatic downloading is not a universal licensing exemption.

If you opt into bundling tools with `-p:IncludeBundledTools=true`:

1. Inspect **the exact executables being shipped** with `ffmpeg.exe -version`, `ffmpeg.exe -buildconf`, `ffmpeg.exe -L`, and `ffprobe.exe -L`. Keep these results with the release.
2. Include the applicable FFmpeg license(s), copyright notices, and the licenses of all included libraries. An LGPLv3 distribution needs the GPLv3 text incorporated by LGPLv3 as well.
3. Provide the complete corresponding source for the exact FFmpeg build and relevant dependencies, including changes, build instructions, and scripts, using a method the license permits. A generic FFmpeg homepage or current master source is insufficient. FFmpeg's recommended route is hosting matching source alongside the binary download. Preserve a verified durable copy yourself if you depend on an external source host.
4. Keep source availability and any required offer valid for the relevant distribution method. Do not prohibit rights the licenses grant, such as applicable reverse engineering for library modification.
5. Supply a reviewed redistribution record. The project deliberately errors if local FFmpeg binaries would be published without the license/source/build-information files described below.

Bundling tools is an optional developer release mode, not required user setup. Default first-run downloads are the supported simple path.

For a bundled FFmpeg release, place these files under `redistribution/ffmpeg/`:

- `LICENSE.txt`: the license for the exact build.
- `BUILDINFO.txt`: exact versions, build flags, upstream provenance, and source package inventory.
- `SOURCE.txt`: directions to the complete corresponding source you make available with this release. Include exact archive URLs, hashes, build/dependency sources and any offer required by the chosen license route. This file is documentation, not a substitute for providing those sources.

Those files are copied into the publish output. Their existence is a packaging check, not a legal approval.

Primary sources: https://ffmpeg.org/legal.html · https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md · https://github.com/FFmpeg-Builds/FFmpeg-Builds

## Models and sharing engine

| Component | Practical redistribution requirement |
| --- | --- |
| Whisper and Silero weights | MIT permits redistribution; retain the original copyright and permission notices. Preserve provenance for converted weights. |
| NVIDIA diarization weights | OpenMDW 1.1 permits distribution, including commercial use, subject to its terms; retain the agreement and applicable origin notices. |
| NeMo CPU runtime | Preserve Apache-2.0 LICENSE and NOTICE, plus its third-party notices and applicable component licenses. |
| audEERING emotion weights | CC BY-NC-SA 4.0 permits non-commercial use/sharing subject to attribution and other terms. Commercial use needs separate permission. Share-alike concerns adapted model material, not automatic relicensing of the independent app. |
| Syncthing | Preserve MPL-2.0 terms and notices, and make the applicable source available if you redistribute its executable. Unmodified source is at the exact v2.1.7 tag; modifications have additional file-level obligations. |

Bundling models remains an explicit opt-in: `-p:IncludeBundledModels=true`. Include the model licenses and origin notices alongside the weights if using that mode. The app's download path does not expand the rights the model providers grant.

Primary sources: https://github.com/openai/whisper/blob/main/LICENSE · https://github.com/snakers4/silero-vad/blob/master/LICENSE · https://openmdw.ai/license/1-1/ · https://github.com/NVIDIA/NeMo-Speech.cpp/tree/v0.3.0 · https://github.com/audeering/w2v2-how-to · https://github.com/syncthing/syncthing/tree/v2.1.7

## Remaining release-specific checks

- **Application source license:** no LICENSE for your own code was supplied. You can distribute code you own, but choose and add an appropriate license if you want other GitHub users to have explicit permission to reuse it. Third-party notices do not grant rights to your own code.
- **Riot registration and assets:** Riot requires registration/audit for products using its APIs, including the League Client API, and specifies permitted asset sources. The current embedded objective images came from CommunityDragon rather than a supplied Riot approval record. The update preserves their source attribution, but does not claim CommunityDragon access is a redistribution license. Confirm registration/audit and that Riot accepts these asset uses; use approved assets or obtain permission where needed. The required non-endorsement wording is in the in-app notices.
- **Published binaries:** this upload has source only. Check the actual final publish directory for extra EXEs, DLLs, fonts, or model weights copied from a local machine. The reviewed license files cannot certify unidentified additions.
- **CUDA:** the declared Whisper CUDA package metadata says MIT, and the package file inventories contain the native Whisper/ggml wrappers. Separately installed NVIDIA software has separate terms. Do not copy an entire CUDA Toolkit or GPU driver into the release; check the exact redistributable list if you choose to add NVIDIA libraries. Keep the corresponding EULA/notices.
- **Windows dependencies:** distribute only permitted runtime/projection components, retain the relevant SDK/runtime terms, and use Microsoft's permitted installation/distribution route if bundling the WebView2 browser runtime or Visual C++ runtime. An SDK package license is not a license for every Microsoft executable.
- **Recordings:** code and model licenses do not grant permission to publish other people's recorded voices, messages, or gameplay data. This patch contains none of your recordings.

Riot policy: https://support-developer.riotgames.com/hc/en-us/articles/22698591841939-General-Policies

Windows SDK terms: https://aka.ms/WinSDKLicenseURL

CUDA terms and redistribution list: https://docs.nvidia.com/cuda/eula/

The software changes make the default release easier to distribute correctly. A blanket statement that every possible release is legally cleared would be unsupported without the actual binaries and Riot approval information above.
