using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace ManometroCPU.Serial;

// LikelyArduino é uma pista pelo USB, não uma confirmação do firmware presente na placa.
public record PortInfo(string Port, string Description, string DeviceId, bool LikelyArduino)
{
    public override string ToString() => $"{Port} — {Description}";
}

public static class ArduinoDiscoveryService
{
    public static PortInfo[] List()
    {
        // Começa pelas COM disponíveis e acrescenta descrição/identidade obtidas pelo WMI.
        var ports = SerialPort.GetPortNames().Select(p => new PortInfo(p, p, "", false)).ToDictionary(p => p.Port);
        try
        {
            using var search = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            using var results = search.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    string name = item["Name"]?.ToString() ?? "";
                    string id = item["PNPDeviceID"]?.ToString() ?? "";
                    var match = Regex.Match(name, @"\((COM\d+)\)");
                    if (!match.Success || !ports.ContainsKey(match.Groups[1].Value)) continue;
                    bool likely = name.Contains("Arduino", StringComparison.OrdinalIgnoreCase) ||
                        new[] { "VID_2341", "VID_2A03", "VID_1A86", "VID_0403" }.Any(v => id.Contains(v, StringComparison.OrdinalIgnoreCase));
                    ports[match.Groups[1].Value] = new(match.Groups[1].Value, name, id, likely);
                }
            }
        }
        catch (ManagementException) { /* Bare COM names still allow manual selection. */ }
        catch (UnauthorizedAccessException) { /* WMI may be restricted; manual COM remains available. */ }
        return ports.Values.OrderBy(p => p.Port).ToArray();
    }
}
