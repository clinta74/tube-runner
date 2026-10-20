"""Builds the power-up icons and the target in Blender, exports them to the game, and renders a
preview sheet.

    blender -b --factory-startup --python tools/blender/build_props.py -- <preview_dir> [install]

With `install`, writes game/models/pickups.glb and game/models/target.glb, which `tube play`
imports before it starts; commit them and the .import files Godot writes beside them.

Written in Godot's frame - X right, Y up, forward is -Z - and converted on the way in, as in
build_ship.py: Godot (x, y, z) is Blender (x, -z, y).

The icons, one object each named Icon_<kind>, are flat shapes facing +Z with a little depth. The
game billboards them, as it did the text labels they replace, so they always face the camera and
read on any wall. Each has two materials: Icon, the lit shape, which the game gives the power-up's
colour; and Trim, the dark detail on its face.

The target, one object named Target, fits a unit box and spins about Y, the wall's normal. Shell is
its lit core, which takes the target's colour - or its key's, or its turn's - and Frame is the
dark ring and spikes it is held in.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else os.path.join(os.getcwd(), 'out')
INSTALL = len(argv) > 1 and argv[1] == 'install'
os.makedirs(OUT, exist_ok=True)
GAME_MODELS = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'game', 'models'))


def G(x, y, z):
    return Vector((x, -z, y))


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def material(name, color, emission=0.0, rough=0.4, metal=0.1):
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


ICON = material('Icon', (0.9, 0.9, 0.9), emission=0.8)
TRIM = material('Trim', (0.04, 0.045, 0.06), rough=0.6)
SHELL = material('Shell', (1.0, 0.23, 0.42), emission=1.2)
FRAME = material('Frame', (0.1, 0.11, 0.14), rough=0.5, metal=0.4)

DEPTH = 0.14        # how thick an icon is
TRIM_LIFT = 0.012   # how far the trim stands proud of the icon's face


def new_object(name, mats):
    mesh = bpy.data.meshes.new(name)
    for m in mats:
        mesh.materials.append(m)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


def finish(obj, bm):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()


# ------------------------------------------------------------------ flat shapes, extruded
def prism(bm, outline, z0, z1, mat_index):
    """An outline in X,Y (Godot) extruded from z0 (back) to z1 (front)."""
    back = [bm.verts.new(G(x, y, z0)) for x, y in outline]
    front = [bm.verts.new(G(x, y, z1)) for x, y in outline]
    faces = [bm.faces.new(front), bm.faces.new(list(reversed(back)))]
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((back[i], back[j], front[j], front[i])))
    for f in faces:
        f.material_index = mat_index


def band(bm, outer, inner, z0, z1, mat_index):
    """The ring between two outlines of the same length, extruded: a shape with a hole in it."""
    n = len(outer)
    ob = [bm.verts.new(G(x, y, z0)) for x, y in outer]
    of = [bm.verts.new(G(x, y, z1)) for x, y in outer]
    ib = [bm.verts.new(G(x, y, z0)) for x, y in inner]
    inf = [bm.verts.new(G(x, y, z1)) for x, y in inner]
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((of[i], of[j], inf[j], inf[i])))   # front
        faces.append(bm.faces.new((ob[j], ob[i], ib[i], ib[j])))     # back
        faces.append(bm.faces.new((ob[i], ob[j], of[j], of[i])))     # outer side
        faces.append(bm.faces.new((ib[j], ib[i], inf[i], inf[j])))   # inner side
    for f in faces:
        f.material_index = mat_index


def rect(cx, cy, w, h):
    return [(cx - w / 2, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + w / 2, cy + h / 2), (cx - w / 2, cy + h / 2)]


def circle(cx, cy, r, n=24, phase=0.0):
    return [(cx + r * math.cos(phase + math.tau * k / n), cy + r * math.sin(phase + math.tau * k / n)) for k in range(n)]


def shield_outline(scale=1.0, n=10):
    """A heater shield: a gently arched top, straight sides, and curves meeting in a point."""
    pts = []
    top, shoulder, point, half = 0.52, 0.02, -0.6, 0.44
    for k in range(n + 1):                                  # the top, left to right, arched
        x = -half + 2 * half * k / n
        pts.append((x, top + 0.05 * (1 - (x / half) ** 2)))
    for k in range(1, n + 1):                               # the right side curving to the point
        t = k / n
        pts.append((half * (1 - t ** 1.6), shoulder + (point - shoulder) * math.sin(t * math.pi / 2)))
    for k in range(n - 1, 0, -1):                           # and back up the left
        t = k / n
        pts.append((-half * (1 - t ** 1.6), shoulder + (point - shoulder) * math.sin(t * math.pi / 2)))
    pts.append((-half, shoulder))
    return [(x * scale, y * scale) for x, y in pts]


def plus(bm, cx, cy, size, bar, z, mat_index):
    prism(bm, rect(cx, cy, size, bar), z, z + TRIM_LIFT, mat_index)
    prism(bm, rect(cx, cy, bar, size), z, z + TRIM_LIFT, mat_index)


ICON_I, TRIM_I = 0, 1


def icon(name, build):
    obj = new_object(name, [ICON, TRIM])
    bm = bmesh.new()
    build(bm)
    finish(obj, bm)
    return obj


def shield(bm):
    prism(bm, shield_outline(), 0, DEPTH, ICON_I)
    plus(bm, 0, 0.02, 0.46, 0.13, DEPTH, TRIM_I)


def full_shields(bm):
    prism(bm, shield_outline(), 0, DEPTH, ICON_I)
    for y in (0.26, 0.04, -0.18):
        w = 0.56 if y > 0 else 0.56 - (0.04 - y) * 0.9
        prism(bm, rect(0, y, w, 0.11), DEPTH, DEPTH + TRIM_LIFT, TRIM_I)


def shield_slot(bm):
    outer = shield_outline()
    inner = shield_outline(scale=0.72)
    # A shield frame with nothing in it, which is what an extra slot is, and a plus in the gap.
    band(bm, outer, [(x, y + 0.025) for x, y in inner], 0, DEPTH, ICON_I)
    prism(bm, [(x, y + 0.025) for x, y in inner], 0, DEPTH * 0.4, TRIM_I)
    plus(bm, 0, 0.06, 0.34, 0.1, DEPTH * 0.4, ICON_I)


def chevron(cy, w=0.5, h=0.34, t=0.15):
    return [(0, cy + h), (w, cy + h - w * 0.8), (w, cy + h - w * 0.8 - t), (0, cy + h - t),
            (-w, cy + h - w * 0.8 - t), (-w, cy + h - w * 0.8)]


def rapid_fire(bm):
    for cy in (0.12, -0.14, -0.4):
        prism(bm, chevron(cy), 0, DEPTH, ICON_I)


def ring_gun(bm):
    n = 32
    band(bm, circle(0, 0, 0.4, n), circle(0, 0, 0.27, n), 0, DEPTH, ICON_I)
    for k in range(8):
        a = math.tau * k / 8 + math.pi / 8
        c, s = math.cos(a), math.sin(a)
        tri = [(0.38 * c - 0.07 * s, 0.38 * s + 0.07 * c), (0.62 * c, 0.62 * s), (0.38 * c + 0.07 * s, 0.38 * s - 0.07 * c)]
        prism(bm, tri, 0, DEPTH, ICON_I)
    prism(bm, circle(0, 0, 0.12, 12), DEPTH * 0.3, DEPTH, ICON_I)


def unstoppable(bm):
    star = []
    for k in range(16):
        r = 0.62 if k % 2 == 0 else 0.3
        a = math.pi / 2 + math.tau * k / 16
        star.append((r * math.cos(a), r * math.sin(a)))
    prism(bm, star, 0, DEPTH, ICON_I)
    prism(bm, circle(0, 0, 0.17, 12), DEPTH, DEPTH + TRIM_LIFT, TRIM_I)


def agility(bm):
    # A wing, feathers swept out and down, centred on the icon.
    wing = [(-0.5, -0.22), (-0.52, 0.06), (-0.32, 0.3), (0.04, 0.46), (0.6, 0.56),
            (0.36, 0.3), (0.44, 0.12), (0.18, 0.08), (0.24, -0.12), (0.0, -0.1),
            (0.02, -0.32), (-0.2, -0.22)]
    prism(bm, [(x - 0.04, y - 0.06) for x, y in wing], 0, DEPTH, ICON_I)
    # Dark lines between the feathers.
    for (x0, y0), (x1, y1) in (((0.3, 0.24), (-0.18, 0.12)), ((0.12, 0.02), (-0.28, -0.06))):
        dx, dy = x1 - x0, y1 - y0
        length = math.hypot(dx, dy)
        nx, ny = -dy / length * 0.025, dx / length * 0.025
        quad = [(x0 + nx - 0.04, y0 + ny - 0.06), (x1 + nx - 0.04, y1 + ny - 0.06),
                (x1 - nx - 0.04, y1 - ny - 0.06), (x0 - nx - 0.04, y0 - ny - 0.06)]
        prism(bm, quad, DEPTH, DEPTH + TRIM_LIFT, TRIM_I)


ICONS = [
    ('Icon_Shield', shield, (0.3, 1.0, 0.45)),
    ('Icon_FullShields', full_shields, (0.2, 0.9, 1.0)),
    ('Icon_ShieldSlot', shield_slot, (1.0, 0.85, 0.3)),
    ('Icon_RapidFire', rapid_fire, (1.0, 0.5, 0.15)),
    ('Icon_RingGun', ring_gun, (1.0, 0.3, 1.0)),
    ('Icon_Unstoppable', unstoppable, (1.0, 0.2, 0.2)),
    ('Icon_Agility', agility, (0.62, 0.5, 1.0)),
]
icons = [(icon(name, build), color) for name, build, color in ICONS]


# ------------------------------------------------------------------ the target
def octagon_ring(r, y, n=8, phase=math.pi / 8):
    return [G(r * math.cos(phase + math.tau * k / n), y, r * math.sin(phase + math.tau * k / n)) for k in range(n)]


def build_target():
    obj = new_object('Target', [SHELL, FRAME])
    bm = bmesh.new()
    SHELL_I, FRAME_I = 0, 1
    # The core: the diamond the game has always used, a little smaller, so the frame sits round it.
    r, h = 0.34, 0.5
    equator = [bm.verts.new(G(r * math.cos(math.tau * k / 4), 0, r * math.sin(math.tau * k / 4))) for k in range(4)]
    top, bottom = bm.verts.new(G(0, h, 0)), bm.verts.new(G(0, -h, 0))
    for k in range(4):
        j = (k + 1) % 4
        bm.faces.new((equator[k], equator[j], top)).material_index = SHELL_I
        bm.faces.new((equator[j], equator[k], bottom)).material_index = SHELL_I

    # The frame: a flat octagonal ring round the equator, standing off the core.
    def ring_faces(outer_r, inner_r, half):
        ot, ob = octagon_ring(outer_r, half), octagon_ring(outer_r, -half)
        it, ib = octagon_ring(inner_r, half), octagon_ring(inner_r, -half)
        vs = {k: [bm.verts.new(p) for p in ring] for k, ring in (('ot', ot), ('ob', ob), ('it', it), ('ib', ib))}
        for i in range(8):
            j = (i + 1) % 8
            for quad in ((vs['ot'][i], vs['ot'][j], vs['it'][j], vs['it'][i]),
                         (vs['ob'][j], vs['ob'][i], vs['ib'][i], vs['ib'][j]),
                         (vs['ob'][i], vs['ob'][j], vs['ot'][j], vs['ot'][i]),
                         (vs['ib'][j], vs['ib'][i], vs['it'][i], vs['it'][j])):
                bm.faces.new(quad).material_index = FRAME_I
    ring_faces(0.5, 0.41, 0.045)

    # Four spikes off the ring, with lit tips, so the silhouette turns as it spins.
    for k in range(4):
        a = math.tau * k / 4
        c, s = math.cos(a), math.sin(a)
        base = 0.48
        side = Vector((-s, 0, c))
        bc = Vector((base * c, 0, base * s))
        corners = [bc + side * 0.05 + Vector((0, 0.05, 0)), bc - side * 0.05 + Vector((0, 0.05, 0)),
                   bc - side * 0.05 - Vector((0, 0.05, 0)), bc + side * 0.05 - Vector((0, 0.05, 0))]
        tip = Vector((0.66 * c, 0, 0.66 * s))
        mid = Vector((0.58 * c, 0, 0.58 * s))
        vb = [bm.verts.new(G(*p)) for p in corners]
        vm = [bm.verts.new(G(*(mid + (p - bc) * 0.55))) for p in corners]
        vt = bm.verts.new(G(*tip))
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((vb[i], vb[j], vm[j], vm[i])).material_index = FRAME_I
            bm.faces.new((vm[i], vm[j], vt)).material_index = SHELL_I
        bm.faces.new(list(reversed(vb))).material_index = FRAME_I
    finish(obj, bm)
    return obj


target = build_target()


# ------------------------------------------------------------------ export
def export(objects, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_yup=True, use_selection=True)
    print('exported', path)


targets_dir = GAME_MODELS if INSTALL else OUT
os.makedirs(targets_dir, exist_ok=True)
export([o for o, _ in icons], os.path.join(targets_dir, 'pickups.glb'))
export([target], os.path.join(targets_dir, 'target.glb'))

# ------------------------------------------------------------------ preview
# Each icon in its own power-up's colour, on a pad, in a row, with the target at the end.
for i, (obj, color) in enumerate(icons):
    tinted = material(f'Preview_{obj.name}', color, emission=1.2)
    obj.data.materials[0] = tinted
    obj.location = G(-3.6 + 1.2 * i, 0.3, 0)
    pad = bpy.data.meshes.new(f'Pad{i}')
    bm = bmesh.new()
    ring = [bm.verts.new(G(-3.6 + 1.2 * i + 0.5 * math.cos(math.tau * k / 24), -0.75, 0.5 * math.sin(math.tau * k / 24))) for k in range(24)]
    bm.faces.new(ring)
    bm.to_mesh(pad)
    bm.free()
    pad.materials.append(material(f'PadPreview{i}', color, emission=1.5))
    pad_obj = bpy.data.objects.new(f'Pad{i}', pad)
    scene.collection.objects.link(pad_obj)
target.location = G(4.9, 0.1, 0)
target.scale = (1.4, 1.4, 1.4)
target.rotation_euler = (0, 0, math.radians(20))

world = bpy.data.worlds.new('World')
scene.world = world
world.use_nodes = True
world.node_tree.nodes.get('Background').inputs['Color'].default_value = (0.012, 0.014, 0.02, 1)
for name, energy, color, pitch, yaw in (('Key', 3.0, (1, 0.95, 0.88), 40, -30), ('Fill', 0.8, (0.7, 0.8, 1), 70, 150)):
    data = bpy.data.lights.new(name, 'SUN')
    data.energy, data.color = energy, color
    light = bpy.data.objects.new(name, data)
    scene.collection.objects.link(light)
    light.rotation_euler = (math.radians(pitch), 0, math.radians(yaw))
for engine in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
    try:
        scene.render.engine = engine
        break
    except TypeError:
        continue
scene.view_settings.view_transform = 'Standard'
scene.render.resolution_x, scene.render.resolution_y = 1800, 420
cam_data = bpy.data.cameras.new('Camera')
cam = bpy.data.objects.new('Camera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.location = G(0.7, 0.9, 9.0)
cam.rotation_mode = 'QUATERNION'
cam.rotation_quaternion = (G(0.7, 0.0, 0) - cam.location).to_track_quat('-Z', 'Y')
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 10.4
scene.render.filepath = os.path.join(OUT, 'props.png')
bpy.ops.render.render(write_still=True)
print('rendered', scene.render.filepath)
