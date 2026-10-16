"""Regression gate: the package must include fleck compensation and NMC companions.

Usage: python ValidateIntegratedRelease.py <package-root> <minimum-package-version>
The old 3.0.155 package (core 3.0.151) must fail, even though its About version is newer.
"""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
p.add_argument('package_root', type=Path)
p.add_argument('minimum_package_version', nargs='?', default='3.0.156')
args = p.parse_args()
assembly = args.package_root / '1.6/Assemblies/MP_MeowOnlineShop.dll'
meta = ET.parse(args.package_root / 'About/About.xml').getroot()
result = subprocess.run(['ilspycmd', '-t', 'MP_MeowOnlineShop.Patch_MultifactionTpsOptimize', str(assembly)],
                        capture_output=True, text=True, encoding='utf-8', errors='replace', check=True)
code = result.stdout
for required in ['TickSkippedMapFlecks(__instance)', 'AsyncTickingMapField.GetValue(null)', 'map.flecks.FleckManagerTick()',
                 'Synchronous map fleck lifetime compensation installed']:
    if required not in code:
        raise SystemExit('FAIL: core does not include integrated lifetime fix: ' + required)
# Assembly version is inspected by the controller separately; the package label
# can never compensate for a missing method/call in the actual core DLL above.
version = tuple(map(int, meta.findtext('modVersion').split('.')))
minimum = tuple(map(int, args.minimum_package_version.split('.')))
if version < minimum:
    raise SystemExit('FAIL: package version predates the integrated update')
expected_companions = {
    'Meow.RaceTrioCompatibility.dll': 'AF603C8EC052B366122D8E8E213F0759CB1E3F55734CC3B9AAF980CAFFB5DD0C',
    'Meow.TaleNivarianCompatibility.dll': '02B4F148512BA3C286F9663F39B5004948C8318C1A51626CD37E7AFAADE50964',
}
for name, expected in expected_companions.items():
    actual = hashlib.sha256((args.package_root / '1.6/Assemblies' / name).read_bytes()).hexdigest().upper()
    if actual != expected:
        raise SystemExit('FAIL: reviewed NMC 0.9.8 companion changed: ' + name)
print(json.dumps({'status': 'PASS', 'packageVersion': meta.findtext('modVersion'),
                  'coreCompensationPresent': True, 'nmcCompanionsUnchanged': True}))
