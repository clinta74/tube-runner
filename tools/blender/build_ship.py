"""Builds the Tube Runner ship in Blender, renders preview views, and exports a .glb.

    blender -b --factory-startup --python tools/blender/build_ship.py -- <out_dir> <variant> [install]

The game flies variant B. With `install`, the .glb is also written to game/models/ship.glb, which
`tube play` imports before it starts; commit that file and the .import Godot writes beside it.

Everything is written in Godot's frame - X right, Y up, forward is -Z - and converted on the way
in, so the numbers line up with ShipView.cs. Blender is Z up, and glTF export maps Blender +Y to
-Z, so Godot (x, y, z) is Blender (x, -z, y).

Parts the game animates are separate objects with their pivots where the game turns them:
ailerons (hinged on their front edge), fins (at the root), nozzles (at their centre). Materials
are named Hull, Panel, Dark, Accent, Nozzle and Glass so the game can recolour each per theme.

Panel is the hull's second tone, on facets the chase camera sees - the rear facet of each wing,
the upper sides of the body, the strakes, the trailing half of each fin - so the two tones follow
the ship's own structure and the lighter facets ahead read as leading-edge bands. Dark also draws the panel lines: frames across the body, the
creases along its shoulders, the seam behind the nose, panel breaks and the hinge line on each
wing, the rudder line on each fin, and the bars across the canopy.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else os.path.join(os.getcwd(), 'out')
VARIANT = argv[1] if len(argv) > 1 else 'B'
INSTALL = len(argv) > 2 and argv[2] == 'install'
os.makedirs(OUT, exist_ok=True)


def G(x, y, z):
    """Godot coordinates to Blender's."""
    return Vector((x, -z, y))


# ------------------------------------------------------------------ scene
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def material(name, color, emission=0.0, rough=0.4, metal=0.2):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1.0)
    bsdf.inputs['Roughness'].default_value = rough
    bsdf.inputs['Metallic'].default_value = metal
    if emission > 0:
        bsdf.inputs['Emission Color'].default_value = (*color, 1.0)
        bsdf.inputs['Emission Strength'].default_value = emission
    return m


# Preview colours: the first level's white hull and cyan seam light.
WHITE = (0.92, 0.94, 0.97)
CYAN = (0.5, 0.94, 1.0)
HULL = material('Hull', WHITE, emission=0.08, rough=0.38, metal=0.2)
DARK = material('Dark', (0.16, 0.17, 0.2), rough=0.55, metal=0.25)
ACCENT = material('Accent', CYAN, emission=0.9)
NOZZLE = material('Nozzle', CYAN, emission=1.3)
GLASS = material('Glass', (0.05, 0.08, 0.12), emission=0.3, rough=0.12, metal=0.6)
PANEL = material('Panel', (0.50, 0.53, 0.58), emission=0.04, rough=0.45, metal=0.2)
MATERIALS = [HULL, DARK, ACCENT, NOZZLE, GLASS, PANEL]

# Panel lines: narrow, and lifted just clear of the surface they are drawn on.
LINE_WIDTH = 0.012
LINE_LIFT = 0.004


