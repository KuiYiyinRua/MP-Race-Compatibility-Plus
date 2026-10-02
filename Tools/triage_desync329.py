import hashlib
import json
import shutil
import zipfile
from pathlib import Path
from analyze_desync_batch import analyze

repo = Path(__file__).resolve().parents[1]
root = repo / 'BuildValidation/DesyncEvidence/Desync325-339_20261001'
root.mkdir(parents=True, exist_ok=True)
inventory = []
for number in range(325, 340):
    source = repo / f'Desync-{number}.zip'
    if not source.exists():
        inventory.append(dict(bundle=source.name, missing=True))
        continue
    dest = root / source.name
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if not dest.exists():
        shutil.copy2(source, dest)
    assert hashlib.sha256(dest.read_bytes()).hexdigest() == digest
    folder = root / source.stem
    with zipfile.ZipFile(dest) as archive:
        for item in archive.infolist():
            target = (folder / item.filename).resolve()
            assert target.is_relative_to(folder.resolve())
        if not folder.exists():
            archive.extractall(folder)
        inventory.append(dict(bundle=source.name, sha256=digest,
                              files=[dict(name=i.filename, size=i.file_size) for i in archive.infolist()]))
(root / 'inventory.json').write_text(json.dumps(inventory, indent=2), encoding='utf-8')
rows = analyze(root)
for row in rows:
    print(row['bundle'], 'info=', {k:v for k,v in row['info'].items() if any(s in k.lower() for s in ['tick','version','player','map','async','faction'])})
    for peer in ('host','local'):
        first = row['traces'][peer]['first']
        if first:
            print(peer, {k:v for k,v in first.items() if k != 'stack'})
            print('\n'.join(first['stack'][:12]))
    print('metadata=', row['compatibility_metadata'])
