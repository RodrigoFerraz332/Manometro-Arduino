using ManometroCPU.Calibration;
using ManometroCPU.Hardware;
using ManometroCPU.Serial;
using ManometroCPU.Settings;
using System.Globalization;

namespace ManometroCPU.UI;

// Edita uma cópia das configurações; a janela principal aplica os valores após a confirmação.
public sealed class SettingsForm : Form
{
    public AppSettings Value { get; private set; }
    private readonly TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new(16) };
    // Cada campo registra uma ação que copia seu conteúdo para o modelo ao clicar em Salvar.
    private readonly List<Action> apply = [];
    private readonly ComboBox ports = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox calibration = new() { Multiline = true, Height = 160, Width = 330, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };

    public SettingsForm(AppSettings settings, TemperatureReading[] sensors)
    {
        Value = settings;
        Text = "Configurações — Manometro CPU"; Font = new("Segoe UI", 10); Size = new(740, 740); MinimumSize = new(720, 500); StartPosition = FormStartPosition.CenterParent;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(table); Controls.Add(scroll);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new(8) }; Controls.Add(footer);
        var save = new Button { Text = "Salvar", AutoSize = true }; footer.Controls.Add(save);
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true }; footer.Controls.Add(cancel); CancelButton = cancel;
        Check("Porta automática", settings.AutoPort, v => settings.AutoPort = v);
        ports.Text = settings.ManualPort; Row("Porta manual", ports); apply.Add(() => settings.ManualPort = ports.Text.Trim().ToUpperInvariant());
        var refresh = new Button { Text = "Atualizar portas", AutoSize = true }; Row("", refresh); refresh.Click += async (_, _) => await RefreshPorts(); Shown += async (_, _) => await RefreshPorts();
        Number("Baud (firmware padrão: 9600)", settings.BaudRate, 1200, 115200, 0, v => settings.BaudRate = (int)v);
        var driver = new Button { Text = DriverSetupService.Installed ? "Reparar driver de sensores" : "Instalar driver de sensores", AutoSize = true };
        Row("Preparação inicial (PawnIO)", driver);
        driver.Click += async (_, _) =>
        {
            driver.Enabled = false;
            try
            {
                await DriverSetupService.Install();
                MessageBox.Show(this,"Driver preparado. Saia pelo menu da bandeja e abra o aplicativo novamente para detectar os sensores.");
            }
            catch (Exception ex) { MessageBox.Show(this,"Instalação não concluída: " + ex.Message); }
            finally { if (!IsDisposed) driver.Enabled = true; }
        };
        var sensor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        sensor.Items.Add(new SensorOption("", "Automático"));
        foreach (var s in sensors) sensor.Items.Add(new SensorOption(s.Id, s.Name + " [" + s.Id + "]"));
        if (settings.SensorId.Length > 0 && !sensors.Any(s => s.Id == settings.SensorId)) sensor.Items.Add(new SensorOption(settings.SensorId, "Indisponível: " + settings.SensorId));
        sensor.SelectedItem = sensor.Items.Cast<SensorOption>().First(s => s.Id == settings.SensorId); Row("Sensor CPU", sensor); apply.Add(() => settings.SensorId = ((SensorOption)sensor.SelectedItem!).Id);
        Choice("Protocolo de saída", settings.Output, v => settings.Output = v);
        Row("", new Label { AutoSize = true, MaximumSize = new(330,0), Text = "CalibratedMcp aplica sua tabela. Temperature usa a tabela padrão do firmware. Legacy não confirma entrega e usa firmware antigo." });
        Number("Leitura CPU (ms)", settings.ReadIntervalMs, 500, 3000, 0, v => settings.ReadIntervalMs = (int)v);
        Number("Envio serial (ms)", settings.SendIntervalMs, 500, 3000, 0, v => settings.SendIntervalMs = (int)v);
        Number("Temperatura mínima (°C)", settings.TempMin, 0, 124, 1, v => settings.TempMin = v);
        Number("Temperatura máxima (°C)", settings.TempMax, 1, 125, 1, v => settings.TempMax = v);
        Choice("Suavização", settings.Smoothing, v => settings.Smoothing = v);
        Number("Sensibilidade EMA (alfa)", settings.EmaAlpha, .01, 1, 2, v => settings.EmaAlpha = v);
        Number("Janela média móvel (leituras)", settings.AverageWindow, 1, 60, 0, v => settings.AverageWindow = (int)v);
        Number("Deadband (°C)", settings.Deadband, 0, 10, 1, v => settings.Deadband = v);
        Number("Velocidade: ms por passo MCP", settings.StepIntervalMs, 1, 1000, 0, v => settings.StepIntervalMs = (int)v);
        Number("MCP seguro (falha de comunicação)", settings.SafeMcp, 0, 255, 0, v => settings.SafeMcp = (int)v);
        calibration.Text = string.Join(Environment.NewLine, settings.Calibration.Select(p => $"{p.Temperature.ToString(CultureInfo.InvariantCulture)} = {p.Mcp}")); Row("Calibração: °C = MCP", calibration);
        Row("", new Label { AutoSize = true, MaximumSize = new(330,0), Text = "Use ponto decimal. MCP deve diminuir à medida que a temperatura aumenta. Valores dependem do seu circuito." });
        Check("Iniciar junto com o Windows", settings.StartWithWindows, v => settings.StartWithWindows = v);
        Row("", new Label { AutoSize = true, MaximumSize = new(330,0), Text = StartupService.IsAdministrator ? "Execução elevada: será criada tarefa no logon para permitir acesso ao sensor sem novo UAC." : "Inicialização por usuário. Se a CPU exigir elevação, abra o aplicativo como administrador uma vez e salve esta opção novamente." });
        Check("Minimizar para bandeja", settings.MinimizeToTray, v => settings.MinimizeToTray = v);
        Row("Configuração local", new TextBox { Text = SettingsService.ConfigPath, ReadOnly = true, Width = 330 });
        save.Click += (_, _) =>
        {
            try
            {
                foreach (var action in apply) action();
                // Converte as linhas "temperatura = MCP"; Validate confere faixa e ordem dos pontos.
                settings.Calibration = calibration.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(line =>
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) throw new FormatException("Use temperatura = MCP, um ponto por linha.");
                    return new CalibrationPoint(double.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
                }).ToList();
                settings.Validate(); Value = settings; DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { MessageBox.Show(this, ex.Message, "Verifique as configurações", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
    }
    private async Task RefreshPorts()
    {
        try
        {
            // A consulta WMI pode demorar; executa fora da interface e verifica se a janela ainda existe.
            var list = await Task.Run(ArduinoDiscoveryService.List);
            if (IsDisposed) return;
            string selected = ports.Text; ports.Items.Clear(); ports.Items.AddRange(list.Select(p => (object)p.Port).ToArray()); ports.Text = selected;
        }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, "Não foi possível listar portas: " + ex.Message); }
    }
    // Auxiliares padronizam a montagem da tabela e a coleta dos valores dos controles.
    private void Row(string label, Control control)
    { int row = table.RowCount++; table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new(3,9,12,9) },0,row); table.Controls.Add(control,1,row); }
    private void Check(string label, bool value, Action<bool> setter)
    { var control = new CheckBox { Checked = value, AutoSize = true }; Row(label,control); apply.Add(() => setter(control.Checked)); }
    private void Number(string label, double value, double min, double max, int decimals, Action<double> setter)
    { var control = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, DecimalPlaces = decimals, Value = (decimal)value, Width = 130, Increment = decimals == 0 ? 1 : .1m }; Row(label,control); apply.Add(() => setter((double)control.Value)); }
    private void Choice<T>(string label, T value, Action<T> setter) where T : struct, Enum
    { var control = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, DataSource = Enum.GetValues<T>(), SelectedItem = value }; Row(label,control); apply.Add(() => setter((T)control.SelectedItem!)); }
    private record SensorOption(string Id, string Name) { public override string ToString() => Name; }
}
