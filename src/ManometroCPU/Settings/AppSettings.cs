using ManometroCPU.Calibration;

namespace ManometroCPU.Settings;

// CalibratedMcp usa a tabela do PC; Temperature usa a do Uno; Legacy não exige confirmação.
public enum OutputMode { CalibratedMcp, TemperatureDecimal, TemperatureInteger, LegacyDecimal, LegacyInteger }

// Valores padrão e dados persistidos em config.json. Alterações da janela passam por Validate.
public sealed class AppSettings
{
    public bool AutoPort { get; set; } = true;
    public string ManualPort { get; set; } = "";
    public string LastPort { get; set; } = "";
    public string LastDeviceId { get; set; } = "";
    public int BaudRate { get; set; } = 9600;
    // Identificador vazio seleciona automaticamente o sensor prioritário.
    public string SensorId { get; set; } = "";
    public int ReadIntervalMs { get; set; } = 1000;
    public int SendIntervalMs { get; set; } = 1000;
    public double TempMin { get; set; } = 30;
    public double TempMax { get; set; } = 95;
    public SmoothingMode Smoothing { get; set; } = SmoothingMode.Ema;
    public double EmaAlpha { get; set; } = 0.25;
    public int AverageWindow { get; set; } = 5;
    // Diferença mínima em °C para aceitar uma nova posição da agulha.
    public double Deadband { get; set; } = 0.3;
    public int StepIntervalMs { get; set; } = 120;
    public int SafeMcp { get; set; } = 25;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public OutputMode Output { get; set; } = OutputMode.CalibratedMcp;
    public List<CalibrationPoint> Calibration { get; set; } = [new(30,25),new(40,22),new(50,18),new(60,14),new(70,10),new(75,8),new(80,6),new(90,2),new(95,0)];

    public void Validate()
    {
        // Rejeita dados inconsistentes antes de salvar ou controlar o hardware.
        // Intervalos de até 3 s deixam margem em relação ao watchdog de 10 s do Uno.
        if (!Enum.IsDefined(Smoothing) || !Enum.IsDefined(Output)) throw new ArgumentException("Modo inválido.");
        ManualPort ??= ""; LastPort ??= ""; LastDeviceId ??= ""; SensorId ??= "";
        if (AutoPort && Output is OutputMode.LegacyDecimal or OutputMode.LegacyInteger) throw new ArgumentException("O modo Legacy exige porta manual porque não identifica o firmware.");
        if (!AutoPort && !System.Text.RegularExpressions.Regex.IsMatch(ManualPort ?? "", @"^COM\d+$")) throw new ArgumentException("Selecione uma porta COM manual.");
        if (BaudRate < 1200 || BaudRate > 115200) throw new ArgumentException("Baud deve estar entre 1200 e 115200.");
        if (ReadIntervalMs < 500 || ReadIntervalMs > 3000 || SendIntervalMs < 500 || SendIntervalMs > 3000) throw new ArgumentException("Intervalos devem estar entre 500 e 3000 ms.");
        if (!double.IsFinite(TempMin) || !double.IsFinite(TempMax) || TempMin < 0 || TempMax > 125 || TempMin >= TempMax) throw new ArgumentException("Faixa de temperatura inválida.");
        if (!double.IsFinite(EmaAlpha) || EmaAlpha <= 0 || EmaAlpha > 1 || AverageWindow < 1 || AverageWindow > 60) throw new ArgumentException("Suavização inválida.");
        if (!double.IsFinite(Deadband) || Deadband < 0 || Deadband > 10 || StepIntervalMs < 1 || StepIntervalMs > 1000 || SafeMcp < 0 || SafeMcp > 255) throw new ArgumentException("Parâmetros do manômetro inválidos.");
        if (Calibration is null || Calibration.Count < 2 || Calibration.Any(p => p is null || !double.IsFinite(p.Temperature) || p.Mcp < 0 || p.Mcp > 255)) throw new ArgumentException("Informe pelo menos dois pontos válidos de calibração.");
        // Neste circuito, maior temperatura exige MCP menor; não permite inverter essa relação.
        Calibration = Calibration.OrderBy(p => p.Temperature).ToList();
        for (int i = 1; i < Calibration.Count; i++)
            if (Calibration[i].Temperature <= Calibration[i-1].Temperature || Calibration[i].Mcp > Calibration[i-1].Mcp) throw new ArgumentException("Temperaturas devem ser únicas e MCP deve diminuir ou permanecer igual quando a temperatura aumenta.");
        if (Calibration[0].Temperature > TempMin || Calibration[^1].Temperature < TempMax) throw new ArgumentException("A tabela deve cobrir a faixa mínima/máxima.");
    }
}