def make_object(name, build, parent=None, location=None, rotation=None):
    """Creates a mesh object from a bmesh-building function: build(bm) -> None, faces tagged with
    a material index into MATERIALS through face.material_index."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    build(bm)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    for m in MATERIALS:
        mesh.materials.append(m)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    if location is not None:
        obj.location = location
    if rotation is not None:
        obj.rotation_mode = 'XYZ'
        obj.rotation_euler = rotation
    return obj


def empty(name, parent=None, location=(0, 0, 0), rotation=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    scene.collection.objects.link(obj)
    obj.parent = parent
    obj.location = location
    obj.rotation_mode = 'XYZ'
    obj.rotation_euler = rotation
    return obj


def loft(bm, sections, mat, cap_start=True, cap_end=True, edge_mats=None):
    """Quads between consecutive rings of points (Blender coordinates). A ring of one point is an
    apex. Rings must share a winding and a length, apexes aside. `edge_mats` gives the facets
    between ring points i and i+1 a material of their own, all the way along."""
    rings = [[bm.verts.new(p) for p in ring] for ring in sections]
    faces = []
    edges = []
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1:
            for i in range(len(b)):
                faces.append(bm.faces.new((a[0], b[i], b[(i + 1) % len(b)])))
                edges.append(i)
        elif len(b) == 1:
            for i in range(len(a)):
                faces.append(bm.faces.new((a[i], a[(i + 1) % len(a)], b[0])))
                edges.append(i)
        else:
            for i in range(len(a)):
                faces.append(bm.faces.new((a[i], a[(i + 1) % len(a)], b[(i + 1) % len(b)], b[i])))
                edges.append(i)
    if cap_start and len(rings[0]) > 2:
        faces.append(bm.faces.new(rings[0]))
        edges.append(None)
    if cap_end and len(rings[-1]) > 2:
        faces.append(bm.faces.new(list(reversed(rings[-1]))))
        edges.append(None)
    for f, edge in zip(faces, edges):
        chosen = (edge_mats or {}).get(edge, mat) if edge is not None else mat
        f.material_index = MATERIALS.index(chosen)
    return faces


def ribbon(bm, points, ups, width, thickness, mat):
    """A thin closed strip along a polyline of Godot points, lying across each point's up."""
    sections = []
    for i, p in enumerate(points):
        p = Vector(p)
        q0 = Vector(points[max(i - 1, 0)])
        q1 = Vector(points[min(i + 1, len(points) - 1)])
        tangent = (q1 - q0).normalized()
        up = Vector(ups[i]).normalized()
        side = tangent.cross(up).normalized()
        up = side.cross(tangent).normalized()
        w, d = side * width / 2, up * thickness / 2
        sections.append([G(*(p - w - d)), G(*(p + w - d)), G(*(p + w + d)), G(*(p - w + d))])
    loft(bm, sections, mat)


def line(bm, points, normals):
    """A panel line: a narrow dark strip over a polyline of Godot surface points, each lifted
    along its outward normal so it sits just clear of the surface."""
    lifted = [tuple(Vector(p) + Vector(n).normalized() * LINE_LIFT) for p, n in zip(points, normals)]
    ribbon(bm, lifted, normals, LINE_WIDTH, LINE_LIFT * 1.5, DARK)


def lerp(a, b, t):
    return a + (b - a) * t


def table(stations, z):
    """Interpolates rows of (z, values...) at z."""
    if z <= stations[0][0]:
        return stations[0][1:]
    for s0, s1 in zip(stations, stations[1:]):
        if s0[0] <= z <= s1[0]:
            t = (z - s0[0]) / (s1[0] - s0[0])
            return tuple(lerp(a, b, t) for a, b in zip(s0[1:], s1[1:]))
    return stations[-1][1:]


# ------------------------------------------------------------------ the design, per variant
if VARIANT == 'A':
    # (z, half width, top, bottom, chine height)
    FUSELAGE = [
        (-1.30, 0.10, 0.08, -0.06, 0.00),
        (-1.00, 0.19, 0.17, -0.11, 0.00),
        (-0.70, 0.26, 0.24, -0.15, -0.005),
        (-0.35, 0.31, 0.26, -0.18, -0.01),
        (0.05, 0.32, 0.22, -0.20, -0.02),
        (0.55, 0.30, 0.18, -0.19, -0.02),
        (0.92, 0.24, 0.14, -0.15, -0.02),
    ]
    NOSE_TIP = -1.58
    CANOPY = (-1.12, -0.18, 0.045, 0.125)          # from z, to z, bulge, half width
    NACELLE = dict(x=0.28, y=-0.01, r=0.145, front=-0.30, back=0.95, squash=1.0)
    FIN = dict(root=(0.2, 0.15, 0.70), cant=22, height=0.42, root_chord=0.40, tip_chord=0.20, sweep=0.22)
    STRAKE = None
    WING_Y = -0.02
else:
    # Wider, flatter, chines sharper and lower: a blended body.
    FUSELAGE = [
        (-1.30, 0.13, 0.07, -0.05, -0.01),
        (-1.00, 0.25, 0.14, -0.09, -0.02),
        (-0.70, 0.35, 0.19, -0.12, -0.03),
        (-0.35, 0.42, 0.20, -0.14, -0.04),
        (0.05, 0.45, 0.17, -0.15, -0.04),
        (0.55, 0.42, 0.14, -0.14, -0.04),
        (0.92, 0.33, 0.11, -0.12, -0.04),
    ]
    NOSE_TIP = -1.62
    CANOPY = (-1.15, -0.30, 0.04, 0.12)
    NACELLE = dict(x=0.25, y=-0.03, r=0.13, front=-0.10, back=0.95, squash=0.75)
    FIN = dict(root=(0.24, 0.10, 0.66), cant=30, height=0.46, root_chord=0.46, tip_chord=0.20, sweep=0.28)
    STRAKE = dict(front=-1.0, back=-0.42)
    WING_Y = -0.03


