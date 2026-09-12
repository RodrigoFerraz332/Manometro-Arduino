using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using ManometroCPU.Settings;

namespace ManometroCPU.Hardware;

// Preparação inicial do PawnIO, driver usado pela biblioteca para ler sensores de baixo nível.
public static class DriverSetupService
{
    public static bool Installed
    {
        get
        {
            using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return Version.TryParse(key?.GetValue("DisplayVersion")?.ToString(), out var version) && version >= new Version(2,0);
        }
    }
    public static async Task Install()
    {
        // Extrai o instalador oficial incorporado em uma pasta exclusiva para esta execução.
        // A instalação só ocorre pelo botão da interface e pode solicitar UAC ao usuário.
        string directory = Path.Combine(SettingsService.DirectoryPath,"setup",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,"PawnIO_setup.exe");
        try
        {
            await using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ManometroCPU.Resources.PawnIO_setup.exe") ?? throw new FileNotFoundException("Instalador integrado ausente"))
            await using (var file = File.Create(path)) await stream.CopyToAsync(file);
            using var process = Process.Start(new ProcessStartInfo(path, "-install") { UseShellExecute = true, Verb = "runas" }) ?? throw new IOException("Instalador não iniciou");
            // Aguarda sem congelar a janela; 3010 indica sucesso com reinicialização necessária.
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 && process.ExitCode != 3010) throw new IOException("Instalador retornou código " + process.ExitCode);
        }
        finally { try { File.Delete(path); Directory.Delete(directory); } catch (IOException) { } }
    }
}
