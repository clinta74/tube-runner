using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>
/// The player's interceptor: a wedge fuselage with a faceted nose, swept wings, canted tail fins and
/// twin engines, all built from primitives. From six units back only the silhouette and a few bright
/// accents read, so most of the detail is in how the parts move - nozzles stretch with speed,
/// ailerons deflect into a bank, the fins flare as you ease off, and the hull runs hot while
/// unstoppable. Forward is -Z, matching Godot's look_at.
/// </summary>
public partial class ShipView : Node3D
{
    private const float FlashSeconds = 0.07f;

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

    /// <param name="bank">Smoothed steer in [-1, 1], the same value the whole ship rolls by.</param>
    /// <param name="throttle">Ship throttle; below 1 means easing off.</param>
    /// <param name="intensity">Speed effect in [0, 1], driving the engines.</param>
    public void UpdateState(float dt, float bank, float throttle, float intensity, float ramLeft, float recoveryLeft)
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
            _shellMaterial.EmissionEnergyMultiplier = 0.25f + 0.55f * pulse;
        }

        // Ailerons deflect into the bank, opposite sides opposite ways.
        for (int i = 0; i < _ailerons.Count; i++)
        {
            _ailerons[i].RotationDegrees = new Vector3(Side(i) * 24f * bank, 0f, 0f);
        }

        // Easing off the throttle flares the fins out like airbrakes.
        float brake = Mathf.Clamp(1f - throttle, 0f, 1f);
        for (int i = 0; i < _fins.Count; i++)
        {
            _fins[i].RotationDegrees = new Vector3(0f, 0f, Side(i) * -(22f + 30f * brake));
        }

        // Engines stretch and brighten with speed; the plume grows backwards from the nozzle.
        float stretch = 1f + 1.1f * intensity;
        foreach (var nozzle in _nozzles) nozzle.Scale = new Vector3(1f, stretch, 1f);
        for (int i = 0; i < _plumes.Count; i++)
        {
            float length = 0.25f + 1.9f * intensity;
            float width = 0.5f + 0.2f * intensity;
            _plumes[i].Visible = intensity > 0.02f;
            _plumes[i].Scale = new Vector3(width, length, width);
            _plumes[i].Position = new Vector3(Side(i) * 0.28f, -0.01f, 1.14f + 0.35f * length);
        }
        // Kept near the glow's HDR threshold of 1.0: any hotter and the two nozzles bloom into one
        // white mass that swallows the back of the ship.
        _nozzleMaterial.EmissionEnergyMultiplier = 0.5f + 1.8f * intensity;

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
        _accentMaterial = Glowing(1.2f);
        _nozzleMaterial = Glowing(1.2f);
        // Only the far side of the shell is drawn, so it reads as a bubble the ship sits inside
        // rather than a blob painted over it.
        _shellMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0.1f),
            EmissionEnabled = true,
            Emission = Colors.White,
            EmissionEnergyMultiplier = 0.5f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        // Fuselage: a wedge, tipped with a four-sided nose so it stays faceted.
        Part(new PrismMesh { Size = new Vector3(0.58f, 0.4f, 1.8f) }, _hullMaterial, new Vector3(0f, 0f, 0.1f), Vector3.Zero);
        Part(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.27f, Height = 0.7f, RadialSegments = 4 },
            _hullMaterial, new Vector3(0f, 0.02f, -1.15f), new Vector3(-90f, 0f, 0f));

        // Swept wings, each with a glowing leading edge and an aileron on the trailing edge.
        for (int i = 0; i < 2; i++)
        {
            float side = Side(i);
            // Hard sweep and a deep chord: from behind the pair reads as one A-frame delta.
            var wing = Part(new BoxMesh { Size = new Vector3(1.0f, 0.08f, 0.85f) }, _hullMaterial,
                new Vector3(side * 0.62f, -0.02f, 0.3f), new Vector3(0f, side * -38f, side * 12f));

            Part(new BoxMesh { Size = new Vector3(1.0f, 0.04f, 0.12f) }, _accentMaterial,
                new Vector3(0f, 0.025f, -0.37f), Vector3.Zero, wing);

            _ailerons.Add(Part(new BoxMesh { Size = new Vector3(0.42f, 0.06f, 0.18f) }, _darkMaterial,
                new Vector3(side * 0.25f, 0f, 0.5f), Vector3.Zero, wing));
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
