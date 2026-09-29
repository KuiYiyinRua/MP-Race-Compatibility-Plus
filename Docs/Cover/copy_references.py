import json,shutil
from pathlib import Path
from PIL import Image
rows=json.loads(Path('Docs/Cover/race-sources.json').read_text(encoding='utf8'))
out=Path('Docs/Cover/references');out.mkdir(exist_ok=True)
for key in ['Ancot.MiliraRace','MelonDove.WolfeinRace','ZuoYao.RavenRace','HenTaiLoliTeam.Axolotl','Ancot.KiiroRace','Solaris.RatkinRaceMod']:
 row=next(r for r in rows if r['id']==key)
 f=next(Path(f) for f in row['previews'] if Path(f).name.lower()=='preview.png')
 dest=out/(key+'.png');shutil.copyfile(f,dest)
 print(key,Image.open(dest).size)
