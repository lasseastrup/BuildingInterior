#!/usr/bin/env python3
"""Compile-check the Storey shaders without Unity.

The shaders under Packages/com.triband.storey/Unity/Shaders (and the Color Palette Lit
sample's) include URP's ShaderLibrary, which only exists inside a Unity project, so the
headless suite can't compile them and a mistake in Storey's own HLSL used to surface as
pink buildings in the editor. This compiles every pass of every .shader file without
keywords, with each of Storey's own keywords the pass declares (STOREY_STEADY,
STOREY_SIMPLE_LIGHTING) and all of them together, and with each main-light shadow keyword,
the punctual-light shadow caster's and each SH evaluation keyword it declares (Storey picks
its shadow coordinate, the caster's light direction, and where the ambient probe is
evaluated, by them), with the Slang compiler against
stubs/: small stand-ins for the URP functions and macros the shaders call, with URP's
signatures. It catches what Storey's code gets wrong (a field a variant's Varyings doesn't
have, a misspelt name, a type mismatch); URP itself, the #pragma keywords' other
combinations and the platform back ends are still Unity's to check.

    pip install slangpy==0.43.1     # the Slang compiler, no GPU needed
    python3 unity/tools/shaders/check.py [shader directory ...]

With no arguments it checks the package's shaders, the Color Palette Lit sample and the play
kit's. A stub the shaders start to need goes in stubs/, with URP's signature.
"""

import os
import re
import shutil
import sys
import tempfile
from pathlib import Path

here = Path(__file__).resolve().parent
package = here.parent.parent / "Packages" / "com.triband.storey"


def blocks(text, start, end):
    out, i = [], 0
    while (a := text.find(start, i)) >= 0:
        b = text.find(end, a)
        out.append(text[a + len(start):b])
        i = b + len(end)
    return out


def main(dirs):
    import slangpy as spy

    root = Path(tempfile.mkdtemp())
    try:
        # the include root: the stubs, the package where "Packages/com.triband.storey/..." finds it, and each shader
        # directory copied with Unity's #include_with_pragmas made a plain #include
        shutil.copytree(here / "stubs", root, dirs_exist_ok=True)
        (root / "Packages" / "com.triband.storey").symlink_to(package)
        checked, failed = 0, 0
        for n, d in enumerate(dirs):
            copy = root / f"dir{n}"
            copy.mkdir()
            for f in Path(d).iterdir():
                if f.suffix in (".hlsl", ".shader"):
                    (copy / f.name).write_text(f.read_text().replace("#include_with_pragmas", "#include"))
            for shader in sorted(copy.glob("*.shader")):
                text = re.sub(r"//[^\n]*", "", shader.read_text())
                shared = "".join(blocks(text, "HLSLINCLUDE", "ENDHLSL"))
                for p, prog in enumerate(blocks(text, "HLSLPROGRAM", "ENDHLSL")):
                    vert = re.search(r"#pragma\s+vertex\s+(\w+)", prog).group(1)
                    frag = re.search(r"#pragma\s+fragment\s+(\w+)", prog).group(1)
                    # Storey's own keywords, each alone and all together, and each shadow keyword and SH evaluation
                    # keyword the pass declares: Storey's code branches on what URP makes of those (the shadow coordinate,
                    # the caster's light direction, where the ambient probe is evaluated)
                    own = re.findall(r"#pragma\s+multi_compile\w*\s+_\s+(STOREY_\w+)", prog)
                    shadows = [k for k in ("_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN",
                                           "_CASTING_PUNCTUAL_LIGHT_SHADOW", "EVALUATE_SH_MIXED", "EVALUATE_SH_VERTEX")
                               if re.search(r"#pragma\s+multi_compile\w*\s+_\s[^\n]*\b" + k + r"\b", prog)]
                    body = re.sub(r"#pragma[^\n]*", "", prog)
                    for keys in [[]] + [[k] for k in own] + ([own] if len(own) > 1 else []) + [[k] for k in shadows]:
                        entry = (f'[shader("fragment")] float4 __frag(Varyings IN, FRONT_FACE_TYPE f : FRONT_FACE_SEMANTIC) : SV_Target {{ return {frag}(IN, f); }}'
                                 if frag == "StoreyFrag" else f'[shader("fragment")] float4 __frag(Varyings IN) : SV_Target {{ return {frag}(IN); }}')
                        src = "".join(f"#define {k}\n" for k in keys) + shared + body + \
                            f'\n[shader("vertex")] Varyings __vert(Attributes IN) {{ return {vert}(IN); }}\n{entry}\n'
                        label = f"{Path(d).name}/{shader.name} pass {p} {' '.join(keys) or '(no keyword)'}"
                        device = spy.Device(type=spy.DeviceType.cpu, compiler_options=spy.SlangCompilerOptions(
                            {"include_paths": [str(root), str(copy), str(package / "Unity" / "Shaders")]}))
                        checked += 1
                        try:
                            module = device.load_module_from_source(f"m{checked}", src)
                            module.entry_point("__vert"), module.entry_point("__frag")
                            print("ok  ", label)
                        except Exception as e:
                            failed += 1
                            print("FAIL", label)
                            print("\n".join("     " + l for l in str(e).splitlines() if l.strip())[:4000])
        print(f"{checked} variants, {failed} failed")
        return 1 if failed else 0
    finally:
        shutil.rmtree(root, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:] or [package / "Unity" / "Shaders", package / "Samples~" / "ColorPaletteLit",
                                   package.parent / "com.triband.storey.playkit" / "Runtime" / "Shaders"]))
