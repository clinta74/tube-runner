using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Prototype scene: a straight tube that stays put while the wall pattern scrolls past
/// (floating-origin style). The ship rides the wall at track position U.
/// </summary>
public partial class Main : Node3D
{
    // Multiple of the shader's ring spacing (8) and hue period (1 / 0.02 = 50),
    // so wrapping the scroll value is seamless.
    private const double ScrollWrap = 200.0;
    private const float RingStep = 2f;
    private const int RadialSegments = 48;

    [Export] public ShaderMaterial WallMaterial { get; set; } = null!;
    [Export] public float TubeRadius { get; set; } = 6f;
    [Export] public float TubeLength { get; set; } = 400f;
    [Export] public float ForwardSpeed { get; set; } = 80f;
    [Export] public float SteerRate { get; set; } = 0.6f;

    private ShipSim _sim = null!;
    private Node3D _ship = null!;
    private Camera3D _camera = null!;

    public override void _Ready()
    {
        _ship = GetNode<Node3D>("Ship");
        _camera = GetNode<Camera3D>("Camera3D");
        // U = 0.75 is the bottom of the tube.
        _sim = new ShipSim(new ShipSettings(ForwardSpeed, SteerRate), new TrackPosition(0, 0, 0.75f, 0));
        AddChild(BuildTube(new CircleProfile(TubeRadius)));
    }

    public override void _Process(double delta)
    {
        float steer = Input.GetAxis("ui_left", "ui_right");
        if (Input.IsKeyPressed(Key.A)) steer -= 1f;
        if (Input.IsKeyPressed(Key.D)) steer += 1f;

        _sim.Step((float)delta, steer);
        var pos = _sim.Position;

        WallMaterial.SetShaderParameter("scroll", (float)(pos.S % ScrollWrap));

        // "Up" for the ship points from the wall toward the tube center.
        var wall = CircleProfile.PointAt(pos.U, 1f);
        var up = new Vector3(-wall.X, -wall.Y, 0f);

        var shipPos = ToWorld(CircleProfile.PointAt(pos.U, TubeRadius - 0.6f), 0f);
        _ship.LookAtFromPosition(shipPos, shipPos + Vector3.Forward, up);

        var camPos = ToWorld(CircleProfile.PointAt(pos.U, TubeRadius - 2.2f), 5f);
        _camera.LookAtFromPosition(camPos, shipPos + Vector3.Forward * 10f, up);
    }

    private static Vector3 ToWorld(System.Numerics.Vector2 p, float z) => new(p.X, p.Y, z);

    private MeshInstance3D BuildTube(CircleProfile profile)
    {
        // Start a little behind the camera so the tube wraps around it.
        const float behind = 20f;
        int rings = (int)(TubeLength / RingStep) + 1;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int r = 0; r < rings; r++)
        {
            float along = r * RingStep;
            for (int a = 0; a <= RadialSegments; a++)
            {
                float u = a / (float)RadialSegments;
                st.SetUV(new Vector2(u, along));
                st.AddVertex(ToWorld(profile.PointAt(u), behind - along));
            }
        }

        int stride = RadialSegments + 1;
        for (int r = 0; r < rings - 1; r++)
        {
            for (int a = 0; a < RadialSegments; a++)
            {
                int i = r * stride + a;
                st.AddIndex(i);
                st.AddIndex(i + stride);
                st.AddIndex(i + 1);
                st.AddIndex(i + 1);
                st.AddIndex(i + stride);
                st.AddIndex(i + stride + 1);
            }
        }

        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = WallMaterial };
    }
}
