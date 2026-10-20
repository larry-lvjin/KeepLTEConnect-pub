using System.IO.Ports;
using System.Text.Json;

namespace KeepLTEConnect.Protocol;

public enum SerialProtocol { Auto, Uart, Usb }

public sealed record CommandResult(string Command, bool Acked, string? Error);

/// <summary>Serial channel to SIM Simulator. Opens, handshakes, sends shell commands.</summary>
public sealed class SimSerialClient : IDisposable
{
    private readonly SerialPort _port;
    public string PortName => _port.PortName;
    public bool UseUsb { get; private set; }

    private SimSerialClient(SerialPort port, bool useUsb)
    {
        _port = port;
        UseUsb = useUsb;
    }

    public static async Task<SimSerialClient?> OpenAsync(string portName, SerialProtocol prefer, CancellationToken ct)
    {
        // 实现细节未公开（算法核心）
        throw new NotImplementedException("该方法的实现不在开源范围内");
    }

    public static async Task<SimSerialClient?> AutoOpenAsync(SerialProtocol prefer, CancellationToken ct)
    {
        // 实现细节未公开（算法核心）
        throw new NotImplementedException("该方法的实现不在开源范围内");
    }

    private static async Task<bool?> HandshakeAutoAsync(SerialPort sp, CancellationToken ct)
    {
        // 实现细节未公开（算法核心）
        throw new NotImplementedException("该方法的实现不在开源范围内");
    }

    private static async Task<bool> TryHandshakeAsync(SerialPort sp, bool useUsb, CancellationToken ct)
    {
        // 实现细节未公开（算法核心）
        throw new NotImplementedException("该方法的实现不在开源范围内");
    }

    public async Task<CommandResult> SendShellAsync(string command, CancellationToken ct)
    {
        // 实现细节未公开（算法核心）
        throw new NotImplementedException("该方法的实现不在开源范围内");
    }

    private static async Task<JsonDocument?> SendAndWaitInternalAsync(
        SerialPort sp, byte[] frame, bool useUsb, TimeSpan timeout, CancellationToken ct)
    {
        // 实现细节未公开（算法核心）
        throw new NotImplementedException("该方法的实现不在开源范围内");
    }

    public void Dispose()
    {
        // 实现细节未公开（算法核心）
        throw new NotImplementedException("该方法的实现不在开源范围内");
    }
}