def fuselage_section(z):
    w, t, b, c = table(FUSELAGE, z)
    pts = [(0, t), (w * 0.6, t * 0.82), (w, c), (w * 0.65, b * 0.9),
           (0, b), (-w * 0.65, b * 0.9), (-w, c), (-w * 0.6, t * 0.82)]
    return [G(x, y, z) for x, y in pts]


def section_points(z):
    """The fuselage section at z in Godot coordinates, with each point's outward direction from
    the section's middle."""
    w, t, b, c = table(FUSELAGE, z)
    pts = [(0, t), (w * 0.6, t * 0.82), (w, c), (w * 0.65, b * 0.9),
           (0, b), (-w * 0.65, b * 0.9), (-w, c), (-w * 0.6, t * 0.82)]
    middle = (t + b) / 2
    return [((x, y, z), (x, y - middle, 0)) for x, y in pts]


def top_at(z):
    return table(FUSELAGE, z)[1]


# ------------------------------------------------------------------ the body
root = empty('Ship')


def build_body(bm):
    zs = [r[0] for r in FUSELAGE]
    # Extra rings so the facets stay even along the length.
    stations = sorted(set(zs + [lerp(a, b, 0.5) for a, b in zip(zs, zs[1:])]))
    sections = [[G(0, 0.0, NOSE_TIP)]] + [fuselage_section(z) for z in stations]
    # The upper sides, between the shoulder crease and the chine, in the second tone.
    loft(bm, sections, HULL, cap_start=False, cap_end=True, edge_mats={1: PANEL, 6: PANEL})

    # Panel lines. The seam behind the nose goes right round; the frames run over the top from
    # chine to chine; the shoulder creases run the length of the body.
    seam = section_points(-1.18)
    line(bm, [p for p, n in seam] + [seam[0][0]], [n for p, n in seam] + [seam[0][1]])
    for z in (-0.2, 0.18, 0.56):
        frame = [section_points(z)[i] for i in (6, 7, 0, 1, 2)]
        line(bm, [p for p, n in frame], [n for p, n in frame])
    for i in (1, 7):
        crease = [section_points(z)[i] for z in (-1.0, -0.6, -0.2, 0.2, 0.6, 0.88)]
        line(bm, [p for p, n in crease], [n for p, n in crease])

    # The keel: a shallow ventral fin, pure silhouette.
    keel = [[G(-0.022, -0.17, 0.42), G(0.022, -0.17, 0.42)],
            [G(-0.016, -0.31, 0.62), G(0.016, -0.31, 0.62)],
            [G(-0.016, -0.31, 0.80), G(0.016, -0.31, 0.80)],
            [G(-0.022, -0.15, 0.88), G(0.022, -0.15, 0.88)]]
    a = [bm.verts.new(p) for pair in keel for p in pair]
    quads = [(0, 2, 3, 1), (2, 4, 5, 3), (4, 6, 7, 5), (0, 1, 7, 6), (0, 6, 4, 2), (1, 3, 5, 7)]
    for q in quads:
        bm.faces.new([a[i] for i in q]).material_index = MATERIALS.index(HULL)

    # The spine strip and the chine lines along the nose: the few lit marks that carry the
    # silhouette from the chase camera.
    spine = [(0, top_at(z) + 0.004, z) for z in (-0.12, 0.2, 0.5, 0.86)]
    ribbon(bm, spine, [(0, 1, 0)] * len(spine), 0.02, 0.01, ACCENT)
    for side in (1, -1):
        chine = []
        for z in (-1.28, -1.0, -0.7, -0.42):
            w, t, b, c = table(FUSELAGE, z)
            chine.append((side * (w + 0.004), c + 0.004, z))
        ribbon(bm, chine, [(side, 0.3, 0)] * len(chine), 0.02, 0.014, ACCENT)

    # Leading-edge strakes, variant B: thin blades from the cockpit's chine out to the wing root.
    if STRAKE:
        for side in (1, -1):
            f, bk = STRAKE['front'], STRAKE['back']
            wf = table(FUSELAGE, f)[0]
            wb = table(FUSELAGE, bk)[0]
            c = table(FUSELAGE, bk)[3]
            outline = [(side * (wf - 0.02), table(FUSELAGE, f)[3], f),
                       (side * (wb + 0.24), c - 0.005, bk + 0.18),
                       (side * (wb + 0.16), c - 0.005, bk + 0.42),
                       (side * (wb - 0.05), c, bk + 0.42)]
            top = [G(x, y + 0.012, z) for x, y, z in outline]
            bot = [G(x, y - 0.012, z) for x, y, z in outline]
            vt = [bm.verts.new(p) for p in top]
            vb = [bm.verts.new(p) for p in bot]
            bm.faces.new(vt).material_index = MATERIALS.index(PANEL)
            bm.faces.new(list(reversed(vb))).material_index = MATERIALS.index(HULL)
            for i in range(4):
                j = (i + 1) % 4
                bm.faces.new((vt[i], vb[i], vb[j], vt[j])).material_index = MATERIALS.index(HULL)
            # A lit edge along the strake's leading edge, and a line where it meets the body.
            ribbon(bm, [outline[0], outline[1]], [(0, 1, 0)] * 2, 0.022, 0.03, ACCENT)
            root_line = [(x, y + 0.012, z) for x, y, z in (outline[0], outline[3])]
            line(bm, root_line, [(0, 1, 0)] * 2)


