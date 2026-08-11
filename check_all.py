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

def check_level(level_path, meta_guids):
    missing = []
    try:
        with open(level_path, 'r', encoding='utf-8') as f:
            content = f.read()
            guids_in_level = re.findall(r'guid:\s*([a-f0-9]{32})', content)
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
    meta_guids = get_all_meta_guids("Assets")
    for subdir, dirs, files in os.walk("Assets/Levels"):
        for file in files:
            if file.endswith("_LevelData.asset"):
                level_path = os.path.join(subdir, file)
                missing = check_level(level_path, meta_guids)
                if missing:
                    print(f"{file} has {len(missing)} missing prefabs.")

if __name__ == '__main__':
    main()
