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
        static bool NoMaterials = false;

        static void Main(string[] args)
        {
            if (args.Length == 0) { PrintUsage(); return; }

            string target = null;
            string assetsRoot = null;
            string mode = args[0].ToLower();

            // Simple but reliable argument reading
            for (int i = 0; i < args.Length; i++) {
                if (args[i].Equals("-nomat", StringComparison.OrdinalIgnoreCase)) NoMaterials = true;
                if ((args[i].Equals("-o") || args[i].Equals("--out")) && i + 1 < args.Length) ExportDir = args[++i];
            }

            if (mode == "-u" || mode == "--unpack") {
                if (args.Length < 3) { Console.WriteLine("Usage: TdrExport -u <archives_root> <output_dir>"); return; }
                UnpackAll(args[1], args[2]); return;
            }

            if (args.Length < 2) { PrintUsage(); return; }
            target = args[1];
            assetsRoot = args[2];

            if (!Directory.Exists(assetsRoot)) {
                Console.WriteLine($"Error: Assets directory not found: {assetsRoot}");
                return;
            }

            VFS.IndexDirectory(assetsRoot);
            Directory.CreateDirectory(ExportDir);
            Console.WriteLine($"Current Work Dir: {Environment.CurrentDirectory}");
            Console.WriteLine($"Export Folder: {Path.GetFullPath(ExportDir)}");

            if (mode == "-l" || mode == "--level") ConvertLevel(target, assetsRoot);
            else if (mode == "-i" || mode == "--info") InvestigateLevel(target, assetsRoot);
            else if (mode == "-m" || mode == "--movables") ConvertMovablesToObj(target, assetsRoot);
            else ConvertHieToObj(target);
        }

        static void PrintUsage()
        {
            Console.WriteLine("TdrExport: Carmageddon TDR2000 Asset Tool");
            Console.WriteLine("\nUsage:");
            Console.WriteLine("  TdrExport -l <track_name> <assets_path> [-o <out>] [-nomat]");
            Console.WriteLine("  TdrExport -i <track_name> <assets_path>");
            Console.WriteLine("  TdrExport -u <assets_path> <out_path>");
            Console.WriteLine("\nExample:");
            Console.WriteLine("  TdrExport -l hollowood C:\\Games\\TDR2000\\ASSETS");
        }

        static string ResolveLevelTxt(string input)
        {
            if (input.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return input;
            string target = input.ToLower();
            var vfsFiles = VFS.GetFiles();
            
            // Try Tracks\Name\name.txt
            var match = vfsFiles.FirstOrDefault(f => f.Name.ToLower().EndsWith($"tracks\\{target}\\{target}.txt") || f.Name.ToLower().EndsWith($"tracks/{target}/{target}.txt"));
            if (match == null) match = vfsFiles.FirstOrDefault(f => f.Name.ToLower().EndsWith($"{target}.txt"));
            
            if (match != null) return match.Name;
            return input + ".txt"; // Fallback guess
        }

        static void ConvertLevel(string trackName, string root)
        {
            string levelTxt = ResolveLevelTxt(trackName);
            Console.WriteLine($"\n=== EXPORTING LEVEL: {levelTxt} ===");
            byte[] data = VFS.LoadFile(levelTxt);
            if (data == null && File.Exists(Path.Combine(root, levelTxt))) data = File.ReadAllBytes(Path.Combine(root, levelTxt));
            
            if (data == null) { Console.WriteLine($"Error: Config {levelTxt} not found in PAKs or disk."); return; }

            var lines = Encoding.ASCII.GetString(data).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines) {
                string clean = line.Split("//")[0].Trim();
                if (clean.StartsWith("SKY_SPHERE") || clean.StartsWith("WATER_MESH") || clean.StartsWith("HARDSHADOW_HIE")) {
                    var p = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length > 1) ConvertHieToObj(p[1].Trim('"'));
                }
            }
            foreach (var line in lines) {
                string clean = line.Split("//")[0].Trim();
                if (clean.StartsWith("STATIC_MESH_DESCRIPTOR") || clean.StartsWith("BREAKABLES_DESCRIPTOR") || clean.StartsWith("ANIMATED_PROPS")) {
                    var p = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length > 1) ParseDescriptorRecursive(p[1].Trim('"'), root, null, null, root, visited, true);
                }
            }
            foreach (var line in lines) {
                string clean = line.Split("//")[0].Trim();
                if (clean.StartsWith("MOVABLE_OBJECTS")) {
                    var p = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length > 1) ConvertMovablesToObj(p[1].Trim('"'), root);
                }
            }
        }

        static void InvestigateLevel(string trackName, string root)
        {
            string levelTxt = ResolveLevelTxt(trackName);
            Console.WriteLine($"\n=== INVESTIGATION: {levelTxt} ===");
            byte[] data = VFS.LoadFile(levelTxt);
            if (data == null) return;
            var lines = Encoding.ASCII.GetString(data).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines) {
                string clean = line.Split("//")[0].Trim();
                var p = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                string key = p[0].ToUpper();
                string val = p[1].Trim('"');
                if (key == "SKY_SPHERE" || key == "WATER_MESH" || key == "HARDSHADOW_HIE") ValidateHieInfo(val, root, textures);
                else if (val.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) ParseDescriptorRecursive(val, root, null, textures, root, visited, false);
            }

            Console.WriteLine("\n[Texture Check]");
            var vfsNames = new HashSet<string>(VFS.GetFiles().Select(f => Path.GetFileName(f.Name)), StringComparer.OrdinalIgnoreCase);
            foreach (var tex in textures.OrderBy(t => t))
                Console.WriteLine($"  {(vfsNames.Contains(tex + ".tga") ? "[+]" : "[!]")} {tex}");
        }

        static void ValidateHieInfo(string hieName, string root, HashSet<string> textures)
        {
            byte[] data = LoadFileOnDiskOrPak(root, hieName);
            if (data != null) {
                Console.WriteLine($"  [+] {hieName}");
                try { var hie = HIE.Load(data, hieName, null, VFS); foreach (var t in hie.Textures) textures.Add(t.Trim('"')); } catch {}
            } else Console.WriteLine($"  [?] {hieName} (Missing)");
        }

        static void ParseDescriptorRecursive(string descName, string root, Dictionary<string, MSHS> cache, HashSet<string> texs, string dir, HashSet<string> visited, bool export)
        {
            if (visited.Contains(descName)) return;
            visited.Add(descName);
            byte[] data = LoadFileOnDiskOrPak(root, descName);
            if (data == null) return;
            var lines = Encoding.ASCII.GetString(data).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines) {
                string clean = line.Split("//")[0].Trim();
                if (string.IsNullOrWhiteSpace(clean)) continue;
                var p = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string entry = p[0].Trim('"');
                if (entry.EndsWith(".hie", StringComparison.OrdinalIgnoreCase)) {
                    if (export) ConvertHieToObj(entry);
                    else ValidateHieInfo(entry, root, texs);
                }
                else if (entry.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) ParseDescriptorRecursive(entry, root, cache, texs, dir, visited, export);
            }
        }

        static void ConvertHieToObj(string hiePath)
        {
            string fileName = Path.GetFileName(hiePath);
            string tempObj = null;
            try {
                byte[] data = VFS.LoadFile(fileName);
                if (data == null && File.Exists(hiePath)) data = File.ReadAllBytes(hiePath);
                if (data == null) return;
                var hie = HIE.Load(data, fileName, null, VFS);
                if (hie.Root == null) return;

                string outName = fileName.Replace(".hie", "");
                string objPath = Path.Combine(ExportDir, outName + ".obj");
                string mtlPath = Path.ChangeExtension(objPath, ".mtl");
                tempObj = objPath + ".tmp";

                using (StreamWriter w = new StreamWriter(tempObj)) {
                    w.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");
                    ProcessNode(hie.Root, Matrix4D.Identity, "Default", hie, null, new Dictionary<string, MSHS>(), new HashSet<string>(), w, ref vOffset, ref vtOffset, ref vnOffset);
                }
                WriteMtlFile(mtlPath, usedTexturesGlobal, "");
                if (File.Exists(objPath)) File.Delete(objPath);
                File.Move(tempObj, objPath);
                Console.WriteLine($"  [OK] {outName}");
            } catch (Exception ex) { if (tempObj != null && File.Exists(tempObj)) File.Delete(tempObj); }
        }
        
        static int vOffset = 1, vtOffset = 1, vnOffset = 1;
        static HashSet<string> usedTexturesGlobal = new HashSet<string>();

        static void ConvertMovablesToObj(string descPath, string root)
        {
            byte[] data = LoadFileOnDiskOrPak(root, descPath);
            if (data == null) return;
            var lines = Encoding.ASCII.GetString(data).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string objPath = Path.Combine(ExportDir, Path.GetFileNameWithoutExtension(descPath) + "_movables.obj");
            string mtlPath = Path.ChangeExtension(objPath, ".mtl");
            string tempObj = objPath + ".tmp";
            vOffset = 1; vtOffset = 1; vnOffset = 1; usedTexturesGlobal.Clear();

            using (StreamWriter w = new StreamWriter(tempObj)) {
                w.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");
                foreach (var line in lines) {
                    var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 8) continue;
                    string name = p[0].Trim('"');
                    Matrix4D mat = Matrix4D.CreateFromQuaternion(new Quaternion(float.Parse(p[4], CultureInfo.InvariantCulture), float.Parse(p[5], CultureInfo.InvariantCulture), float.Parse(p[6], CultureInfo.InvariantCulture), float.Parse(p[7], CultureInfo.InvariantCulture)));
                    mat.M41 = float.Parse(p[1], CultureInfo.InvariantCulture); mat.M42 = float.Parse(p[2], CultureInfo.InvariantCulture); mat.M43 = float.Parse(p[3], CultureInfo.InvariantCulture);
                    byte[] hData = VFS.LoadFile(name + ".hie");
                    if (hData != null) {
                        var hie = HIE.Load(hData, name + ".hie", null, VFS);
                        if (hie.Root != null) ProcessNode(hie.Root, mat, "Default", hie, null, new Dictionary<string, MSHS>(), usedTexturesGlobal, w, ref vOffset, ref vtOffset, ref vnOffset);
                    }
                }
            }
            WriteMtlFile(mtlPath, usedTexturesGlobal, root);
            if (File.Exists(objPath)) File.Delete(objPath);
            File.Move(tempObj, objPath);
            Console.WriteLine($"  [OK] Movables -> {objPath}");
        }

        static void WriteMtlFile(string mtlPath, HashSet<string> textures, string root)
        {
            using (StreamWriter mtl = new StreamWriter(mtlPath)) {
                mtl.WriteLine("newmtl Default\nKd 0.8 0.8 0.8");
                if (NoMaterials) return;
                var vfs = VFS.GetFiles();
                foreach (var t in textures) {
                    if (string.IsNullOrWhiteSpace(t) || t == "Default") continue;
                    mtl.WriteLine($"\nnewmtl {t}\nKd 1.0 1.0 1.0");
                    var best = vfs.Where(f => f.Name.Contains(t, StringComparison.OrdinalIgnoreCase) && f.Name.EndsWith(".tga")).OrderByDescending(f => f.Name.Contains("_32")).FirstOrDefault();
                    if (best != null) {
                        string tName = Path.GetFileName(best.Name);
                        if (!File.Exists(Path.Combine(ExportDir, tName))) { byte[] d = VFS.LoadFile(best.Name); if (d != null) File.WriteAllBytes(Path.Combine(ExportDir, tName), d); }
                        mtl.WriteLine($"map_Kd {tName}\nmap_d {tName}");
                    }
                }
            }
        }

        static void ProcessNode(TDRNode n, Matrix4D p, string t, HIE h, string d, Dictionary<string, MSHS> c, HashSet<string> ts, StreamWriter w, ref int v, ref int vt, ref int vn)
        {
            if (n == null) return;
            Matrix4D m = (n.Transform ?? Matrix4D.Identity) * p;
            if (n.Type == TDRNode.NodeType.Texture && n.Index >= 0 && n.Index < h.Textures.Count) t = h.Textures[n.Index].Trim('"');
            if (n.Type == TDRNode.NodeType.Mesh) {
                string mName = h.Meshes.Count == 1 ? h.Meshes[0] : (n.Index < h.Meshes.Count ? h.Meshes[n.Index] : null);
                if (mName != null) {
                    if (!c.TryGetValue(mName, out MSHS msh)) { byte[] b = VFS.LoadFile(mName); if (b != null) { msh = MSHS.Load(b, mName); c[mName] = msh; } }
                    if (msh != null) {
                        w.WriteLine($"o {n.Name}_{n.ID}\nusemtl {t}"); ts.Add(t);
                        int sub = h.Meshes.Count == 1 ? n.Index : -1;
                        if (sub >= 0 && sub < msh.Meshes.Count) WriteSubMesh(msh.Meshes[sub], m, w, ref v, ref vt, ref vn);
                        else foreach (var sm in msh.Meshes) WriteSubMesh(sm, m, w, ref v, ref vt, ref vn);
                    }
                }
            }
            foreach (var ch in n.Children) ProcessNode(ch, m, t, h, d, c, ts, w, ref v, ref vt, ref vn);
        }

        static void WriteSubMesh(TDRMesh m, Matrix4D t, StreamWriter w, ref int v, ref int vt, ref int vn)
        {
            if (m.Mode == TDRMesh.MSHMode.TriIndexedPosition) {
                foreach (var f in m.Faces) {
                    for (int i = 0; i < 3; i++) {
                        var vert = f.Vertices[i]; Vector3 pos = TransformPoint(m.Positions[vert.PositionIndex], t); Vector3 n = TransformVector(vert.Normal, t);
                        w.WriteLine($"v {F(pos.X)} {F(pos.Y)} {F(pos.Z)}\nvt {F(vert.UV.X)} {F(1.0f - vert.UV.Y)}\nvn {F(n.X)} {F(n.Y)} {F(n.Z)}");
                    }
                    w.WriteLine($"f {v}/{vt}/{vn} {v+1}/{vt+1}/{vn+1} {v+2}/{vt+2}/{vn+2}"); v += 3; vt += 3; vn += 3;
                }
            } else if (m.Mode == TDRMesh.MSHMode.Tri) {
                int s = v; foreach(var vert in m.Vertices) {
                    Vector3 p = TransformPoint(vert.Position, t); Vector3 n = TransformVector(vert.Normal, t);
                    w.WriteLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}\nvt {F(vert.UV.X)} {F(1.0f - vert.UV.Y)}\nvn {F(n.X)} {F(n.Y)} {F(n.Z)}"); v++; vt++; vn++;
                }
                foreach(var f in m.Faces) w.WriteLine($"f {s+f.V1}/{s+f.V1}/{s+f.V1} {s+f.V2}/{s+f.V2}/{s+f.V2} {s+f.V3}/{s+f.V3}/{s+f.V3}");
            } else {
                foreach(var f in m.Faces) {
                    string l = "f"; foreach(var vert in f.Vertices) {
                        Vector3 p = TransformPoint(vert.Position, t); Vector3 n = TransformVector(vert.Normal, t);
                        w.WriteLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}\nvt {F(vert.UV.X)} {F(1.0f - vert.UV.Y)}\nvn {F(n.X)} {F(n.Y)} {F(n.Z)}");
                        l += $" {v}/{vt}/{vn}"; v++; vt++; vn++;
                    }
                    w.WriteLine(l);
                }
            }
        }

        static void UnpackAll(string r, string o) { VFS.IndexDirectory(r); foreach (var e in VFS.GetFiles()) { byte[] d = VFS.LoadFile(e.Name); if (d != null) { string p = Path.Combine(o, e.Name.Replace('/', '\\')); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, d); } } }
        static byte[] LoadFileOnDiskOrPak(string r, string n) { string p = FindFileOnDiskOrPak(r, n); return p == null ? null : (File.Exists(p) ? File.ReadAllBytes(p) : VFS.LoadFile(Path.GetFileName(n))); }
        static string FindFileOnDiskOrPak(string r, string n) { string f = Path.GetFileName(n); string d = Path.Combine(r, n); if (File.Exists(d)) return d; try { string s = Directory.EnumerateFiles(r, f, SearchOption.AllDirectories).FirstOrDefault(); if (s != null) return s; } catch {} return VFS.FileExists(f) ? f : null; }
        static Vector3 TransformPoint(Vector3 p, Matrix4D m) => new Vector3(p.X*m.M11+p.Y*m.M21+p.Z*m.M31+m.M41, p.X*m.M12+p.Y*m.M22+p.Z*m.M32+m.M42, p.X*m.M13+p.Y*m.M23+p.Z*m.M33+m.M43);
        static Vector3 TransformVector(Vector3 p, Matrix4D m) => new Vector3(p.X*m.M11+p.Y*m.M21+p.Z*m.M31, p.X*m.M12+p.Y*m.M22+p.Z*m.M32, p.X*m.M13+p.Y*m.M23+p.Z*m.M33);
        static string F(float f) => f.ToString("0.000000", CultureInfo.InvariantCulture);
    }
}