make_object('Body', build_body, parent=root)


def build_canopy(bm):
    z0, z1, bulge, half = CANOPY
    sections = []
    n = 7
    for i in range(n + 1):
        z = lerp(z0, z1, i / n)
        t = top_at(z)
        u = i / n
        # Rises fast at the front, settles slowly behind: the glass sits low in the nose.
        rise = bulge * (math.sin(math.pi * u) ** 0.7)
        w = half * (math.sin(math.pi * u) ** 0.5)
        if i in (0, n):
            sections.append([G(0, t - 0.005, z)])
            continue
        base = t - 0.05
        sections.append([G(-w, base, z), G(-w * 0.82, t + rise * 0.55, z), G(0, t + rise, z),
                         G(w * 0.82, t + rise * 0.55, z), G(w, base, z)])
    loft(bm, sections, GLASS, cap_start=False, cap_end=False)
    # Close the underside, which is buried in the hull.
    # A lit sill round the glass: along each side of its base.
    for side in (1, -1):
        sill = []
        for i in range(1, n):
            z = lerp(z0, z1, i / n)
            u = i / n
            w = half * (math.sin(math.pi * u) ** 0.5)
            sill.append((side * (w + 0.008), top_at(z) - 0.012, z))
        ribbon(bm, sill, [(side * 0.6, 1, 0)] * len(sill), 0.016, 0.012, ACCENT)

    # The frame bars across the glass, which is what makes it read as a canopy and not a gem.
    for u in (0.36, 0.64):
        z = lerp(z0, z1, u)
        t = top_at(z)
        rise = bulge * (math.sin(math.pi * u) ** 0.7)
        w = half * (math.sin(math.pi * u) ** 0.5)
        base = t - 0.012
        bar = [(-w, base, z), (-w * 0.82, t + rise * 0.55, z), (0, t + rise, z),
               (w * 0.82, t + rise * 0.55, z), (w, base, z)]
        normals = [(-1, 0.3, 0), (-0.6, 1, 0), (0, 1, 0), (0.6, 1, 0), (1, 0.3, 0)]
        line(bm, bar, normals)


make_object('Canopy', build_canopy, parent=root)


# ------------------------------------------------------------------ engines
def octagon(cx, cy, z, r, squash):
    return [G(cx + r * math.cos(a), cy + r * squash * math.sin(a), z)
            for a in (math.tau * k / 8 + math.pi / 8 for k in range(8))]


