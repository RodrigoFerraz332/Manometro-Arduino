using ManometroCPU.Settings;

namespace ManometroCPU.Logging;

public sealed class LogService
{
    private readonly object gate = new();
    public string? LastError { get; private set; }
    public void Write(string message)
    {
        // Serializa gravação e rotação caso duas partes da aplicação registrem eventos juntas.
        lock (gate)
        {
            try
            {
                string dir = Path.Combine(SettingsService.DirectoryPath, "logs");
                Directory.CreateDirectory(dir);
                foreach (string old in Directory.GetFiles(dir, "manometrocpu-*.log"))
                    if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-14)) File.Delete(old);
                string path = Path.Combine(dir, $"manometrocpu-{DateTime.Now:yyyy-MM-dd}.log");
                // Mantém dois segmentos de aproximadamente 2 MB por dia e retenção de 14 dias.
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) File.Move(path, path.Replace(".log", "-previous.log"), true);
                File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = ex.Message; }
        }
    }
}
