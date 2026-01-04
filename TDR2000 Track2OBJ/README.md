# Track2Obj: Carmageddon: TDR 2000 (created by me and AI)

A standalone CLI utility for **Carmageddon TDR2000** asset management. It allows you to investigate level structures, unpack game archives, and export complete game levels into **Wavefront OBJ + MTL** format for Blender, 3ds Max, or modern game engines.

## Features
- **Smart Archive Access:** Directly reads assets from `.pak` archives. You don't need to unpack the game to export levels.
- **Recursive Tracing:** Automatically follows chains of descriptor files (TXT -> TXT -> HIE) to find all level components.
- **Level Export (`-l`):** Exports all layers (geometry, breakables, props, water, sky) in one go with unique filenames.
- **Auto Positioning:** Places props like cactuses or cars at their exact game coordinates.
- **Asset Selection:** Automatically chooses 32-bit textures (`_32.tga`) and highest available resolutions.
- **Blender Ready:** Flipped V-axis for UVs and transparency support (`map_d` in MTL).
- **Safety:** Atomic writes using `.tmp` files and crash cleanup.

---

## Before You Start
- **Permissions:** Run the tool from a folder where you have write access (like `Downloads` or `Documents`). Avoid using the root of `C:\` for output unless running as Administrator.
- **Assets Root:** The `<assets_root>` argument should point to the game's `ASSETS` folder where `.pak` and `.dir` files are located.
- **Running:** Open a terminal (PowerShell or CMD) in the folder where `TdrExport.exe` is located.

---

## Usage

### Level Export (-l)
Exports the entire map. You can provide a track folder name (like `Hollowood` or `Tracks\Hollowood`), and the tool will automatically locate the necessary `.txt` config within the archives.

**Syntax:**
`TdrExport -l <track_name_or_dir> <assets_root> [-o <out_dir>]`

**Flags:**
- `-o <dir>`: Optional. Specifies the output directory for OBJ and MTL files. If not provided, an `EXPORT` folder is created in the current directory.

**Examples:**
- `TdrExport -l Tracks\Hollowood ASSETS`
- `TdrExport -l 1920s ASSETS -o C:\MyScene`

### Investigation (-i)
Scouts a level and reports all dependencies. Use this to check for missing files or textures before exporting.

**Syntax:**
`TdrExport -i <level_name_or_txt> <assets_root>`

**Example:**
- `TdrExport -i hollowood.txt ASSETS`

### Movable Objects (-m)
Exports only the dynamic props (trees, street lights, etc.) using their world coordinates from a descriptor file.

**Syntax:**
`TdrExport -m <descriptor_txt> <assets_root> [-o <out_dir>]`

**Example:**
- `TdrExport -m hollowood_moveabledescriptor.txt ASSETS -o C:\Props`

### Archive Unpacker (-u)
Extracts all files from every `.pak` archive in a folder to a target directory.

**Syntax:**
`TdrExport -u <archives_root> <output_dir>`

### Single HIE Export
Converts one `.hie` file to OBJ.

**Syntax:**
`TdrExport <file.hie> [assets_root]`

---

## Technical Details
- **Target:** .NET 9.0 (Windows)
- **Output:** Wavefront OBJ, MTL.
- **Textures:** Links to `.tga` files. Supports transparency.
- **Assets Root:** Must point to the folder containing game's `.pak` and `.dir` files.

## Credits
- **Archive Engine:** Custom high-performance implementation by the project owner (Trie-indexing and zIG/RAW decompression).
- **Format Parsers:** Based on logic from the [ToxicRagers](https://github.com/MaxxWyndham/ToxicRagers) project, refactored for .NET 9.
