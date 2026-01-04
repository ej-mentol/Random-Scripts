# Track2Obj: Carmageddon TDR 2000 Universal Tool

A high-performance standalone CLI utility for **Carmageddon TDR2000** asset management. It allows you to investigate level structures, unpack game archives, and export complete game levels into **Wavefront OBJ + MTL** format.

## Features
- **Virtual File System (VFS):** Transparently reads assets directly from `.pak` archives using `.dir` indices. You can export a level pointing only to the original game's `ASSETS` folder.
- **Recursive Dependency Tracing:** Automatically follows chains of descriptors (TXT -> TXT -> HIE) to find every piece of the level.
- **Smart Level Export (`-l`):** Exports all layers (Static geometry, Breakables, Props, Water, Sky) in one pass with unique naming to avoid file conflicts.
- **Automatic World Positioning:** Processes `MOVABLE_OBJECTS` to place thousands of props (cactuses, cars, etc.) at their exact game coordinates.
- **High-Quality Asset Selection:** Prioritizes 32-bit textures (`_32.tga`) and highest available resolutions.
- **Blender Ready:** Automatic V-axis flipping for UVs and transparency support (`map_d` in MTL).
- **Atomic Operations:** Safe writing using temporary files and crash cleanup.

---

## Usage

### 1. Investigation & Scouting (`-i`)
Scans a level and reports all dependencies, identifying what's found and what's missing.
**Command:** `TdrExport -i <level.txt> <game_assets_root>`
*Note: <level.txt> can be just a filename; the tool will find it inside the PAKs.*

### 2. Full Level Export (`-l`)
The "one-click" mode to get the whole map.
**Command:** `TdrExport -l <level.txt> <game_assets_root>`

### 3. Archive Unpacker (`-u`)
Extracts all files from all PAKs in a directory.
**Command:** `TdrExport -u <archives_folder> <output_folder>`

### 4. Single File Export
**Command:** `TdrExport <file.hie> [game_assets_root]`

---

## Technical Details
- **Target Platform:** .NET 9.0 (Windows)
- **Output Formats:** Wavefront OBJ, MTL.
- **Texture Support:** Links directly to `.tga` files. Supports alpha transparency for windows and foliage.

## Credits
- **Archive Engine (`.PAK` / `.DIR`):** Custom high-performance implementation by the project owner, featuring full Trie-index support and zIG/RAW block decompression.
- **Format Parsers (`.HIE`, `.MSHS`, `.H`):** Based on the logic from the [ToxicRagers](https://github.com/MaxxWyndham/ToxicRagers) project, significantly refactored and modernized.