def build_nacelles(bm):
    nx, ny, r, sq = NACELLE['x'], NACELLE['y'], NACELLE['r'], NACELLE['squash']
    front, back = NACELLE['front'], NACELLE['back']
    for side in (1, -1):
        cx = side * nx
        rings = [(front, r * 0.78), (front + 0.15, r * 0.94), (0.1, r), (back - 0.08, r), (back, r * 0.92)]
        sections = [octagon(cx, ny, z, rr, sq) for z, rr in rings]
        faces = loft(bm, sections, DARK, cap_start=True, cap_end=True)
        # The intake face reads as an opening: dark, with a lit lip round it.
        faces[-2].material_index = MATERIALS.index(DARK)
        lip = octagon(cx, ny, front - 0.004, r * 0.8, sq)
        lip_in = octagon(cx, ny, front - 0.004, r * 0.62, sq)
        vo = [bm.verts.new(p) for p in lip]
        vi = [bm.verts.new(p) for p in lip_in]
        for i in range(8):
            j = (i + 1) % 8
            bm.faces.new((vo[i], vo[j], vi[j], vi[i])).material_index = MATERIALS.index(ACCENT)
        # Two lit bands round each nacelle, which is what the victory camera looks at.
        for z in (0.26, 0.56):
            band = [octagon(cx, ny, z - 0.0125, r * 1.06, sq), octagon(cx, ny, z + 0.0125, r * 1.06, sq)]
            loft(bm, band, ACCENT)


make_object('Nacelles', build_nacelles, parent=root)


def build_nozzle(bm):
    # Built about its own centre and along the game's -90° X cylinder axis: the game scales it
    # along its length with thrust, so its pivot is its middle.
    length, front_r, back_r = 0.28, 0.135, 0.095
    outer = [octagon(0, 0, -length / 2, front_r, 1.0), octagon(0, 0, length / 2, back_r, 1.0)]
    loft(bm, outer, NOZZLE, cap_start=True, cap_end=False)
    # The open end: a dark throat inside a lit rim.
    rim_in = octagon(0, 0, length / 2, back_r * 0.72, 1.0)
    vo = [bm.verts.new(p) for p in outer[1]]
    vi = [bm.verts.new(p) for p in rim_in]
    for i in range(8):
        j = (i + 1) % 8
        bm.faces.new((vo[i], vo[j], vi[j], vi[i])).material_index = MATERIALS.index(NOZZLE)
    deep = octagon(0, 0, length / 2 - 0.09, back_r * 0.6, 1.0)
    vd = [bm.verts.new(p) for p in deep]
    for i in range(8):
        j = (i + 1) % 8
        bm.faces.new((vi[i], vi[j], vd[j], vd[i])).material_index = MATERIALS.index(DARK)
    bm.faces.new(list(reversed(vd))).material_index = MATERIALS.index(DARK)


for side, label in ((1, 'R'), (-1, 'L')):
    make_object(f'Nozzle_{label}', build_nozzle, parent=root,
                location=G(side * NACELLE['x'], NACELLE['y'], 1.0))


# ------------------------------------------------------------------ wings
WING_ROOT, SPAN = 0.2, 1.05
ROOT_LEAD, TIP_LEAD, ROOT_TRAIL, TIP_TRAIL = -0.42, 0.42, 0.6, 0.74
AILERON_FROM, AILERON_TO, AILERON_CHORD = 0.4, 1.0, 0.22


def lead(x):
    return ROOT_LEAD + (TIP_LEAD - ROOT_LEAD) * x / SPAN


def trail(x):
    return ROOT_TRAIL + (TIP_TRAIL - ROOT_TRAIL) * x / SPAN


def thick(x):
    return 0.085 * (1 - 0.6 * max(0.0, x) / SPAN)


