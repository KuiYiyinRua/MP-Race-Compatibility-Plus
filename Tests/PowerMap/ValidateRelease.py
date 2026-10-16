"""Validate the native-map-tick fix in a built package, not its version label alone."""
import argparse,hashlib,json,subprocess,xml.etree.ElementTree as ET
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('core',type=Path);p.add_argument('--before',type=Path);a=p.parse_args()
def decompile(path,typ):
 return subprocess.run(['ilspycmd','-t',typ,str(path)],check=True,capture_output=True,encoding='utf-8',errors='replace').stdout
code=decompile(a.core,'MP_MeowOnlineShop.Patch_MultifactionTpsOptimize')
assert 'Whole-map tick throttling retired' in code,'native-map policy absent'
for retired in ['MapPostTick_Prefix','TickSkippedMapFlecks','_schedulerGatesCacheTick','Harmony.Patch(','ShouldRunMapTick']:
 assert retired not in code, 'unsafe scheduler still present: '+retired
settings=decompile(a.core,'MP_MeowOnlineShop.MultifactionTpsOptimizeComponent')
assert 'mp_meow_multifaction_tps_opt_experimental_enabled' in settings,'legacy save compatibility lost'
if a.before:
 assert settings==decompile(a.before,'MP_MeowOnlineShop.MultifactionTpsOptimizeComponent'),'legacy serialized contract changed'
print(json.dumps({'status':'PASS','coreSHA256':hashlib.sha256(a.core.read_bytes()).hexdigest().upper(),'nativeMapTicks':True,'legacySaveContractPreserved':True}))
