using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Xml.Linq;

namespace IconGen;

/// One rect of the source drawing, in the reference box of its group.
internal sealed record SvgRect(
    double X, double Y, double Width, double Height, double Rx,
    Color? Fill, Color? Stroke, double StrokeWidth);

internal sealed record SvgGroup(string Id, IReadOnlyList<SvgRect> Rects);

/// Reads the icon source: groups of rects, each group in its own reference box.
internal static class IconSvg
{
    public const double ReferenceBox = 16.0;

    private const double Tolerance = 1e-9;
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static readonly HashSet<string> KnownGroupAttributes = ["id", "transform"];
    private static readonly HashSet<string> KnownRectAttributes =
        ["x", "y", "width", "height", "rx", "fill", "stroke", "stroke-width"];

    public static IReadOnlyList<SvgGroup> Load(string path)
    {
        XElement root = XDocument.Load(path).Root
            ?? throw new InvalidDataException($"{path} has no root element.");

        var groups = new List<SvgGroup>();
        foreach (XElement element in root.Elements())
        {
            if (element.Name != Svg + "g")
                throw new InvalidDataException($"{path} has an unexpected root element '{element.Name.LocalName}'.");
            RequireKnownAttributes(element, KnownGroupAttributes, "A group");

            string id = (string?)element.Attribute("id")
                ?? throw new InvalidDataException($"{path} has a group without an id.");

            var rects = new List<SvgRect>();
            foreach (XElement child in element.Elements())
            {
                if (child.Name != Svg + "rect")
                    throw new InvalidDataException($"Group '{id}' has an unexpected element '{child.Name.LocalName}'.");
                rects.Add(ParseRect(child));
            }
            if (rects.Count == 0) throw new InvalidDataException($"Group '{id}' has no rect.");
            foreach (SvgRect rect in rects) RequireInsideBox(id, rect);
            groups.Add(new SvgGroup(id, rects));
        }

        if (groups.Count == 0) throw new InvalidDataException($"{path} has no group.");
        return groups;
    }

    private static SvgRect ParseRect(XElement element)
    {
        RequireKnownAttributes(element, KnownRectAttributes, "A rect");

        Color? stroke = Paint(element, "stroke", required: false);
        double strokeWidth = Number(element, "stroke-width", 0);
        if (stroke is not null && strokeWidth <= 0)
            throw new InvalidDataException("A stroked rect needs a positive stroke-width.");

        return new SvgRect(
            Number(element, "x", 0), Number(element, "y", 0),
            Required(element, "width"), Required(element, "height"),
            Number(element, "rx", 0),
            Paint(element, "fill", required: true), stroke, strokeWidth);
    }

    /// Rejects any attribute outside the known set, so a typo or a presentation attribute
    /// like transform/style/opacity is never silently ignored.
    private static void RequireKnownAttributes(XElement element, HashSet<string> known, string subject)
    {
        foreach (XAttribute attribute in element.Attributes())
            if (!known.Contains(attribute.Name.LocalName))
                throw new InvalidDataException($"{subject} has an unexpected attribute '{attribute.Name.LocalName}'.");
    }

    /// The group transform only places the boxes side by side; each group draws in its own box.
    private static void RequireInsideBox(string groupId, SvgRect rect)
    {
        double half = rect.Stroke is null ? 0 : rect.StrokeWidth / 2;
        bool inside = rect.X - half >= -Tolerance
                   && rect.Y - half >= -Tolerance
                   && rect.X + rect.Width + half <= ReferenceBox + Tolerance
                   && rect.Y + rect.Height + half <= ReferenceBox + Tolerance;
        if (!inside)
            throw new InvalidDataException(
                $"Group '{groupId}' draws outside its {ReferenceBox} unit box — rect coordinates must be group-local.");
    }

    private static double Required(XElement element, string name)
    {
        if (element.Attribute(name) is null)
            throw new InvalidDataException($"A rect is missing '{name}'.");
        return Number(element, name, 0);
    }

    private static double Number(XElement element, string name, double fallback)
    {
        string? raw = (string?)element.Attribute(name);
        if (raw is null) return fallback;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value))
            throw new InvalidDataException($"Attribute {name}=\"{raw}\" is not a number.");
        return value;
    }

    /// A missing "stroke" and "none" both mean no such paint; "fill" is mandatory instead so a
    /// rect always states its intent — use fill="none" for a deliberately unfilled shape.
    private static Color? Paint(XElement element, string name, bool required)
    {
        string? raw = (string?)element.Attribute(name);
        if (raw is null)
        {
            if (required) throw new InvalidDataException($"A rect is missing '{name}'.");
            return null;
        }
        if (raw == "none") return null;
        if (raw.Length != 7 || raw[0] != '#')
            throw new InvalidDataException($"Attribute {name}=\"{raw}\" is not a #RRGGBB colour.");
        return Color.FromRgb(Hex(raw, 1), Hex(raw, 3), Hex(raw, 5));
    }

    private static byte Hex(string colour, int offset) =>
        byte.Parse(colour.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
