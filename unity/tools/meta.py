#!/usr/bin/env python3
"""Write the .meta files the packages need, for the ones that are missing.

Unity's asset database identifies every asset by the GUID in its sidecar .meta
file, and a package installed from a git URL lands in an *immutable* folder
where Unity cannot write one. Its response is not to invent a GUID: it logs

    <asset> has no meta file, but it's in an immutable folder.
    The asset will be ignored.

and ignores the asset. For a package that is nothing but scripts, that is the
whole package. So the .meta files are part of what is shipped and are committed.

Normally the editor writes them. There is no editor here, so this does; the
formats are copied from packages that install correctly, byte for byte
including the trailing space after `userData:`, so that opening the project
does not rewrite all of them and hand somebody a dirty tree.

GUIDs are derived from the asset's path (salted with the package name) rather
than drawn at random. If these files are ever lost, running this again restores
the same GUIDs instead of breaking every reference in every project that uses
the package. The cost is that a *rename* has to carry its .meta along (`git mv`
both): this script only ever adds files that are missing.

    python3 unity/tools/meta.py [--check] [package ...]
"""

import hashlib
import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parent.parent / "Packages"

ALL = [
    "com.triband.storey",
    "com.triband.storey.authoring",
    "com.triband.storey.playkit",
]

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

SIMPLE = """fileFormatVersion: 2
guid: {guid}
{importer}:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

SHADER_INCLUDE = """fileFormatVersion: 2
guid: {guid}
ShaderIncludeImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

IMPORTERS = {
    ".cs": ("MonoImporter", SCRIPT),
    ".asmdef": ("AssemblyDefinitionImporter", SIMPLE),
    ".asmref": ("AssemblyDefinitionReferenceImporter", SIMPLE),
    ".json": ("TextScriptImporter", SIMPLE),
    ".md": ("TextScriptImporter", SIMPLE),
    ".txt": ("TextScriptImporter", SIMPLE),
    ".hlsl": ("ShaderIncludeImporter", SHADER_INCLUDE),
    ".cginc": ("ShaderIncludeImporter", SHADER_INCLUDE),
}


def guid_for(package: str, relative: str) -> str:
    return hashlib.sha256(f"{package}/{relative}".encode()).hexdigest()[:32]


def hidden(relative: Path) -> bool:
    """A trailing ~ hides a folder from the asset database; so does a leading dot."""
    return any(p.endswith("~") or p.startswith(".") for p in relative.parts)


def wanted(package: str):
    root = PACKAGES / package
    for path in sorted(root.rglob("*")):
        relative = path.relative_to(root)
        if hidden(relative) or path.name.endswith(".meta"):
            continue
        if path.is_dir():
            body = FOLDER.format(guid=guid_for(package, str(relative)))
        else:
            entry = IMPORTERS.get(path.suffix)
            if entry is None:
                print(f"  ? {package}/{relative}: no importer known for {path.suffix}", file=sys.stderr)
                continue
            importer, template = entry
            body = template.format(guid=guid_for(package, str(relative)), importer=importer)
        yield path.with_name(path.name + ".meta"), body


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--check"]
    check = "--check" in sys.argv
    packages = argv or ALL

    unknown = [p for p in packages if not (PACKAGES / p).is_dir()]
    if unknown:
        for p in unknown:
            print(f"no such package: {p}", file=sys.stderr)
        return 2

    missing, wrong, total = [], [], 0
    for package in packages:
        for meta, body in wanted(package):
            total += 1
            relative = meta.relative_to(PACKAGES)
            if not meta.exists():
                missing.append(relative)
                if not check:
                    meta.write_text(body)
            elif meta.read_text() != body:
                # Left alone deliberately: Unity may have rewritten it with a different
                # GUID, and overwriting that is how references break.
                wrong.append(relative)

    for m in missing:
        print(("missing: " if check else "wrote ") + str(m))
    for w in wrong:
        print(f"differs from what would be generated (left alone): {w}")
    if not missing and not wrong:
        print(f"all {total} .meta files present across {len(packages)} packages")
    return 1 if (check and missing) else 0


if __name__ == "__main__":
    sys.exit(main())
