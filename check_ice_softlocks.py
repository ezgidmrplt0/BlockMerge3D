import os
import re

def parse_vector3int_list(content, array_name):
    # Matches lines like:
    #   array_name:
    #   - {x: 1, y: 0, z: 2}
    cells = []
    lines = content.split('\n')
    in_array = False
    for line in lines:
        if line.startswith(f"  {array_name}:"):
            in_array = True
            continue
        if in_array:
            if line.startswith("  - {x:"):
                # extract x, y, z
                match = re.search(r'\{x:\s*(-?\d+),\s*y:\s*(-?\d+),\s*z:\s*(-?\d+)\}', line)
                if match:
                    cells.append((int(match.group(1)), int(match.group(2)), int(match.group(3))))
            elif not line.startswith("  -"):
                # end of array
                break
    return cells

def get_all_meta_guids(root_dir):
    meta_guids = {}
    for subdir, dirs, files in os.walk(root_dir):
        for file in files:
            if file.endswith(".meta"):
                filepath = os.path.join(subdir, file)
                try:
                    with open(filepath, 'r', encoding='utf-8') as f:
                        content = f.read()
                        match = re.search(r'guid:\s*([a-f0-9]{32})', content)
                        if match:
                            meta_guids[match.group(1)] = filepath[:-5]
                except Exception as e:
                    pass
    return meta_guids

def get_level_order_guids(level_order_path):
    guids = []
    try:
        with open(level_order_path, 'r', encoding='utf-8') as f:
            for line in f:
                if "- {fileID:" in line and "guid:" in line:
                    match = re.search(r'guid:\s*([a-f0-9]{32})', line)
                    if match:
                        guids.append(match.group(1))
    except Exception as e:
        pass
    return guids

def get_main_shape_guid(level_path):
    try:
        with open(level_path, 'r', encoding='utf-8') as f:
            content = f.read()
            match = re.search(r'mainShapePrefab:\s*\{[^}]*guid:\s*([a-f0-9]{32})', content)
            if match:
                return match.group(1)
    except Exception as e:
        pass
    return None

def check_unmeltable_ice(prefab_path):
    try:
        with open(prefab_path, 'r', encoding='utf-8') as f:
            content = f.read()
    except Exception as e:
        return None
        
    occupied = parse_vector3int_list(content, "occupiedCells")
    prefilled = parse_vector3int_list(content, "prefilledCells")
    frozen = parse_vector3int_list(content, "frozenCells")
    
    if not frozen:
        return [] # No ice, no problem
        
    occ_set = set(occupied)
    pref_set = set(prefilled)
    froz_set = set(frozen)
    
    empty_set = occ_set - pref_set - froz_set
    
    def get_horizontal_neighbors(c):
        x, y, z = c
        return [(x-1, y, z), (x+1, y, z), (x, y, z-1), (x, y, z+1)]
        
    while True:
        found = False
        to_remove = set()
        for f in froz_set:
            neighbors = get_horizontal_neighbors(f)
            # If any neighbor is in empty_set, this ice can be melted
            can_melt = any(n in empty_set for n in neighbors)
            if can_melt:
                to_remove.add(f)
                found = True
        
        for f in to_remove:
            froz_set.remove(f)
            empty_set.add(f)
            
        if not found:
            break
            
    return list(froz_set)

def main():
    meta_guids = get_all_meta_guids("Assets")
    level_guids = get_level_order_guids("Assets/LevelOrder.asset")
    
    print("Checking levels for unmeltable ice...")
    for idx, l_guid in enumerate(level_guids):
        level_num = idx + 1
        if l_guid not in meta_guids:
            continue
            
        level_path = meta_guids[l_guid]
        shape_guid = get_main_shape_guid(level_path)
        if not shape_guid or shape_guid not in meta_guids:
            continue
            
        shape_path = meta_guids[shape_guid]
        unmeltable = check_unmeltable_ice(shape_path)
        
        if unmeltable:
            print(f"Level {level_num} ({os.path.basename(level_path)}) has UNMELTABLE ICE at: {unmeltable}")

if __name__ == '__main__':
    main()
