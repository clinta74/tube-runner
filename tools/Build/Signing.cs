using System.Runtime.InteropServices;

namespace TubeRunner.Build;

/// <summary>
/// Authenticode signing: what makes Windows name a publisher instead of warning about an unknown one,
/// and what stops Defender and SmartScreen offering to delete the download.
///
/// The publisher players see is the certificate's subject - the name the certificate authority
/// validated - not anything written in the installer. Package.wxs's Manufacturer only names the
/// publisher in the installed-apps list, and should be set to match.
///
/// The certificate is configured through the environment rather than command-line options, because in
/// CI these are repository secrets. Exactly one of the three names the key:
///
///   TUBE_SIGN_THUMBPRINT  SHA1 thumbprint of a certificate in the Windows certificate store. This is
///                         how a hardware token signs, once its driver is installed; the token has to
///                         be plugged into the machine doing the build, so CI cannot use it.
///   TUBE_SIGN_PFX         A .pfx file, with TUBE_SIGN_PFX_PASSWORD. Only an older certificate comes
///                         as a file: since June 2023 a newly issued key has to live on a hardware
///                         token or in a cloud HSM, so this is for a key issued before that.
///   TUBE_SIGN_DLIB        A signtool signing library, with TUBE_SIGN_DLIB_METADATA naming the JSON
///                         file that says which account and certificate profile to sign with. This is
///                         how the cloud services sign, and the only one of the three that works
///                         unattended in CI: Azure Trusted Signing, DigiCert KeyLocker, SSL.com eSigner.
///
/// With none of them set nothing is signed, so a working copy without a certificate still builds; the
/// build says so rather than passing it off as a release. `tube export --signed` and
/// `tube installer --signed` turn that into an error, for a release that must not go out unsigned.
/// </summary>
internal sealed class Signing
{
    // Any public timestamp server will do for a certificate held on a token or in a file.
    private const string DefaultTimestamp = "http://timestamp.digicert.com";

    // Azure Trusted Signing issues certificates that are valid for about three days, so a signature is
    // only durable once timestamped, and it expects Microsoft's own server.
    private const string AzureTimestamp = "http://timestamp.acs.microsoft.com";

    private readonly string _tool;
    private readonly string[] _key;
    private readonly string _timestamp;

    private Signing(string tool, string[] key, string timestamp)
    {
        _tool = tool;
        _key = key;
        _timestamp = timestamp;
    }

    /// <summary>The signer the environment describes, or null if no certificate is configured.</summary>
    public static Signing? Configured()
    {
        var thumbprint = Setting("TUBE_SIGN_THUMBPRINT");
        var pfx = Setting("TUBE_SIGN_PFX");
        var dlib = Setting("TUBE_SIGN_DLIB");

        var chosen = new[] { thumbprint, pfx, dlib }.Count(value => value is not null);
        if (chosen == 0) return null;
        if (chosen > 1)
        {
            throw new BuildFailure(
                "Set only one of TUBE_SIGN_THUMBPRINT, TUBE_SIGN_PFX and TUBE_SIGN_DLIB; they are three "
                + "ways of naming one key.");
        }

        string[] key;
        string timestamp = DefaultTimestamp;
        if (thumbprint is not null)
        {
            // Spaces and the invisible left-to-right mark come with a thumbprint copied out of the
            // certificate dialog, and signtool matches on the bare hex.
            key = ["/sha1", new string(thumbprint.Where(Uri.IsHexDigit).ToArray())];
        }
        else if (pfx is not null)
        {
            if (!File.Exists(pfx)) throw new BuildFailure($"TUBE_SIGN_PFX names {pfx}, which isn't there.");
            // The password goes in the argument list, not through a shell, so it stays out of shell
            // history - though it is briefly visible in the process list, as it is with any signtool use.
            key = Setting("TUBE_SIGN_PFX_PASSWORD") is string password
                ? ["/f", pfx, "/p", password]
                : ["/f", pfx];
        }
        else
        {
            var metadata = Setting("TUBE_SIGN_DLIB_METADATA")
                ?? throw new BuildFailure(
                    "TUBE_SIGN_DLIB also needs TUBE_SIGN_DLIB_METADATA, the JSON file naming the account "
                    + "and certificate profile to sign with.");
            if (!File.Exists(dlib!)) throw new BuildFailure($"TUBE_SIGN_DLIB names {dlib}, which isn't there.");
            if (!File.Exists(metadata)) throw new BuildFailure($"TUBE_SIGN_DLIB_METADATA names {metadata}, which isn't there.");
            key = ["/dlib", dlib!, "/dmdf", metadata];
            timestamp = AzureTimestamp;
        }

        return new Signing(FindTool(), key, Setting("TUBE_SIGN_TIMESTAMP") ?? timestamp);
    }

    /// <summary>Signs a file in place, and stops the build if signing fails.</summary>
    /// <param name="what">The path to print, so the build log says what was signed.</param>
    public void Sign(string file, string what)
    {
        // /fd is the digest the file is signed with and /td the one the timestamp uses: SHA-256, since
        // Windows no longer accepts SHA-1. /tr is the RFC 3161 timestamp, as opposed to the older /t -
        // without a timestamp every signature stops verifying the day the certificate expires.
        Proc.Run(_tool, ["sign", "/fd", "SHA256", "/tr", _timestamp, "/td", "SHA256", .. _key, file]);
        Console.WriteLine($"Signed   {what}");
    }

    /// <summary>Checks a signed file the way Windows will, as a release build's last word.</summary>
    public void Verify(string file)
    {
        // /pa picks the ordinary Authenticode policy rather than the driver one, which a game would fail.
        Proc.Run(_tool, ["verify", "/pa", file]);
    }

    private static string? Setting(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value.Trim() : null;

    private static string FindTool()
    {
        if (Setting("TUBE_SIGNTOOL") is string custom)
        {
            return File.Exists(custom)
                ? custom
                : throw new BuildFailure($"TUBE_SIGNTOOL names {custom}, which isn't there.");
        }

        // The Windows SDK installs a signtool per SDK version, under a folder named for that version.
        // Take the newest, and the build matching the machine rather than always x64.
        var kits = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };

        if (Directory.Exists(kits))
        {
            var found = Directory.EnumerateDirectories(kits)
                .Select(folder => (folder, version: Version.TryParse(Path.GetFileName(folder), out var v) ? v : null))
                .Where(entry => entry.version is not null)
                .OrderByDescending(entry => entry.version)
                .Select(entry => Path.Combine(entry.folder, architecture, "signtool.exe"))
                .FirstOrDefault(File.Exists);
            if (found is not null) return found;
        }

        throw new BuildFailure(
            "A signing certificate is configured, but signtool.exe isn't installed. It comes with the "
            + "Windows SDK (winget install Microsoft.WindowsSDK), or set TUBE_SIGNTOOL to a copy.");
    }
}
