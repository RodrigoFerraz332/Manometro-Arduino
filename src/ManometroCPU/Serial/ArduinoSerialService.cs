using System.IO.Ports;

namespace ManometroCPU.Serial;

// Todos os métodos rodam no trabalhador do controlador, nunca na thread da interface.
public sealed class ArduinoSerialService : IDisposable
{
    private SerialPort? serial;
    public string? Port => serial?.IsOpen == true ? serial.PortName : null;

    public void Connect(string port, int baud, bool verify = true)
    {
        Disconnect();
        serial = new SerialPort(port, baud) { NewLine = "\n", ReadTimeout = 700, WriteTimeout = 700, DtrEnable = true };
        try
        {
            serial.Open();
            Thread.Sleep(2000); // O DTR reinicia o Uno; aguarda o boot sem bloquear a interface.
            serial.DiscardInBuffer();
            if (verify)
            {
                try { Command("VERSION", "VERSION:ManometroCPU:1.0"); }
                catch (TimeoutException)
                {
                    throw new TimeoutException($"{port} encontrada, mas o firmware não respondeu a VERSION. Grave o firmware ManometroCPU ou configure Legacy para o código antigo.");
                }
            }
        }
        catch { Disconnect(); throw; }
    }

    public void Command(string command, string expected = "OK")
    {
        if (serial?.IsOpen != true) throw new IOException("Arduino desconectado");
        // Envia uma linha ASCII e espera a confirmação antes de permitir outro comando.
        serial.WriteLine(command);
        var deadline = Environment.TickCount64 + 1600;
        while (Environment.TickCount64 < deadline)
        {
            string response;
            try { response = serial.ReadLine().Trim(); }
            catch (TimeoutException) { continue; }
            if (response == expected) return;
            if (response.StartsWith("ERR:")) throw new IOException(response);
        }
        throw new TimeoutException("Arduino não confirmou o comando");
    }

    public void SendLegacy(string value)
    {
        // O firmware antigo recebe apenas números e pode não responder; escrita não comprova entrega.
        if (serial?.IsOpen != true) throw new IOException("Arduino desconectado");
        serial.WriteLine(value);
        serial.DiscardInBuffer();
    }
    public void Disconnect() { serial?.Dispose(); serial = null; }
    public void Dispose() => Disconnect();
}