def build_wing(side):
    def build(bm):
        def section(x, cut):
            zl, zt = lead(x), trail(x)
            ridge = zl + 0.38 * (zt - zl)
            if cut:
                zt -= AILERON_CHORD
            h = thick(x) / 2
            pts = [(x, 0, zl), (x, h, ridge), (x, 0, zt), (x, -h, ridge)]
            return [G(side * px, py, pz) for px, py, pz in pts]

        stations = [(-0.16, False), (AILERON_FROM, False), (AILERON_FROM, True),
                    (AILERON_TO, True), (AILERON_TO, False), (SPAN, False)]
        # The facet from the ridge back to the trailing edge in the second tone: it is the one the
        # chase camera sees, and the lighter facet ahead of it reads as a leading-edge band.
        loft(bm, [section(x, c) for x, c in stations], HULL, edge_mats={1: PANEL})

        def top(x, z):
            """A point on the wing's upper surface, at span x and chord position z."""
            zl, zt = lead(x), trail(x)
            ridge = zl + 0.38 * (zt - zl)
            if AILERON_FROM < x < AILERON_TO:
                zt -= AILERON_CHORD
            h = thick(x) / 2
            y = h * (z - zl) / (ridge - zl) if z <= ridge else h * (zt - z) / (zt - ridge)
            return (side * x, max(0.0, y), z)

        # Panel breaks across the wing, and the hinge line in front of the aileron.
        for x in (0.42, 0.74):
            zl, zt = lead(x), trail(x) - AILERON_CHORD
            ridge = zl + 0.38 * (trail(x) - zl)
            pts = [top(x, z) for z in (zl + 0.05, ridge, zt - 0.02)]
            line(bm, pts, [(0, 1, 0)] * 3)
        hinge = [top(x, trail(x) - AILERON_CHORD - 0.025) for x in (0.42, 0.7, 0.98)]
        line(bm, hinge, [(0, 1, 0)] * 3)

        # The lit leading edge, and a lit tip.
        edge = [(side * x, 0.0, lead(x) - 0.006) for x in (0.0, 0.35, 0.7, SPAN)]
        ribbon(bm, edge, [(0, 1, 0)] * len(edge), 0.05, 0.03, ACCENT)
        tip = [(side * (SPAN + 0.004), 0.0, z) for z in (lead(SPAN), trail(SPAN))]
        ribbon(bm, tip, [(0, 1, 0)] * 2, 0.02, 0.05, ACCENT)
    return build


def build_aileron(length):
    def build(bm):
        # Local frame: X along the hinge, the hinge at Z 0, the trailing edge at +Z.
        h = 0.028
        def section(x):
            return [G(x, h, 0), G(x, 0.0, AILERON_CHORD), G(x, -h, 0)]
        loft(bm, [section(-length / 2), section(length / 2)], HULL)
        # A lit trailing edge, the part of it the eye follows as it moves.
        te = [(-length / 2, 0.0, AILERON_CHORD - 0.012), (length / 2, 0.0, AILERON_CHORD - 0.012)]
        ribbon(bm, te, [(0, 1, 0)] * 2, 0.03, 0.022, ACCENT)
    return build


for side, label in ((1, 'R'), (-1, 'L')):
    wing = empty(f'Wing_{label}', parent=root, location=G(side * WING_ROOT, WING_Y, 0),
                 rotation=(0, side * math.radians(10), 0))
    make_object(f'WingPanel_{label}', build_wing(side), parent=wing)

    hinge_from = (side * AILERON_FROM, trail(AILERON_FROM) - AILERON_CHORD)
    hinge_to = (side * AILERON_TO, trail(AILERON_TO) - AILERON_CHORD)
    mx, mz = (hinge_from[0] + hinge_to[0]) / 2, (hinge_from[1] + hinge_to[1]) / 2
    length = math.hypot(hinge_to[0] - hinge_from[0], hinge_to[1] - hinge_from[1]) - 0.02
    rake = math.atan2(TIP_TRAIL - ROOT_TRAIL, SPAN)
    # Godot's mount turns -side*rake about its Y, which is Blender's Z.
    mount = empty(f'AileronMount_{label}', parent=wing, location=G(mx, 0, mz),
                  rotation=(0, 0, -side * rake))
    make_object(f'Aileron_{label}', build_aileron(length), parent=mount)


