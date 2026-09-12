using System.Collections.Concurrent;
using System.Globalization;
using ManometroCPU.Calibration;
using ManometroCPU.Hardware;
using ManometroCPU.Logging;
using ManometroCPU.Serial;
using ManometroCPU.Settings;

namespace ManometroCPU;

// Fotografia do estado consultada pela interface; Temperature é real e Filtered é suavizada.
public sealed record AppState(double? Temperature, double? Filtered, string Sensor, string? Port, string Status, TemperatureReading[] Sensors);

public sealed class AppController : IDisposable
{
    private readonly CpuTemperatureService cpu = new();
    private readonly ArduinoSerialService serial = new();
    private readonly LogService log;
    // A interface enfileira pedidos; somente o trabalhador acessa sensores e porta serial.
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly CancellationTokenSource stop = new();
    private AppSettings settings;
    private TemperatureFilter filter = new();
    private bool enabled = true, testing;
    private double? testTemperature;
    private int? testMcp;
    private long nextRead, nextSend, nextConnect;
    // held guarda o último valor aceito pelo deadband, que pode diferir do valor filtrado.
    private double? filtered, held;
    private TemperatureReading? reading;
    private string status = "Inicializando", lastLogged = "", lastSensor = "";
    public AppState State { get; private set; } = new(null,null,"Procurando sensor…",null,"Inicializando",[]);
    public AppSettings Settings => SettingsService.Clone(settings);
    public Task? Worker { get; private set; }

