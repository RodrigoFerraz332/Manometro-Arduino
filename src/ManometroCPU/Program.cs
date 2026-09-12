using ManometroCPU.UI;

namespace ManometroCPU;

internal static class Program
{
    // WinForms utiliza uma thread STA para os componentes da interface do Windows.
    [STAThread]
    static void Main(string[] args)
    {
        // Impede duas instâncias na mesma sessão disputando a porta serial.
        using var mutex = new Mutex(true, @"Local\ManometroCPU", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        try { Application.Run(new MainForm(args.Contains("--minimized"))); }
        catch (Exception ex)
        {
            new Logging.LogService().Write("Falha fatal: " + ex);
            MessageBox.Show("Não foi possível executar o Manometro CPU.\n" + ex.Message, "Manometro CPU", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
