namespace ManometroCPU.UI;

// Permite medir pontos da calibração usando temperatura simulada ou posição MCP explícita.
public sealed class CalibrationForm : Form
{
    public CalibrationForm(AppController controller)
    {
        Text = "Testar manômetro"; Size = new(560, 360); Font = new("Segoe UI",10); StartPosition = FormStartPosition.CenterParent;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), FlowDirection = FlowDirection.TopDown, WrapContents = false }; Controls.Add(panel);
        panel.Controls.Add(new Label { Text = "O teste substitui a saída da CPU até esta janela fechar.", AutoSize = true });
        var temperature = new NumericUpDown { Minimum = 0, Maximum = 125, DecimalPlaces = 1, Value = 50, Width = 120 }; panel.Controls.Add(temperature);
        var send = new Button { Text = "Enviar temperatura simulada", AutoSize = true }; panel.Controls.Add(send);
        send.Click += (_, _) => controller.Test((double)temperature.Value, null);
        var quick = new FlowLayoutPanel { AutoSize = true }; panel.Controls.Add(quick);
        foreach (int value in new[] {30,40,50,60,70,80,90})
        { var b = new Button { Text = value + " °C", Width = 64 }; b.Click += (_, _) => { temperature.Value = value; controller.Test(value,null); }; quick.Controls.Add(b); }
        var mcp = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 25, Width = 120 }; panel.Controls.Add(mcp);
        var direct = new Button { Text = "Enviar MCP direto (0–255)", AutoSize = true }; panel.Controls.Add(direct);
        // Firmware antigo não entende MCP:; evita enviar comandos incompatíveis nesse modo.
        direct.Enabled = controller.Settings.Output is not (Settings.OutputMode.LegacyDecimal or Settings.OutputMode.LegacyInteger);
        direct.Click += (_, _) => controller.Test(null,(int)mcp.Value);
        var status = new Label { AutoSize = true, MaximumSize = new(500,0) }; panel.Controls.Add(status);
        var timer = new System.Windows.Forms.Timer { Interval = 500 }; timer.Tick += (_, _) => status.Text = controller.State.Status; timer.Start();
        // Ao sair do teste, retoma a saída calculada a partir da temperatura real da CPU.
        FormClosed += (_, _) => { timer.Dispose(); controller.EndTest(); };
    }
}
