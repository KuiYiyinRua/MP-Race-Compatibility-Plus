"""Freeze the latest replay's trade identity evidence without modifying it."""
import collections
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
import zipfile

sys.stdout.reconfigure(encoding="utf-8")
root = Path(__file__).resolve().parents[2]
evidence = root / "BuildValidation/TradeMismatch_20260921"
with zipfile.ZipFile(evidence / "input.zip") as archive:
    documents = [ET.fromstring(archive.read(name)) for name in archive.namelist()
                 if name.endswith("_save")]
things = {"Thing_" + node.findtext("id"): node
          for document in documents for node in document.iter()
          if node.findtext("id") and node.find("def") is not None}
records = []
for document in documents:
    for session in document.iter():
        if session.get("Class") != "Multiplayer.Client.MpTradeSession":
            continue
        groups = collections.defaultdict(list)
        for row in session.findall("tradeDeal/tradeables/li"):
            sides, objects = [], []
            for side in ("thingsColony", "thingsTrader"):
                values = []
                for reference in row.findall(side + "/li"):
                    thing = things.get(reference.text)
                    if thing is None:
                        continue
                    values.append(tuple(thing.findtext(field, "") for field in
                                        ("def", "stuff", "quality", "health", "kindDef", "name", "gender", "stackCount")))
                    inner = thing.find("innerContainer/innerList/li")
                    objects.append({"id": reference.text, "side": side,
                                    "def": thing.findtext("def"),
                                    "innerDef": inner.findtext("def") if inner is not None else None,
                                    "innerStuff": inner.findtext("stuff") if inner is not None else None})
                sides.append(tuple(sorted(values)))
            groups[tuple(sides)].append(objects)
        records.append({"session": session.findtext("sessionId"),
                        "trader": session.findtext("trader"),
                        "negotiator": session.findtext("playerNegotiator"),
                        "potentialIdentityCollisions": [rows for rows in groups.values() if len(rows) > 1]})
(evidence / "save-trade-identities.json").write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding="utf-8")
print("Saved static collision candidates; runtime CLR identity and actual transfers remain separate evidence.")
