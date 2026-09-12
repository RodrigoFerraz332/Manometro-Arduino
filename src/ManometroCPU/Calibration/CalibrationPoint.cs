namespace ManometroCPU.Calibration;
// Um ponto medido da tabela: temperatura em °C e posição digital do cursor entre 0 e 255.
public sealed record CalibrationPoint(double Temperature, int Mcp);
