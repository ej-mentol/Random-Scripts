using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace TDR_PAK_Manager_CS
{
    public partial class MainWindow : Window
    {
        public enum NodeType { Root, Dir, Archive, File, VirtualDir, VirtualFile }

        public class Node : INotifyPropertyChanged
        {
            private bool _isExpanded;
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string VirtualPath { get; set; } = "";
            public NodeType Type { get; set; }
            public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
            public string Icon => GetIcon();
            public IBrush IconColor => GetIconColor();
            public ObservableCollection<Node> Children { get; set; } = new ObservableCollection<Node>();
            public uint Offset { get; set; }
            public uint Size { get; set; }
            private string GetIcon() => Type switch { NodeType.Root or NodeType.Dir or NodeType.VirtualDir => "📁", NodeType.Archive => "📦", _ => "📄" };
            private IBrush GetIconColor() => Type switch { NodeType.Root or NodeType.Dir or NodeType.VirtualDir => Brushes.Gold, NodeType.Archive => Brushes.SkyBlue, NodeType.VirtualFile => Brushes.White, _ => Brushes.Gray };
            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private string _sourceRoot = "";
        private string _targetRoot = "";
        private bool _isBusy = false;

        public MainWindow()
        {
            InitializeComponent();
            GenerateExampleConfig();
            _targetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TDR_Extracted");
            TargetPathBox.Text = _targetRoot;
            RefreshTree(TargetTree, _targetRoot, false);
            SourceTree.SelectionChanged += (s, e) => { UpdateStatus(SourceTree); UpdateActionButtons(); };
            TargetTree.SelectionChanged += (s, e) => { UpdateStatus(TargetTree); UpdateActionButtons(); };
        }

        private void GenerateExampleConfig() {
            try {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "example.config.json");
                if (!File.Exists(p)) {
                    var config = new { Project = "TDR2000 Manager", Version = "1.0.0" };
                    File.WriteAllText(p, System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
            } catch (Exception ex) { 
                System.Diagnostics.Debug.WriteLine($"Error generating example config: {ex.Message}");
            }
        }

        private void UpdateActionButtons()
        {
            if (_isBusy) return;
            var s = SourceTree.SelectedItems; var t = TargetTree.SelectedItems; var p = SourceTree.SelectedItem as Node;
            ExtractButton.IsEnabled = s.Count > 0;
            PackButton.IsEnabled = t.Count > 0 && s.Count == 1 && p?.Type == NodeType.Archive;
            CleanButton.IsEnabled = s.Count == 1 && p?.Type == NodeType.Archive;
            MirrorButton.IsEnabled = !string.IsNullOrEmpty(_sourceRoot) && !string.IsNullOrEmpty(_targetRoot);
        }

        private void UpdateStatus(TreeView tree) {
            if (tree.SelectedItem is Node node) {
                if (tree == SourceTree) {
                    string virt = string.IsNullOrEmpty(node.VirtualPath) ? "" : " >> " + node.VirtualPath;
                    StatusText.Text = "Source: " + node.Path + virt;
                } else {
                    string rel = Path.GetRelativePath(_targetRoot, node.Path);
                    if (rel == ".") rel = Path.DirectorySeparatorChar.ToString();
                    StatusText.Text = "Target: " + rel;
                }
            }
        }

        public async void OnBrowseSourceClick(object? sender, RoutedEventArgs e) { var f = await GetFolder(); if (f != null) { _sourceRoot = f; SourcePathBox.Text = f; RefreshTree(SourceTree, f, true); } }
        public async void OnBrowseTargetClick(object? sender, RoutedEventArgs e) { var f = await GetFolder(); if (f != null) { _targetRoot = f; TargetPathBox.Text = f; RefreshTree(TargetTree, f, false); } }
        private async Task<string?> GetFolder() { var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()); return r.Count > 0 ? r[0].Path.LocalPath : null; }
        public void OnRefreshSourceClick(object? sender, RoutedEventArgs e) => RefreshTree(SourceTree, _sourceRoot, true);
        public void OnRefreshTargetClick(object? sender, RoutedEventArgs e) => RefreshTree(TargetTree, _targetRoot, false);
        public void OnOpenSourceClick(object? sender, RoutedEventArgs e) => OpenDir(_sourceRoot);
        public void OnOpenTargetClick(object? sender, RoutedEventArgs e) => OpenDir(_targetRoot);
        private void OpenDir(string p) { if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true }); }

        private void RefreshTree(TreeView tree, string path, bool isSource) {
            if (!Directory.Exists(path)) { tree.ItemsSource = null; return; }
            var root = new Node { Name = new DirectoryInfo(path).Name, Path = path, Type = NodeType.Root, IsExpanded = true };
            if (string.IsNullOrEmpty(root.Name)) root.Name = path;
            PopulateNode(root, isSource);
            tree.ItemsSource = new ObservableCollection<Node> { root };
            UpdateActionButtons();
        }

        private void PopulateNode(Node parent, bool isSource) {
            try {
                var entries = Directory.GetFileSystemEntries(parent.Path);
                var dirFiles = entries.Where(e => e.EndsWith(".dir", StringComparison.OrdinalIgnoreCase)).Select(e => Path.GetFileNameWithoutExtension(e).ToLower()).ToHashSet();
                var list = new List<Node>();
                foreach (var entry in entries) {
                    string name = Path.GetFileName(entry);
                    if (isSource && name.EndsWith(".dir", StringComparison.OrdinalIgnoreCase)) continue;
                    bool isDir = Directory.Exists(entry);
                    string? baseN = name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) ? name[..^4].ToLower() : null;
                    bool isArchive = baseN != null && dirFiles.Contains(baseN);
                    var node = new Node { Name = name, Path = entry, Type = isDir ? NodeType.Dir : (isArchive ? NodeType.Archive : NodeType.File), IsExpanded = false };
                    if (isDir) PopulateNode(node, isSource);
                    if (isArchive) PopulateArchive(node);
                    list.Add(node);
                }
                foreach (var n in list.OrderBy(GetSortOrder).ThenBy(n => n.Name.ToLower())) parent.Children.Add(n);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error populating {parent.Path}: {ex.Message}");
            }
        }

        private int GetSortOrder(Node n) => n.Type switch { NodeType.Root => 0, NodeType.Dir or NodeType.VirtualDir => 1, NodeType.Archive => 2, _ => 3 };

        private void PopulateArchive(Node pakNode) {
            var files = TDRArchive.ParseTrieIndex(pakNode.Path[..^4] + ".dir");
            foreach (var f in files) {
                string[] parts = f.Name.Split('/'); Node current = pakNode;
                for (int i = 0; i < parts.Length; i++) {
                    string part = parts[i];
                    if (i < parts.Length - 1) {
                        var folder = current.Children.FirstOrDefault(c => c.Name == part && c.Type == NodeType.VirtualDir);
                        if (folder == null) { 
                            folder = new Node { Name = part, Type = NodeType.VirtualDir, Path = pakNode.Path, VirtualPath = string.Join("/", parts.Take(i+1)), IsExpanded = false }; 
                            current.Children.Add(folder); 
                        }
                        current = folder;
                    } else current.Children.Add(new Node { Name = part, Type = NodeType.VirtualFile, Path = pakNode.Path, VirtualPath = f.Name, Offset = f.Offset, Size = f.Size });
                }
            }
            SortRecursive(pakNode);
        }

        private void SortRecursive(Node p) {
            if (p.Children.Count == 0) return;
            var sorted = p.Children.OrderBy(GetSortOrder).ThenBy(n => n.Name.ToLower()).ToList();
            p.Children.Clear(); foreach (var s in sorted) { p.Children.Add(s); SortRecursive(s); }
        }

        private void SetBusy(bool busy, string text = "Ready") => Dispatcher.UIThread.Post(() => { 
            _isBusy = busy; OpProgressBar.IsVisible = busy; OpProgressBar.Value = 0; StatusText.Text = text;
            TopToolbar.IsEnabled = !busy; ActionPanel.IsEnabled = !busy; SourceTree.IsEnabled = !busy; TargetTree.IsEnabled = !busy;
            if (!busy) UpdateActionButtons();
        });
        private void UpdateProgress(double v) => Dispatcher.UIThread.Post(() => OpProgressBar.Value = v);

        public async void OnExtractClick(object? sender, RoutedEventArgs e) {
            if (SourceTree.SelectedItem is not Node node || string.IsNullOrEmpty(_targetRoot)) return;
            bool flatten = FlattenCheck.IsChecked == true; SetBusy(true, "Extracting...");
            await Task.Run(() => {
                if (node.Type == NodeType.VirtualFile) ExtractFile(node, _targetRoot, flatten);
                else if (node.Type == NodeType.Archive) {
                    var files = GetVirtualFiles(node).ToList(); string archiveName = Path.GetFileName(node.Path);
                    for (int i = 0; i < files.Count; i++) { ExtractFile(files[i], _targetRoot, flatten, archiveName); UpdateProgress((i + 1.0) / files.Count * 100); }
                } else if (node.Type == NodeType.Dir || node.Type == NodeType.Root) {
                    string dest = node.Type == NodeType.Root ? _targetRoot : Path.Combine(_targetRoot, node.Name);
                    MirrorRecursive(node.Path, dest, true, flatten);
                }
            });
            RefreshTree(TargetTree, _targetRoot, false); SetBusy(false);
        }

        private IEnumerable<Node> GetVirtualFiles(Node p) { if (p.Type == NodeType.VirtualFile) yield return p; foreach (var c in p.Children) foreach (var v in GetVirtualFiles(c)) yield return v; }

        private void ExtractFile(Node node, string outDir, bool flatten, string? archiveName = null) {
            try {
                using (var pak = File.OpenRead(node.Path)) {
                    pak.Seek(node.Offset, SeekOrigin.Begin);
                    string vPath = node.VirtualPath;
                    if (!flatten && !string.IsNullOrEmpty(archiveName)) vPath = TDRArchive.NormalizeArchivePath(vPath, archiveName);
                    string sanitized = TDRArchive.SanitizePath(vPath);
                    string rel = flatten ? Path.GetFileName(sanitized) : sanitized.Replace("/", Path.DirectorySeparatorChar.ToString());
                    string outPath = Path.Combine(outDir, rel); Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    using (var outS = File.Create(outPath)) {
                        // Read header to check if file is encapsulated (zIG/RAW)
                        byte[] header = new byte[8];
                        pak.ReadExactly(header, 0, 8);
                        
                        // Check if this is a zIG/RAW block by attempting to decode the signature
                        byte key = header[0];
                        byte[] sigBytes = { (byte)(header[1] ^ key), (byte)(header[2] ^ key), (byte)(header[3] ^ key) };
                        string sig = System.Text.Encoding.ASCII.GetString(sigBytes);
                        
                        if (sig == "zIG" || sig == "RAW") {
                            // Encapsulated file - decompress entire block
                            pak.Seek(node.Offset, SeekOrigin.Begin);
                            byte[] raw = new byte[node.Size];
                            pak.ReadExactly(raw, 0, (int)node.Size);
                            byte[] d = TDRArchive.DecompressZig(raw);
                            outS.Write(d, 0, d.Length);
                        } else {
                            // Raw file - copy as-is from current position
                            pak.Seek(node.Offset, SeekOrigin.Begin);
                            CopyStream(pak, outS, (int)node.Size);
                        }
                    }
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error extracting {node.VirtualPath}: {ex.Message}");
            }
        }

        private void CopyStream(Stream input, Stream output, int len) { 
            byte[] buf = new byte[81920]; // 80KB buffer is optimal for most disk I/O
            int rem = len; 
            while (rem > 0) { 
                int r = input.Read(buf, 0, Math.Min(rem, buf.Length)); 
                if (r <= 0) break; 
                output.Write(buf, 0, r); 
                rem -= r; 
            } 
        }

        public async void OnPackClick(object? sender, RoutedEventArgs e) {
            if (SourceTree.SelectedItem is not Node targetArchive || targetArchive.Type != NodeType.Archive || TargetTree.SelectedItem is not Node sourceNode) return;
            SetBusy(true, "Packing...");
            await Task.Run(async () => {
                string pPath = targetArchive.Path; string dPath = pPath[..^4] + ".dir"; var idx = TDRArchive.ParseTrieIndex(dPath);
                using (var pak = File.Open(pPath, FileMode.Append)) await PackNode(sourceNode, sourceNode.Path, pak, idx);
                File.WriteAllBytes(dPath, TDRArchive.SerializeTrieIndex(idx));
            });
            RefreshTree(SourceTree, _sourceRoot, true); SetBusy(false);
        }

        private async Task PackNode(Node node, string rootPath, FileStream pak, List<FileEntry> index) {
            if (node.Type == NodeType.Dir || node.Type == NodeType.Root) foreach (var child in node.Children) await PackNode(child, rootPath, pak, index);
            else if (node.Type == NodeType.File) {
                try {
                    string rel = Path.GetRelativePath(Path.GetDirectoryName(rootPath)!, node.Path);
                    byte[] data = await File.ReadAllBytesAsync(node.Path); 
                    byte[] packed = TDRArchive.CreateZigHeader(data, true);
                    TDRArchive.WriteAligned(pak, packed); 
                    index.Add(new FileEntry { Name = TDRArchive.SanitizePath(rel), Offset = (uint)(pak.Position - packed.Length), Size = (uint)packed.Length });
                } catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"Error packing {node.Path}: {ex.Message}");
                }
            }
        }

        public async void OnMirrorClick(object? sender, RoutedEventArgs e) { if (string.IsNullOrEmpty(_sourceRoot) || string.IsNullOrEmpty(_targetRoot)) return; SetBusy(true, "Mirroring..."); await Task.Run(() => MirrorRecursive(_sourceRoot, _targetRoot, true, FlattenCheck.IsChecked == true)); RefreshTree(TargetTree, _targetRoot, false); SetBusy(false, "Mirror complete."); }

        private void MirrorRecursive(string src, string dst, bool isRoot, bool flatten) {
            Directory.CreateDirectory(dst); var entries = Directory.GetFileSystemEntries(src);
            for (int i = 0; i < entries.Length; i++) {
                string e = entries[i]; string name = Path.GetFileName(e);
                if (Directory.Exists(e)) {
                    // Always create subdirectory for folders - don't merge with archive names
                    MirrorRecursive(e, Path.Combine(dst, name), false, flatten);
                }
                else if (name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.ChangeExtension(e, ".dir"))) {
                    var files = TDRArchive.ParseTrieIndex(Path.ChangeExtension(e, ".dir"));
                    foreach (var f in files) {
                        using var pak = File.OpenRead(e); pak.Seek(f.Offset, SeekOrigin.Begin); byte[] raw = new byte[f.Size]; pak.ReadExactly(raw, 0, (int)f.Size);
                        byte[] d = TDRArchive.DecompressZig(raw); string vPath = TDRArchive.NormalizeArchivePath(f.Name, name);
                        string rel = flatten ? Path.GetFileName(vPath) : TDRArchive.SanitizePath(vPath).Replace("/", Path.DirectorySeparatorChar.ToString());
                        string outPath = Path.Combine(dst, rel); Directory.CreateDirectory(Path.GetDirectoryName(outPath)!); File.WriteAllBytes(outPath, d);
                    }
                } else if (!name.EndsWith(".dir", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) {
                    File.Copy(e, Path.Combine(dst, name), true);
                }
                if (isRoot) UpdateProgress((i + 1.0) / entries.Length * 100);
            }
        }

        public async void OnNewArchiveClick(object? sender, RoutedEventArgs e) { 
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "New Archive", DefaultExtension = ".pak" }); 
            if (file != null) { string p = file.Path.LocalPath; File.WriteAllBytes(p, Array.Empty<byte>()); File.WriteAllBytes(Path.ChangeExtension(p, ".dir"), Array.Empty<byte>()); RefreshTree(SourceTree, _sourceRoot, true); } 
        }

        public void OnDeleteFromIndexClick(object? sender, RoutedEventArgs e) {
            Node? target = SourceTree.SelectedItem as Node; if (target == null || target.Type == NodeType.Root || string.IsNullOrEmpty(target.VirtualPath)) return;
            Node? pakNode = FindParentArchive(target); if (pakNode == null) return;
            string dPath = Path.ChangeExtension(pakNode.Path, ".dir"); var idx = TDRArchive.ParseTrieIndex(dPath); string v = target.VirtualPath.ToLower();
            idx.RemoveAll(f => f.Name.ToLower().Equals(v) || f.Name.ToLower().StartsWith(v + "/"));
            File.WriteAllBytes(dPath, TDRArchive.SerializeTrieIndex(idx)); RefreshTree(SourceTree, _sourceRoot, true);
        }

        private Node? FindParentArchive(Node node) => node.Type == NodeType.Archive ? node : (node.Path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) ? new Node { Path = node.Path, Type = NodeType.Archive } : null);
        public void OnDeletePhysicalClick(object? sender, RoutedEventArgs e) { 
            if (TargetTree.SelectedItem is not Node node || node.Type == NodeType.Root) return; 
            try { 
                if (node.Type == NodeType.Dir) Directory.Delete(node.Path, true); 
                else File.Delete(node.Path); 
                RefreshTree(TargetTree, _targetRoot, false); 
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error deleting file: {ex.Message}");
            } 
        }

        public async void OnRebuildArchiveClick(object? sender, RoutedEventArgs e) {
            if (SourceTree.SelectedItem is not Node pakNode || pakNode.Type != NodeType.Archive) return;
            SetBusy(true, "Cleaning...");
            await Task.Run(() => {
                string pPath = pakNode.Path; 
                var idx = TDRArchive.ParseTrieIndex(Path.ChangeExtension(pPath, ".dir")); 
                var newIdx = new List<FileEntry>();
                try {
                    using (var oldP = File.OpenRead(pPath)) 
                    using (var newP = File.Create(pPath + ".tmp")) {
                        for (int i = 0; i < idx.Count; i++) {
                            var f = idx[i]; 
                            oldP.Seek(f.Offset, SeekOrigin.Begin);
                            
                            // Align the new stream
                            long startPos = newP.Position;
                            int pad = (int)((4 - (startPos % 4)) % 4);
                            if (pad > 0) newP.Write(new byte[pad], 0, pad);
                            
                            long alignedPos = newP.Position;
                            CopyStream(oldP, newP, (int)f.Size);
                            
                            newIdx.Add(new FileEntry { 
                                Name = f.Name, 
                                Offset = (uint)alignedPos, 
                                Size = f.Size 
                            });
                            UpdateProgress((i + 1.0) / idx.Count * 100);
                        }
                    }
                    File.Delete(pPath); 
                    File.Move(pPath + ".tmp", pPath); 
                    File.WriteAllBytes(Path.ChangeExtension(pPath, ".dir"), TDRArchive.SerializeTrieIndex(newIdx));
                } catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"Error rebuilding archive: {ex.Message}");
                    if (File.Exists(pPath + ".tmp")) try { File.Delete(pPath + ".tmp"); } catch { }
                }
            });
            RefreshTree(SourceTree, _sourceRoot, true); SetBusy(false, "Optimized.");
        }

        public void OnSourceTreeKeyDown(object? sender, Avalonia.Input.KeyEventArgs e) { if (e.Key == Avalonia.Input.Key.F5 && ExtractButton.IsEnabled) OnExtractClick(null, new RoutedEventArgs()); else if (e.Key == Avalonia.Input.Key.Delete) OnDeleteFromIndexClick(null, new RoutedEventArgs()); }
        public void OnTargetTreeKeyDown(object? sender, Avalonia.Input.KeyEventArgs e) { if (e.Key == Avalonia.Input.Key.F5 && PackButton.IsEnabled) OnPackClick(null, new RoutedEventArgs()); else if (e.Key == Avalonia.Input.Key.Delete) OnDeletePhysicalClick(null, new RoutedEventArgs()); }
        public void OnExitClick(object? sender, RoutedEventArgs e) => Close();
    }
}