# -*- coding: utf-8 -*-
"""
TDR2000 PAK Manager
Utility for Torc Engine .PAK/.DIR archives with Trie indexing and zIG compression.
"""

import os
import struct
import zlib
import shutil
import random
import logging
import tkinter as tk
from tkinter import ttk, messagebox, filedialog
from threading import Thread

logging.basicConfig(level=logging.INFO, format='%(asctime)s [%(levelname)s] %(message)s')

class TorcEngine:
    @staticmethod
    def rotate_right8(val, bits):
        return ((val >> bits) | (val << (8 - bits))) & 0xFF

    @staticmethod
    def create_zig_header(data, compress=True):
        """
        zIG format: [key:u8][sig_encrypted:3bytes][size_encrypted:u32][payload]
        sig = "zIG" (compressed) or "RAW" (uncompressed)
        size = original uncompressed size
        meta_key = ror8(key, 3) used for size encryption
        """
        key = random.randint(1, 254)
        final_data = zlib.compress(data, 9) if compress else data
        sig = b"zIG" if compress else b"RAW"
        header = bytearray([key])
        for b in sig: header.append(b ^ key)
        meta_key = TorcEngine.rotate_right8(key, 3)
        header.extend([b ^ meta_key for b in struct.pack("<I", len(data))])
        return header + final_data

    @staticmethod
    def decompress_zig(raw_data):
        if len(raw_data) < 8: return raw_data
        key = raw_data[0]
        sig = bytes([raw_data[1]^key, raw_data[2]^key, raw_data[3]^key])
        if sig != b"zIG": return raw_data
        try: return zlib.decompress(raw_data[8:], -15)
        except Exception as e:
            logging.warning(f"zIG decompression failed (wbits=-15): {e}")
            try: return zlib.decompress(raw_data[8:])
            except Exception as e2:
                logging.error(f"zIG decompression failed (default): {e2}")
                return raw_data

    @staticmethod
    def get_zig_metadata(pak_path, offset, size):
        try:
            with open(pak_path, "rb") as f:
                f.seek(offset); h = f.read(8)
                if len(h) < 8: return None
                key = h[0]; meta_key = TorcEngine.rotate_right8(key, 3)
                orig_sz = struct.unpack("<I", bytes([h[i]^meta_key for i in range(4, 8)]))[0]
                return {"orig_size": orig_sz, "key": key}
        except Exception as e:
            logging.error(f"Failed to read zIG metadata at {pak_path}+0x{offset:X}: {e}")
            return None

    @staticmethod
    def parse_trie_index(path):
        """
        Trie format: recursive structure of [char:u8][flags:u8][offset:u32,size:u32]?
        flags: 0x08=file_entry, 0x40=has_children, 0x80=has_sibling
        """
        files = []
        if not os.path.exists(path): return files
        try:
            with open(path, "rb") as f: data = f.read()
        except Exception as e:
            logging.error(f"Failed to read trie index {path}: {e}")
            return []
        pos = 0
        def walk(prefix):
            nonlocal pos
            while pos < len(data):
                if pos + 2 > len(data): break
                c, flags = data[pos], data[pos+1]; pos += 2
                name = prefix + chr(c)
                if flags & 0x08:
                    if pos + 8 > len(data): break
                    off, sz = struct.unpack("<II", data[pos:pos+8]); pos += 8
                    files.append({'name': name, 'offset': off, 'size': sz})
                if flags & 0x40: walk(name)
                if not (flags & 0x80): break
        walk("")
        logging.info(f"Parsed {len(files)} files from {path}")
        return files

    @staticmethod
    def serialize_trie_index(files_list):
        unique_files = {f['name']: f for f in files_list}
        tree = {}
        for name in sorted(unique_files.keys()):
            f = unique_files[name]
            curr = tree
            for i, char in enumerate(name):
                if char not in curr: curr[char] = {'flags': 0, 'meta': None, 'children': {}}
                if i == len(name) - 1:
                    curr[char]['flags'] |= 0x08; curr[char]['meta'] = (f['offset'], f['size'])
                else:
                    curr[char]['flags'] |= 0x40; curr = curr[char]['children']
        def serialize(nodes):
            out = bytearray(); items = list(nodes.items())
            for i, (char, info) in enumerate(items):
                flags = info['flags']
                if i < len(items) - 1: flags |= 0x80
                out.append(ord(char)); out.append(flags)
                if info['meta']: out.extend(struct.pack("<II", *info['meta']))
                if info['children']: out.extend(serialize(info['children']))
            return out
        return serialize(tree)