    public AppController(AppSettings settings, LogService log) { this.settings = settings; this.log = log; }
    public void Start() => Worker = Task.Run(Run);
    public void SetConnected(bool value) => commands.Enqueue(() => { enabled = value; serial.Disconnect(); nextConnect = 0; });
    // Retorna uma tarefa para a janela saber se a gravação/configuração realmente terminou.
    public Task Apply(AppSettings value)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Enqueue(() =>
        {
            try
            {
                // Preserva descobertas de porta feitas enquanto a janela de configurações estava aberta.
                value.LastPort = settings.LastPort; value.LastDeviceId = settings.LastDeviceId;
                SettingsService.Save(value);
                try
                {
                    if (value.StartWithWindows != settings.StartWithWindows || value.StartWithWindows)
                        StartupService.SetEnabled(value.StartWithWindows);
                }
                // Se configurar o início automático falhar, restaura o JSON anterior.
                catch { SettingsService.Save(settings); throw; }
                settings = value; filter = new(); filtered = held = null; serial.Disconnect(); nextConnect = nextRead = nextSend = 0;
                completion.SetResult();
            }
            catch (Exception ex) { log.Write("Erro ao salvar configurações: " + ex); completion.SetException(ex); }
        });
        return completion.Task;
    }
    // O teste substitui temporariamente a saída real por temperatura simulada ou MCP direto.
    public void Test(double? temperature, int? mcp) => commands.Enqueue(() =>
    { testing = true; testTemperature = temperature; testMcp = mcp; nextSend = 0; });
    public void EndTest() => commands.Enqueue(() => { testing = false; testTemperature = null; testMcp = null; held = null; nextSend = 0; });
    public void Stop() => stop.Cancel();

    private async Task Run()
    {
        log.Write("Aplicação iniciada");
        try
        {
            while (!stop.IsCancellationRequested)
            {
                while (commands.TryDequeue(out var action))
                    try { action(); } catch (Exception ex) { SetStatus("Erro de configuração: " + ex.Message); }
                // Relógio monotônico: mudanças de data/hora do Windows não alteram os intervalos.
                long now = Environment.TickCount64;
                if (now >= nextRead)
                {
                    nextRead = now + settings.ReadIntervalMs;
                    try
                    {
                        reading = cpu.Read(settings.SensorId);
                        filtered = reading is null ? null : filter.Update(reading.Value, settings.Smoothing, settings.EmaAlpha, settings.AverageWindow);
                        // Não reutiliza uma temperatura antiga quando o sensor deixa de responder.
                        if (reading is null) { held = null; filter = new(); }
                        if (reading != null && reading.Id != lastSensor) { log.Write("Sensor encontrado: " + reading.Name + " " + reading.Id); lastSensor = reading.Id; }
                    }
                    catch (Exception ex) { reading = null; filtered = held = null; SetStatus("Erro no sensor: " + ex.Message); }
                }
                if (enabled && serial.Port is null && now >= nextConnect)
                {
                    nextConnect = now + 5000;
                    try { Connect(); } catch (Exception ex) { SetStatus("Arduino desconectado: " + ex.Message); }
                }
                if (enabled && serial.Port != null && now >= nextSend)
                {
                    nextSend = now + settings.SendIntervalMs;
                    try
                    {
                        if (testing && testMcp is int mcp)
                        {
                            if (Legacy) throw new InvalidOperationException("Teste MCP exige o firmware ManometroCPU e protocolo confirmado.");
                            serial.Command("MCP:" + mcp);
                        }
                        else if ((testing ? testTemperature : filtered) is double value)
                        {
                            value = Math.Clamp(value, settings.TempMin, settings.TempMax);
                            if (held is null || Math.Abs(value - held.Value) >= settings.Deadband || testing) held = value;
                            // Reenvia mesmo sem variação: o watchdog precisa receber posições periodicamente.
                            SendTemperature(held.Value);
                        }
                        else if (!Legacy) serial.Command("PING", "PONG"); // Verifica o enlace sem renovar o watchdog.
                        SetStatus(testing ? "Modo de teste" : reading is null ? "Sensor indisponível — saída em failsafe após 10 s" : "Funcionando");
                    }
                    catch (Exception ex) { serial.Disconnect(); nextConnect = Environment.TickCount64 + 3000; SetStatus("Conexão perdida: " + ex.Message); }
                }
                if (!enabled) SetStatus("Desconectado pelo usuário");
                else if (serial.Port is null && !status.StartsWith("Arduino") && !status.StartsWith("Conexão")) SetStatus("Arduino desconectado — procurando");
                State = new(reading?.Value, filtered, reading?.Name ?? (DriverSetupService.Installed ? "Indisponível — verifique permissões" : "Instale o driver em Configurações"), serial.Port, status, cpu.Sensors);
                await Task.Delay(100, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            // Tenta levar a saída à posição segura; se a comunicação falhar, vale o watchdog do Uno.
            try { if (serial.Port != null && !Legacy) serial.Command("MCP:" + settings.SafeMcp); } catch (Exception ex) { log.Write("Failsafe ao sair: " + ex.Message); }
            serial.Dispose(); cpu.Dispose(); log.Write("Aplicação encerrada");
        }
    }
    private bool Legacy => settings.Output is OutputMode.LegacyDecimal or OutputMode.LegacyInteger;
    private void Connect()
    {
        var ports = ArduinoDiscoveryService.List();
        string? connectionError = null;
        // Identidade USB e última COM ajudam na reconexão; VERSION confirma o firmware novo.
        var candidates = settings.AutoPort
            ? ports.Where(p => p.LikelyArduino || (settings.LastDeviceId.Length > 0 && p.DeviceId == settings.LastDeviceId) || p.Port == settings.LastPort)
                .OrderByDescending(p => settings.LastDeviceId.Length > 0 && p.DeviceId == settings.LastDeviceId).ThenByDescending(p => p.Port == settings.LastPort)
            : ports.Where(p => p.Port.Equals(settings.ManualPort, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Port);
        foreach (var port in candidates)
        {
            if (stop.IsCancellationRequested) return;
            try
            {
                serial.Connect(port.Port, settings.BaudRate, !Legacy);
                if (!Legacy) { serial.Command("STEP:" + settings.StepIntervalMs); serial.Command("SAFE:" + settings.SafeMcp); }
                settings.LastPort = port.Port; settings.LastDeviceId = port.DeviceId;
                SettingsService.Save(settings); log.Write("Arduino encontrado/conectado: " + port);
                reading = null; filtered = held = null; nextRead = nextSend = 0; SetStatus("Arduino conectado"); return;
            }
            catch (Exception ex) { serial.Disconnect(); connectionError = ex.Message; log.Write("Falha em " + port.Port + ": " + ex.Message); }
        }
        SetStatus(connectionError is null ? "Arduino desconectado — nenhuma porta compatível encontrada" : "Arduino: " + connectionError);
    }
    private void SendTemperature(double value)
    {
        // Usa ponto decimal na serial, independentemente da configuração regional do Windows.
        // No modo MCP, a tabela personalizada é aplicada aqui; TEMP usa a tabela do firmware.
        bool integer = settings.Output is OutputMode.TemperatureInteger or OutputMode.LegacyInteger;
        string text = value.ToString(integer ? "F0" : "F1", CultureInfo.InvariantCulture);
        if (Legacy) serial.SendLegacy(text);
        else if (settings.Output == OutputMode.CalibratedMcp) serial.Command("MCP:" + GaugeCalibrationService.Map(value, settings.Calibration));
        else serial.Command("TEMP:" + text);
    }
    private void SetStatus(string value) { status = value; if (value != lastLogged) { log.Write(value); lastLogged = value; } }
    public void Dispose() { stop.Dispose(); }
}
