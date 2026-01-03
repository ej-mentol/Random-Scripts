# Technical Reference: TDR2000 Archive Format (.PAK/.DIR)

This document describes the technical implementation of the archive format used by **TDR2000**. The provided C# class, `TDRArchive`, serves as a bit-perfect reference implementation based on extensive reverse engineering of the original SDK tools.

---

## 1. System Architecture

The format employs a dual-file approach to separate metadata from raw data:

1. **The Index (.DIR)**: A binary file containing a recursive **Prefix Tree (Trie)**. This structure enables high-performance file lookups with O(L) complexity, where L is the length of the path.
2. **The Container (.PAK)**: A binary blob where files are stored sequentially, encapsulated within **zIG/RAW** obfuscation blocks.

---

## 2. Technical Specification

### 2.1 Trie Index Logic
The `.DIR` file consists of 2-byte node headers followed by optional metadata.

*   **Node Header**: `[Character: u8] [Flags: u8]`
*   **Flags Architecture**:
    *   `0x08 (IsFile)`: Terminal node indicating a valid file path. Followed immediately by 8 bytes: `[Offset: u32] [Size: u32]`.
    *   `0x40 (HasBranch)`: Indicates the start of a nested child level. The parser should recurse.
    *   `0x80 (HasNext)`: Indicates a sibling node exists at the same hierarchy level. The parser should continue the current loop.

### 2.2 zIG Obfuscation (The .PAK Container)
Every file entry in the `.PAK` is wrapped in an 8-byte header to protect the integrity of the data stream.

*   **Header Format**: `[Key: u8] [XorSignature: 3 bytes] [XorSize: 4 bytes]`
*   **Key**: A random initialization vector used for XOR operations.
*   **MetaKey**: Derived via bitwise rotation: `ROR8(Key, 3)`.
*   **Signatures**: 
    *   `"zIG"`: Data is compressed using standard Deflate (zlib).
    *   `"RAW"`: Data is uncompressed.
*   **Decryption**: Signature is XORed with `Key`. Size is XORed with `MetaKey`.

---

## 3. The C# Implementation (`TDRArchive`)

The `TDRArchive` class is a standalone, dependency-free utility designed for .NET 7+.

### Core Methods

| Method | Description |
| :--- | :--- |
| `ParseTrieIndex(path)` | Decodes a `.DIR` file into a list of `FileEntry` objects. |
| `SerializeTrieIndex(list)` | Encodes a file list into a bit-perfect binary Trie index. |
| `DecompressZig(buffer)` | Decapsulates a zIG block and restores the original data. |
| `CreateZigHeader(data, compress)` | Encapsulates raw data into a compliant zIG/RAW block. |
| `WriteAligned(stream, data)` | Writes data to a stream with verified 4-byte alignment. |

---

## 4. Integration Examples

### Extracting Files
```csharp
var files = TDRArchive.ParseTrieIndex("data.dir");
using var pak = File.OpenRead("data.pak");

foreach (var entry in files) {
    pak.Seek(entry.Offset, SeekOrigin.Begin);
    byte[] buffer = new byte[entry.Size];
    pak.ReadExactly(buffer, 0, (int)entry.Size);

    byte[] data = TDRArchive.DecompressZig(buffer);
    File.WriteAllBytes(entry.Name, data);
}
```

### Creating an Archive
```csharp
var entries = new List<FileEntry>();
// ... populate entries with offsets and sizes ...
byte[] binaryIndex = TDRArchive.SerializeTrieIndex(entries);
File.WriteAllBytes("output.dir", binaryIndex);
```

---

## 5. Normalization Standards
To maintain compatibility with original game assets, the following rules are enforced:
1. **Lowercase**: All paths are converted to lowercase.
2. **Forward Slashes**: Standard `'/'` separator is used throughout the Trie.
3. **ASCII Sorting**: Nodes are sorted lexicographically at every level.
4. **4-Byte Alignment**: Block offsets are padded to 4-byte boundaries.