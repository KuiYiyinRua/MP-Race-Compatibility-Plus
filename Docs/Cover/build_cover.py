from pathlib import Path
import random,math
P=Path(__file__).resolve().parent
s=[]
def add(v):s.append(v)
def path(d,fill,stroke='#171e24',sw=3,extra=''):return f'<path d="{d}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round" stroke-linecap="round" {extra}/>'
def ellipse(x,y,rx,ry,fill,stroke='none',sw=2,extra=''):return f'<ellipse cx="{x}" cy="{y}" rx="{rx}" ry="{ry}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" {extra}/>'
def text(x,y,t,size=22,col='#eed7a4',extra=''):return f'<text x="{x}" y="{y}" font-size="{size}" fill="{col}" {extra}>{t}</text>'
add('''<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720" viewBox="0 0 1280 720"><defs>
<linearGradient id="space" x2=".8" y2="1"><stop stop-color="#172d35"/><stop offset=".55" stop-color="#25434a"/><stop offset="1" stop-color="#09191f"/></linearGradient>
<linearGradient id="gold" x2=".2" y2="1"><stop stop-color="#fff0be"/><stop offset=".6" stop-color="#e4bc73"/><stop offset="1" stop-color="#ad783b"/></linearGradient>
<radialGradient id="planet"><stop stop-color="#496163"/><stop offset=".83" stop-color="#233a40"/><stop offset="1" stop-color="#bc9455"/></radialGradient>
<linearGradient id="whitehair" x2="1" y2=".5"><stop stop-color="#f5f1f0"/><stop offset=".5" stop-color="#d9d7df"/><stop offset="1" stop-color="#aaaebd"/></linearGradient>
<linearGradient id="blonde" x2="1" y2="1"><stop stop-color="#ffffe5"/><stop offset=".6" stop-color="#e5dfb9"/><stop offset="1" stop-color="#b7b38e"/></linearGradient>
<linearGradient id="blackhair" x2="1" y2="1"><stop stop-color="#575455"/><stop offset=".45" stop-color="#33323a"/><stop offset="1" stop-color="#1c212a"/></linearGradient>
<linearGradient id="sandhair" x2="1" y2="1"><stop stop-color="#dfc8a1"/><stop offset=".65" stop-color="#b8a181"/><stop offset="1" stop-color="#877660"/></linearGradient>
<linearGradient id="skin" x2=".3" y2="1"><stop stop-color="#fff1df"/><stop offset="1" stop-color="#efd0bd"/></linearGradient>
<clipPath id="scene"><rect x="19" y="183" width="1242" height="407"/></clipPath>
</defs><rect width="1280" height="720" fill="url(#space)"/>
''')
random.seed(17)
for i in range(170):add(ellipse(random.randint(24,1256),random.randint(172,589),random.choice([.7,.7,1.3]),1,'#e0c590',extra='opacity=".45"'))
add('<g clip-path="url(#scene)">')
add(ellipse(640,515,445,315,'url(#planet)','#d5b573',2))
for i in range(18):
 x=random.randint(270,930);y=random.randint(263,515)
 add(path(f'M{x} {y}l23-8 30 14 22-6 13 27-35 11-35-14Z','#9b986e','none',0,'opacity=".09"'))
add(path('M0 543L80 457 135 507 202 472 314 572 858 567 983 468 1063 533 1160 462 1280 523V600H0Z','#173039','none'))
# Individual character drawings use source-cover silhouettes and clothing.
def eye(x,y,c):
 return ''.join([path(f'M{x-15} {y}Q{x} {y-10} {x+15} {y+1}Q{x+5} {y+18} {x-10} {y+10}Z','#fff7ed','#403139',2),ellipse(x+2,y+5,7,10,c),ellipse(x+2,y+6,3.1,7,'#342b33'),ellipse(x-1,y+1,2.6,3,'#fff'),path(f'M{x-16} {y-1}Q{x} {y-10} {x+16} {y+1}','none','#342e35',3),path(f'M{x-16} {y-1}l-4-4','none','#342e35',2)])
def featherwing(side,white):
 c='#e8e9dc' if white else '#292d32';line='#929da0' if white else '#151e24';tip='#d0d7ce' if white else '#dab760'
 o=['<g'+(' transform="scale(-1 1)"' if side==-1 else '')+'>']
 o.append(path('M54 135Q91 77 103 74Q105 132 137 182L125 181 143 214 125 209 137 243 117 234 124 260 101 245 101 271Q57 245 43 173Z',c,line,3))
 for j in range(6):
  y=129+j*18;x=91+j*4
  o.append(path(f'M69 {y-2}Q{x} {y+35} {x+19} {y+41}L{x+9} {y+20}Q{x} {y+32} 69 {y-2}Z',tip,'none'))
 o.append('</g>');return ''.join(o)
