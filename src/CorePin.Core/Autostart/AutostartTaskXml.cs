using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CorePin.Core.Autostart;

public static class AutostartTaskXml
{
    private const string Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string Build(string exePath, string userName, string userSid)
    {
        var output = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, IndentChars = "  " };

        using (var writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartElement("Task", Ns);
            writer.WriteAttributeString("version", "1.2");

            writer.WriteStartElement("RegistrationInfo", Ns);
            writer.WriteElementString("Author", Ns, "CorePin");
            writer.WriteElementString("Description", Ns, "Starts CorePin at sign-in with administrator rights.");
            writer.WriteEndElement();

            writer.WriteStartElement("Principals", Ns);
            writer.WriteStartElement("Principal", Ns);
            writer.WriteAttributeString("id", "Author");
            writer.WriteElementString("UserId", Ns, userSid);
            writer.WriteElementString("LogonType", Ns, "InteractiveToken");
            writer.WriteElementString("RunLevel", Ns, "HighestAvailable");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("Settings", Ns);
            writer.WriteElementString("DisallowStartIfOnBatteries", Ns, "false");
            writer.WriteElementString("StopIfGoingOnBatteries", Ns, "false");
            writer.WriteElementString("ExecutionTimeLimit", Ns, "PT0S");
            writer.WriteElementString("Priority", Ns, "5");
            writer.WriteEndElement();

            writer.WriteStartElement("Triggers", Ns);
            writer.WriteStartElement("LogonTrigger", Ns);
            writer.WriteElementString("UserId", Ns, userName);
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("Actions", Ns);
            writer.WriteAttributeString("Context", "Author");
            writer.WriteStartElement("Exec", Ns);
            writer.WriteElementString("Command", Ns, $"\"{exePath}\"");
            writer.WriteElementString("Arguments", Ns, "--tray");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        return output.ToString();
    }

    public static (string? ExePath, string? PrincipalSid)? Read(string taskXml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(taskXml);
        }
        catch (XmlException)
        {
            return null;
        }

        XNamespace ns = Ns;
        string? command = document.Root?.Element(ns + "Actions")?.Element(ns + "Exec")?.Element(ns + "Command")?.Value;
        if (string.IsNullOrEmpty(command)) return null;

        string? principalSid = document.Root?.Element(ns + "Principals")?.Element(ns + "Principal")
            ?.Element(ns + "UserId")?.Value;

        return (Unquote(command), principalSid);
    }

    private static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
