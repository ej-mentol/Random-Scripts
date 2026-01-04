using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using TdrExport.TDR2000.Formats;
using TdrExport.Helpers;

namespace TdrExport
{
    class Program
    {
        static PakManager VFS = new PakManager();
        static string ExportDir = "EXPORT";

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            string levelPath = null;
            string assetsRoot = null;
            
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "-l" || args[i] == "--level") && i + 2 < args.Length)
                {
                    levelPath = args[i + 1];
                    assetsRoot = args[i + 2];
                    i += 2;
                }
                else if ((args[i] == "-o" || args[i] == "--out") && i + 1 < args.Length)
                {
                    ExportDir = args[i + 1];
                    i++;
                }
                else if ((args[i] == "-i" || args[i] == "--info") && i + 2 < args.Length)
                {
                    VFS.IndexDirectory(args[i + 2]);
                    InvestigateLevel(args[i + 1], args[i + 2]);
                    return;
                }
                else if ((args[i] == "-m" || args[i] == "--movables") && i + 2 < args.Length)
                {
                    VFS.IndexDirectory(args[i + 2]);
                    ConvertMovablesToObj(args[i + 1], args[i + 2]);
                    return;
                }
                else if ((args[i] == "-u" || args[i] == "--unpack") && i + 2 < args.Length)
                {
                    UnpackAll(args[i + 1], args[i + 2]);
                    return;
                }
            }

            if (levelPath != null && assetsRoot != null)
            {
                Directory.CreateDirectory(ExportDir);
                Console.WriteLine($"Output directory: {Path.GetFullPath(ExportDir)}");
                VFS.IndexDirectory(assetsRoot);
                ConvertLevel(levelPath, assetsRoot);
            }
            else if (args.Length == 1 && (File.Exists(args[0]) || args[0].EndsWith(".hie")))
            {
                Directory.CreateDirectory(ExportDir);
                ConvertHieToObj(args[0]);
            }
            else
            {
                PrintUsage();
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Carmageddon TDR2000 Universal Tool (Track2Obj & Unpacker)");
            Console.WriteLine("Usage:");
            Console.WriteLine("  Full Level Export: TdrExport -l <track_dir_or_txt> <assets_root> [-o <out_dir>]");
            Console.WriteLine("  Investigate:       TdrExport -i <level.txt> <assets_root>");
            Console.WriteLine("  Export Movables:   TdrExport -m <descriptor.txt> <assets_root> [-o <out_dir>]");
            Console.WriteLine("  Unpack Archives:   TdrExport -u <archives_root> <output_dir>");
            Console.WriteLine(@"\nExample: TdrExport -l ASSETS\Tracks\Hollowood ASSETS -o C:\MyExport");
        }

        static void InvestigateLevel(string levelPath, string searchRoot)
        {
            string levelTxt = ResolveLevelTxt(levelPath, searchRoot);
            if (levelTxt == null) return;

            Console.WriteLine($"\n=== INVESTIGATION REPORT: {levelTxt} ===");
            byte[] data = LoadFileOnDiskOrPak(searchRoot, levelTxt);
            if (data == null) { Console.WriteLine("  [!] CRITICAL: Root level file not found!"); return; }

            string rawText = Encoding.ASCII.GetString(data);
            var lines = rawText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            
            var recognizedKeys = new HashSet<string> { "SKY_SPHERE", "WATER_MESH", "HARDSHADOW_HIE", "STATIC_MESH_DESCRIPTOR", "BREAKABLES_DESCRIPTOR", "ANIMATED_PROPS", "MOVABLE_OBJECTS", "PEDS_DESCRIPTOR", "ZOMBIES_DESCRIPTOR", "DRONE_DESCRIPTOR", "RADAR_DESCRIPTOR", "AMBIENT_SOUNDS", "LIGHTS_DESCRIPTOR", "TEXTURE_ANIM_DESCRIPTOR", "PATH_FOLLOWERS", "OCCLUDER_MESH", "STEAM_NODES", "SPECIALV_ENVIRONMENTS", "SPECIALV_H_ENVIRONMENTS", "SPECIALV_SFX_ENVIRONMENTS" };

            Console.WriteLine("\n[1. Global Environment]");
            foreach (var line in lines) {
                string clean = line.Split("//")[0].Trim();
                if (clean.Contains("SUN_") || clean.Contains("FOG_") || clean.Contains("WATER_LEVEL") || clean.Contains("START_"))
                    Console.WriteLine($"  * {clean}");
            }

            Console.WriteLine("\n[2. Component Trace]");
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var line in lines) {
                string clean = line.Split("//")[0].Trim();
                var p = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                string key = p[0].ToUpper();
                string val = p[1].Trim('"');

                if (key == "SKY_SPHERE" || key == "WATER_MESH" || key == "HARDSHADOW_HIE") {
                    ValidateHieInfo(val, searchRoot, textures);
                } else if (recognizedKeys.Contains(key) && val.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) {
                    if (key == "MOVABLE_OBJECTS") Console.WriteLine($"  MOVABLES_DESCRIPTOR       -> [+] {val}");
                    else ParseDescriptorRecursive(val, searchRoot, null, textures, searchRoot, visited, false);
                }
            }

            Console.WriteLine("\n[3. Texture Validation]");
            int missingTex = 0;
            var vfsFileNames = new HashSet<string>(VFS.GetFiles().Select(f => Path.GetFileName(f.Name)), StringComparer.OrdinalIgnoreCase);
            
            foreach (var tex in textures.OrderBy(t => t)) {
                bool found = vfsFileNames.Any(f => f.Contains(tex, StringComparison.OrdinalIgnoreCase));
                if (!found) missingTex++;
                Console.WriteLine($"  {(found ? "[+]" : "[!]")} {tex}");
            }
            if (missingTex > 0) Console.WriteLine($"  ! Warning: {missingTex} textures missing from PAKs.");
            Console.WriteLine("\n=== END OF REPORT ===\n");
        }

        static string ResolveLevelTxt(string path, string root)
        {
            if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return path;
            string trackName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string guess = Path.Combine(path, trackName + ".txt");
            if (File.Exists(guess)) return guess;
            if (VFS.FileExists(trackName + ".txt")) return trackName + ".txt";
            Console.WriteLine($"Error: Could not resolve level config for {path}");
            return null;
        }

        static void ValidateHieInfo(string hieName, string root, HashSet<string> textures)
        {
            byte[] data = LoadFileOnDiskOrPak(root, hieName);
            bool found = data != null;
            Console.WriteLine($"  {hieName,-25} -> {(found ? "[+]" : "[?]")} {hieName}");
            if (found) {
                try {
                    var hie = HIE.Load(data, hieName, null, VFS);
                    foreach (var t in hie.Textures) textures.Add(t.Trim('"'));
                } catch {}
            }
        }

        static void ParseDescriptorRecursive(string descName, string searchRoot, Dictionary<string, MSHS> meshCache, HashSet<string> usedTextures, string baseDir, HashSet<string> visited, bool export)
        {
            if (visited.Contains(descName)) return;
            visited.Add(descName);

            byte[] descData = LoadFileOnDiskOrPak(searchRoot, descName);
            if (descData == null) {
                if (!export) Console.WriteLine($"  [?] Missing Descriptor: {descName}");
                return;
            }

            if (!export) Console.WriteLine($"  DESCRIPTOR: {descName}");
            
            var descLines = Encoding.ASCII.GetString(descData).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var dLine in descLines)
            {
                string dClean = dLine.Split("//")[0].Trim();
                if (string.IsNullOrWhiteSpace(dClean)) continue;
                var parts = dClean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string entry = parts[0].Trim('"');

                if (entry.EndsWith(".hie", StringComparison.OrdinalIgnoreCase))
                {
                    if (export) {
                        string hiePath = FindFileOnDiskOrPak(searchRoot, entry);
                        if (hiePath != null) ConvertHieToObj(hiePath);
                    } else ValidateHieInfo(entry, searchRoot, usedTextures);
                }
                else if (entry.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && !visited.Contains(entry))
                {
                    ParseDescriptorRecursive(entry, searchRoot, meshCache, usedTextures, baseDir, visited, export);
                }
            }
        }

        static void ConvertLevel(string levelPath, string searchRoot)
        {
            string levelTxtPath = ResolveLevelTxt(levelPath, searchRoot);
            if (levelTxtPath == null) return;

            Console.WriteLine($"\n=== AUTOMATIC LEVEL EXPORT: {Path.GetFileName(levelTxtPath)} ===");
            byte[] levelData = LoadFileOnDiskOrPak(searchRoot, levelTxtPath);
            if (levelData == null) { Console.WriteLine($"Error: Level file not found: {levelTxtPath}"); return; }

            var lines = Encoding.ASCII.GetString(levelData).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines) {
                string cleanLine = line.Split("//")[0].Trim();
                if (cleanLine.StartsWith("SKY_SPHERE") || cleanLine.StartsWith("WATER_MESH") || cleanLine.StartsWith("HARDSHADOW_HIE")) {
                    var p = cleanLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length > 1) {
                        string path = FindFileOnDiskOrPak(searchRoot, p[1].Trim('"'));
                        if (path != null) ConvertHieToObj(path);
                    }
                }
            }

            foreach (var line in lines) {
                string cleanLine = line.Split("//")[0].Trim();
                if (cleanLine.StartsWith("STATIC_MESH_DESCRIPTOR") || cleanLine.StartsWith("BREAKABLES_DESCRIPTOR") || cleanLine.StartsWith("ANIMATED_PROPS")) {
                    var p = cleanLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length > 1) ParseDescriptorRecursive(p[1].Trim('"'), searchRoot, null, null, searchRoot, visited, true);
                }
            }

            foreach (var line in lines) {
                string cleanLine = line.Split("//")[0].Trim();
                if (cleanLine.StartsWith("MOVABLE_OBJECTS")) {
                    var p = cleanLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length > 1) {
                        string mPath = FindFileOnDiskOrPak(searchRoot, p[1].Trim('"'));
                        if (mPath != null) ConvertMovablesToObj(mPath, searchRoot);
                    }
                }
            }
            Console.WriteLine($"\n=== LEVEL EXPORT COMPLETE ===\nOutput folder: {Path.GetFullPath(ExportDir)}\n");
        }

        static void UnpackAll(string archivesRoot, string outputDir)
        {
            VFS.IndexDirectory(archivesRoot);
            Console.WriteLine($"Unpacking everything to: {outputDir}");
            var files = VFS.GetFiles();
            int count = 0;
            foreach (var entry in files) {
                byte[] data = VFS.LoadFile(entry.Name);
                if (data != null) {
                    string outPath = Path.Combine(outputDir, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    File.WriteAllBytes(outPath, data);
                    count++;
                    if (count % 100 == 0) Console.WriteLine($"  Extracted {count} files...");
                }
            }
            Console.WriteLine($"\nDONE! Extracted {count} files.");
        }

        static string FindFileOnDiskOrPak(string root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Path.IsPathRooted(name) && File.Exists(name)) return name;
            string fileName = Path.GetFileName(name);
            string direct = Path.Combine(root, name);
            if (File.Exists(direct)) return direct;
            try {
                string found = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return found;
            } catch {}
            if (VFS.FileExists(fileName)) return fileName; 
            return null;
        }

        static byte[] LoadFileOnDiskOrPak(string root, string name)
        {
            string path = FindFileOnDiskOrPak(root, name);
            if (path == null) return null;
            if (File.Exists(path)) return File.ReadAllBytes(path); 
            return VFS.LoadFile(Path.GetFileName(path));
        }

        static void ConvertHieToObj(string hiePath)
        {
            string fileName = Path.GetFileName(hiePath);
            Console.WriteLine($"Processing HIE: {fileName}");
            string tempObj = null;
            try {
                string baseDir = Path.GetDirectoryName(hiePath);
                byte[] data = File.Exists(hiePath) ? File.ReadAllBytes(hiePath) : VFS.LoadFile(fileName);
                if (data == null) return;
                var hie = HIE.Load(data, fileName, baseDir, VFS);
                if (hie.Root == null) return;

                string safeName = fileName.Replace(".hie", "");
                if (hiePath.Contains("Tracks")) {
                    var parts = hiePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var idx = Array.FindIndex(parts, p => p.Equals("Tracks", StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && idx + 1 < parts.Length) safeName = parts[idx+1] + "_" + safeName;
                }

                string objPath = Path.Combine(ExportDir, safeName + ".obj");
                string mtlPath = Path.ChangeExtension(objPath, ".mtl");
                tempObj = objPath + ".tmp";
                Dictionary<string, MSHS> meshCache = new Dictionary<string, MSHS>();
                HashSet<string> usedTextures = new HashSet<string>();
                int vOffset = 1; int vtOffset = 1; int vnOffset = 1;
                using (StreamWriter w = new StreamWriter(tempObj)) {
                    w.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");
                    ProcessNode(hie.Root, Matrix4D.Identity, "Default", hie, baseDir, meshCache, usedTextures, w, ref vOffset, ref vtOffset, ref vnOffset);
                }
                WriteMtlFile(mtlPath, usedTextures, ExportDir);
                if (File.Exists(objPath)) File.Delete(objPath);
                File.Move(tempObj, objPath);
                Console.WriteLine($"  [OK] Saved to: {objPath}");
            } catch (Exception ex) { 
                if (tempObj != null && File.Exists(tempObj)) File.Delete(tempObj);
                Console.WriteLine($"  [!] Error: {ex.Message}"); 
            }
        }

        static void ConvertMovablesToObj(string descriptorPath, string searchRoot)
        {
            string fileName = Path.GetFileName(descriptorPath);
            Console.WriteLine($"Processing Movables: {fileName}");
            string tempObj = null;
            try {
                byte[] data = File.Exists(descriptorPath) ? File.ReadAllBytes(descriptorPath) : VFS.LoadFile(fileName);
                if (data == null) return;
                var lines = Encoding.ASCII.GetString(data).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                string objPath = Path.Combine(ExportDir, Path.GetFileNameWithoutExtension(fileName) + "_movables.obj");
                string mtlPath = Path.ChangeExtension(objPath, ".mtl");
                tempObj = objPath + ".tmp";
                Dictionary<string, HIE> hieCache = new Dictionary<string, HIE>();
                Dictionary<string, MSHS> meshCache = new Dictionary<string, MSHS>();
                HashSet<string> usedTextures = new HashSet<string>();
                int vOffset = 1; int vtOffset = 1; int vnOffset = 1;
                using (StreamWriter w = new StreamWriter(tempObj)) {
                    w.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");
                    int count = 0;
                    foreach (var line in lines) {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 8) continue;
                        string assetName = parts[0].Trim('"');
                        float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                        float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                        float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                        float qx = float.Parse(parts[4], CultureInfo.InvariantCulture);
                        float qy = float.Parse(parts[5], CultureInfo.InvariantCulture);
                        float qz = float.Parse(parts[6], CultureInfo.InvariantCulture);
                        float qw = float.Parse(parts[7], CultureInfo.InvariantCulture);
                        Matrix4D instanceTransform = Matrix4D.CreateFromQuaternion(new Quaternion(qx, qy, qz, qw));
                        instanceTransform.M41 = x; instanceTransform.M42 = y; instanceTransform.M43 = z;
                        if (!hieCache.TryGetValue(assetName, out HIE hie)) {
                            byte[] hieData = VFS.LoadFile(assetName + ".hie");
                            if (hieData != null) hie = HIE.Load(hieData, assetName + ".hie", null, VFS);
                            hieCache[assetName] = hie;
                        }
                        if (hie != null && hie.Root != null) {
                            ProcessNode(hie.Root, instanceTransform, "Default", hie, null, meshCache, usedTextures, w, ref vOffset, ref vtOffset, ref vnOffset);
                            count++;
                        }
                    }
                    Console.WriteLine($"  Exported {count} instances.");
                }
                WriteMtlFile(mtlPath, usedTextures, ExportDir);
                if (File.Exists(objPath)) File.Delete(objPath);
                File.Move(tempObj, objPath);
                Console.WriteLine($"  [OK] Saved to: {objPath}");
            } catch (Exception ex) { 
                if (tempObj != null && File.Exists(tempObj)) File.Delete(tempObj);
                Console.WriteLine($"  [!] Error: {ex.Message}"); 
            } 
        }

        static void WriteMtlFile(string mtlPath, HashSet<string> usedTextures, string searchRoot)
        {
            using (StreamWriter mtl = new StreamWriter(mtlPath)) {
                mtl.WriteLine("newmtl Default\nKd 0.8 0.8 0.8");
                var vfsFiles = VFS.GetFiles();
                foreach(var texName in usedTextures) {
                    if (string.IsNullOrWhiteSpace(texName) || texName == "Default") continue;
                    mtl.WriteLine($"\nnewmtl {texName}\nKd 1.0 1.0 1.0");
                    string texFile = texName + ".tga";
                    var best = vfsFiles.Where(f => f.Name.Contains(texName, StringComparison.OrdinalIgnoreCase) && (f.Name.EndsWith(".tga") || f.Name.EndsWith(".png")))
                        .OrderByDescending(f => f.Name.Contains("_32")).ThenByDescending(f => f.Name.Length).FirstOrDefault();
                    if (best != null) texFile = Path.GetFileName(best.Name);
                    mtl.WriteLine($"map_Kd {texFile}\nmap_d {texFile}");
                }
            }
        }

        static void ProcessNode(TDRNode node, Matrix4D parentTransform, string currentTexture, HIE hie, string baseDir, Dictionary<string, MSHS> meshCache, HashSet<string> usedTextures, StreamWriter w, ref int vOffset, ref int vtOffset, ref int vnOffset)
        {
            if (node == null) return;
            Matrix4D globalTransform = (node.Transform ?? Matrix4D.Identity) * parentTransform;
            if (node.Type == TDRNode.NodeType.Texture && node.Index >= 0 && node.Index < hie.Textures.Count)
                currentTexture = hie.Textures[node.Index].Trim('"');
            if (node.Type == TDRNode.NodeType.Mesh) {
                string meshName = null; int subMeshIndex = -1;
                if (hie.Meshes.Count == 1) { meshName = hie.Meshes[0]; subMeshIndex = node.Index; }
                else if (node.Index >= 0 && node.Index < hie.Meshes.Count) { meshName = hie.Meshes[node.Index]; }
                if (meshName != null) {
                    if (!meshCache.TryGetValue(meshName, out MSHS mshs)) {
                        byte[] mshData = LoadFileOnDiskOrPak(baseDir ?? ".", meshName);
                        if (mshData != null) { mshs = MSHS.Load(mshData, meshName); meshCache[meshName] = mshs; }
                    }
                    if (mshs != null) {
                        w.WriteLine($"o {node.Name}_{node.ID}\nusemtl {currentTexture}");
                        usedTextures.Add(currentTexture);
                        if (subMeshIndex >= 0 && subMeshIndex < mshs.Meshes.Count)
                            WriteSubMesh(mshs.Meshes[subMeshIndex], globalTransform, w, ref vOffset, ref vtOffset, ref vnOffset);
                        else if (subMeshIndex < 0)
                            foreach (var sm in mshs.Meshes) WriteSubMesh(sm, globalTransform, w, ref vOffset, ref vtOffset, ref vnOffset);
                    }
                }
            }
            string scopeTexture = currentTexture;
            foreach (var child in node.Children) {
                if (child.Type == TDRNode.NodeType.Texture && child.Index >= 0 && child.Index < hie.Textures.Count)
                    scopeTexture = hie.Textures[child.Index].Trim('"');
                ProcessNode(child, globalTransform, scopeTexture, hie, baseDir, meshCache, usedTextures, w, ref vOffset, ref vtOffset, ref vnOffset);
            }
        }

        static void WriteSubMesh(TDRMesh mesh, Matrix4D transform, StreamWriter w, ref int vOffset, ref int vtOffset, ref int vnOffset)
        {
            if (mesh.Mode == TDRMesh.MSHMode.TriIndexedPosition) {
                foreach (var face in mesh.Faces) {
                    if (face.Vertices.Count < 3) continue;
                    for (int i = 0; i < 3; i++) {
                        var vert = face.Vertices[i];
                        Vector3 pos = TransformPoint(mesh.Positions[vert.PositionIndex], transform);
                        Vector3 n = TransformVector(vert.Normal, transform);
                        w.WriteLine($"v {F(pos.X)} {F(pos.Y)} {F(pos.Z)}\nvt {F(vert.UV.X)} {F(1.0f - vert.UV.Y)}\nvn {F(n.X)} {F(n.Y)} {F(n.Z)}");
                    }
                    w.WriteLine($"f {vOffset}/{vtOffset}/{vnOffset} {vOffset+1}/{vtOffset+1}/{vnOffset+1} {vOffset+2}/{vtOffset+2}/{vnOffset+2}");
                    vOffset += 3; vtOffset += 3; vnOffset += 3;
                }
            } else if (mesh.Mode == TDRMesh.MSHMode.Tri) {
                int start = vOffset;
                foreach(var v in mesh.Vertices) {
                    Vector3 p = TransformPoint(v.Position, transform);
                    Vector3 n = TransformVector(v.Normal, transform);
                    w.WriteLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}\nvt {F(v.UV.X)} {F(1.0f - v.UV.Y)}\nvn {F(n.X)} {F(n.Y)} {F(n.Z)}");
                    vOffset++; vtOffset++; vnOffset++;
                }
                foreach(var f in mesh.Faces) w.WriteLine($"f {start+f.V1}/{start+f.V1}/{start+f.V1} {start+f.V2}/{start+f.V2}/{start+f.V2} {start+f.V3}/{start+f.V3}/{start+f.V3}");
            } else {
                foreach(var face in mesh.Faces) {
                    string fLine = "f";
                    foreach(var v in face.Vertices) {
                        Vector3 p = TransformPoint(v.Position, transform);
                        Vector3 n = TransformVector(v.Normal, transform);
                        w.WriteLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}\nvt {F(v.UV.X)} {F(1.0f - v.UV.Y)}\nvn {F(n.X)} {F(n.Y)} {F(n.Z)}");
                        fLine += $" {vOffset}/{vtOffset}/{vnOffset}"; vOffset++; vtOffset++; vnOffset++;
                    }
                    w.WriteLine(fLine);
                }
            }
        }

        static Vector3 TransformPoint(Vector3 p, Matrix4D m) => new Vector3(p.X*m.M11+p.Y*m.M21+p.Z*m.M31+m.M41, p.X*m.M12+p.Y*m.M22+p.Z*m.M32+m.M42, p.X*m.M13+p.Y*m.M23+p.Z*m.M33+m.M43);
        static Vector3 TransformVector(Vector3 p, Matrix4D m) => new Vector3(p.X*m.M11+p.Y*m.M21+p.Z*m.M31, p.X*m.M12+p.Y*m.M22+p.Z*m.M32, p.X*m.M13+p.Y*m.M23+p.Z*m.M33);
        static string F(float f) => f.ToString("0.000000", CultureInfo.InvariantCulture);
    }
}