def flower(x,y,sc=1):
 o=[f'<g transform="translate({x} {y}) scale({sc})">']
 for angle in range(0,360,72):o.append(ellipse(0,-5,4.3,6,'#dc709e','#963e69',.7,extra=f'transform="rotate({angle})"'))
 o.append(ellipse(0,0,2.4,2.4,'#ffe0af'));o.append('</g>');return ''.join(o)
def char(x,y,k,sc=1):
 o=[f'<g id="character-{k}" transform="translate({x} {y}) scale({sc})">']
 hair={'milira':'url(#blonde)','wolfein':'url(#whitehair)','raven':'url(#blackhair)','moelotl':'url(#whitehair)','kiiro':'url(#sandhair)','ratkin':'url(#blackhair)'}[k]
 if k=='milira':o += [featherwing(-1,True),featherwing(1,True)]
 if k=='raven':o += [featherwing(-1,False),featherwing(1,False)]
 # rear hair masses
 if k in ['milira','moelotl']:
  for side in [-1,1]:
   o.append(f'<g transform="scale({side} 1)">')
   o.append(path('M34 9Q87-30 90 46L80 127Q77 175 104 206Q53 215 49 146L42 71Z',hair,'#45424a',2.5))
   o.append(path('M67 25Q77 100 68 143Q68 183 88 195','none','#b4afbb' if k=='moelotl' else '#b5b394',1.6))
   o.append('</g>')
 if k=='wolfein':o.append(path('M-53 22Q-89 101-68 174L-50 150-55 183-15 163 48 180 46 150 72 165Q64 97 55 15Z',hair))
 if k=='raven':o.append(path('M-50 15Q-75 84-63 153L-73 182-37 163 7 176 50 163 75 179 59 137Q73 55 44 13Z',hair))
 if k=='kiiro':o.append(path('M-76 61L-103 4-64-44 23-49 77-24 102 48 90 148-72 144Z','#6b5d44','#252c29',3))
 if k=='ratkin':
  o.append(ellipse(-49,-14,28,36,'#b6acb1','#26232c',3));o.append(ellipse(-49,-15,21,29,'#eab7b6'))
  o.append(ellipse(48,-16,28,36,'#b6acb1','#26232c',3));o.append(ellipse(48,-17,21,29,'#eab7b6'))
 # costume outlines
 if k=='milira':
  o.append(path('M-36 113Q-77 112-86 166L-103 274Q-15 296 87 277L75 161Q61 123 36 114Z','#dce0d7'))
  o.append(path('M-23 126L-38 276 30 282 26 129Z','#a6c3c2'))
  o.append(path('M-36 118L-78 141-89 278-34 245 2 154 36 117 70 147 76 280 21 248Z','#e5e5df','#7e8f94',2))
  o.append(path('M-54 133Q0 170 54 133L42 154 3 177-51 148Z','#b8c0bc','#697d81',2))
  o.append(path('M0 164L8 175 0 188-8 175Z','#dac78b','#687d80',1.5))
  for side in [-1,1]:o.append(path(f'M{side*60} 171L{side*72} 254M{side*41} 203L{side*45} 267','none','#a2ada9',2))
 elif k=='wolfein':
  o.append(path('M-39 114L-76 135-85 273Q0 292 85 273L77 138 39 114Z','#262c32'))
  o.append(path('M-27 116L0 135 29 116 20 157-21 157Z','#e6dfd7'))
  o.append(path('M-3 134L6 135 11 171 0 183-9 171Z','#a83b3d'))
  o.append(path('M-34 115L-47 151-27 148-19 165 1 186 30 165 25 148 49 151 36 115','none','#a74d4b',3))
  o.append(path('M-79 201L-48 209M-80 215L-47 223M49 208L81 200M48 222L82 214','none','#a13d3d',9))
  o.append(path('M-56 239H56V253H-56Z','#181d22'))
  o.append(path('M-10 237H10V256H-10Z','#866b54','#b99876',2))
  o.append(path('M-5 241H5V251H-5Z','#282e32','none'))
  for yy in [190,210,229]:o.append(ellipse(8,yy,2,2,'#b8ada0'))
 elif k=='raven':
  o.append(path('M-39 114L-75 138-76 280Q0 293 76 280L75 138 39 114Z','#292c33'))
  o.append(path('M-22 116L-27 230 0 270 27 230 22 116Z','#e9e5d9'))
  o.append(path('M-21 118L0 135 22 118-8 154-24 145M22 119L0 135 10 153 25 143Z','#f4efe0','#63686b',1.5))
  o.append(path('M0 136V250','none','#b9a16c',2))
  o.append(path('M-34 124L-54 146-36 159-48 177-26 254M34 124L54 146 36 159 48 177 26 254','none','#877347',2))
  o.append(path('M-35 146Q0 172 35 146M-36 146Q-62 134-46 166L-35 146-23 173Q-9 164-35 146M35 146Q62 134 46 166L35 146 23 173Q9 164 35 146','none','#dbb35b',3))
  for yy in [164,187,210]:o.append(ellipse(0,yy,2.7,2.7,'#806741'))
 elif k=='moelotl':
  o.append(path('M-37 114L-65 138-74 282Q0 291 75 282L66 138 37 114Z','#49303f'))
  o.append(path('M-21 111L-22 133 0 148 23 133 21 111Z','#49303f','#c39b87',2))
  o.append(path('M0 121V280M-18 131H18M-15 142H15','none','#cead86',2))
  o.append(path('M-41 142L-66 168-88 256-45 270-25 180M41 142L66 168 88 256 45 270 25 180','#623a50','#ca8fa6',3))
  for side in [-1,1]:
   o.append(path(f'M{side*64} 167Q{side*85} 174 {side*79} 194Q{side*96} 207 {side*78} 218Q{side*92} 236 {side*68} 247L{side*43} 234Q{side*35} 218 {side*48} 204Q{side*34} 188 {side*53} 184Q{side*43} 170 {side*64} 167Z','#f0dfe5','#ae859b',2))
   o.append(flower(side*64,213,1.1))
  for xx,yy in [(-23,182),(20,220),(-17,259)]:
   o.append(path(f'M{xx} {yy+16}q17-23 0-34','none','#795166',1.5));o.append(flower(xx,yy,.45))
 elif k=='kiiro':
  o.append(path('M-60 113L-82 163-76 280Q0 291 76 280L81 164 59 111Z','#6c6049'))
  o.append(path('M-43 148L0 195 41 149-3 174Z','#a99979'))
  o.append(path('M-77 145L-101 164-49 179 9 169 50 144 86 141 74 116 20 129-22 126Z','#827254','#343b35',3))
  o.append(path('M-75 143Q-8 178 68 136M-61 205L-68 265M46 187L57 270','none','#b5a17b',2))
  o.append(path('M31 170L-43 275','none','#302f29',12));o.append(path('M31 170L-43 275','none','#b4a68a',2))
  o.append(path('M6 198L20 208 4 229-10 219Z','#b9ae95','#2f352f',2))
 elif k=='ratkin':
  o.append(path('M-42 120L-75 146-88 277Q0 294 86 277L75 145 41 119Z','#676273'))
  o.append(path('M-51 116L-73 138-60 157-83 176Q-15 187 67 168L80 145 52 114Q0 139-51 116Z','#883e50','#392d3a',3))
  o.append(path('M-61 146Q0 174 63 144M-47 170Q0 186 52 164','none','#ba6f76',2))
  o.append(path('M-2 182V276M-54 203L-65 270M49 205L63 270','none','#464657',2))
  for yy in [197,218,239]:o.append(ellipse(0,yy,2.3,2.3,'#cfc1a2'))
 # neck and face
 o.append(path('M-17 100V124Q0 140 18 124V99Z','url(#skin)','#694e49',2))
 if k=='milira':o.append(path('M-45 48L-82 27-55 70M45 48L82 27 55 70','#eed2bf','#795d55',2))
 if k=='moelotl':
  for side in [-1,1]:o.append(path(f'M{side*48} 48L{side*91} 30 {side*76} 53 {side*98} 54 {side*76} 66 {side*90} 76 {side*51} 80Z','#e8aecb','#a25983',2))
 o.append(path('M-52 21Q-57 5 0-3Q57 5 52 21L51 69Q48 98 0 115Q-48 98-51 69Z','url(#skin)','#775c58',2))
 iris={'milira':'#b78955','wolfein':'#c18b49','raven':'#c4a35e','moelotl':'#c36a8c','kiiro':'#87a380','ratkin':'#985b70'}[k]
 o.append(eye(-26,61,iris));o.append(eye(26,61,iris))
 o.append(path('M-37 45L-15 47M16 47L37 45','none','#8a6a60',1.5))
 o.append(path('M-4 92Q0 95 5 92','none','#ac736f',1.5))
 o.append(path('M0 74l-2 8 3 0','none','#c99d8b',1))
 for xx in [-37,37]:o.append(ellipse(xx,83,9,3,'#e9a6a4',extra='opacity=".36"'))
 # ears & front hair, distinctive silhouettes
 if k=='wolfein':
  o.append(path('M-52 16L-69-66Q-41-56-15-14M17-13Q45-53 65-68L56 24',hair))
  o.append(path('M-53-13L-58-45-29-13M33-12L54-47 48-8','#8a8390','#807786',1))
  o.append(path('M-62 55Q-77 12-44-16L-13-27Q12-42 42-20Q78 0 59 69L45 44 47 17 23 42 31 7 0 42-4 17-29 53-30 23-50 68Z',hair,'#4a4851',2.5))
  o.append(path('M-12-23Q-47 5-50 38M8-24Q-9 0-12 25M27-14Q47 5 47 33','none','#faf6f1',3))
 elif k=='milira':
  o.append(path('M-52 69Q-76 23-48-12Q-20-36 13-22Q62-27 65 24L53 78 41 40 39 18Q19 43-12 33L-25 10Q-28 44-52 69Z',hair,'#777662',2))
  o.append(path('M-19-16Q-11 18 26 27M-41 0Q-53 24-49 42M14-18Q48-2 51 32','none','#ffffec',3))
  o.append('<g transform="translate(59 -34) rotate(-28)">');o.append(ellipse(0,0,27,8,'none','#fff4ca',3));o.append(path('M-38 0H-26M27 0H38M0-16V-8M0 8V16','none','#fff4ca',2));o.append('</g>')
 elif k=='raven':
  for side in [-1,1]:
   o.append(path(f'M{side*52} 3Q{side*79}-26 {side*86} 19L{side*101} 56 {side*83} 47 {side*86} 70 {side*71} 57 {side*65} 72 {side*53} 43Z','#35363c','#171d26',2))
   o.append(path(f'M{side*74} 30l{side*12} 20-{side*9}-5M{side*66} 40l{side*11} 20','none','#dbb352',3))
  o.append(path('M-52 91Q-78 52-55 7Q-36-30 6-25Q59-24 60 23L52 56Q35 45 27 17Q19 56-7 87Q-27 105-52 91Z',hair,'#1e232b',2.5))
  o.append(path('M-29-13Q-58 16-39 63M-9-15Q-30 15-26 40M17-12Q36-2 44 21','none','#69615d',2))
  o.append(path('M35 21l15-6m-14-2 14 10','none','#e0b95f',2))
 elif k=='moelotl':
  o.append(path('M-53 74Q-80 21-48-11Q-16-34 20-21Q63-18 62 25L52 75 38 43 35 18 13 49 10 15-10 47-20 14-38 46-39 19Z',hair,'#726779',2))
  o.append(path('M-43-6Q-21 7-16 29M-4-17Q13-3 16 25M29-10Q46 3 47 40','none','#fff4fa',3))
  for side in [-1,1]:
   o.append(path(f'M{side*47}-5Q{side*76}-21 {side*60}-49Q{side*58}-24 {side*36}-17Z','#473039','#201f29',2))
   o.append(flower(side*47,-8,1.2));o.append(flower(side*62,6,.7));o.append(flower(side*55,23,.65))
  o.append(path('M-47 1Q0 41 47 1','none','#c57c9a',1.5))
  for i in range(7):o.append(ellipse(-35+i*12,12+9*math.sin(i/6*math.pi),2,2,'#ce7697'))
  o.append(path('M0 28L4 34 0 40-4 34Z','#ce7697','#f3b2c7',1))
 elif k=='kiiro':
  o.append(path('M-49 11L-77-12-76 35-53 48M41 13L60-18 70 22 49 42',hair,'#594e40',2))
  o.append(path('M-57 17L-69 2-67 24M51 16L58 0 62 20','#eccbb6','none'))
  o.append(path('M-52 85Q-64 55-51 7Q-23-16 14-9Q49-14 54 20L50 82 36 74 38 26 17 47 14 17Q5 66-25 88L-40 77Z',hair,'#6a5b4c',2))
  o.append(path('M-30 8Q-40 36-35 62M-3 1Q-5 36-16 55M26 9L34 28','none','#e8d4b0',2))
  o.append(path('M-82-6Q-20-44 58-11L72-1Q-15-15-82 17Z','#8c7955','#3d4135',2))
 elif k=='ratkin':
  o.append(path('M-51 88Q-71 78-62 26Q-60-14-12-21Q32-35 55 0Q75 37 48 89L37 63 42 33 21 42 12 16-2 45-18 15-26 43-39 21-40 70Z',hair,'#2c2934',2.5))
  o.append(path('M-25-9Q-48 11-45 37M-7-14Q-6 0 6 19M16-12Q43 1 47 27','none','#777078',2))
  for i in range(7):
   yy=82+i*18;xx=44+4*math.sin(i)
   o.append(path(f'M{xx} {yy-6}q-23 7-5 24q26-1 5-24Z','#46434e','#282631',2))
   o.append(path(f'M{xx-6} {yy+5}l10 5','none','#74707a',1.5))
  o.append(path('M45 207Q18 185 23 213L44 218Q73 231 65 207L45 212Z','#a74c67','#392d3d',2))
  o.append(path('M42 217L36 245 48 234 55 247 50 217Z','#a74c67','#392d3d',2))
 o.append('</g>');return ''.join(o)
