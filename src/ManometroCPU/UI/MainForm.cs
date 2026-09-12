using ManometroCPU.Logging;
using ManometroCPU.Settings;
namespace ManometroCPU.UI;
// Janela principal e bandeja: apresentam o estado, enquanto AppController controla o hardware.
public sealed class MainForm : Form
{
    private readonly Label temperature = new() { Font = new("Segoe UI", 38, FontStyle.Bold), AutoSize = true, Text = "— °C" };
    private readonly Label sensor = new() { AutoSize = true };
    private readonly Label connection = new() { AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(470, 0) };
    private readonly Button connect = new() { Text = "Desconectar", AutoSize = true };
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Visible = true, Text = "Manometro CPU" };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly AppController controller;
    private bool enabled = true, exiting, stopped;
    public MainForm(bool minimized, AppSettings? initialSettings = null)
    {
        Text = "Manometro CPU"; ClientSize = new(550, 350); MinimumSize = new(560, 390);
        Font = new("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 250); StartPosition = FormStartPosition.CenterScreen;
        var log = new LogService();
        string? warning = null;
        var settings = initialSettings ?? SettingsService.Load(out warning);
        if (warning != null) log.Write(warning);
        controller = new(settings, log);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(22), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Controls.Add(panel);
        panel.Controls.Add(new Label { Text = "TEMPERATURA DA CPU", AutoSize = true, ForeColor = Color.DimGray });
        panel.Controls.Add(temperature); panel.Controls.Add(sensor); panel.Controls.Add(connection); panel.Controls.Add(status);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new(0, 18, 0, 0) };
        panel.Controls.Add(buttons); buttons.Controls.Add(connect);
        connect.Click += (_, _) => { enabled = !enabled; controller.SetConnected(enabled); };
        AddButton(buttons, "Configurações", ShowSettings);
        AddButton(buttons, "Testar manômetro", () => { using var form = new CalibrationForm(controller); form.ShowDialog(this); });
        var minimize = new CheckBox { Text = "Minimizar para bandeja", Checked = settings.MinimizeToTray, AutoSize = true };
        panel.Controls.Add(minimize);
        minimize.CheckedChanged += async (_, _) =>
        {
            var s = controller.Settings; s.MinimizeToTray = minimize.Checked;
            try { await controller.Apply(s); } catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar: " + ex.Message); }
        };
        // A bandeja mantém acesso ao aplicativo mesmo quando a janela está oculta.
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowWindow());
        menu.Items.Add("Mostrar temperatura", null, (_, _) => { tray.BalloonTipTitle = "Manometro CPU"; tray.BalloonTipText = temperature.Text + "\n" + sensor.Text; tray.ShowBalloonTip(3000); });
        menu.Items.Add("Conectar Arduino", null, (_, _) => { enabled = true; controller.SetConnected(true); });
        menu.Items.Add("Configurações", null, (_, _) => { ShowWindow(); ShowSettings(); });
        menu.Items.Add("Sair", null, (_, _) => { exiting = true; Close(); });
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && controller.Settings.MinimizeToTray) Hide(); };
        Shown += (_, _) => { controller.Start(); timer.Start(); if (minimized) { WindowState = FormWindowState.Minimized; Hide(); } if (warning != null) MessageBox.Show(this, warning); };
        timer.Tick += (_, _) => RefreshState();
        FormClosing += OnClosing;
    }
    private static void AddButton(Control parent, string text, Action action)
    { var b = new Button { Text = text, AutoSize = true }; b.Click += (_, _) => action(); parent.Controls.Add(b); }
    private async void ShowSettings()
    {
        using var form = new SettingsForm(controller.Settings, controller.State.Sensors);
        if (form.ShowDialog(this) == DialogResult.OK)
            try { await controller.Apply(form.Value); } catch (Exception ex) { MessageBox.Show(this, "Configurações não aplicadas: " + ex.Message); }
    }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void RefreshState()
    {
        // O timer atualiza apenas textos e cores; nenhuma operação serial ocorre aqui.
        var s = controller.State;
        temperature.Text = s.Temperature is double t ? $"{t:F1} °C" : "— °C";
        sensor.Text = "Sensor: " + s.Sensor;
        connection.Text = s.Port is null ? "● Arduino: desconectado" : "● Arduino: conectado  |  " + s.Port;
        connection.ForeColor = s.Port is null ? Color.Firebrick : Color.SeaGreen;
        status.Text = "Status: " + s.Status; connect.Text = enabled ? "Desconectar" : "Conectar";
        tray.Text = $"CPU: {(s.Temperature is double temp ? temp.ToString("F0") : "—")} °C | Arduino: {(s.Port is null ? "OFF" : "OK")}";
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        // Fechar pelo X pode somente ocultar. Sair aguarda o trabalhador liberar os recursos.
        if (stopped) return;
        if (!exiting && e.CloseReason == CloseReason.UserClosing && controller.Settings.MinimizeToTray) { e.Cancel = true; Hide(); return; }
        e.Cancel = true;
        if (exiting && !Enabled) return;
        exiting = true; Enabled = false; timer.Stop(); controller.Stop();
        if (controller.Worker != null) await controller.Worker;
        tray.Visible = false; tray.Dispose(); timer.Dispose(); controller.Dispose(); stopped = true; Close();
    }
}
