# Validation

The following checks were performed on the delivered source:

- Compiled all 66 intended C# source files using the .NET 9 Roslyn compiler, .NET 9 and Windows desktop reference assemblies, and the declared managed dependency versions. Result: **zero compiler errors**. The manual compiler route reported five framework-reference unification warnings (CS1701) for compatible older-targeted dependencies. This was a source compilation check, not a complete SDK restore/publish.
- Queried MSBuild's evaluated project items to verify the exact compile/resource set and default tool/model publish exclusions, including a simulated obsolete root source/resource file. Checked the optional bundled-FFmpeg packaging target with and without its required documentation files.
- Ran a semantic source audit after cleanup: no redundant imports and no unreferenced private methods remained.
- Parsed the project/resource XML, checked both player JavaScript files with node --check, and checked asset IDs, relative paths, digest formats, and archive anchor paths.
- Downloaded the exact selected FFmpeg archive and verified its SHA-256 digest and byte count. Read its LGPLv3 license, obtained the upstream license/notice files for the other reviewed dependencies, and inspected native-runtime package file inventories and metadata.
- Executed **18 platform-independent checks** using the actual SharePackage.cs and AssetDownloads.cs code. They covered unsafe Windows filenames, normal filenames, share links describing games over 1 GB, matching pairing codes, expired/malformed links, transcript sidecars, executable exclusion, verified import, idempotent import, corrupted files, failed-import cleanup, altered manifests, FFmpeg DLL layout, license-file preservation, extraction cleanup, and ZIP path traversal rejection. The over-1-GB check validates the link metadata limit; it is not a timed transfer of a 1-GB recording.

The portable checks are included in tests/CoreTests.csproj. Run them with:

```powershell
dotnet run --project '.\tests\CoreTests.csproj'
```

A Windows desktop session was not available, so the actual WinForms layout, Windows audio/video capture, model inference, and a live two-computer Syncthing transfer have **not** been executed here. A complete self-contained publish was also not run. Before replacing a working install, smoke-test the view selector/count shortcuts, sharing Send/Receive/approval flows, notices viewer, first-run FFmpeg conversion, and normal recording/review on Windows. Test the sharing window at the display scaling you use.