# Portraits intentionally separated enough for ears and individual costume reads.
for x,y,k,sc in [(133,321,'milira',.96),(337,305,'wolfein',1.02),(541,318,'raven',.98),(746,321,'moelotl',.96),(947,322,'kiiro',.96),(1141,319,'ratkin',.96)]:add(char(x,y,k,sc))
add('</g>')
add('''<g font-family="Microsoft YaHei,Arial,sans-serif" text-anchor="middle">
''')
add(text(640,49,'RIMWORLD 1.6  /  MULTIPLAYER',19,'#b6c9c5','letter-spacing="5"'))
add(text(640,105,'多种族联机兼容',59,'url(#gold)','font-weight="900" stroke="#0d2028" stroke-width="5" paint-order="stroke" letter-spacing="4"'))
add(text(640,149,'MP-RACE-COMPATIBILITY-PLUS',27,'#e9d7b0','font-weight="700" letter-spacing="2.5"'))
add(path('M38 176H469L487 184H793L811 176H1242','none','#c8a56d',2))
# small race labels on individual nameplates
names=[('米莉拉','MILIRA'),('沃芬','WOLFEIN'),('渡鸦','RAVEN'),('萌螈','MOELOTL'),('绮罗','KIIRO'),('鼠族','RATKIN')]
for x,(cn,en) in zip([133,337,541,746,947,1141],names):
 add(path(f'M{x-69} 554H{x+69}L{x+75} 560V599H{x-75}V560Z','#10262d','#a58d65',1))
 add(text(x,577,cn,19,'#e8d8b1','font-weight="700"'))
 add(text(x,593,en,10,'#aec1bb','letter-spacing="2"'))
