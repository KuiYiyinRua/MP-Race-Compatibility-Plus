from pathlib import Path
import re,json
from PIL import Image
roots=[Path('..'),Path('G:/Steam/steamapps/workshop/content/294100')]
rows=[]
for root in roots:
 for mod in root.iterdir():
  a=mod/'About'/'About.xml'
  if not a.exists(): continue
  t=a.read_text(encoding='utf-8-sig',errors='replace')
  name=re.search(r'<name>(.*?)</name>',t,re.S); pid=re.search(r'<packageId>(.*?)</packageId>',t,re.S)
  if not name:continue
  n=name[1]; ident=pid[1] if pid else ''
  if re.search(r'Milira|Wolfein|Raven Race|MoeLotl|Kiiro Race|NewRatkin|Perspective Shift|萌螈|渡鸦种族|沃芬|米莉拉种族|绮罗种族|鼠族',n+' '+ident,re.I):
   previews=list((mod/'About').glob('*review*'))
   rows.append({'name':n,'id':ident,'dir':str(mod.resolve()),'previews':[str(f.resolve()) for f in previews]})
Path('Docs/Cover/race-sources.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
for r in rows: print(r['name'],r['id'],r['dir'],r['previews'])
