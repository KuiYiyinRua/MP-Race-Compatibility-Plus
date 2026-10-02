"""Freeze reviewable code/evidence; keep production deployment separate from validation."""
import difflib, hashlib, json, shutil, zipfile
from pathlib import Path
repo=Path(__file__).resolve().parents[1]
e=repo/'BuildValidation/DesyncEvidence/Desync338-343_20261001'
old=repo/'BuildValidation/DesyncEvidence/Desync325-339_20261001/Before'
c=e/'Candidate02'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest().upper()
assert sha(c/'MP_MeowOnlineShop.dll')=='E62439A616B44C1E05E2FF216B88C0DB250C34CD911AFA9ECEE2725278815057'
assert sha(repo/'1.6/Assemblies/MP_MeowOnlineShop.dll')==sha(old/'MP_MeowOnlineShop.dll')
for label in ('MBB1','MBB2','MBF1','MRB1','MBD1','MBD2','MBD3','MBD4','MBS1','MBS2','MRB2','MBF2'):
    run=Path('G:/')/label
    if not (run/'result.json').is_file():continue
    dest=e/'Runtime'/label;dest.mkdir(parents=True,exist_ok=True)
    for f in run.iterdir():
        if f.is_file():shutil.copy2(f,dest/f.name)
    probe=run/'Game/Mods/Probe/Assemblies'
    if probe.is_dir():shutil.copytree(probe,dest/'HarnessDLLs',dirs_exist_ok=True)
    for peer in run.iterdir():
        if not peer.is_dir() or not (peer.name=='Host' or peer.name.startswith('Client')):continue
        for sub in ('Config','MpDesyncs'):
            source=peer/sub
            if source.is_dir():shutil.copytree(source,dest/peer.name/sub,dirs_exist_ok=True)
    (dest/'archive-hashes.json').write_text(json.dumps({str(f.relative_to(dest)):sha(f) for f in dest.rglob('*') if f.is_file() and f.name!='archive-hashes.json'},indent=2),encoding='utf-8')
newsource=repo/'Source/MP_MeowOnlineShop'
changes=[]
for before,after in ((old/'Patch_SmeltedLoongMp.cs',newsource/'Patch_SmeltedLoongMp.cs'),(old/'AssemblyInfo.cs',newsource/'Properties/AssemblyInfo.cs')):
    changes.extend(difflib.unified_diff(before.read_text(encoding='utf-8-sig').splitlines(True),after.read_text(encoding='utf-8-sig').splitlines(True),fromfile='before/'+before.name,tofile='after/'+after.name))
added=newsource/'MiliraAddons/MilianGenerationMapContext.cs'
changes.extend(difflib.unified_diff([],added.read_text(encoding='utf-8-sig').splitlines(True),fromfile='/dev/null',tofile='after/MilianGenerationMapContext.cs'))
(e/'candidate.diff').write_text(''.join(changes),encoding='utf-8')
shutil.copy2(newsource/'Patch_SmeltedLoongMp.cs',c/'Patch_SmeltedLoongMp.cs')
manifest={'status':'candidate; not deployed; representative release gate incomplete','assemblyVersion':'3.0.145.0','sha256':sha(c/'MP_MeowOnlineShop.dll'),'formalDllUnchanged':sha(repo/'1.6/Assemblies/MP_MeowOnlineShop.dll'),'runs':{p.name:json.loads((p/'result.json').read_text(encoding='utf-8-sig')) for p in (e/'Runtime').iterdir() if (p/'result.json').exists()}}
(e/'validation-status.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
with zipfile.ZipFile(e/'MP-MeowOnlineShop-3.0.145-candidate-unverified.zip','w',zipfile.ZIP_DEFLATED) as z:
    for f in c.iterdir():
        if f.is_file():z.write(f,'Candidate02/'+f.name)
    for name in ('REPORT.md','candidate.diff','validation-status.json','source-provenance.json','native-mvids.json'):
        z.write(e/name,name)
print(json.dumps({'candidate':str(c/'MP_MeowOnlineShop.dll'),'formalUnchanged':True,'archive':str(e/'MP-MeowOnlineShop-3.0.145-candidate-unverified.zip')},ensure_ascii=False))
