# TDR2000 Archive Manager (.PAK/.DIR)

This project provides a professional-grade implementation of the archive format used by **TDR2000**. The core logic is encapsulated in the `TDRArchive.cs` class, which serves as a standalone technical reference.

## 1. Format Overview

The archive system consists of two separate files:
- **.DIR**: A binary index using a **Recursive Trie (Prefix Tree)** structure. This allows for O(L) file lookups regardless of the total number of files.
- **.PAK**: The data container where files are stored as **zIG encapsulated blocks**.

### zIG Encapsulation
Each file in the `.PAK` is wrapped in an 8-byte header:
- `Key` (1 byte): Random IV.
- `Magic` (3 bytes): "zIG" (Compressed) or "RAW" (Uncompressed), XORed with `Key`.
- `Size` (4 bytes): Original size, XORed with `MetaKey`.
- `MetaKey` is derived via `ROR8(Key, 3)`.

## 2. Using the TDRArchive Class

The `TDRArchive` class is designed to be autonomous and can be integrated into any .NET project without dependencies on the UI.

### Parsing an Index
```csharp
List<FileEntry> files = TDRArchive.ParseTrieIndex("levels.dir");
```

### Extracting a File
```csharp
byte[] extractedData = TDRArchive.DecompressZig(buffer);
```

## 3. Engineering Best Practices

1. **Path Normalization**: All paths are automatically converted to lowercase and use forward slashes (`/`).
2. **Deterministic Sorting**: Trie nodes are sorted by ASCII code to ensure binary search compatibility.
3. **4-Byte Alignment**: The `WriteAligned` method ensures that every file block starts at a multiple of 4 bytes.
4. **Traversal Protection**: The `SanitizePath` method scrubs `../` sequences to prevent security vulnerabilities.

## 4. Building the Project

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```