# Track2Obj: Carmageddon: TDR 2000 (created by me and AI)

A standalone CLI utility for Carmageddon TDR2000 asset management. It allows you to investigate level structures, unpack game archives, and export complete game levels into Wavefront OBJ + MTL format.

## Features
- **Smart Archive Access:** Directly reads assets from .pak archives. Manual unpacking is not required.
- **Auto-Discovery:** Automatically locates the game's ASSETS folder if the tool is near the game files.
- **Recursive Tracing:** Follows chains of descriptor files (TXT -> TXT -> HIE) to find all level components.
- **Level Export (-l):** Exports all layers (geometry, breakables, props, water, sky) in one go with unique filenames.
- **Auto Texture Extraction:** Automatically extracts required .tga files from PAKs to the output folder. Use -nomat to skip this.
- **Safety:** Atomic writes using .tmp files and crash cleanup.

---

## Usage

### Level Export (-l)
Exports the entire map. You can provide a track folder name (like Hollowood) or a path to a specific level txt.

**Syntax:**
TdrExport -l <track_name_or_dir> [assets_root] [-o <out_dir>] [-nomat]

**Examples:**
- TdrExport -l Hollowood (Uses local ASSETS folder, saves to EXPORT/)
- TdrExport -l 1920s "C:\Games\TDR2000\ASSETS" -o C:\MyScene
- TdrExport -l Tracks\PoliceState -nomat (Export geometry only)

### Investigation (-i)
Analyzes level dependencies and checks for missing assets. Traces all descriptors and validates textures against the archives.

**Syntax:**
TdrExport -i <level_name_or_txt> [assets_root]

### Movable Objects (-m)
Exports and positions dynamic props (trees, street lights, cars) using coordinates from a descriptor file.

**Syntax:**
TdrExport -m <descriptor_txt> [assets_root] [-o <out_dir>] [-nomat]

### Archive Unpacker (-u)
Extracts everything from all .pak archives in a folder while keeping the directory structure.

**Syntax:**
TdrExport -u <archives_root> <output_dir>

### Single HIE Export
Converts one .hie file to OBJ.

**Syntax:**
TdrExport <file.hie> [assets_root]

---

## Technical Details
- **Target:** .NET 9.0 (Windows)
- **Output:** Wavefront OBJ, MTL, TGA.
- **Assets Root:** Optional if the tool is near the game folder.
- **Transparency:** Supports map_d in MTL for 32-bit textures.

## Credits
- **Archive Engine:** Custom high-performance implementation by the project owner (Trie-indexing and zIG/RAW decompression).
- **Format Parsers:** Based on logic from the ToxicRagers (https://github.com/MaxxWyndham/ToxicRagers) project, refactored for .NET 9.
