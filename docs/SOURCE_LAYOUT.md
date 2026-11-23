# Source layout

Open the existing League Data Recorder.sln as before. Namespaces, recorded-data paths, model paths, and saved settings formats are unchanged.

| Location | Responsibility |
| --- | --- |
| Form1.cs, Form1.Designer.cs, Form1.resx, Program.cs | Application entry, main form, and designer-owned layout |
| src/UI/ | Main-form features and dialogs |
| src/UI/Controls/ | Theme, reusable controls, and loading display |
| src/Assets/ | Preferences and first-run component downloads |
| src/Sharing/ | Share links, portable game snapshots, Syncthing, and sharing window |
| src/Recording/ | Audio/video capture, timing, trimming, and recording file operations |
| src/Transcription/ | Speech pipeline, role assignment, vocabulary, cleanup, and acceleration |
| src/Analysis/ | Comms findings, speech intervals, emotion/Outlook, aggregation, and export |
| src/League/ | League client access, captured session data, and match timeline storage |
| src/Video/ | Review data, media delivery, timing, and synchronized replay audio |
| VideoReview/ | Embedded HTML/CSS/JavaScript player and artwork provenance |
| Notices/ | Third-party license texts and origin notices |
| docs/ | Migration, validation, and redistribution information |
| tests/ | Portable package/import and tool-installation checks |

The project compiles the main form/entry files and src/**/*.cs explicitly. Removed root-level copies are ignored while applying this patch, so extraction does not introduce duplicate types. Remove the paths in FILES_TO_REMOVE.txt to finish the cleanup.

The obsolete replay-bubble implementation and empty Inference class are deleted. Replay audio remains. Emotion options and speech-window helpers are consolidated into CommsEmotion.cs. The old unused speaking-share popup/calculator and an unreferenced detection helper are removed. Shared theme controls, the role editor, and recording file operations have been taken out of Form1.History.cs. The unused legacy App.config and reflection lookup for a settings class absent from the project are removed; current JSON settings remain. Redundant imports and comments describing earlier update delivery were removed; comments explaining timing, heuristics, and compatibility behavior remain.
