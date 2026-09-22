using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>
/// The player's interceptor: a wedge fuselage with a faceted nose and its cockpit glass set into it, cropped
/// delta wings, canted tail fins and twin engines, built from primitives and two cut planforms. From six units back only the silhouette and a few bright
/// accents read, so most of the detail is in how the parts move - nozzles stretch with speed,
/// the ailerons and rudders work the way an aircraft's do, the fins flare as you ease off, and the
/// hull runs hot while unstoppable. Forward is -Z, matching Godot's look_at.
/// </summary>
public partial class ShipView : Node3D
{
    private const float FlashSeconds = 0.07f;

    // How far a control surface swings at full deflection, and how fast it gets there. Quick, but
    // not instant: a key is either down or not, and a surface that snapped with it would flicker.
    private const float AileronDegrees = 38f;
    private const float RudderDegrees = 16f;
    private const float SurfaceRate = 16f;

    // An aileron answers two things. Most of it is how hard the ship is being asked to roll that it
    // is not yet doing - the kick as a turn starts, and the opposite kick as it is let go. The rest
    // is held for as long as the stick is, because steering round a tube is a roll that carries on.
    private const float AileronKick = 0.95f;
    private const float AileronHold = 0.5f;

    // The wing's planform, in its own frame: X out along the span from the root, Z aft. A cropped
    // delta - the leading edge raked back about forty degrees, the trailing edge about eight.
    private const float WingRoot = 0.2f;
    private const float Span = 1.05f;
    private const float RootLead = -0.42f;
    private const float TipLead = 0.42f;
    private const float RootTrail = 0.6f;
    private const float TipTrail = 0.74f;

    // The nose: a four-sided spike, its corners up, down and to either side. The cockpit glass is
    // laid on its upper faces, so both are built from the same numbers.
    private const float NoseRadius = 0.27f;
    private const float NoseY = 0.02f;
    private const float NoseBaseZ = -0.8f;
    private const float NoseTipZ = -1.5f;

    // The aileron's place along the span, and how deep it is cut into the trailing edge.
    private const float AileronFrom = 0.4f;
    private const float AileronTo = 1.0f;
    private const float AileronChord = 0.22f;

    private readonly List<Node3D> _ailerons = new();
    private readonly List<Node3D> _fins = new();
    private readonly List<Node3D> _nozzles = new();
    private readonly List<Node3D> _plumes = new();

    private StandardMaterial3D _hullMaterial = null!;
    private StandardMaterial3D _darkMaterial = null!;
    private StandardMaterial3D _accentMaterial = null!;
    private StandardMaterial3D _nozzleMaterial = null!;
    private StandardMaterial3D _shellMaterial = null!;
    private MeshInstance3D _flash = null!;
    private MeshInstance3D _shell = null!;

    private Color _shipColor = Colors.White;
    private Color _accentColor = Colors.White;
    private float _glow;
    private float _flashLeft;
    private float _time;
    private float _aileron;
    private float _rudder;

    public override void _Ready() => Build();

    /// <summary>Pops the muzzle flash at the nose; called when the gun fires.</summary>
    public void Fire() => _flashLeft = FlashSeconds;

    /// <summary>Colors the ship for a level: hull in its ship color, accents in its seam light.</summary>
    public void ApplyTheme(Theme theme)
    {
        _shipColor = theme.Ship.ToColor();
        _accentColor = theme.SeamLight.ToColor();
        _glow = theme.Glow;

        _hullMaterial.AlbedoColor = _shipColor;
        // A floor under the theme's glow keeps unlit faces in the ship's color instead of black.
        // Low: the ship is lit by a key, a fill and the environment's ambient now, and a hull that
        // glows on its own washes out the shading those give it.
        _hullMaterial.EmissionEnabled = true;
        _hullMaterial.Emission = _shipColor;
        _hullMaterial.EmissionEnergyMultiplier = HullGlow;

        _darkMaterial.AlbedoColor = _shipColor.Darkened(0.7f);

        _accentMaterial.AlbedoColor = _accentColor;
        _accentMaterial.Emission = _accentColor;

        _nozzleMaterial.AlbedoColor = _accentColor;
        _nozzleMaterial.Emission = _accentColor;

        _shellMaterial.AlbedoColor = new Color(_accentColor, 0.1f);
        _shellMaterial.Emission = _accentColor;
    }

