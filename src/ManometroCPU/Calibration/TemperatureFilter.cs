namespace ManometroCPU.Calibration;

public enum SmoothingMode { None, Ema, MovingAverage }

public sealed class TemperatureFilter
{
    private readonly Queue<double> samples = new();
    private double? ema;
    public double Update(double value, SmoothingMode mode, double alpha, int window)
    {
        // Mantém apenas as últimas N amostras para a média móvel.
        samples.Enqueue(value);
        while (samples.Count > window) samples.Dequeue();
        // A primeira leitura inicializa a EMA. Alfa menor reduz oscilações, mas aumenta o atraso.
        ema = ema is null ? value : alpha * value + (1 - alpha) * ema.Value;
        return mode switch { SmoothingMode.Ema => ema.Value, SmoothingMode.MovingAverage => samples.Average(), _ => value };
    }
}
