from pathlib import Path
import zipfile,shutil,hashlib,json
root=Path.cwd();run=root/'BuildValidation/DesyncEvidence/Desync449-450_20261011';run.mkdir(exist_ok=False)
records=[]
for n in (449,450):
 src=root/f'Desync-{n}.zip';dst=run/src.name;shutil.copy2(src,dst)
 sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
 assert sha(src)==sha(dst)
 with zipfile.ZipFile(dst) as z:
  items=[dict(name=i.filename,bytes=i.file_size) for i in z.infolist()]
  for i in z.infolist(): assert not Path(i.filename).is_absolute() and '..' not in Path(i.filename).parts
  z.extractall(run/f'Desync-{n}')
 records.append(dict(bundle=src.name,sha256=sha(src),files=items))
 print(n,items)
 for p in (run/f'Desync-{n}').rglob('*'):
  if p.is_file() and 'desync_info' in p.name:print(p.read_text(encoding='utf-8-sig',errors='replace'))
(run/'inventory.json').write_text(json.dumps(records,indent=2)+'\n')
