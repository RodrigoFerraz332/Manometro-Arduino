namespace ManometroCPU.Calibration;

public static class GaugeCalibrationService
{
    public static int Map(double temperature, IReadOnlyList<CalibrationPoint> points)
    {
        // Recebe a tabela já validada e ordenada por AppSettings.Validate.
        // Fora da tabela, mantém a posição do extremo em vez de extrapolar a resistência.
        if (temperature <= points[0].Temperature) return points[0].Mcp;
        for (int i = 1; i < points.Count; i++)
        {
            if (temperature > points[i].Temperature) continue;
            var a = points[i - 1]; var b = points[i];
            // t representa a fração entre os dois pontos vizinhos (0 = a; 1 = b).
            // Cada trecho tem sua inclinação, permitindo aproximar um manômetro não linear.
            double t = (temperature - a.Temperature) / (b.Temperature - a.Temperature);
            return Math.Clamp((int)Math.Round(a.Mcp + t * (b.Mcp - a.Mcp), MidpointRounding.AwayFromZero), 0, 255);
        }
        return points[^1].Mcp;
    }
}
