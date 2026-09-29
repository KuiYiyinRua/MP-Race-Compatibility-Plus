from pathlib import Path
import random
p=Path('Docs/Cover')
s=['''<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
<defs><linearGradient id="bg" x2="0.8" y2="1"><stop stop-color="#28464b"/><stop offset="1" stop-color="#08181e"/></linearGradient><linearGradient id="gold" x2="0.2" y2="1"><stop stop-color="#ffe3a0"/><stop offset=".5" stop-color="#e6b85f"/><stop offset="1" stop-color="#b77730"/></linearGradient><radialGradient id="planet"><stop stop-color="#5c6860"/><stop offset="1" stop-color="#293e40"/></radialGradient><pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse"><path d="M40 0H0V40" fill="none" stroke="#96c1b7" stroke-opacity=".055"/></pattern></defs>
<rect width="1024" height="1024" fill="url(#bg)"/><rect width="1024" height="1024" fill="url(#grid)"/>
<path d="M10 84V30L30 10H994L1014 30V994L994 1014H30L10 994V84" fill="none" stroke="#cda15a" stroke-width="3"/>
<path d="M28 85V35H78M946 35H989V85M28 940V989H78M946 989H989V940" fill="none" stroke="#f2ce81" stroke-width="6"/>
''']
random.seed(52)
for i in range(95):
 x=random.randint(32,992);y=random.randint(265,735)
 s.append(f'<circle cx="{x}" cy="{y}" r="{random.choice([1,1,2])}" fill="#e5c782" opacity=".55"/>')
s.append('''<circle cx="512" cy="513" r="214" fill="url(#planet)" stroke="#dfb364" stroke-width="5"/><circle cx="512" cy="513" r="199" fill="none" stroke="#d6b16b" stroke-opacity=".2"/>
<path d="M345 405l70-60 62 10 16 42-35 34 20 41-58 31-30-37-47-7ZM539 326l61 33 34 54-36 32 34 48-49 23-27 89-41-25 8-76-32-43 28-57Z" fill="#c19b58" opacity=".28"/>
<ellipse cx="512" cy="522" rx="327" ry="46" transform="rotate(-19 512 522)" fill="none" stroke="#d8ad5f" stroke-width="13"/><ellipse cx="512" cy="522" rx="327" ry="46" transform="rotate(-19 512 522)" fill="none" stroke="#ffe0a0" stroke-width="2"/>
<path d="M0 579L95 404 170 499 209 465 312 604 749 601 835 462 877 499 940 420 1024 570V780H0Z" fill="#142c33"/><path d="M0 664L130 535 215 627 294 562 357 688 706 667 802 586 851 613 926 549 1024 645V790H0Z" fill="#0d2229"/>
''')
for x,y,w,h in [(54,604,65,140),(132,575,39,175),(216,644,70,99),(770,608,61,142),(864,565,36,185),(920,653,80,97)]:
 s.append(f'<path d="M{x} 752V{y}H{x+w}V752Z" fill="#102127" stroke="#446064" stroke-width="3"/>')
 for yy in range(y+15,730,28):s.append(f'<path d="M{x+12} {yy}h8m12 0h8" stroke="#deb879" stroke-width="9"/>')
