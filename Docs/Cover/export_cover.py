from pathlib import Path
from PIL import Image
import json
p=Path(__file__).resolve().parent
root=p.parent.parent
im=Image.open(p/'render-v2.png').convert('RGB')
assert im.size==(1280,720),im.size
im.save(p/'cover-1280x720.png',optimize=True)
preview=im.resize((640,360),Image.Resampling.LANCZOS)
for dest in [root/'About'/'Preview.png',root/'Preview.png',p/'cover-640x360.png']:
 preview.save(dest,optimize=True)
preview.save(root/'Preview.jpg',quality=95,optimize=True,subsampling=0)
preview.resize((320,180),Image.Resampling.LANCZOS).save(p/'thumbnail-320x180.png')
checks=[]
for dest in [root/'About'/'Preview.png',root/'Preview.png',root/'Preview.jpg',p/'cover-1280x720.png']:
 a=Image.open(dest)
 assert a.width*9==a.height*16
 assert dest.stat().st_size<1000000
 checks.append({'file':str(dest.relative_to(root)),'size':list(a.size),'format':a.format,'bytes':dest.stat().st_size})
 a.verify()
(p/'validation.json').write_text(json.dumps(checks,indent=2),encoding='utf8')
print(json.dumps(checks,indent=2))