    /// <param name="thrust">
    /// Where the throttle sits in its own range, 0 to 1. This is what the engines follow: it spans
    /// the same ground whatever speed the level runs at, where the speed effect does not.
    /// </param>
    /// <param name="bank">Smoothed steer in [-1, 1], the same value the whole ship rolls by.</param>
    /// <param name="steer">The stick itself in [-1, 1], which the bank is on its way to.</param>
    /// <param name="roll">
    /// A roll being flown for some other reason, in [-1, 1] with right positive: the half roll of a
    /// jump between walls, which is the hardest the ship ever rolls.
    /// </param>
    /// <param name="throttle">Ship throttle; below 1 means easing off.</param>
    /// <param name="intensity">Speed effect in [0, 1], driving the engines.</param>
    public void UpdateState(float dt, float bank, float steer, float roll, float throttle, float thrust,
        float intensity, float ramLeft, float recoveryLeft)
    {
        _time += dt;
        _flashLeft = Mathf.Max(0f, _flashLeft - dt);

        // Recovery shows as a pulsing shell around a hull that stays solid, so the ship never
        // vanishes at the moment you most need to see where it is.
        _shell.Visible = recoveryLeft > 0f;
        if (_shell.Visible)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 26f);
            _shellMaterial.AlbedoColor = new Color(_accentColor, 0.05f + 0.12f * pulse);
            // Peaks over the 1.3 threshold so the shell actually flares as it pulses, which is the
            // whole point of it: it has to be visible at the moment the ship has just been hit.
            _shellMaterial.EmissionEnergyMultiplier = 0.6f + 1.1f * pulse;
        }

        // Ailerons: a strip let into the back edge of each wing, hinged along its length, one down
        // and one up. Steering right lifts the ship's right wing - it is riding up the wall that
        // side - so it is the right aileron that drops, to lift it, and the left that rises. They
        // lead the ship rather than follow it: full over as a turn begins, easing as the bank
        // arrives, and thrown the other way to stop it, which is what makes them look like they
        // are doing the work.
        float wanted = Mathf.Clamp(AileronKick * (steer - bank) + AileronHold * steer + roll, -1f, 1f);
        float ease = 1f - Mathf.Exp(-SurfaceRate * dt);
        _aileron = Mathf.Lerp(_aileron, wanted, ease);
        _rudder = Mathf.Lerp(_rudder, Mathf.Clamp(steer, -1f, 1f), ease);
        for (int i = 0; i < _ailerons.Count; i++)
        {
            // About the hinge's own length, a positive angle drops the trailing edge.
            _ailerons[i].RotationDegrees = new Vector3(Side(i) * AileronDegrees * _aileron, 0f, 0f);
        }

        // The fins are rudders as well as airbrakes: they swing their trailing edges into the turn,
        // and easing off the throttle flares them out.
        float brake = Mathf.Clamp(1f - throttle, 0f, 1f);
        for (int i = 0; i < _fins.Count; i++)
        {
            _fins[i].RotationDegrees = new Vector3(0f, RudderDegrees * _rudder, Side(i) * -(22f + 30f * brake));
        }

        // Engines stretch and brighten with how hard they are being driven, which is mostly the
        // throttle rather than how fast the track happens to be.
        //
        // They used to run off the speed effect alone, and that maps absolute speed onto a fixed
        // window: at the back of the game a ship is over the top of it by a tenth of the way up the
        // throttle, so the engines sat pinned at full for the whole of the range the player actually
        // uses. The one place they should have been most expressive was the one place they were
        // frozen. A little of the speed effect is kept, so a genuinely fast level still reads as one.
        float drive = Mathf.Clamp(0.75f * thrust + 0.25f * intensity, 0f, 1f);

        float stretch = 1f + 1.1f * drive;
        foreach (var nozzle in _nozzles) nozzle.Scale = new Vector3(1f, stretch, 1f);
        for (int i = 0; i < _plumes.Count; i++)
        {
            float length = 0.25f + 1.9f * drive;
            float width = 0.5f + 0.2f * drive;
            // Always lit: the throttle floor is half speed, so an idling ship is still under power
            // and should look like it rather than like a dead one.
            _plumes[i].Scale = new Vector3(width, length, width);
            _plumes[i].Position = new Vector3(Side(i) * 0.28f, -0.01f, 1.14f + 0.35f * length);
        }
        // Straddles the glow's HDR threshold of 1.3, so the engines are lit at rest and bloom once
        // there is real speed on. Any hotter and the two nozzles bloom into one white mass that
        // swallows the back of the ship. This tracks the threshold in main.tscn: it was tuned to 1.0
        // and left behind when that moved, which quietly put the thrusters out altogether.
        _nozzleMaterial.EmissionEnergyMultiplier = 0.9f + 2.1f * drive;

        // Unstoppable burns the hull hot; otherwise it sits at the theme's glow.
        if (ramLeft > 0f)
        {
            float flicker = 0.75f + 0.25f * Mathf.Sin(_time * 40f);
            _hullMaterial.EmissionEnabled = true;
            _hullMaterial.Emission = _shipColor.Lerp(new Color(1f, 0.35f, 0.12f), 0.8f);
            _hullMaterial.EmissionEnergyMultiplier = 3f * flicker;
        }
        else
        {
            _hullMaterial.EmissionEnabled = true;
            _hullMaterial.Emission = _shipColor;
            _hullMaterial.EmissionEnergyMultiplier = HullGlow;
        }

        float flash = _flashLeft / FlashSeconds;
        _flash.Visible = flash > 0f;
        if (_flash.Visible) _flash.Scale = Vector3.One * (0.35f + 0.9f * flash);
    }

    private float HullGlow => 0.1f + 0.3f * _glow;

    // Right-hand parts are built first, so even indices are the +X side.
    private static float Side(int index) => index % 2 == 0 ? 1f : -1f;

    private void Build()
    {
        // Low metallic on purpose: the only light is a headlight on the camera and there is no sky
        // to reflect, so a metallic hull goes black wherever a face sits edge-on to it.
        _hullMaterial = new StandardMaterial3D { AlbedoColor = Colors.White, Metallic = 0.2f, Roughness = 0.38f };
        _darkMaterial = new StandardMaterial3D { AlbedoColor = Colors.Gray, Metallic = 0.25f, Roughness = 0.55f };
        // Above the 1.3 HDR threshold, or these read as flat paint: the accents are the few bright
        // marks meant to carry the silhouette from six units back.
        _accentMaterial = Glowing(1.7f);
        _nozzleMaterial = Glowing(1.7f);
        // Only the far side of the shell is drawn, so it reads as a bubble the ship sits inside
        // rather than a blob painted over it.
        _shellMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0.1f),
            EmissionEnabled = true,
            Emission = Colors.White,
            // Replaced every frame while the shell is up; this is just the value it starts at.
            EmissionEnergyMultiplier = 1.1f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        // Fuselage: a wedge, tipped with a four-sided nose so it stays faceted.
        Part(new PrismMesh { Size = new Vector3(0.58f, 0.4f, 1.8f) }, _hullMaterial, new Vector3(0f, 0f, 0.1f), Vector3.Zero);
        Part(new CylinderMesh { TopRadius = 0f, BottomRadius = NoseRadius, Height = NoseBaseZ - NoseTipZ, RadialSegments = 4 },
            _hullMaterial, new Vector3(0f, NoseY, (NoseBaseZ + NoseTipZ) * 0.5f), new Vector3(-90f, 0f, 0f));

        // The cockpit is part of the nose, not something carried on it. The nose is a four-sided
        // spike with a sharp ridge on top, and nothing round sits in that: a dome on the spine read
        // as a ball on the ship's back, and a teardrop pushed through the ridge read as a pod lying
        // on it. So the glass is faceted like the hull - a dark pane set flush into each of the
        // nose's two upper faces, meeting at the ridge, with a lit sill round it - and behind it a
        // low dark fairing tapers back along the spine, which is all of it that shows from behind.
        var tip = new Vector3(0f, NoseY, NoseTipZ);
        var crown = new Vector3(0f, NoseY + NoseRadius, NoseBaseZ);
        for (int i = 0; i < 2; i++)
        {
            var shoulder = new Vector3(Side(i) * NoseRadius, NoseY, NoseBaseZ);
            // A point on this face: how far from the base toward the tip, and how far from the
            // ridge down toward the shoulder.
            Vector3 On(float along, float down) => crown.Lerp(tip, along).Lerp(shoulder.Lerp(tip, along), down);

            var facing = (shoulder - crown).Cross(tip - crown).Normalized();
            if (facing.Y < 0f) facing = -facing;
            AddChild(new MeshInstance3D
            {
                Mesh = Pane(new[] { On(0.02f, 0f), On(0.06f, 0.62f), On(0.42f, 0.56f), On(0.72f, 0f) }, facing, 0.004f),
                MaterialOverride = _accentMaterial,
            });
            AddChild(new MeshInstance3D
            {
                Mesh = Pane(new[] { On(0.05f, 0f), On(0.09f, 0.52f), On(0.41f, 0.46f), On(0.66f, 0f) }, facing, 0.008f),
                MaterialOverride = _darkMaterial,
            });
        }
        Part(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.105f, Height = 0.7f, RadialSegments = 4 },
            _darkMaterial, new Vector3(0f, 0.2f, NoseBaseZ + 0.35f), new Vector3(90f, 0f, 0f));

        // A strip down the spine, and two along the nose. Emissive because the ship is lit by one
        // headlight from the camera and nothing else - a panel line cut into the hull would be
        // invisible wherever the face it sits on turns away.
        Part(new BoxMesh { Size = new Vector3(0.04f, 0.02f, 0.95f) }, _accentMaterial,
            new Vector3(0f, 0.2f, 0.42f), Vector3.Zero);
        for (int i = 0; i < 2; i++)
        {
            Part(new BoxMesh { Size = new Vector3(0.02f, 0.02f, 0.42f) }, _accentMaterial,
                new Vector3(Side(i) * 0.1f, 0.02f, -1.0f), new Vector3(0f, Side(i) * 4f, 0f));
        }

        // Intakes either side of the wedge, with a lit lip so they read as openings.
        for (int i = 0; i < 2; i++)
        {
            float side = Side(i);
            Part(new BoxMesh { Size = new Vector3(0.1f, 0.13f, 0.38f) }, _darkMaterial,
                new Vector3(side * 0.29f, -0.03f, -0.12f), Vector3.Zero);
            Part(new BoxMesh { Size = new Vector3(0.115f, 0.02f, 0.03f) }, _accentMaterial,
                new Vector3(side * 0.29f, -0.03f, -0.31f), Vector3.Zero);
        }

        // A shallow ventral fin. Pure silhouette: from the side and from below it is the difference
        // between a shape and a slab.
        Part(new BoxMesh { Size = new Vector3(0.05f, 0.22f, 0.34f) }, _hullMaterial,
            new Vector3(0f, -0.2f, 0.62f), Vector3.Zero);

        // Swept wings: a cropped delta, long at the root and short at the tip, with the leading
        // edge raked hard back and the trailing edge only a little. A box turned on its corner was
        // standing in for this, and from behind it read as a plank; a planform cut to shape is a
        // wing. Lit along the leading edge and across the tip, which is the outline of the ship
        // from the chase camera, and with the aileron let into the back edge.
        for (int i = 0; i < 2; i++)
        {
            float side = Side(i);
            var wing = new Node3D
            {
                Position = new Vector3(side * WingRoot, -0.02f, 0f),
                RotationDegrees = new Vector3(0f, 0f, side * 10f),
            };
            AddChild(wing);

            // Outboard is +X on the right wing and -X on the left, so every X here is times the side.
            Vector2 At(float x, float z) => new(side * x, z);
            float TrailingEdge(float x) => RootTrail + (TipTrail - RootTrail) * x / Span;

            // Round the outline from the root's leading edge, with a notch cut in the back for the
            // aileron to sit in: it is part of the wing's shape, not something hung behind it.
            var outline = new[]
            {
                At(0f, RootLead), At(Span, TipLead), At(Span, TipTrail),
                At(AileronTo, TrailingEdge(AileronTo)), At(AileronTo, TrailingEdge(AileronTo) - AileronChord),
                At(AileronFrom, TrailingEdge(AileronFrom) - AileronChord), At(AileronFrom, TrailingEdge(AileronFrom)),
                At(0f, RootTrail),
            };
            wing.AddChild(new MeshInstance3D { Mesh = Planform(outline, 0.07f), MaterialOverride = _hullMaterial });

            // The lit leading edge, laid along the rake, and a lit tip.
            var lead = At(Span, TipLead) - At(0f, RootLead);
            Part(new BoxMesh { Size = new Vector3(lead.Length() - 0.04f, 0.045f, 0.07f) }, _accentMaterial,
                new Vector3(side * Span * 0.5f, 0.02f, (RootLead + TipLead) * 0.5f + 0.03f),
                new Vector3(0f, Mathf.RadToDeg(-Mathf.Atan2(lead.Y, lead.X)), 0f), wing);
            Part(new BoxMesh { Size = new Vector3(0.035f, 0.08f, TipTrail - TipLead) }, _accentMaterial,
                new Vector3(side * Span, 0f, (TipLead + TipTrail) * 0.5f), Vector3.Zero, wing);

            // The aileron: a strip along the back edge of the wing, hinged along its long front
            // edge so the lit trailing edge lifts and drops. The mount lines it up with the
            // trailing edge, which is not square to the ship, and the hinge inside it only ever
            // turns about its own length.
            var hingeFrom = At(AileronFrom, TrailingEdge(AileronFrom) - AileronChord);
            var hingeTo = At(AileronTo, TrailingEdge(AileronTo) - AileronChord);
            var middle = (hingeFrom + hingeTo) * 0.5f;
            float length = (hingeTo - hingeFrom).Length() - 0.02f;
            float rake = Mathf.Atan2(TipTrail - RootTrail, Span);
            var mount = new Node3D
            {
                Position = new Vector3(middle.X, 0f, middle.Y),
                RotationDegrees = new Vector3(0f, -side * Mathf.RadToDeg(rake), 0f),
            };
            wing.AddChild(mount);
            var hinge = new Node3D();
            mount.AddChild(hinge);
            Part(new BoxMesh { Size = new Vector3(length, 0.06f, AileronChord) }, _hullMaterial,
                new Vector3(0f, 0f, AileronChord * 0.5f), Vector3.Zero, hinge);
            Part(new BoxMesh { Size = new Vector3(length, 0.07f, 0.04f) }, _accentMaterial,
                new Vector3(0f, 0f, AileronChord - 0.02f), Vector3.Zero, hinge);
            Part(new BoxMesh { Size = new Vector3(length, 0.075f, 0.015f) }, _darkMaterial,
                Vector3.Zero, Vector3.Zero, hinge);
            _ailerons.Add(hinge);
        }

        // Canted tail fins.
        for (int i = 0; i < 2; i++)
        {
            _fins.Add(Part(new BoxMesh { Size = new Vector3(0.06f, 0.42f, 0.38f) }, _hullMaterial,
                new Vector3(Side(i) * 0.2f, 0.2f, 0.72f), new Vector3(0f, 0f, Side(i) * -22f)));
        }

        // Twin engines: a dark nacelle, a bright nozzle, and the plume it throws.
        for (int i = 0; i < 2; i++)
        {
            float side = Side(i);
            Part(new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.14f, Height = 0.8f, RadialSegments = 8 },
                _darkMaterial, new Vector3(side * 0.28f, -0.01f, 0.48f), new Vector3(-90f, 0f, 0f));

            // Two bands round each nacelle. The engines are what the victory camera is pointed at,
            // and a bare cylinder at that range is the one part of the ship with nothing on it.
            for (int band = 0; band < 2; band++)
            {
                Part(new CylinderMesh { TopRadius = 0.155f, BottomRadius = 0.155f, Height = 0.025f, RadialSegments = 8 },
                    _accentMaterial, new Vector3(side * 0.28f, -0.01f, 0.26f + band * 0.3f), new Vector3(-90f, 0f, 0f));
            }

            _nozzles.Add(Part(new CylinderMesh { TopRadius = 0.075f, BottomRadius = 0.135f, Height = 0.28f, RadialSegments = 8 },
                _nozzleMaterial, new Vector3(side * 0.28f, -0.01f, 1.0f), new Vector3(-90f, 0f, 0f)));

            _plumes.Add(Part(new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.014f, Height = 0.7f, RadialSegments = 8 },
                _nozzleMaterial, new Vector3(side * 0.28f, -0.01f, 1.45f), new Vector3(-90f, 0f, 0f)));
        }

        _flash = Part(new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 6, Rings = 3 },
            _accentMaterial, new Vector3(0f, 0.02f, -1.55f), Vector3.Zero);
        _flash.Visible = false;

        _shell = Part(new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 14, Rings = 7 },
            _shellMaterial, new Vector3(0f, 0f, 0.1f), Vector3.Zero);
        _shell.Scale = new Vector3(1.15f, 0.62f, 1.45f);
        _shell.Visible = false;
    }

    /// <summary>
    /// A flat slab cut to an outline: the outline in X and Z, extruded <paramref name="thickness"/>
    /// in Y about zero. Flat-shaded, a normal per face, like the primitives the rest of the ship
    /// is made of. The outline may be concave and may run either way round, which is what lets one
    /// wing be the other's mirror image.
    /// </summary>
    private static ArrayMesh Planform(Vector2[] outline, float thickness)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float h = thickness * 0.5f;

        // Godot's front faces wind clockwise. Rather than reason about which way round each face
        // happens to come out, every triangle is checked against the way it is meant to face.
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            if ((b - a).Cross(c - a).Dot(normal) > 0f) (b, c) = (c, b);
            foreach (var v in new[] { a, b, c })
            {
                st.SetNormal(normal);
                st.AddVertex(v);
            }
        }

        var triangles = Geometry2D.TriangulatePolygon(outline);
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            var (a, b, c) = (outline[triangles[i]], outline[triangles[i + 1]], outline[triangles[i + 2]]);
            Face(new Vector3(a.X, h, a.Y), new Vector3(b.X, h, b.Y), new Vector3(c.X, h, c.Y), Vector3.Up);
            Face(new Vector3(a.X, -h, a.Y), new Vector3(b.X, -h, b.Y), new Vector3(c.X, -h, c.Y), Vector3.Down);
        }

        // The edge, facing out. Which side is out depends on which way round the outline runs.
        float area = 0f;
        for (int i = 0; i < outline.Length; i++)
        {
            var (p, q) = (outline[i], outline[(i + 1) % outline.Length]);
            area += p.X * q.Y - q.X * p.Y;
        }
        float turn = Mathf.Sign(area);
        for (int i = 0; i < outline.Length; i++)
        {
            var (p, q) = (outline[i], outline[(i + 1) % outline.Length]);
            var out2 = new Vector2(q.Y - p.Y, -(q.X - p.X)).Normalized() * turn;
            var normal = new Vector3(out2.X, 0f, out2.Y);
            var (pt, pb) = (new Vector3(p.X, h, p.Y), new Vector3(p.X, -h, p.Y));
            var (qt, qb) = (new Vector3(q.X, h, q.Y), new Vector3(q.X, -h, q.Y));
            Face(pt, qt, qb, normal);
            Face(pt, qb, pb, normal);
        }
        return st.Commit();
    }

    /// <summary>
    /// A flat four-sided panel facing <paramref name="facing"/>, lifted <paramref name="lift"/> off
    /// the surface it lies on so the two do not fight over the same pixels.
    /// </summary>
    private static ArrayMesh Pane(Vector3[] corners, Vector3 facing, float lift)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 1; i + 1 < corners.Length; i++)
        {
            var (a, b, c) = (corners[0], corners[i], corners[i + 1]);
            // Godot's front faces wind clockwise as seen from the side they face.
            if ((b - a).Cross(c - a).Dot(facing) > 0f) (b, c) = (c, b);
            foreach (var v in new[] { a, b, c })
            {
                st.SetNormal(facing);
                st.AddVertex(v + facing * lift);
            }
        }
        return st.Commit();
    }

    private MeshInstance3D Part(Mesh mesh, Material material, Vector3 position, Vector3 rotationDegrees,
        Node3D? parent = null)
    {
        var node = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Position = position,
            RotationDegrees = rotationDegrees,
        };
        (parent ?? this).AddChild(node);
        return node;
    }

    private static StandardMaterial3D Glowing(float energy) => new()
    {
        AlbedoColor = Colors.White,
        EmissionEnabled = true,
        Emission = Colors.White,
        EmissionEnergyMultiplier = energy,
    };
}
