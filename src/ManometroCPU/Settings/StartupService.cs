using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Principal;
using System.Xml.Linq;

namespace ManometroCPU.Settings;

// Gerencia o início no logon: tarefa elevada para sensores que precisam de administrador.
public static class StartupService
{
    private static string TaskName => "ManometroCPU-" + WindowsIdentity.GetCurrent().User!.Value;
    public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        bool taskExists = RunTaskTool("/Query", "/TN", TaskName) == 0;
        if (!enabled)
        {
            if (taskExists && RunTaskTool("/Delete", "/TN", TaskName, "/F") != 0)
                throw new InvalidOperationException("Abra como administrador para remover a inicialização elevada.");
            key.DeleteValue("ManometroCPU", false); return;
        }
        if (IsAdministrator)
        {
            string sid = WindowsIdentity.GetCurrent().User!.Value;
            // Token interativo usa a sessão do usuário sem guardar senha; não é um serviço.
            // O XML também permite funcionar na bateria e sem limite de duração.
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var xml = new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger", new XElement(ns + "Enabled", true), new XElement(ns + "UserId", sid))),
                new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"), new XElement(ns + "UserId", sid), new XElement(ns + "LogonType", "InteractiveToken"), new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings", new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"), new XElement(ns + "DisallowStartIfOnBatteries", false), new XElement(ns + "StopIfGoingOnBatteries", false), new XElement(ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"), new XElement(ns + "Exec", new XElement(ns + "Command", Environment.ProcessPath!), new XElement(ns + "Arguments", "--minimized")))));
            string path = Path.Combine(SettingsService.DirectoryPath, "startup-task.xml");
            xml.Save(path);
            try { if (RunTaskTool("/Create", "/TN", TaskName, "/XML", path, "/F") != 0) throw new IOException("Falha ao registrar tarefa de inicialização."); }
            finally { File.Delete(path); }
            key.DeleteValue("ManometroCPU", false);
        }
        else if (!taskExists) key.SetValue("ManometroCPU", $"\"{Environment.ProcessPath}\" --minimized");
    }
    private static int RunTaskTool(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe")) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        // Drena as duas saídas para o processo não bloquear por buffer cheio.
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000)) { process.Kill(); throw new TimeoutException("Agendador de tarefas não respondeu."); }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        return process.ExitCode;
    }
}
