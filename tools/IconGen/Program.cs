using System.IO;
using System.Security.Cryptography;

namespace IconGen;

/// Renders assets/icon.svg into the three checked-in .ico files. Run: dotnet run --project tools\IconGen
internal static class Program
{
    private static readonly int[] TraySizes = [16, 20, 24, 32];

    /// The two small sizes come from the borderless tile, the rest from the bordered one.
    // 20/24 px scale by 1.25/1.5, so their 1px strokes land on fractional pixels — accepted by design.
    private static readonly (string Group, int Size)[] AppFrames =
    [
        ("app-tile-borderless", 16),
        ("app-tile", 20),
        ("app-tile-borderless", 24),
        ("app-tile", 32),
        ("app-tile", 48),
        ("app-tile", 64),
        ("app-tile", 256),
    ];

    [STAThread]
    private static void Main()
    {
        string assets = Path.Combine(FindRepositoryRoot(), "assets");
        var groups = IconSvg.Load(Path.Combine(assets, "icon.svg")).ToDictionary(group => group.Id);

        Write(assets, "CorePin.ico", groups, AppFrames);
        Write(assets, "tray-light.ico", groups, TrayFrames("tray-light"));
        Write(assets, "tray-dark.ico", groups, TrayFrames("tray-dark"));
        WriteSvgHashSidecar(assets);
    }

    /// Lets the build's freshness check compare against the SVG that produced these .ico files.
    private static void WriteSvgHashSidecar(string assets)
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(assets, "icon.svg"));
        string hash = Convert.ToHexString(SHA256.HashData(svg));
        File.WriteAllText(Path.Combine(assets, "icon.svg.sha256"), hash + "\n");
    }

    private static IEnumerable<(string Group, int Size)> TrayFrames(string group) =>
        TraySizes.Select(size => (Group: group, Size: size));

    private static void Write(
        string assets, string fileName, IReadOnlyDictionary<string, SvgGroup> groups,
        IEnumerable<(string Group, int Size)> frames)
    {
        var bitmaps = frames
            .Select(frame => FrameRenderer.Render(Group(groups, frame.Group), frame.Size))
            .ToList();

        string path = Path.Combine(assets, fileName);
        IcoWriter.Write(path, bitmaps);
        Console.WriteLine($"{fileName}: {bitmaps.Count} frames, {new FileInfo(path).Length} bytes");
    }

    private static SvgGroup Group(IReadOnlyDictionary<string, SvgGroup> groups, string id) =>
        groups.TryGetValue(id, out SvgGroup? group)
            ? group
            : throw new InvalidDataException($"icon.svg has no group '{id}'.");

    /// Walks up from the binary so the tool works no matter which directory it was started from.
    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "assets", "icon.svg"))) return dir.FullName;

        throw new InvalidDataException($"No assets\\icon.svg found above {AppContext.BaseDirectory}.");
    }
}