# ------------------------------------------------------------------ fins
def build_fin(bm):
    # Local frame: the root along Z at Y 0, the pivot at the root's middle.
    hgt, rc, tc, sw = FIN['height'], FIN['root_chord'], FIN['tip_chord'], FIN['sweep']
    t0, t1 = 0.05, 0.024
    root_s = [G(0, 0, -rc / 2), G(t0 / 2, 0, -rc / 2 + rc * 0.4), G(0, 0, rc / 2), G(-t0 / 2, 0, -rc / 2 + rc * 0.4)]
    tz = -rc / 2 + sw
    tip_s = [G(0, hgt, tz), G(t1 / 2, hgt, tz + tc * 0.4), G(0, hgt, tz + tc), G(-t1 / 2, hgt, tz + tc * 0.4)]
    # The trailing half of each side in the second tone, the half the chase camera sees.
    loft(bm, [root_s, tip_s], HULL, edge_mats={1: PANEL, 2: PANEL})
    # The rudder's hinge line, on both sides.
    chord = 0.62
    for flank in (1, -1):
        thin = 1 - (chord - 0.4) / 0.6
        rudder = [(flank * t0 / 2 * thin, 0.03, -rc / 2 + chord * rc),
                  (flank * t1 / 2 * thin, hgt - 0.03, tz + chord * tc)]
        line(bm, rudder, [(flank, 0, 0)] * 2)
    # A lit cap along the top of the fin.
    cap = [(0, hgt + 0.006, tz - 0.004), (0, hgt + 0.006, tz + tc + 0.004)]
    ribbon(bm, cap, [(1, 0, 0)] * 2, 0.022, 0.035, ACCENT)


for side, label in ((1, 'R'), (-1, 'L')):
    x, y, z = FIN['root']
    make_object(f'Fin_{label}', build_fin, parent=root, location=G(side * x, y, z),
                rotation=(0, side * math.radians(FIN['cant']), 0))


# ------------------------------------------------------------------ export
bpy.ops.object.select_all(action='DESELECT')
glb = os.path.join(OUT, f'ship_{VARIANT}.glb')
bpy.ops.export_scene.gltf(filepath=glb, export_format='GLB', export_yup=True, export_apply=False)
print('exported', glb)
if INSTALL:
    game = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'game', 'models', 'ship.glb')
    bpy.ops.export_scene.gltf(filepath=os.path.normpath(game), export_format='GLB', export_yup=True, export_apply=False)
    print('installed', os.path.normpath(game))

# ------------------------------------------------------------------ preview renders
world = bpy.data.worlds.new('World')
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get('Background')
bg.inputs['Color'].default_value = (0.012, 0.014, 0.02, 1)
bg.inputs['Strength'].default_value = 1.0


def sun(name, energy, color, pitch, yaw):
    data = bpy.data.lights.new(name, 'SUN')
    data.energy = energy
    data.color = color
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.rotation_euler = (math.radians(pitch), 0, math.radians(yaw))
    return obj


sun('Key', 3.2, (1.0, 0.95, 0.88), 40, -35)
sun('Fill', 0.9, (0.7, 0.8, 1.0), 70, 140)
sun('Rim', 1.6, (0.8, 0.9, 1.0), 110, 180)

for engine in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
    try:
        scene.render.engine = engine
        break
    except TypeError:
        continue
scene.view_settings.view_transform = 'Standard'
scene.render.resolution_x = 960
scene.render.resolution_y = 600
scene.render.film_transparent = False

cam_data = bpy.data.cameras.new('Camera')
cam = bpy.data.objects.new('Camera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

VIEWS = {
    'chase': dict(at=G(0, 1.25, 4.6), look=G(0, 0.0, -0.9), lens=50, ortho=None),
    'front': dict(at=G(2.4, 1.1, -3.1), look=G(0, 0, 0.0), lens=50, ortho=None),
    'side': dict(at=G(-6, 0.15, 0.0), look=G(0, 0.15, 0.0), lens=50, ortho=3.6),
    'top': dict(at=G(0, 6, 0.0), look=G(0, 0, 0.0), lens=50, ortho=3.4),
}
for view, v in VIEWS.items():
    cam.location = v['at']
    direction = v['look'] - v['at']
    # The camera looks down its -Z with its Y up.
    cam.rotation_mode = 'QUATERNION'
    cam.rotation_quaternion = direction.to_track_quat('-Z', 'Y')
    if view == 'top':
        # Looking straight down with the nose up the frame.
        cam.rotation_mode = 'XYZ'
        cam.rotation_euler = (0, 0, 0)
    if v['ortho']:
        cam_data.type = 'ORTHO'
        cam_data.ortho_scale = v['ortho']
    else:
        cam_data.type = 'PERSP'
        cam_data.lens = v['lens']
    scene.render.filepath = os.path.join(OUT, f'{VARIANT}_{view}.png')
    bpy.ops.render.render(write_still=True)
    print('rendered', scene.render.filepath)
