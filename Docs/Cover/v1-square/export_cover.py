from PIL import Image
from pathlib import Path
p=Path('Docs/Cover')
im=Image.open(p/'render.png').convert('RGB')
assert im.size==(1024,1024),im.size
for dest in [Path('About/Preview.png'),Path('Preview.png')]:
 im.save(dest,optimize=True)
im.save('Preview.jpg',quality=94,optimize=True,subsampling=0)
im.resize((256,256),Image.Resampling.LANCZOS).save(p/'thumbnail-256.png')
for f in ['About/Preview.png','Preview.png','Preview.jpg']:
 a=Image.open(f);a.verify();print(f,Path(f).stat().st_size,'bytes; 1024x1024 RGB')
(p/'README.md').write_text('''# Workshop cover

Final mod preview: `../../About/Preview.png` (1024 × 1024, RGB PNG).
Root `Preview.png` and `Preview.jpg` are matching workshop upload copies.

- `reference.png`: AI-generated composition reference.
- `cover.svg`: original editable vector reconstruction; no embedded raster images.
- `cover.html`: standalone HTML with inline SVG, fixed 1024 × 1024 artwork.
- `build_cover.py`: regenerates the SVG and HTML using Python standard library.
- `render.png`: Chrome rendering of the HTML.
- `thumbnail-256.png`: small-size visual check.
- `backup/`: previous About and root covers.

The generated reference informed the gold/teal palette, planetary backdrop, five-pawn group and compatibility emphasis. The final artwork uses original geometric SVG characters and Chinese typography. Characters are symbolic race archetypes, not exact portraits from third-party mods.

To export, render cover.html in headless Chrome at 1024 × 1024 with device scale factor 1 and hidden scrollbars. Convert the resulting screenshot to RGB PNG (and JPEG quality 94) with Pillow. No gameplay files changed.
''',encoding='utf-8')
