using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManometroCPU.Settings;

// Centraliza leitura e gravação do JSON na pasta de dados do usuário, fora do executável.
public static class SettingsService
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ManometroCPU");
    public static string ConfigPath => Path.Combine(DirectoryPath, "config.json");
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    // Cópia profunda: editar a tabela na janela não altera a configuração ativa antes de Salvar.
    public static AppSettings Clone(AppSettings value) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;
    public static AppSettings Load(out string? warning)
    {
        warning = null;
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(ConfigPath)) { var defaults = new AppSettings(); Save(defaults); return defaults; }
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath), JsonOptions) ?? throw new InvalidDataException("JSON vazio");
            settings.Validate(); return settings;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException)
        {
            // Preserva o arquivo inválido para diagnóstico antes de restaurar os padrões.
            File.Copy(ConfigPath, ConfigPath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            warning = "Configuração inválida; cópia preservada. " + ex.Message;
            var defaults = new AppSettings(); Save(defaults); return defaults;
        }
    }
    public static void Save(AppSettings settings)
    {
        settings.Validate(); Directory.CreateDirectory(DirectoryPath);
        // Só substitui o arquivo anterior depois que a escrita do novo JSON termina.
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, ConfigPath, true);
    }
}
