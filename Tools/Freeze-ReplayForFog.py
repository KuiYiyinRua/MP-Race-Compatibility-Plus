"""Freeze one MP snapshot and merge its map XML into a disposable vanilla save."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
import zipfile

p = argparse.ArgumentParser()
p.add_argument('source', type=Path)
p.add_argument('destination', type=Path)
args = p.parse_args()
args.destination.mkdir(parents=True, exist_ok=False)
frozen = args.destination / 'input.zip'
shutil.copyfile(args.source, frozen)
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
assert sha(args.source) == sha(frozen)
with zipfile.ZipFile(frozen) as z:
    info = ET.fromstring(z.read('info'))
    sections = info.findall('sections/li')
    assert len(sections) == 1, 'Require a single saved snapshot; no command replay is discarded'
    assert sections[0].findtext('start') == sections[0].findtext('end'), 'Require a snapshot without subsequent commands'
    world_names = [n for n in z.namelist() if n.startswith('world/') and n.endswith('_save')]
    assert len(world_names) == 1
    world = ET.fromstring(z.read(world_names[0]))
    maps = world.find('game/maps')
    assert maps is not None and len(maps) == 0
    map_names = sorted(n for n in z.namelist() if n.startswith('maps/') and n.endswith('_save'))
    for n in map_names:
        maps.append(ET.fromstring(z.read(n)))
    rws = args.destination / 'FogInput.rws'
    ET.ElementTree(world).write(rws, encoding='utf-8', xml_declaration=True)
    record = dict(source=str(args.source.resolve()), frozen_zip_sha256=sha(frozen), rws_sha256=sha(rws),
                  mods=len(info.findall('modIds/li')), snapshot_tick=int(sections[0].findtext('start')),
                  maps=len(map_names), world_entry=world_names[0], map_entries=map_names,
                  scope='Merged XML copy only; original snapshot and all original saves are unchanged')
    (args.destination / 'provenance.json').write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: record[k] for k in ('mods', 'maps', 'snapshot_tick', 'frozen_zip_sha256')}, ensure_ascii=True))
