import os
import re

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
            lines = f.readlines()
            for line in lines:
                if "- {fileID:" in line and "guid:" in line:
                    match = re.search(r'guid:\s*([a-f0-9]{32})', line)
                    if match:
                        guids.append(match.group(1))
    except Exception as e:
        pass
    return guids

def check_level(level_path, meta_guids):
    missing = []
    try:
        with open(level_path, 'r', encoding='utf-8') as f:
            content = f.read()
            # find all guids in this level data
            guids_in_level = re.findall(r'guid:\s*([a-f0-9]{32})', content)
            # ignore the script guid usually at the top
            script_guid = "b028b703fbc9f754ab24c7e4664569c0"
            for g in set(guids_in_level):
                if g == script_guid:
                    continue
                if g not in meta_guids:
                    missing.append(g)
    except Exception as e:
        pass
    return missing

def main():
    print("Building GUID map...")
    meta_guids = get_all_meta_guids("Assets")
    print(f"Found {len(meta_guids)} GUIDs in Assets.")
    
    level_guids = get_level_order_guids("Assets/LevelOrder.asset")
    print(f"Found {len(level_guids)} levels in LevelOrder.asset.")
    
    issues_found = False
    for idx, l_guid in enumerate(level_guids):
        if l_guid not in meta_guids:
            print(f"Level {idx+1} (GUID {l_guid}) is MISSING entirely!")
            issues_found = True
            continue
            
        level_path = meta_guids[l_guid]
        missing_pieces = check_level(level_path, meta_guids)
        if missing_pieces:
            issues_found = True
            print(f"Level {idx+1} ({os.path.basename(level_path)}) has MISSING prefabs:")
            for m in missing_pieces:
                print(f"  - {m}")
                
    if not issues_found:
        print("All levels in LevelOrder.asset are perfectly fine. No missing prefabs!")

if __name__ == '__main__':
    main()
