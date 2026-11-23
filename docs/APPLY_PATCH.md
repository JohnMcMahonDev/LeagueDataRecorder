# Apply the final-touches patch

This is a changed-files patch for the attached VideoReviewTiming(4).zip project. Extract it over the existing project root, beside League Data Recorder.csproj. Keep the unchanged files already there, including the solution, Form1.resx, vocabulary JSON, and player HTML/CSS.

1. Close the app and Visual Studio, then copy the patch contents into the project folder.
2. Remove the exact old paths in the root FILES_TO_REMOVE.txt. Most are source files that moved under src/. The project ignores their old root copies, but deleting them finishes the structural cleanup.
3. Reopen the same solution, delete old bin/ and obj/ build output if necessary, and rebuild/publish to a **new, empty output folder**. Reusing an old publish folder can leave old models or tools there even when the new publish rules exclude them.

To remove only the listed obsolete files in PowerShell, run this from the project root after copying the patch:

```powershell
Get-Content -LiteralPath '.\FILES_TO_REMOVE.txt' | ForEach-Object {
    if (Test-Path -LiteralPath $_ -PathType Leaf) {
        Remove-Item -LiteralPath $_
    }
}
```

Recordings, Transcriptions, Analysis, models, local asset downloads, and saved preferences are not on that removal list. The old generated-settings App.config and personal csproj.user file are removed; the app continues using its existing JSON settings. This patch does not migrate a settings file from a substantially older application that never used app-settings.json.

Publish normally, choosing a new output directory:

```powershell
dotnet publish '.\League Data Recorder.csproj' -c Release -r win-x64 --self-contained true -o '.\bin\publish\final-touches'
```

The default publish leaves local models and standalone tool copies out of the release. Missing FFmpeg, diarization/transcription/VAD/emotion components download automatically on first launch. Syncthing downloads when sharing is first used. Downloads can be stopped and resumed. Existing compatible installed assets are reused.

Legacy tools/ffmpeg.exe and tools/ffprobe.exe are no longer the default FFmpeg path. If they are only left over from the previous application setup, they may also be removed. They are deliberately not on the source-file removal list because they were not included in the attached ZIP. Do not add unknown old executables back to a public release; see REDISTRIBUTION.md for the optional bundling mode.

## Changes visible to players

- Sharing is a small control beside the Games list. More → Share game link still opens sharing for a selected game.
- The sharing window separates sending and receiving, uses a readable transfer table, and shows recipient approval controls when relevant. Pairing, link expiry, verification, and automatic import behavior are preserved.
- One Game view selector contains Overview, Transcript, Comms · Players, and Comms · Callouts. The nested Players/Callouts tabs are gone. Clicking a player's count switches directly to that speaker's matching callouts.
- Detailed counting explanations live under How it works, leaving more space for results.
- Settings exposes Downloads and Licenses and credits, including the model restrictions and Riot attribution.

SOURCE_LAYOUT.md describes the directories. VALIDATION.md records what was checked and what still needs a Windows smoke test. REDISTRIBUTION.md explains the reviewed licenses and remaining release-specific requirements.
