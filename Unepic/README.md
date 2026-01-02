# Unepic Toolkit

A portable, browser-based tool for exploring and modifying Unepic game files. This toolkit is designed for personal use and quick data analysis without the need for complex environments.

## Features

### Sprite Viewer
Allows loading `.idb` files to extract and view sprite atlases.
- **BGR Fix**: Automatic color correction for sprite textures.
- **Multi-selection**: Use `Ctrl` + `Click` to select multiple sprites and export them as a **ZIP archive**.
- **Export**: Individual sprite export to PNG.
- **Visual Aids**: Pixel grid and checkerboard background for transparency.

### Map Unlocker
A utility to patch map files (`.map`, `.dat`).
- **Safe Mode**: Basic unlock (Flag 0) and checksum recalculation.
- **Advanced Mode**: Version 38 fix and Hybrid ID injection (3e91) for multiplayer visibility.
- **Experimental (Risky)**: Internal name injection and `DECT`/`CDLG` tag repair. Note: These may interfere with external localization.

## Technical Details
- **Decryption**: Uses a custom rolling XOR/subtraction algorithm with a 4-byte key found at the end of map files.
- **Compression**: Atlases and map data are compressed using Zlib (Pako library).
- **IDB Format**: Scans for `TEXC` (Texture Container) and `IMG ` (Image Metadata) markers to reconstruct the sprite list.
