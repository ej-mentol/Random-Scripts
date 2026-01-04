# Track2Obj: Carmageddon: TDR 2000 (created by me and AI)

Standalone tool for TDR2000 level export and archive management. It reads assets directly from game PAKs and generates OBJ + MTL scenes.

## Features
- **Smart Archive Access:** Directly reads assets from .pak archives. Manual unpacking is not required.
- **Auto-Config Resolution:** Automatically finds track configuration files by name (e.g. "hollowood").
- **Layered Export:** Dumps geometry, props, breakables, and movables into uniquely named OBJ files.
- **Texture Extraction:** Automatically pulls the highest quality .tga files. Use -nomat to skip.
- **Safety:** Atomic writes using .tmp files and crash cleanup.

---

## Usage

### Track Export (-l)
The primary mode to export a full map. Provide the track name and the path to the game's ASSETS folder.
**Command:**
`TdrExport -l <track_name> <assets_path> [-o <out_dir>] [-nomat]`

**Example:**
`TdrExport -l hollowood ASSETS`
*(Files will be saved to the EXPORT subfolder by default)*

### Investigation (-i)
Get a report on track components and check for missing assets.
**Command:**
`TdrExport -i <track_name> <assets_path>`

### Archive Unpacker (-u)
Extracts all files from .pak archives.
**Command:**
`TdrExport -u <source_pak_folder> <target_output_folder>`

---

## Technical Details
- **Output:** OBJ, MTL, TGA (auto-flipped UVs).
- **Target:** .NET 9.0.
- **Credits:** Archive engine by the project owner. Parsers based on [ToxicRagers](https://github.com/MaxxWyndham/ToxicRagers) logic.