add(path('M25 610H1255V685L1236 704H44L25 685Z','#0c222a','#d5ae6c',2))
add(path('M38 616H1242','none','#e7c588',1))
add(text(216,658,'多种族兼容',34,'#f0d292','font-weight="900"'))
add(text(216,684,'六大代表种族 · 更多种族支持',14,'#b5c5bd'))
add(path('M409 633V686M683 633V686','none','#927f5d',1.5))
add(text(545,660,'RJW 兼容',37,'#f0d292','font-weight="900"'))
add(text(545,684,'MULTIPLAYER SUPPORT',11,'#b5c5bd','letter-spacing="1.5"'))
add(text(961,657,'Perspective Shift',36,'#f0d292','font-weight="900"'))
add(text(961,683,'联 机 兼 容',19,'#cfdbc9'))
add('</g>')
add(path('M12 55V12H484L495 19H785L796 12H1268V55M12 574V693L27 708H1253L1268 693V574','none','#d0ac70',3))
add(path('M22 52V23H66M1214 23H1257V52','none','#e6c489',5))
add('</svg>')
svg=''.join(s)
(P/'cover.svg').write_text(svg,encoding='utf8')
(P/'cover.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>MP Race Compatibility Plus — 16:9 cover</title><style>*{box-sizing:border-box}html,body{margin:0;width:1280px;height:720px;overflow:hidden;background:#10262b}svg{display:block;width:1280px;height:720px}</style>'+svg+'</html>',encoding='utf8')
print('Created 1280x720 HTML + SVG; 6 source-based character drawings.')
