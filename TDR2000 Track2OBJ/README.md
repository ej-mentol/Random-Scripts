# Track2Obj: Carmageddon TDR2000 (by me and ai)

A standalone CLI utility for **Carmageddon TDR2000** asset management. It allows you to investigate level structures, unpack game archives, and export complete game levels into **Wavefront OBJ + MTL** format for Blender, 3ds Max, or modern game engines.

## Features
- **Virtual File System (VFS):** Automatically reads assets directly from `.pak` archives using `.dir` indices. No need to manualy unpack the whole game.
- **Smart Level Export (`-l`):** Parses master level descriptors and exports all layers (Static geometry, Breakables, Props, Water, Sky) in one go.
- **Movable Objects Support:** Automatically places thousands of dynamic props (cactuses, trees, debris) in their correct world coordinates.
- **High-Quality Textures:** Automatically selects 32-bit (`_32.tga`) and high-resolution versions of textures.
- **Atomic Writes:** Uses temporary files and cleanups to prevent corrupted outputs if an export is interrupted.
- **Cycle Protection:** Safe recursive parsing of nested descriptors.

---

## Usage

### 1. Full Level Export (`-l`)
This is the main "Smart" mode. 
**Command:** `TdrExport -l <path_to_level.txt> <game_assets_root>`

**What it does:**
1. Parses the main level file (e.g., `hollowood.txt`).
2. Identifies all sub-descriptors (Mesh, Breakables, Props).
3. Recursively finds all `.hie` hierarchy files referenced in the level.
4. Exports every component into separate, uniquely named `.obj` files.
5. Processes `MOVABLE_OBJECTS` to generate a combined `_movables.obj` containing all placed props.
6. Prints Environment Data (Sun vector, Light colors, Fog settings) to the console for your 3D scene setup.

**Example:**
`TdrExport -l "Tracks\Hollowood\Hollowood.txt" "C:\Games\TDR2000\ASSETS"`

---

### 2. Investigation Mode (`-i`)
Use this to "scout" a level before exporting.
**Command:** `TdrExport -i <level.txt> <game_assets_root>`

**What it does:**
- Traces all dependencies (nested TXT and HIE files).
- Checks if every file exists on disk or inside PAK archives.
- **Validates Textures:** Lists every texture used by the level and marks missing ones with `[!]`.
- Prints a structured report of the "puzzle pieces" required for the level.

---

### 3. Unpacker Mode (`-u`)
Extracts all files from game archives.
**Command:** `TdrExport -u <archives_folder> <output_folder>`

---

### 4. Single File Export
Export a specific geometry file.
**Command:** `TdrExport <file.hie> [game_assets_root]`

---

## Technical Details
- **Target:** .NET 9.0 (Windows)
- **Output:** Wavefront OBJ (Geometry), MTL (Materials).
- **Texture Logic:** Automatically flips V-coordinate for OBJ compatibility and adds `map_d` for transparency support in 32-bit TGA files.
- **Conflict Resolution:** OBJ filenames are generated using their relative paths to prevent overwriting files with identical names (like `wall.hie`) from different folders.

## Requirements
To get textures in your 3D software, make sure the `.tga` files are present in the search path or extracted folder. The exporter will link them relatively in the `.mtl` file.

 ### Credits
  - Format Parsers (`.HIE`, `.MSHS`, `.H`): Based on the logic from the ToxicRagers
  (https://github.com/MaxxWyndham/ToxicRagers) project, significantly refactored for .NET 9 and modern 3D workflows.
