using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>
/// The player's ship: a flat, chined, blended body with strakes running from the cockpit to the
/// wings, cropped delta wings, canted tail fins and the engines buried in the body. It is a model,
/// `models/ship.glb`, built by `tools/blender/build_ship.py` (variant B); this class gives it the
/// level's colours and moves its parts. From six units back only the silhouette and a few bright
/// accents read, so most of the detail is in how the parts move - nozzles stretch with speed, the
/// ailerons and rudders work the way an aircraft's do, the fins flare as you ease off, and the
/// hull runs hot while unstoppable. Forward is -Z, matching Godot's look_at.
///
/// The model's materials are named Hull, Dark, Accent, Nozzle and Glass, and each is replaced by
/// one of this class's own, which is what lets every level recolour the ship. The parts that move
/// are their own nodes, Aileron_R and _L hinged on their front edge, Fin_R and _L at their root,
/// Nozzle_R and _L at their middle, so they are driven exactly as the primitives they replace were.
/// The plumes, the muzzle flash and the recovery shell are effects rather than parts of the ship,
/// and stay built here.
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

    // Where the model lives, and the places on it the effects are hung from: each engine's centre
    // across and up, and the nose tip the muzzle flash pops at. They follow the model's numbers.
    private const string ModelPath = "res://models/ship.glb";
    private const float EngineX = 0.25f;
    private const float EngineY = -0.03f;
    private const float NoseTipZ = -1.62f;

    // How far the fins flare out beyond the model's own cant as the throttle comes off.
    private const float FinFlare = 30f;

    private readonly List<Node3D> _ailerons = new();
    private readonly List<Node3D> _fins = new();
    private readonly List<Node3D> _nozzles = new();
    private readonly List<Node3D> _plumes = new();
    // Each fin's cant in the model, which the flare is added to.
    private readonly List<float> _finCant = new();

    private StandardMaterial3D _hullMaterial = null!;
    private StandardMaterial3D _darkMaterial = null!;
    private StandardMaterial3D _accentMaterial = null!;
    private StandardMaterial3D _nozzleMaterial = null!;
    private StandardMaterial3D _shellMaterial = null!;
    private StandardMaterial3D _glassMaterial = null!;
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
        _glassMaterial.AlbedoColor = _shipColor.Darkened(0.85f);

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
            _fins[i].RotationDegrees = new Vector3(0f, RudderDegrees * _rudder, _finCant[i] - Side(i) * FinFlare * brake);
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
        // The model's nozzles lie along Z, the way the ship points.
        foreach (var nozzle in _nozzles) nozzle.Scale = new Vector3(1f, 1f, stretch);
        for (int i = 0; i < _plumes.Count; i++)
        {
            float length = 0.25f + 1.9f * drive;
            float width = 0.5f + 0.2f * drive;
            // Always lit: the throttle floor is a third of the track's speed, so an idling ship is
            // still under power and should look like it rather than like a dead one.
            _plumes[i].Scale = new Vector3(width, length, width);
            _plumes[i].Position = new Vector3(Side(i) * EngineX, EngineY, 1.14f + 0.35f * length);
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
        // Low metallic on purpose: there is no sky to reflect, so a metallic hull goes black
        // wherever a face sits edge-on to the lights.
        _hullMaterial = new StandardMaterial3D { AlbedoColor = Colors.White, Metallic = 0.2f, Roughness = 0.38f };
        _darkMaterial = new StandardMaterial3D { AlbedoColor = Colors.Gray, Metallic = 0.25f, Roughness = 0.55f };
        // The cockpit: dark like the trim, but glossy, so the key light catches it as glass.
        _glassMaterial = new StandardMaterial3D { AlbedoColor = Colors.DimGray, Metallic = 0.5f, Roughness = 0.15f };
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

        var model = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        AddChild(model);
        var materials = new Dictionary<string, Material>
        {
            ["Hull"] = _hullMaterial,
            ["Dark"] = _darkMaterial,
            ["Accent"] = _accentMaterial,
            ["Nozzle"] = _nozzleMaterial,
            ["Glass"] = _glassMaterial,
        };
        Recolour(model, materials);

        // Right-hand parts first, so even indices are the +X side, as Side() expects.
        foreach (var label in new[] { "R", "L" })
        {
            _ailerons.Add(Find(model, $"Aileron_{label}"));
            var fin = Find(model, $"Fin_{label}");
            _fins.Add(fin);
            _finCant.Add(fin.RotationDegrees.Z);
            _nozzles.Add(Find(model, $"Nozzle_{label}"));
        }

        // The plume each engine throws, stretched and moved every frame by UpdateState.
        for (int i = 0; i < 2; i++)
        {
            _plumes.Add(Part(new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.014f, Height = 0.7f, RadialSegments = 8 },
                _nozzleMaterial, new Vector3(Side(i) * EngineX, EngineY, 1.45f), new Vector3(-90f, 0f, 0f)));
        }

        _flash = Part(new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 6, Rings = 3 },
            _accentMaterial, new Vector3(0f, 0f, NoseTipZ - 0.05f), Vector3.Zero);
        _flash.Visible = false;

        _shell = Part(new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 14, Rings = 7 },
            _shellMaterial, new Vector3(0f, 0f, 0.1f), Vector3.Zero);
        _shell.Scale = new Vector3(1.15f, 0.62f, 1.45f);
        _shell.Visible = false;
    }

    // Gives every surface of every mesh in the model the material its name asks for.
    private static void Recolour(Node node, Dictionary<string, Material> materials)
    {
        if (node is MeshInstance3D instance && instance.Mesh is { } mesh)
        {
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                string name = mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
                if (materials.TryGetValue(name, out var material)) instance.SetSurfaceOverrideMaterial(i, material);
                else GD.PushWarning($"Ship model: no material for '{name}' on {instance.Name}.");
            }
        }
        foreach (var child in node.GetChildren()) Recolour(child, materials);
    }

    // A named part of the model. Missing is a broken model, and says so rather than flying on
    // with a wing that no longer moves.
    private static Node3D Find(Node model, string name) =>
        model.FindChild(name, recursive: true, owned: false) as Node3D
        ?? throw new System.InvalidOperationException($"Ship model {ModelPath} has no part called {name}.");

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
