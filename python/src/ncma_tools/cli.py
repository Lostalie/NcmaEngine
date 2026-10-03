from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .manifest import inspect_project, write_manifest


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="ncma", description="NcmaEngine project tools")
    subparsers = parser.add_subparsers(dest="command", required=True)

    inspect = subparsers.add_parser("inspect", help="Print a machine-readable project summary")
    inspect.add_argument("root", nargs="?", type=Path, default=Path.cwd())

    manifest = subparsers.add_parser("manifest", help="Write the Agent/tool project manifest")
    manifest.add_argument("output", type=Path)
    manifest.add_argument("--root", type=Path, default=Path.cwd())
    mcp = subparsers.add_parser("mcp", help="Serve local stdio MCP tools for an isolated native animation preview")
    mcp.add_argument("--root", type=Path, default=Path.cwd())
    mcp.add_argument("--allow-mutations", action="store_true")
    fbx = subparsers.add_parser("import-fbx", help="Import and inspect FBX character data through the native engine (read-only)")
    fbx.add_argument("source", type=Path)
    fbx.add_argument("--root", type=Path, default=Path.cwd())
    fbx.add_argument("--sample-rate", type=float, default=30)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "inspect":
        print(json.dumps(inspect_project(args.root), ensure_ascii=False, indent=2))
        return 0
    if args.command == "manifest":
        write_manifest(args.root, args.output)
        return 0
    if args.command == "mcp":
        from .mcp_server import serve
        return serve(args.root, args.allow_mutations)
    if args.command == "import-fbx":
        from .fbx import inspect_fbx
        try:
            print(json.dumps(inspect_fbx(args.root, args.source, args.sample_rate), ensure_ascii=False, indent=2))
            return 0
        except (OSError, ValueError, RuntimeError) as error:
            print(f"FBX import failed: {error}", file=sys.stderr)
            return 1
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
