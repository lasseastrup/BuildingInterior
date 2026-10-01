#!/usr/bin/env python3
"""Map a Storey layout's CSS colours to a Color Pipeline palette (docs/COLOURS.md §3.8).

    python3 unity/tools/map_to_palette.py ColorPalette.palette layout.storey out.storey

The command-line stand-in for the editor's *Map colours to palette...* until it exists. Every style colour
becomes the palette id of its nearest entry, by Color Pipeline's own measure (the Model Remapper's CIE76
Delta E, first entry wins a tie). The ten optional style colours (door, rail, ...) are written out too, with
their defaults mapped, so the layout holds no CSS colour at all and needs no StoreyColorSettings. The output
carries the palette block, so the prototype renders it with the palette's colours: what Unity will show.
A report of every mapping goes to stdout.
"""
import json, sys

COLORS = ['wall', 'trim', 'interior', 'floor', 'roof', 'core', 'glass']
OPTIONAL = {'door': '#3B3129', 'rail': '#3D4448', 'metal': '#A3ABAE', 'ceiling': '#F3F2EE', 'liftInterior': '#8E9A9E',
            'liftButton': '#FFB36B', 'detailMetal': '#C9CDCB', 'grille': '#6E7476', 'detailDark': '#5B5F5E', 'dish': '#DDE0DE'}


def palette_id(v0, v1):
    """Storey's palette id: SerializableGUID's m_Value0 and m_Value1 as 16 hex digits each (ColorRef.PaletteId)."""
    return f'{v0:016x}{v1:016x}'


def lab(r8, g8, b8):
    """Color Pipeline's ColorFormulas: sRGB 0..255 to CIE L*a*b* (D65, 2 degrees)."""
    def lin(c):
        c /= 255.0
        return ((c + 0.055) / 1.055) ** 2.4 if c > 0.04045 else c / 12.92
    R, G, B = (lin(c) * 100 for c in (r8, g8, b8))
    X, Y, Z = R * 0.4124 + G * 0.3576 + B * 0.1805, R * 0.2126 + G * 0.7152 + B * 0.0722, R * 0.0193 + G * 0.1192 + B * 0.9505
    def f(t): return t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    fx, fy, fz = f(X / 95.047), f(Y / 100.0), f(Z / 108.883)
    return 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)


def norm(h):
    h = h.lstrip('#').upper()
    return '#' + (''.join(c * 2 for c in h) if len(h) == 3 else h)


def main(palette_path, layout_path, out_path):
    entries = []
    for e in json.load(open(palette_path, encoding='utf-8-sig'))['m_Colors']:
        c = e['color']
        floor8 = [int(c[k] * 255) for k in 'rgb']                   # ColorEditorUtility: Mathf.FloorToInt(c * 255)
        hexv = '#' + ''.join(f'{min(255, max(0, round(c[k] * 255))):02X}' for k in 'rgb')
        entries.append({'id': palette_id(e['id']['m_Value0'], e['id']['m_Value1']), 'name': e['name'], 'hex': hexv, 'lab': lab(*floor8)})
    doc = json.load(open(layout_path, encoding='utf-8-sig'))

    def nearest(h):
        L = lab(*(int(h[i:i + 2], 16) for i in (1, 3, 5)))
        best, bd = None, 1e9
        for e in entries:
            d = sum((a - b) ** 2 for a, b in zip(L, e['lab'])) ** 0.5
            if d < bd: best, bd = e, d
        return best, bd

    chosen, uses = {}, {}
    for b in doc['buildings']:
        for st in [b['style']] + [f['style'] for f in b.get('floors', []) if f.get('style')]:
            for k in COLORS + list(OPTIONAL):
                v = st.get(k, OPTIONAL.get(k))
                if v is None or not v.startswith('#'): continue
                h = norm(v)
                if h not in chosen: chosen[h] = nearest(h)
                uses.setdefault(h, set()).add(k)
                st[k] = chosen[h][0]['id']
    used = {}
    for h, (e, _) in chosen.items(): used[e['id']] = {'name': e['name'], 'hex': e['hex']}
    doc['palette'] = dict(sorted(used.items()))
    with open(out_path, 'w') as f: f.write(json.dumps(doc, indent=1) + '\n')

    print(f'{len(chosen)} colours mapped to {len(used)} entries of {len(entries)}; written to {out_path}')
    print(f"{'layout':8}  {'entry':14} {'hex':8} {'dE':>5} {'similar':>7}  used for")
    for h, (e, d) in sorted(chosen.items(), key=lambda kv: -kv[1][1]):
        sim = max(0.0, 1 - round(d) / 255) * 100                     # GetColorSimilarity, as the Remapper shows it
        print(f"{h:8}  {e['name'][:14]:14} {e['hex']:8} {d:5.1f} {sim:6.1f}%  {', '.join(sorted(uses[h]))}")


if __name__ == '__main__':
    if len(sys.argv) != 4: sys.exit(__doc__)
    main(*sys.argv[1:])
