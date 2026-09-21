using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>
/// The player's interceptor: a wedge fuselage with a faceted nose, swept wings, canted tail fins and
/// twin engines, all built from primitives. From six units back only the silhouette and a few bright
/// accents read, so most of the detail is in how the parts move - nozzles stretch with speed,
/// the ailerons and rudders work the way an aircraft's do, the fins flare as you ease off, and the
/// hull runs hot while unstoppable. Forward is -Z, matching Godot's look_at.
/// </summary>
public partial class ShipView : Node3D
{
    private const float FlashSeconds = 0.07f;

    // How far a control surface swings at full deflection, and how fast it gets there. Quick, but
    // not instant: a key is either down or not, and a surface that snapped with it would flicker.
    private const float AileronDegrees = 30f;
    private const float RudderDegrees = 16f;
    private const float SurfaceRate = 16f;

    // An aileron answers two things. Most of it is how hard the ship is being asked to roll that it
    // is not yet doing - the kick as a turn starts, and the opposite kick as it is let go. The rest
    // is held for as long as the stick is, because steering round a tube is a roll that carries on.
    private const float AileronKick = 0.95f;
    private const float AileronHold = 0.5f;

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
        _hullMaterial.EmissionEnabled = true;
        _hullMaterial.Emission = _shipColor;
        _hullMaterial.EmissionEnergyMultiplier = Mathf.Max(_glow, 0.3f);

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

        // Ailerons: the wingtips, each hinged along its own leading edge, one up and one down. To
        // roll right the right one lifts, spoiling that wing, and the left one drops. They lead the ship rather
        // than follow it - full over as a turn begins, easing as the bank arrives, and thrown the
        // other way to stop it - which is what makes them look like they are doing the work.
        float wanted = Mathf.Clamp(AileronKick * (steer - bank) + AileronHold * steer + roll, -1f, 1f);
        float ease = 1f - Mathf.Exp(-SurfaceRate * dt);
        _aileron = Mathf.Lerp(_aileron, wanted, ease);
        _rudder = Mathf.Lerp(_rudder, Mathf.Clamp(steer, -1f, 1f), ease);
        for (int i = 0; i < _ailerons.Count; i++)
        {
            // About the hinge's X, a negative angle lifts the trailing edge.
            _ailerons[i].RotationDegrees = new Vector3(-Side(i) * AileronDegrees * _aileron, 0f, 0f);
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
            _hullMaterial.EmissionEnergyMultiplier = Mathf.Max(_glow, 0.3f);
        }

        float flash = _flashLeft / FlashSeconds;
        _flash.Visible = flash > 0f;
        if (_flash.Visible) _flash.Scale = Vector3.One * (0.35f + 0.9f * flash);
    }

    // Right-hand parts are built first, so even indices are the +X side.
    private static float Side(int index) => index % 2 == 0 ? 1f : -1f;

    private void Build()
    {
        // Low metallic on purpose: the only light is a headlight on the camera and there is no sky
        // to reflect, so a metallic hull goes black wherever a face sits edge-on to it.
        _hullMaterial = new StandardMaterial3D { AlbedoColor = Colors.White, Metallic = 0.2f, Roughness = 0.5f };
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
        Part(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.27f, Height = 0.7f, RadialSegments = 4 },
            _hullMaterial, new Vector3(0f, 0.02f, -1.15f), new Vector3(-90f, 0f, 0f));

        // A canopy, low and swept back. The ship is looked at from the front right on the victory
        // lap, so it wants something up front that reads as a cockpit rather than a blank wedge.
        var canopy = Part(new SphereMesh { Radius = 0.14f, Height = 0.28f, RadialSegments = 10, Rings = 5 },
            _darkMaterial, new Vector3(0f, 0.15f, -0.45f), Vector3.Zero);
        canopy.Scale = new Vector3(0.82f, 0.6f, 1.9f);
        // Its rim, which is what actually carries the shape: nothing here is lit well enough for a
        // dark dome on a pale hull to show on its own.
        Part(new BoxMesh { Size = new Vector3(0.26f, 0.015f, 0.52f) }, _accentMaterial,
            new Vector3(0f, 0.11f, -0.45f), Vector3.Zero);

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

        // Swept wings, each with a glowing leading edge and an all-moving tip for an aileron.
        for (int i = 0; i < 2; i++)
        {
            float side = Side(i);
            // Hard sweep and a deep chord: from behind the pair reads as one A-frame delta. The wing
            // is a frame with the panels hung in it, so the fixed part can stop short of the tip.
            // Its local +X runs outboard on both sides once the sweep is applied, so an offset times
            // the side lands on the right wing's tip and the left's alike.
            var wing = new Node3D
            {
                Position = new Vector3(side * 0.62f, -0.02f, 0.3f),
                RotationDegrees = new Vector3(0f, side * -38f, side * 12f),
            };
            AddChild(wing);

            Part(new BoxMesh { Size = new Vector3(0.74f, 0.08f, 0.85f) }, _hullMaterial,
                new Vector3(side * -0.13f, 0f, 0f), Vector3.Zero, wing);
            Part(new BoxMesh { Size = new Vector3(0.74f, 0.04f, 0.12f) }, _accentMaterial,
                new Vector3(side * -0.13f, 0.025f, -0.37f), Vector3.Zero, wing);

            // The aileron is the wingtip itself: the outer quarter of the span, hinged along its
            // own leading edge, where the winglets used to stand. Out at the tip is where a roll
            // surface has the most leverage, and it is also the widest point of the silhouette, so
            // from six units back a tip lifting or dropping is the easiest movement on the ship to
            // see. In the hull's colour, so level it is simply the end of the wing; the lit trailing
            // and outer edges are what trace it as it moves, and the dark line is the gap it moves in.
            var hinge = new Node3D { Position = new Vector3(side * 0.37f, 0f, -0.2f) };
            wing.AddChild(hinge);
            Part(new BoxMesh { Size = new Vector3(0.26f, 0.07f, 0.62f) }, _hullMaterial,
                new Vector3(0f, 0f, 0.31f), Vector3.Zero, hinge);
            Part(new BoxMesh { Size = new Vector3(0.26f, 0.08f, 0.05f) }, _accentMaterial,
                new Vector3(0f, 0f, 0.61f), Vector3.Zero, hinge);
            Part(new BoxMesh { Size = new Vector3(0.035f, 0.08f, 0.62f) }, _accentMaterial,
                new Vector3(side * 0.125f, 0f, 0.31f), Vector3.Zero, hinge);
            Part(new BoxMesh { Size = new Vector3(0.02f, 0.085f, 0.62f) }, _darkMaterial,
                new Vector3(side * -0.13f, 0f, 0.31f), Vector3.Zero, hinge);
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
