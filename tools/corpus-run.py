#!/usr/bin/env python3
"""Convert every STL in a folder and print a markdown table of what happened.

    python tools/corpus-run.py "C:/path/to/models"                 # uses the Debug CLI
    python tools/corpus-run.py "C:/path/to/models" --cli path.exe  # or a published build

A model that kills the kernel still produces a row: the exit code is the access
violation (3221225477) and the result column is empty. That is the point - a
corpus run has to survive the crashes it is looking for.
"""
import argparse
import pathlib
import subprocess
import sys
import tempfile
import time

DEFAULT_CLI = ("src/Meshsmith.Cli/bin/Debug/net8.0-windows7.0/Meshsmith.Cli.exe")


def run(cli: pathlib.Path, stl: pathlib.Path, out_dir: pathlib.Path, timeout: int):
    target = out_dir / (stl.stem.replace(" ", "_") + ".step")
    started = time.time()
    row = {"file": stl.name, "mb": stl.stat().st_size / 1048576}
    try:
        proc = subprocess.run([str(cli), "convert", str(stl), "-o", str(target)],
                              capture_output=True, text=True, timeout=timeout)
        row["secs"] = time.time() - started
        row["rc"] = proc.returncode
        lines = [l for l in proc.stdout.splitlines() if l.startswith("faces=")]
        row["result"] = lines[-1] if lines else ""
        row["warn"] = [l[6:] for l in proc.stdout.splitlines() if l.startswith("warn: ")]
        detail = [l[len("artifactDetail="):] for l in proc.stdout.splitlines() if l.startswith("artifactDetail=")]
        row["artifact_detail"] = detail[0] if detail else ""
        row["step_mb"] = target.stat().st_size / 1048576 if target.exists() else 0
    except subprocess.TimeoutExpired:
        row.update(secs=timeout, rc="TIMEOUT", result="", warn=[], step_mb=0)
    return row


def fields(result: str):
    pairs = {}
    for token in result.split():
        if "=" in token:
            key, _, value = token.partition("=")
            pairs[key] = value
    return pairs


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--cli", default=DEFAULT_CLI)
    ap.add_argument("--timeout", type=int, default=900)
    ap.add_argument("--out", default=None, help="where the STEP files go (default: a temp dir)")
    args = ap.parse_args()

    cli = pathlib.Path(args.cli).resolve()
    if not cli.exists():
        print(f"no such CLI: {cli}", file=sys.stderr)
        return 2

    folder = pathlib.Path(args.folder)
    stls = sorted(folder.glob("*.stl"))
    if not stls:
        print(f"no STL files in {folder}", file=sys.stderr)
        return 2

    out_dir = pathlib.Path(args.out) if args.out else pathlib.Path(tempfile.mkdtemp(prefix="corpus-"))
    out_dir.mkdir(parents=True, exist_ok=True)

    print(f"| file | in MB | s | faces | solids | valid | artifact | volume | faceted | STEP MB | exit |")
    print(f"|---|---|---|---|---|---|---|---|---|---|---|")
    failures = 0
    for stl in stls:
        row = run(cli, stl, out_dir, args.timeout)
        f = fields(row["result"])
        if row["rc"] != 0:
            failures += 1
        print(f"| {row['file']} | {row['mb']:.2f} | {row['secs']:.1f} | {f.get('faces', '-')} | "
              f"{f.get('solids', '-')} | {f.get('valid', '-')} | {f.get('artifact', '-')} | "
              f"{f.get('volume', '-')} | {f.get('faceted', '-')} | {row['step_mb']:.2f} | {row['rc']} |")
        if row.get("artifact_detail"):
            print(f"|   ↳ | | | | | | | | | | {row['artifact_detail']} |")
        for warning in row["warn"][:3]:
            print(f"|   ↳ | | | | | | | | | {warning} |")
    print(f"\n{len(stls)} models, {failures} not converted to a valid solid.")
    print(f"STEP files: {out_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
