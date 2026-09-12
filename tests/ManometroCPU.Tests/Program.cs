using ManometroCPU;
using ManometroCPU.Calibration;
using ManometroCPU.Hardware;
using ManometroCPU.Logging;
using ManometroCPU.Serial;
using ManometroCPU.Settings;
using ManometroCPU.UI;
using System.Globalization;
using System.Text.Json;

// Verificações sem envio serial: cálculos, configurações e renderização das janelas.
// A opção --probe também consulta o sensor real e lista portas, sem controlar a placa.
internal static class Program
{
    private static int passed;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var settings = new AppSettings(); settings.Validate();
            Equal(25, GaugeCalibrationService.Map(-10, settings.Calibration), "Clamp frio");
            Equal(0, GaugeCalibrationService.Map(150, settings.Calibration), "Clamp quente");
            Equal(20, GaugeCalibrationService.Map(45, settings.Calibration), "Interpolação não linear");
            Equal(8, GaugeCalibrationService.Map(75, settings.Calibration), "Ponto exato");
            for (int t = 31; t <= 95; t++)
                Assert(GaugeCalibrationService.Map(t,settings.Calibration) <= GaugeCalibrationService.Map(t-1,settings.Calibration), "Monotonicidade");
            var ema = new TemperatureFilter();
            Equal(40,ema.Update(40,SmoothingMode.Ema,.25,5),"Primeira amostra EMA");
            Equal(50,ema.Update(80,SmoothingMode.Ema,.25,5),"Resposta ao degrau EMA");
            var moving = new TemperatureFilter();
            moving.Update(30,SmoothingMode.MovingAverage,.25,2);
            Equal(40,moving.Update(50,SmoothingMode.MovingAverage,.25,2),"Média móvel");
            Equal(60,moving.Update(70,SmoothingMode.MovingAverage,.25,2),"Descarte amostra antiga");
            Equal(100,moving.Update(100,SmoothingMode.None,.25,2),"Sem suavização");
            Reject(s => s.Calibration = [new(30,25),new(30,0)],"Temperatura duplicada");
            Reject(s => s.Calibration = [new(30,0),new(95,25)],"Resistência crescente");
            Reject(s => s.Calibration = [new(30,256),new(95,0)],"MCP fora de faixa");
            Reject(s => s.TempMax = s.TempMin,"Faixa vazia");
            Reject(s => s.EmaAlpha = double.NaN,"NaN");
            Reject(s => s.SendIntervalMs = 10000,"Intervalo que dispara watchdog");
            Reject(s => {s.AutoPort=false;s.ManualPort="";},"Porta manual vazia");
            var clone = SettingsService.Clone(settings); clone.Calibration.Add(new(100,0));
            Equal(9, settings.Calibration.Count,"Clone isolado");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var json = JsonSerializer.Serialize(settings, SettingsService.JsonOptions);
            var restored = JsonSerializer.Deserialize<AppSettings>(json,SettingsService.JsonOptions)!; restored.Validate();
            Equal(.25, restored.EmaAlpha,"JSON invariável à cultura");
            string root = Path.GetFullPath(Path.Combine("artifacts","validation")); Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root,"config.example.json"),json);
            ApplicationConfiguration.Initialize();
            using var main = new MainForm(false,settings);
            using var configuration = new SettingsForm(SettingsService.Clone(settings), [new("/amdcpu/0/temperature/0","Core (Tctl/Tdie)",47.3)]);
            using var controller = new AppController(SettingsService.Clone(settings),new LogService());
            using var calibration = new CalibrationForm(controller);
            foreach (var pair in new[] {(Form: (Form)main,Name:"main"),(Form:(Form)configuration,Name:"settings"),(Form:(Form)calibration,Name:"test")})
            {
                pair.Form.CreateControl(); pair.Form.PerformLayout();
                using var bitmap = new Bitmap(pair.Form.Width,pair.Form.Height);
                pair.Form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                bitmap.Save(Path.Combine(root,pair.Name+".png"));
                Assert(bitmap.Width > 0,"Render " + pair.Name);
            }
            if (args.Contains("--probe"))
            {
                using var cpu = new CpuTemperatureService();
                var reading = cpu.Read();
                Console.WriteLine("CPU: " + (reading?.ToString() ?? "Sensor indisponível"));
                foreach (var port in ArduinoDiscoveryService.List()) Console.WriteLine("Porta: " + port);
            }
            Console.WriteLine($"PASS: {passed} verificações; telas em {root}"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; }
    private static void Equal(double expected,double actual,string name) => Assert(Math.Abs(expected-actual)<1e-8,name);
    private static void Reject(Action<AppSettings> change,string name)
    {
        var value = new AppSettings(); change(value);
        try { value.Validate(); } catch (ArgumentException) { passed++; return; }
        throw new Exception("FAIL: deveria rejeitar " + name);
    }
}
