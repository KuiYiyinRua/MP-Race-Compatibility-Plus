"""Read frozen DesyncEvidence bundles; never infer a current session from stale logs.

Usage: python Tools/analyze_desync_batch.py BuildValidation/DesyncEvidence/<run>
Original ZIPs must first be copied and hash-verified. This script does not run RimWorld.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

TRACE = re.compile(r"^(\d+) Tick:(\d+) Hash:(-?\d+) '(.*)'$", re.M)


def read_trace(path):
    text = path.read_text(encoding="utf-8-sig")
    matches = list(TRACE.finditer(text))
    records = []
    for i, match in enumerate(matches):
        tail = text[match.end():matches[i + 1].start() if i + 1 < len(matches) else len(text)]
        records.append(dict(index=int(match[1]), tick=int(match[2]), hash=int(match[3]),
                            label=match[4], stack=[line for line in tail.splitlines() if line.startswith("at ")]))
    section = text.find("Context traces:")
    first = next((r for r, m in zip(records, matches) if section < 0 or m.start() < section), None)
    context = {r["index"]: r for r, m in zip(records, matches) if section >= 0 and m.start() > section}
    count = re.search(r"Trace count: (\d+)", text)
    return dict(count=int(count[1]) if count else 0, first=first, context=context,
                unequal_length_note="traces differ in amount" in text)


def analyze(root):
    rows = []
    for folder in sorted((p for p in root.glob("Desync-*") if p.is_dir()),
                         key=lambda p: int(p.name.split("-")[1])):
        info = dict(line.split("|||", 1) for line in (folder / "desync_info").read_text(encoding="utf-8-sig").splitlines() if "|||" in line)
        last = int(info["Last Valid Tick - Local"])
        logs = (folder / "local_logs.txt").read_text(encoding="utf-8-sig")
        meta = (folder / "local_metadata.txt").read_text(encoding="utf-8-sig")
        desyncs = list(re.finditer(r"^.*Desynced after last valid tick (-?\d+):.*$", logs, re.M))
        matching = [m for m in desyncs if int(m[1]) == last and last >= 0]
        end = matching[-1].end() if matching else len(logs)
        bounds = list(re.finditer(r"^.*(?:Connecting directly|Multiplayer: rejoining|Loading game from file).*$", logs[:end], re.M))
        segment = logs[bounds[-1].start() if bounds and matching else 0:end]
        # These vehicle debug counters hide earlier meaningful messages in trimmed cumulative logs.
        lines = [line for line in segment.splitlines() if line and not line.startswith(
            ("at ", "pawnsInVehicle:", "pawnsReturning:", "totalPawns:", "-----"))]
        (folder / "review-tail.txt").write_text(
            ("MATCHED CURRENT DESYNC\n" if matching else "CUMULATIVE/STALE: NO MATCH FOR THIS BUNDLE\n") +
            "\n".join(lines[-220:]), encoding="utf-8")
        traces = {side: read_trace(folder / (side + "_traces.txt")) for side in ("host", "local")}
        common = sorted(set(traces["host"]["context"]) & set(traces["local"]["context"]))
        differences = [i for i in common if any(traces["host"]["context"][i][key] != traces["local"]["context"][i][key]
                                                for key in ("tick", "hash", "label"))]
        row = dict(bundle=folder.name, info=info, traces=traces,
                   common_context=len(common), first_context_difference=differences[0] if differences else None,
                   context_equal_count=len(common)-len(differences),
                   desync_records=[m[0] for m in desyncs], matching_record=matching[-1][0] if matching else None,
                   log_sha256=hashlib.sha256((folder / "local_logs.txt").read_bytes()).hexdigest(),
                   log_trimmed="log trimmed" in logs,
                   metadata_date=meta.splitlines()[0],
                   compatibility_metadata=[line for line in meta.splitlines() if "local.mp.meowonlineshop" in line],
                   startup=[line for line in logs.splitlines() if "TaleNivarianCompat" in line and ("READY" in line or "FAILED" in line)])
        for side in ("host", "local"):
            near, section = [], "unknown"
            for line in (folder / (side + "_jitted_methods.txt")).read_text(encoding="utf-8-sig").splitlines():
                if line.startswith("In "):
                    section = line
                tick = re.search(r" t:(\d+) ", line)
                if tick and last >= 0 and last - 120 <= int(tick[1]) <= last + 180:
                    near.append(section + " " + line)
            (folder / (side + "-jit-window.txt")).write_text("\n".join(near), encoding="utf-8")
            row[side + "_jit_window_count"] = len(near)
        rows.append(row)
        print(folder.name, "last=" + str(last), "context-first=" + str(row["first_context_difference"]),
              "matched-current-log=" + str(bool(matching)))
    (root / "batch-analysis.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    return rows


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence_directory", type=Path)
    analyze(parser.parse_args().evidence_directory.resolve())
