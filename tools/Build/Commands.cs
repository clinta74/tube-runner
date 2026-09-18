using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace TubeRunner.Build;

internal static class Commands
{
    private const string Usage = """
        Tube Runner build commands. From the repo root: tube <command> [options]

          play        Build the C# code and run the game.
                        --level <file>       level in game/levels, e.g. level_05.json or bench/wire.json
                        --start <units>      start partway through the level (a practice run)
                        --editor             open the Godot editor instead
                        --record             record to recordings/ as an AVI
                        --record-fps <n>     frame rate to record at (default 60)
                        --summary            open straight onto the end-of-run results screen
                        --menu               open straight onto the Escape menu
                        --settings           open straight onto the settings page
                        --shot <file>        save one frame to that file, then quit

          export      Export the standalone Windows build to builds/windows, and zip it.
                        --version <x.y.z>    stamp the version, so the game checks for newer releases
                        --debug              debug export, which opens a console with errors
                        --signed             fail rather than leave the exe unsigned

          installer   Build builds/TubeRunner-<version>.msi from the export. Run export first.
                        --version <x.y.z>    defaults to the latest git tag
                        --signed             fail rather than leave the installer unsigned

          icon        Rebuild game/icon.ico from game/icon.svg.

        Set GODOT to use a specific Godot .NET executable. Signing is off unless a certificate is
        configured; Signing.cs lists the TUBE_SIGN_* variables that name one.
        """;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "/?")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        var options = args[1..];
        switch (args[0])
        {
            case "play":
                Play(Options.Parse(options, flags: ["editor", "record", "summary", "menu", "settings"], values: ["level", "start", "record-fps", "shot"]));
                break;
            case "export":
                Export(Options.Parse(options, flags: ["debug", "signed"], values: ["version"]));
                break;
            case "installer":
                Installer(Options.Parse(options, flags: ["signed"], values: ["version"]));
                break;
            case "icon":
                Options.Parse(options, flags: [], values: []);
                Icon();
                break;
            default:
                throw new BuildFailure($"Unknown command '{args[0]}'.\n\n{Usage}");
        }
        return 0;
    }

    private static void Play(Options options)
    {
        Proc.Run("dotnet", ["build", Repo.Path("game", "TubeRunner.sln"), "-v", "q", "-nologo"]);

        var godot = Godot.Find(console: false);
        var engine = new List<string> { "--path", Repo.Path("game") };
        if (options.Flag("editor")) engine.Add("--editor");

        var level = options.Value("level")?.Replace('\\', '/');
        if (options.Flag("record"))
        {
            // Godot renders every frame however long it takes, so the video is perfectly smooth - but play
            // no longer runs in real time while it records. Good for a flythrough; use a screen recorder
            // for an actual run. The file is uncompressed and runs to gigabytes a minute. Convert it:
            //   ffmpeg -i recordings/run-....avi -c:v libx264 -crf 20 -pix_fmt yuv420p run.mp4
            var folder = Repo.Path("recordings");
            Directory.CreateDirectory(folder);
            var name = level is null ? "run" : Path.GetFileNameWithoutExtension(level);
            var file = Path.Combine(folder, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.avi");
            var fps = options.Number("record-fps") ?? 60;
            // Engine flags, so they go before the "--" that separates the game's own arguments.
            engine.AddRange(["--write-movie", file, "--fixed-fps", ((int)fps).ToString(CultureInfo.InvariantCulture)]);
            Console.WriteLine($"Recording to {file}");
            Console.WriteLine("Play will not run at real time while recording; the video itself will be correct.");
        }

        var game = new List<string>();
        if (level is not null) game.Add($"--level=res://levels/{level}");
        if (options.Number("start") is double start) game.Add("--start=" + start.ToString(CultureInfo.InvariantCulture));
        if (options.Flag("summary")) game.Add("--summary");
        if (options.Flag("menu")) game.Add("--menu");
        if (options.Flag("settings")) game.Add("--settings");
        if (options.Value("shot") is string shot) game.Add("--shot=" + Path.GetFullPath(shot));
        if (game.Count > 0) engine.AddRange(["--", .. game]);

        Proc.Run(godot, engine, wait: false);
    }

    private static void Export(Options options)
    {
        var stamp = options.Value("version") is string version ? Versions.Require(version) : null;
        var signing = Signer(options);

        var godot = Godot.Find(console: true);
        var engineVersion = Godot.Version(godot);
        Godot.RequireTemplates(engineVersion);

        var output = Repo.Path("builds", "windows");
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        var exe = Path.Combine(output, "TubeRunner.exe");

        // The version reaches the game's assembly through the GameVersion MSBuild property, which the
        // export's own build reads from the environment. It is set for Godot alone, so nothing tracked in
        // git changes, and without one the build reads 0.0.0 - a development build that never checks.
        Proc.Run(godot,
            ["--headless", "--path", Repo.Path("game"), options.Flag("debug") ? "--export-debug" : "--export-release", "Windows Desktop", exe],
            environment: new Dictionary<string, string?> { ["GameVersion"] = stamp });
        if (!File.Exists(exe)) throw new BuildFailure("Godot finished without writing the exe.");

        // Signed here rather than later for two reasons: `tube installer` packages this exe, so signing
        // it afterwards would leave the copy inside the MSI unsigned, and the zip below should carry the
        // signed exe as well. Godot keeps the embedded PCK in its own PE section, so a signature - which
        // goes after the sections - leaves the game's data alone.
        Sign(signing, exe, exe);

        var zip = Repo.Path("builds", "TubeRunner-windows.zip");
        File.Delete(zip);
        ZipFile.CreateFromDirectory(output, zip, CompressionLevel.Optimal, includeBaseDirectory: false);

        Console.WriteLine(stamp is null ? "Version  none (a development build, which never checks for updates)" : $"Version  {stamp}");
        Console.WriteLine($"Exported {exe}");
        Console.WriteLine($"Zipped   {zip}");
    }

    private static void Installer(Options options)
    {
        var signing = Signer(options);
        var build = Repo.Path("builds", "windows");
        if (!File.Exists(Path.Combine(build, "TubeRunner.exe")))
        {
            throw new BuildFailure("There's no export to package. Run `tube export` first.");
        }

        // CI passes the tag it is releasing; locally the newest tag stands in.
        var version = Versions.Require(options.Value("version") ?? Versions.LatestTag());

        Proc.Run("dotnet", ["build", Repo.Path("installer", "TubeRunner.wixproj"), "-c", "Release", "-nologo", "-v", "q",
            $"-p:GameVersion={version}", $"-p:GameBuild={build}"]);

        var msi = Repo.Path("builds", $"TubeRunner-{version}.msi");
        if (!File.Exists(msi)) throw new BuildFailure($"The installer build finished, but {msi} isn't there.");

        // Signing an MSI covers the cabinet embedded in it too, so this has to come after WiX has built
        // it. The exe inside was signed during export, which is what stops the warning when the game is
        // launched rather than when it is installed.
        //
        // The WiX SDK hard-links the MSI from installer/obj into builds, so the two names are one file:
        // signing in place would sign MSBuild's cached copy as well, and MSBuild would then hand that
        // already-signed file back as an up-to-date output on the next build, carrying a stale signature
        // into a later release. Giving builds its own copy first leaves obj holding what WiX produced.
        if (signing is not null) Unlink(msi);
        Sign(signing, msi, msi);
        Console.WriteLine($"Installer {msi}");
    }

    /// <summary>
    /// The signer to use, or null to leave the output unsigned. Resolved before a command does any work,
    /// so a missing certificate or signtool is reported straight away rather than after a long export.
    /// --signed makes an unsigned build an error, which is how a release refuses to publish something
    /// Windows will warn about. See Signing.cs for the variables that configure a certificate.
    /// </summary>
    private static Signing? Signer(Options options)
    {
        var signing = Signing.Configured();
        if (signing is null && options.Flag("signed"))
        {
            throw new BuildFailure(
                "--signed was given, but no signing certificate is configured. Set one of "
                + "TUBE_SIGN_THUMBPRINT, TUBE_SIGN_PFX or TUBE_SIGN_DLIB; see Signing.cs.");
        }
        return signing;
    }

    /// <summary>
    /// Replaces a file with an independent copy of itself, so that writing to it no longer writes to any
    /// hard link sharing its contents.
    /// </summary>
    private static void Unlink(string path)
    {
        var separate = path + ".tmp";
        File.Copy(path, separate, overwrite: true);
        File.Delete(path);
        File.Move(separate, path);
    }

    private static void Sign(Signing? signing, string file, string what)
    {
        if (signing is null)
        {
            Console.WriteLine($"Unsigned {what} (no certificate configured, so Windows will warn about it)");
            return;
        }
        signing.Sign(file, what);
        signing.Verify(file);
    }

    // Godot renders the SVG at each icon size (game/tools/make_icon.gd), then the PNGs are packed into an
    // .ico here. An .ico is a 6-byte header, a 16-byte entry per image, then the images themselves;
    // since Windows Vista those can simply be PNGs, so no image library is needed.
    private static void Icon()
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        // Scratch PNGs go under builds/, which git ignores.
        var work = Repo.Path("builds", "icon");
        Directory.CreateDirectory(work);

        var godot = Godot.Find(console: true);
        Proc.Run(godot, ["--headless", "--path", Repo.Path("game"), "--script", "res://tools/make_icon.gd",
            "--", Repo.Path("game", "icon.svg"), work]);

        var images = sizes.Select(size =>
        {
            var png = Path.Combine(work, $"icon_{size}.png");
            return File.Exists(png) ? File.ReadAllBytes(png) : throw new BuildFailure($"Godot didn't write {png}.");
        }).ToArray();

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);             // reserved
            writer.Write((ushort)1);             // type: icon
            writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] % 256));  // width; 256 is stored as 0
                writer.Write((byte)(sizes[i] % 256));  // height
                writer.Write((byte)0);                  // palette size
                writer.Write((byte)0);                  // reserved
                writer.Write((ushort)1);                // colour planes
                writer.Write((ushort)32);               // bits per pixel
                writer.Write((uint)images[i].Length);
                writer.Write((uint)offset);
                offset += images[i].Length;
            }
            foreach (var image in images) writer.Write(image);
        }

        var path = Repo.Path("game", "icon.ico");
        File.WriteAllBytes(path, ico.ToArray());
        Console.WriteLine($"Wrote {path} ({sizes.Length} sizes, {ico.Length} bytes)");
    }
}