# Original geometric pawn illustrations, fully vector.
def pawn(x,y,k,scale=1):
 colors={'wolf':('#a89989','#5c6262'),'angel':('#eedbc0','#d3d4c8'),'human':('#d9ac80','#64746f'),'axolotl':('#e6aaa0','#587d7e'),'dragon':('#af6652','#4e5d62')}
 skin,coat=colors[k]
 out=[f'<g transform="translate({x} {y}) scale({scale})" stroke="#081318" stroke-width="7" stroke-linejoin="round">']
 if k=='angel':out.append('<path d="M-37 102Q-141 41-121 234L-76 207-85 242-31 215M37 102Q141 41 121 234L76 207 85 242 31 215" fill="#d8d6bd"/>')
 if k=='dragon':out.append('<path d="M-47 9Q-108-81-48-69L-59-42-8-10M38 9Q108-81 48-69L59-42 8-10" fill="#8a8274"/>')
 if k=='wolf':out.append('<path d="M-65 29L-76-65-13-23 15-23 78-65 65 41" fill="#8b8984"/><path d="M-59-37L-52 6-26-13M58-37L51 6 26-13" fill="#e4d5b7" stroke-width="4"/>')
 if k=='axolotl':
  out.append('<path d="M-56 17L-108-11-94 22-120 28-91 47-117 67-64 78M56 17L108-11 94 22 120 28 91 47 117 67 64 78" fill="#d98f8c"/>')
 out.append(f'<path d="M-57 119Q-91 133-101 239Q0 276 101 239Q91 133 57 119Z" fill="{coat}"/>')
 out.append('<path d="M-52 132L-36 248M52 132L36 248" stroke="#b7aa88" stroke-width="15"/><path d="M-52 132L-36 248M52 132L36 248" stroke-width="4"/><path d="M-87 215Q0 246 87 215L85 242Q0 269-85 242Z" fill="#343f3e"/>')
 out.append(f'<rect x="-65" y="-20" width="130" height="153" rx="60" fill="{skin}"/>')
 if k in ('wolf','angel','human'):
  hair={'wolf':'#898782','angel':'#e7e5d8','human':'#39352f'}[k]
  out.append(f'<path d="M-68 63L-80 6-62-28-73-38-25-43-8-58 43-43 73-13 69 58 40 18 18 4-12 37-8 6-42 43-43 16Z" fill="{hair}"/>')
 if k=='dragon':out.append('<path d="M-60 23L-81 4-54-7-58-27-21-19 8-37 40-18 64 18 46 45 12 25-8 39Z" fill="#734536"/>')
 out.append('<path d="M-38 62l21 4M19 66l21-4" stroke-width="5"/><ellipse cx="-27" cy="80" rx="5" ry="9" fill="#081318" stroke="none"/><ellipse cx="29" cy="80" rx="5" ry="9" fill="#081318" stroke="none"/>')
 out.append('<path d="M-38 129L0 157 38 129 47 152 0 181-47 152Z" fill="#b6ac91"/><path d="M-26 184H26V222H-26Z" fill="#283c40" stroke-width="4"/><path d="M0 190V215M-12 202H12" stroke="#e1b968" stroke-width="5"/>')
 if k=='wolf':out.append('<path d="M-58 116Q0 144 58 116L49 146-38 170-62 142Z" fill="#874e3d"/>')
 out.append('</g>');return ''.join(out)
s.extend([pawn(134,492,'axolotl',.83),pawn(868,463,'dragon',.94),pawn(714,463,'angel',1.08),pawn(310,454,'wolf',1.12),pawn(513,442,'human',1.24)])
s.append('''<g font-family="Microsoft YaHei,Arial,sans-serif" text-anchor="middle">
<text x="512" y="65" fill="#b5c8c4" font-size="21" letter-spacing="5">RIMWORLD 1.6 · MULTIPLAYER</text>
<text x="512" y="153" fill="url(#gold)" stroke="#081318" stroke-width="9" paint-order="stroke" font-size="76" font-weight="900">多种族联机兼容</text>
<text x="512" y="210" fill="#f4d597" font-size="35" font-weight="800" letter-spacing="1">MP-RACE-COMPATIBILITY-PLUS</text>
<path d="M35 242H319L338 253H687L706 242H989" fill="none" stroke="#d6ae67" stroke-width="4"/>
<text x="512" y="291" fill="#d1ded5" font-size="23" letter-spacing="6">让不同种族，共建同一个世界</text>
<path d="M27 778L48 757H976L997 778V976L976 997H48L27 976Z" fill="#10262b" stroke="#dcb268" stroke-width="4"/>
<path d="M48 765H976" stroke="#ffdc91" stroke-width="2"/>
<text x="512" y="821" fill="#f7d491" font-size="34" font-weight="900">多种族兼容</text>
<text x="512" y="854" fill="#b7c9c3" font-size="18" letter-spacing="2">萌螈 · 渡鸦 · 沃芬 · 米莉拉 · 绮罗 · 鼠族 …</text>
<path d="M62 878H962M363 895V966" stroke="#647065" stroke-width="2"/>
<text x="211" y="944" fill="#f8d187" font-size="61" font-weight="900">RJW</text>
<text x="673" y="934" fill="#f8d187" font-size="43" font-weight="900">Perspective Shift</text>
<text x="673" y="968" fill="#b8cfc5" font-size="21" letter-spacing="4">联 机 兼 容</text>
<text x="211" y="975" fill="#b8cfc5" font-size="19" letter-spacing="4">联机兼容</text>
</g></svg>''')
svg=''.join(s)
(p/'cover.svg').write_text(svg,encoding='utf-8')
(p/'cover.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>MP-Race-Compatibility-Plus · Workshop Cover</title><style>*{box-sizing:border-box}html,body{margin:0;width:1024px;height:1024px;overflow:hidden;background:#10262b}svg{display:block;width:1024px;height:1024px}</style>'+svg+'</html>',encoding='utf-8')
print('SVG and HTML created')
