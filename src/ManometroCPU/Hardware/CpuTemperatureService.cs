using LibreHardwareMonitor.Hardware;

namespace ManometroCPU.Hardware;

// Id identifica o sensor; Name é o nome exibido; Value é a temperatura em °C.
public record TemperatureReading(string Id, string Name, double Value);

public sealed class CpuTemperatureService : IDisposable
{
    // Consulta somente a CPU, evitando o custo de monitorar GPU, discos e outros dispositivos.
    private readonly Computer computer = new() { IsCpuEnabled = true };
    private bool opened;
    public TemperatureReading[] Sensors { get; private set; } = [];

    public TemperatureReading? Read(string? manualId = null)
    {
        // Reutiliza o acesso ao hardware em vez de carregar o driver a cada leitura.
        if (!opened) { computer.Open(); opened = true; }
        var readings = new List<TemperatureReading>();
        foreach (var hardware in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
            Collect(hardware, readings);
        Sensors = readings.ToArray();
        // Sensor manual ausente retorna null; não é substituído silenciosamente por outro.
        return string.IsNullOrWhiteSpace(manualId)
            ? Sensors.OrderBy(s => Rank(s.Name)).FirstOrDefault()
            : Sensors.FirstOrDefault(s => s.Id == manualId);
    }

    private static void Collect(IHardware hardware, List<TemperatureReading> readings)
    {
        // Atualiza as medições e aceita apenas temperaturas finitas na faixa esperada.
        hardware.Update();
        foreach (var s in hardware.Sensors)
            if (s.SensorType == SensorType.Temperature && s.Value is float v && float.IsFinite(v) && v > 0 && v < 125)
                readings.Add(new(s.Identifier.ToString(), s.Name, v));
        // Percorre também sensores organizados em subcomponentes da CPU.
        foreach (var child in hardware.SubHardware) Collect(child, readings);
    }

    private static int Rank(string name)
    {
        // Quanto menor a pontuação, maior a preferência. Tctl/Tdie é a referência do Ryzen.
        string[] priority = ["CPU (Tctl/Tdie)", "Core (Tctl/Tdie)", "CPU Package", "CPU Die (average)", "Core (Tdie)", "Core Average"];
        int rank = Array.FindIndex(priority, p => name.Equals(p, StringComparison.OrdinalIgnoreCase));
        return rank >= 0 ? rank : 100;
    }

    // Libera os recursos nativos de monitoramento ao encerrar o controlador.
    public void Dispose() { if (opened) computer.Close(); }
}
