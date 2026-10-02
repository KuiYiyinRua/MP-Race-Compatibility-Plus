import hashlib
import json
import shutil
import zipfile
from pathlib import Path
from analyze_desync_batch import analyze

repo = Path(__file__).resolve().parents[1]
root = repo / 'BuildValidation/DesyncEvidence/Desync338-343_20261001'
root.mkdir(parents=True, exist_ok=True)
inventory = []
for number in range(338, 344):
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
            assert (folder / item.filename).resolve().is_relative_to(folder.resolve())
        if not folder.exists():
            archive.extractall(folder)
        inventory.append(dict(bundle=source.name, sha256=digest,
                              files=[dict(name=i.filename, size=i.file_size) for i in archive.infolist()]))
(root / 'inventory.json').write_text(json.dumps(inventory, indent=2), encoding='utf-8')
rows = analyze(root)
for row in rows:
    print(row['bundle'], 'last=', row['info']['Last Valid Tick - Local'])
    for peer in ('host','local'):
        first = row['traces'][peer]['first']
        if first:
            print(peer, first['tick'], first['hash'], first['label'])
            print('\n'.join(first['stack'][:9]))