class TDRPAKManager:
    def __init__(self, root):
        self.root = root
        self.root.title("TDR2000 PAK Manager")
        self.root.geometry("1200x750")
        
        self.src_root = ""
        self.dst_root = os.path.join(os.path.expanduser("~"), "Documents", "TDR_Extracted")
        self.include_extras = tk.BooleanVar(value=True)
        self.archive_folders = tk.BooleanVar(value=True)
        self.flatten_extract = tk.BooleanVar(value=False)
        
        self.setup_menu()
        self.setup_ui()
        
        if os.path.exists(self.dst_root):
            self.refresh_tree(self.dst_tree, self.dst_root, False)

    def setup_menu(self):
        menubar = tk.Menu(self.root)
        file_menu = tk.Menu(menubar, tearoff=0)
        file_menu.add_command(label="New Archive...", command=self.action_new_pak)
        file_menu.add_separator()
        file_menu.add_command(label="Open Source...", command=self.load_src)
        file_menu.add_command(label="Open Target...", command=self.load_dst)
        file_menu.add_separator()
        file_menu.add_command(label="Exit", command=self.root.quit)
        menubar.add_cascade(label="File", menu=file_menu)
        self.root.config(menu=menubar)

    def setup_ui(self):
        top = ttk.Frame(self.root, padding=10)
        top.pack(fill=tk.X)
        
        self.src_var = tk.StringVar()
        self.dst_var = tk.StringVar(value=self.dst_root)
        
        ttk.Label(top, text="Source:").grid(row=0, column=0, sticky='w', padx=(0,5))
        ttk.Entry(top, textvariable=self.src_var).grid(row=0, column=1, sticky='ew', padx=5)
        ttk.Button(top, text="Browse", command=self.load_src).grid(row=0, column=2)
        
        ttk.Label(top, text="Target:").grid(row=1, column=0, sticky='w', padx=(0,5), pady=(5,0))
        ttk.Entry(top, textvariable=self.dst_var).grid(row=1, column=1, sticky='ew', padx=5, pady=(5,0))
        ttk.Button(top, text="Browse", command=self.load_dst).grid(row=1, column=2, pady=(5,0))
        
        top.columnconfigure(1, weight=1)

        paned = ttk.PanedWindow(self.root, orient=tk.HORIZONTAL)
        paned.pack(fill=tk.BOTH, expand=True, padx=10, pady=(0,10))

        src_frame = ttk.LabelFrame(paned, text="Source", padding=5)
        self.src_tree = ttk.Treeview(src_frame, show="tree", selectmode="extended")
        self.src_tree.tag_configure("extra", foreground="#888888")
        self.src_tree.tag_configure("archive", foreground="#0066cc")
        
        src_scroll_y = ttk.Scrollbar(src_frame, orient="vertical", command=self.src_tree.yview)
        src_scroll_x = ttk.Scrollbar(src_frame, orient="horizontal", command=self.src_tree.xview)
        self.src_tree.configure(yscrollcommand=src_scroll_y.set, xscrollcommand=src_scroll_x.set)
        
        self.src_tree.grid(row=0, column=0, sticky='nsew')
        src_scroll_y.grid(row=0, column=1, sticky='ns')
        src_scroll_x.grid(row=1, column=0, sticky='ew')
        
        src_frame.columnconfigure(0, weight=1)
        src_frame.rowconfigure(0, weight=1)
        
        self.src_tree.bind("<<TreeviewOpen>>", self.on_expand)
        self.src_tree.bind("<<TreeviewSelect>>", self.on_select)
        
        btn_frame = ttk.Frame(src_frame)
        btn_frame.grid(row=2, column=0, columnspan=2, sticky='ew', pady=(5,0))
        ttk.Button(btn_frame, text="New Archive", command=self.action_new_pak).pack(side=tk.LEFT)
        
        paned.add(src_frame, weight=1)

        mid_frame = ttk.Frame(paned, padding=20)
        ttk.Button(mid_frame, text="Extract →", command=self.action_extract, width=14).pack(pady=5)
        ttk.Button(mid_frame, text="← Pack Into", command=self.action_pack, width=14).pack(pady=5)
        ttk.Button(mid_frame, text="✕ Delete", command=self.action_delete, width=14).pack(pady=5)
        ttk.Separator(mid_frame, orient='horizontal').pack(fill='x', pady=15)
        ttk.Button(mid_frame, text="Full Mirror", command=self.action_mirror, width=14).pack(pady=5)
        ttk.Separator(mid_frame, orient='horizontal').pack(fill='x', pady=15)
        ttk.Checkbutton(mid_frame, text="Include loose files", variable=self.include_extras).pack(anchor='w', pady=2)
        ttk.Checkbutton(mid_frame, text="Show archive folders", variable=self.archive_folders, command=self.force_refresh_paks).pack(anchor='w', pady=2)
        ttk.Checkbutton(mid_frame, text="Flatten on extract", variable=self.flatten_extract).pack(anchor='w', pady=2)
        paned.add(mid_frame, weight=0)

        dst_frame = ttk.LabelFrame(paned, text="Target", padding=5)
        self.dst_tree = ttk.Treeview(dst_frame, show="tree", selectmode="extended")
        
        dst_scroll_y = ttk.Scrollbar(dst_frame, orient="vertical", command=self.dst_tree.yview)
        dst_scroll_x = ttk.Scrollbar(dst_frame, orient="horizontal", command=self.dst_tree.xview)
        self.dst_tree.configure(yscrollcommand=dst_scroll_y.set, xscrollcommand=dst_scroll_x.set)
        
        self.dst_tree.grid(row=0, column=0, sticky='nsew')
        dst_scroll_y.grid(row=0, column=1, sticky='ns')
        dst_scroll_x.grid(row=1, column=0, sticky='ew')
        
        dst_frame.columnconfigure(0, weight=1)
        dst_frame.rowconfigure(0, weight=1)
        
        self.dst_tree.bind("<<TreeviewOpen>>", self.on_expand_dst)
        paned.add(dst_frame, weight=1)

        self.status_var = tk.StringVar(value="Ready")
        status_bar = ttk.Label(self.root, textvariable=self.status_var, relief=tk.SUNKEN, anchor='w')
        status_bar.pack(side=tk.BOTTOM, fill=tk.X)

    def load_src(self):
        d = filedialog.askdirectory(title="Select Source Folder")
        if d:
            self.src_root = d
            self.src_var.set(d)
            self.refresh_tree(self.src_tree, d, True)
            
    def load_dst(self):
        d = filedialog.askdirectory(title="Select Target Folder")
        if d:
            self.dst_root = d
            self.dst_var.set(d)
            self.refresh_tree(self.dst_tree, d, False)

    def refresh_tree(self, tree, path, is_src):
        for i in tree.get_children(): tree.delete(i)
        if not path or not os.path.exists(path): return
        root_node = tree.insert("", "end", text=os.path.basename(path), 
                               values=(path, "root", os.path.basename(path)), open=True)
        self.populate_node(tree, root_node, path, is_src)

    def populate_node(self, tree, parent, path, is_src):
        if not os.path.isdir(path): return
        items = os.listdir(path)
        dir_files = {i.lower()[:-4] for i in items if i.lower().endswith(".dir")}
        
        def sort_key(name):
            full_path = os.path.join(path, name)
            if os.path.isdir(full_path): return (0, name.lower())
            base = name.lower()[:-4] if name.lower().endswith(".pak") else None
            if base and base in dir_files: return (2, name.lower())
            return (1, name.lower())
        
        for item in sorted(items, key=sort_key):
            if is_src and item.lower().endswith(".dir"): continue
            
            full_path = os.path.join(path, item)
            is_dir = os.path.isdir(full_path)
            base = item.lower()[:-4] if item.lower().endswith(".pak") else None
            is_archive = base and base in dir_files
            
            ntype = "dir" if is_dir else ("archive" if is_archive else "file")
            tags = ("archive",) if is_archive else (("extra",) if ntype == "file" else ())
            icon = "[DIR]" if is_dir else ("[PAK]" if is_archive else "[FILE]")
            
            node = tree.insert(parent, 'end', text=f"{icon} {item}", 
                             values=(full_path, ntype, item), tags=tags)
            if is_dir or is_archive:
                tree.insert(node, 'end', text="...")

    def on_expand(self, event):
        node = self.src_tree.focus()
        values = self.src_tree.item(node, "values")
        
        for child in self.src_tree.get_children(node):
            if self.src_tree.item(child, "text") == "...":
                self.src_tree.delete(child)
            else:
                return
        
        if values[1] in ["dir", "root"]:
            self.populate_node(self.src_tree, node, values[0], True)
        elif values[1] == "archive":
            files = TorcEngine.parse_trie_index(values[0][:-4] + ".dir")
            if self.archive_folders.get():
                self.populate_archive_nested(node, files, values[0])
            else:
                for f in files:
                    self.src_tree.insert(node, 'end', text=f"[FILE] {f['name']}", 
                                       values=(values[0], "vfile", f['offset'], f['size'], f['name']))

    def populate_archive_nested(self, parent, files, pak_path):
        tree_dict = {}
        for f in files:
            parts = f['name'].split('/')
            current = tree_dict
            for part in parts[:-1]:
                if part not in current or not isinstance(current[part], dict):
                    current[part] = {}
                current = current[part]
            current[parts[-1]] = f
        
        def build_tree(parent_node, node_dict):
            for key, value in sorted(node_dict.items()):
                if isinstance(value, dict) and 'offset' not in value:
                    folder_node = self.src_tree.insert(parent_node, 'end', text=f"[DIR] {key}", 
                                                      values=(pak_path, "vdir", 0, 0, key))
                    build_tree(folder_node, value)
                else:
                    self.src_tree.insert(parent_node, 'end', text=f"[FILE] {key}", 
                                       values=(pak_path, "vfile", value['offset'], value['size'], value['name']))
        
        build_tree(parent, tree_dict)

    def force_refresh_paks(self):
        for node in self.src_tree.get_children():
            self._recursive_refresh_paks(node)

    def _recursive_refresh_paks(self, node):
        values = self.src_tree.item(node, "values")
        if values and values[1] == "archive" and self.src_tree.item(node, "open"):
            for child in self.src_tree.get_children(node):
                self.src_tree.delete(child)
            self.src_tree.insert(node, 'end', text="...")
            self.src_tree.item(node, open=False)
            self.src_tree.item(node, open=True)
        else:
            for child in self.src_tree.get_children(node):
                self._recursive_refresh_paks(child)

    def on_expand_dst(self, event):
        node = self.dst_tree.focus()
        values = self.dst_tree.item(node, "values")
        
        for child in self.dst_tree.get_children(node):
            if self.dst_tree.item(child, "text") == "...":
                self.dst_tree.delete(child)
            else:
                return
        
        self.populate_node(self.dst_tree, node, values[0], False)

    def on_select(self, event):
        node = self.src_tree.focus()
        values = self.src_tree.item(node, "values")
        
        if len(values) > 4 and values[1] == "vfile":
            pak_path, offset, size, name = values[0], int(values[2]), int(values[3]), values[4]
            info = TorcEngine.get_zig_metadata(pak_path, offset, size)
            
            if info:
                ratio = (size / info['orig_size']) * 100 if info['orig_size'] > 0 else 0
                msg = f"{name} | Compressed: {size:,} bytes | Original: {info['orig_size']:,} bytes ({ratio:.1f}%) | Offset: 0x{offset:X} | Key: 0x{info['key']:02X}"
            else:
                msg = f"{name} | Size: {size:,} bytes | Offset: 0x{offset:X}"
            
            self.status_var.set(msg)
        else:
            self.status_var.set("Ready")

    def action_new_pak(self):
        node = self.src_tree.focus()
        values = self.src_tree.item(node, "values")
        base_dir = values[0] if (values and values[1] in ["dir", "root"]) else self.src_root
        
        filepath = filedialog.asksaveasfilename(
            initialdir=base_dir,
            title="Create New Archive",
            defaultextension=".pak",
            filetypes=[("PAK Archive", "*.pak"), ("All Files", "*.*")]
        )
        
        if filepath:
            if not filepath.lower().endswith(".pak"):
                filepath += ".pak"
            with open(filepath, "wb") as f:
                pass
            with open(filepath[:-4] + ".dir", "wb") as f:
                pass
            logging.info(f"Created new archive: {filepath}")
            self.refresh_tree(self.src_tree, self.src_root, True)

    def action_extract(self):
        selections = self.src_tree.selection()
        if not selections:
            messagebox.showinfo("Extract", "No items selected")
            return
        
        count = 0
        for node in selections:
            values = self.src_tree.item(node, "values")
            text = self.src_tree.item(node, "text")
            name = text.split("] ", 1)[1] if "]" in text else text
            
            if values[1] == "vfile":
                self.extract_vfile(values[0], values[2], values[3], values[4], self.dst_root)
                count += 1
            elif values[1] == "archive":
                self.unpack_pak(values[0], os.path.join(self.dst_root, name[:-4]))
                count += 1
            elif values[1] == "dir":
                self.mirror_recursive(values[0], os.path.join(self.dst_root, values[2]))
                count += 1
            elif values[1] == "file":
                shutil.copy2(values[0], self.dst_root)
                count += 1
        
        self.refresh_tree(self.dst_tree, self.dst_root, False)
        self.status_var.set(f"Extracted {count} item(s)")

    def extract_vfile(self, pak_path, offset, size, name, output_dir):
        safe_name = os.path.normpath(name).lstrip(os.sep).replace('..', '__')
        
        if self.flatten_extract.get():
            safe_name = os.path.basename(safe_name)
        
        with open(pak_path, "rb") as pak_file:
            pak_file.seek(int(offset))
            raw_data = pak_file.read(int(size))
            decompressed = TorcEngine.decompress_zig(raw_data)
            
            output_path = os.path.join(output_dir, safe_name.replace("/", os.sep))
            os.makedirs(os.path.dirname(output_path), exist_ok=True)
            
            with open(output_path, "wb") as out_file:
                out_file.write(decompressed)
            
            logging.info(f"Extracted: {safe_name} ({len(decompressed):,} bytes)")

    def unpack_pak(self, pak_path, output_folder):
        files = TorcEngine.parse_trie_index(pak_path[:-4] + ".dir")
        for f in files:
            self.extract_vfile(pak_path, f['offset'], f['size'], f['name'], output_folder)

    def action_pack(self):
        selected = self.src_tree.focus()
        selected_values = self.src_tree.item(selected, "values")
        
        if not selected_values or selected_values[1] != "archive":
            messagebox.showwarning("Pack", "Select a [PAK] archive in the source tree")
            return
        
        compress = messagebox.askyesno("Compression", "Enable zIG compression?")
        pak_path = selected_values[0]
        dir_path = pak_path[:-4] + ".dir"
        
        existing_files = TorcEngine.parse_trie_index(dir_path)
        existing_names = {f['name'] for f in existing_files}
        new_files = []
        
        dst_selections = self.dst_tree.selection()
        if not dst_selections:
            messagebox.showinfo("Pack", "No files selected in target tree")
            return
        
        for node in dst_selections:
            src_path, node_type, relative_name = self.dst_tree.item(node, "values")[:3]
            
            if node_type == "dir":
                for root, _, filenames in os.walk(src_path):
                    for filename in filenames:
                        file_path = os.path.join(root, filename)
                        rel_path = os.path.relpath(file_path, os.path.dirname(src_path)).replace(os.sep, '/')
                        
                        if rel_path in existing_names:
                            if not messagebox.askyesno("Conflict", f"'{rel_path}' already exists. Overwrite?"):
                                continue
                            existing_files = [f for f in existing_files if f['name'] != rel_path]
                        
                        new_files.append((file_path, rel_path))
            
            elif os.path.isfile(src_path):
                if relative_name in existing_names:
                    if not messagebox.askyesno("Conflict", f"'{relative_name}' already exists. Overwrite?"):
                        continue
                    existing_files = [f for f in existing_files if f['name'] != relative_name]
                
                new_files.append((src_path, relative_name))
        
        if not new_files:
            messagebox.showinfo("Pack", "No files to pack")
            return
        
        with open(pak_path, "ab") as pak_file:
            for file_path, rel_name in new_files:
                with open(file_path, "rb") as f:
                    data = f.read()
                
                packed = TorcEngine.create_zig_header(data, compress)
                
                current_pos = pak_file.tell()
                padding = (4 - (current_pos % 4)) % 4
                if padding:
                    pak_file.write(b"\x00" * padding)
                
                offset = pak_file.tell()
                pak_file.write(packed)
                
                existing_files.append({'name': rel_name, 'offset': offset, 'size': len(packed)})
                logging.info(f"Packed: {rel_name} at 0x{offset:X} (padded: {padding} bytes)")
        
        with open(dir_path, "wb") as dir_file:
            dir_file.write(TorcEngine.serialize_trie_index(existing_files))
        
        messagebox.showinfo("Success", f"Added {len(new_files)} file(s) to archive")
        self.refresh_tree(self.src_tree, self.src_root, True)
        self.status_var.set(f"Packed {len(new_files)} file(s)")

    def action_delete(self):
        selections = self.src_tree.selection()
        if not selections:
            messagebox.showinfo("Delete", "No items selected")
            return
        
        archives_to_update = {}
        for node in selections:
            values = self.src_tree.item(node, "values")
            if values[1] in ["vfile", "vdir"]:
                pak_path = values[0]
                if pak_path not in archives_to_update:
                    archives_to_update[pak_path] = []
                archives_to_update[pak_path].append((values[4], values[1]))
        
        if not archives_to_update:
            messagebox.showinfo("Delete", "Select files or folders from archives")
            return
        
        if not messagebox.askyesno("Delete", f"Remove selected items from {len(archives_to_update)} archive(s)?\n\nNote: PAK file size will not shrink."):
            return
        
        for pak_path, targets in archives_to_update.items():
            dir_path = pak_path[:-4] + ".dir"
            files = TorcEngine.parse_trie_index(dir_path)
            
            new_files = []
            removed_count = 0
            for f in files:
                keep = True
                for target_path, target_type in targets:
                    if target_type == "vfile" and f['name'] == target_path:
                        keep = False
                        removed_count += 1
                        break
                    if target_type == "vdir" and f['name'].startswith(target_path + "/"):
                        keep = False
                        removed_count += 1
                        break
                if keep:
                    new_files.append(f)
            
            with open(dir_path, "wb") as dir_file:
                dir_file.write(TorcEngine.serialize_trie_index(new_files))
            
            logging.info(f"Removed {removed_count} entries from {os.path.basename(pak_path)}")
        
        messagebox.showinfo("Success", "Items removed from index")
        self.refresh_tree(self.src_tree, self.src_root, True)
        self.status_var.set(f"Deleted from {len(archives_to_update)} archive(s)")

    def action_mirror(self):
        if not self.src_root:
            messagebox.showwarning("Mirror", "No source folder selected")
            return
        
        if self.src_root == self.dst_root:
            messagebox.showerror("Error", "Source and target folders must be different")
            return
        
        self.mirror_recursive(self.src_root, self.dst_root)
        self.refresh_tree(self.dst_tree, self.dst_root, False)
        messagebox.showinfo("Mirror", "Mirror operation complete")
        self.status_var.set("Mirror complete")

    def mirror_recursive(self, source, destination):
        if not os.path.exists(destination):
            os.makedirs(destination)
        
        items = os.listdir(source)
        dir_files = {i.lower()[:-4] for i in items if i.lower().endswith(".dir")}
        
        for item in items:
            src_path = os.path.join(source, item)
            dst_path = os.path.join(destination, item)
            
            if os.path.isdir(src_path):
                self.mirror_recursive(src_path, dst_path)
            else:
                base = item.lower()[:-4] if item.lower().endswith(".pak") else None
                is_archive = base and base in dir_files
                
                if is_archive:
                    self.unpack_pak(src_path, dst_path[:-4])
                elif not item.lower().endswith(".dir") and self.include_extras.get():
                    shutil.copy2(src_path, dst_path)

if __name__ == "__main__":
    root = tk.Tk()
    app = TDRPAKManager(root)
    root.mainloop()
