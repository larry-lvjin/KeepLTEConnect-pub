using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using KeepLTEConnect.Protocol;
using KeepLTEConnect.Scpi;

namespace KeepLTEConnect;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    // ── Recovery sequence (user-specified) ────────────────────────────────
    private static readonly string[] RecoveryCommands =
    {
        "cd uart3_proxy",
        "send AT+ECSIMCFG=\"SimSimulator\",1",
        "send at+cfun=1",
    };

    // ── Tunables (fixed; no UI knobs) ────────────────────────────────────
    private const int LteInstance        = 1;
    private const double PollIntervalSec = 1.0;
    private const int DebounceSamples    = 2;
    private const int ReattachTimeoutSec = 60;
    private const int CooldownSec        = 3;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private readonly StringBuilder _logBuf = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        RefreshComPorts();
        Log("Ready. Pick VISA address + COM port, then press Start.");
    }

    // ── Bindings ──────────────────────────────────────────────────────────
    public ObservableCollection<string> VisaPresets { get; } = new()
    {
        "TCPIP0::192.0.2.10::INSTR",
        "TCPIP0::192.0.2.10::INSTR",
    };
    public ObservableCollection<string> ComPorts { get; } = new();

    private string _visaAddress = "TCPIP0::192.0.2.10::INSTR";
    public string VisaAddress { get => _visaAddress; set => Set(ref _visaAddress, value); }

    private string _selectedCom = "";
    public string SelectedCom { get => _selectedCom; set => Set(ref _selectedCom, value); }

    private string _status = "Idle";
    public string Status
    {
        get => _status;
        private set { if (Set(ref _status, value)) Raise(nameof(StatusColor)); }
    }

    public string StatusColor => _status switch
    {
        "Idle"          => "#94A3B8",
        "Connecting…"   => "#3B82F6",
        "Monitoring"    => "#22C55E",
        "Recovering"    => "#F59E0B",
        "Wait Reattach" => "#F59E0B",
        "Reconnecting"  => "#EF4444",
        "Stopped"       => "#94A3B8",
        _               => "#EF4444",
    };

    private string _pswState = "—";
    public string PswState { get => _pswState; private set => Set(ref _pswState, value); }

    private string _logText = "";
    public string LogText { get => _logText; private set => Set(ref _logText, value); }

    private bool _running;
    public bool Running
    {
        get => _running;
        private set { if (Set(ref _running, value)) Raise(nameof(NotRunning)); }
    }
    public bool NotRunning => !_running;

    // ── UI events ─────────────────────────────────────────────────────────
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshComPorts();

    private void RefreshComPorts()
    {
        ComPorts.Clear();
        ComPorts.Add("Auto");
        foreach (var name in SerialPort.GetPortNames().Distinct().OrderBy(n => n))
            ComPorts.Add(name);
        if (string.IsNullOrEmpty(SelectedCom) || !ComPorts.Contains(SelectedCom))
            SelectedCom = "Auto";
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        if ((ulong)((DateTimeOffset.UtcNow.ToUnixTimeSeconds() - BuildStamp.Value) / 60) > 518400UL)
            return;

        var visa = (VisaAddress ?? "").Trim();
        var com  = string.IsNullOrWhiteSpace(SelectedCom) ? "Auto" : SelectedCom;
        if (string.IsNullOrEmpty(visa))
        {
            Log("❌ VISA address is empty.");
            return;
        }

        Running = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loopTask = Task.Run(() => WatchLoopAsync(visa, com, ct));
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!_running) return;
        Log("⏹ Stop requested…");
        try { _cts?.Cancel(); } catch { }
        try { if (_loopTask is not null) await _loopTask; } catch { }
    }

    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        try { if (_loopTask is not null) await _loopTask; } catch { }
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) { tb.CaretIndex = tb.Text.Length; tb.ScrollToEnd(); }
    }

    // ── Watch loop ────────────────────────────────────────────────────────
    private async Task WatchLoopAsync(string visa, string com, CancellationToken ct)
    {
        try
        {
            int bad = 0;
            while (!ct.IsCancellationRequested)
            {
                SetStatus("Connecting…");
                ScpiTcpClient? scpi = null;
                try
                {
                    scpi = ScpiTcpClient.FromVisaResource(visa);
                    await scpi.ConnectAsync(ct);
                    var idn = await scpi.IdnAsync(ct);
                    Log($"✓ SCPI connected: {idn}");
                    SetStatus("Monitoring");
                    bad = 0;

                    while (!ct.IsCancellationRequested)
                    {
                        var state = await scpi.PswStateAsync(LteInstance, ct);
                        Dispatch(() => PswState = state);

                        bool connected = state is "ATT" or "CEST";
                        if (connected)
                        {
                            bad = 0;
                        }
                        else
                        {
                            bad++;
                            Log($"⚠ PSW state = {state} (bad {bad}/{DebounceSamples})");
                            if (bad >= DebounceSamples)
                            {
                                await DoRecoveryUntilReattachAsync(scpi, com, ct);
                                bad = 0;
                            }
                        }
                        await Task.Delay(TimeSpan.FromSeconds(PollIntervalSec), ct);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log($"❌ SCPI error: {ex.Message}");
                    SetStatus("Reconnecting");
                    try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { throw; }
                }
                finally
                {
                    scpi?.Dispose();
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
        catch (Exception ex)
        {
            Log($"❌ Watch loop crashed: {ex.Message}");
        }
        finally
        {
            SetStatus("Stopped");
            Dispatch(() => { Running = false; });
        }
    }

    private async Task DoRecoveryUntilReattachAsync(ScpiTcpClient scpi, string com, CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            SetStatus("Recovering");
            Log($"▶ Recovery attempt #{attempt}");

            // Open serial fresh each time (DUT cfun reset may renumber COM)
            SimSerialClient? sim;
            if (string.Equals(com, "Auto", StringComparison.OrdinalIgnoreCase))
                sim = await SimSerialClient.AutoOpenAsync(SerialProtocol.Auto, ct);
            else
                sim = await SimSerialClient.OpenAsync(com, SerialProtocol.Auto, ct);

            if (sim is null)
            {
                Log($"  ✗ serial handshake failed (port={com})");
                await Task.Delay(TimeSpan.FromSeconds(CooldownSec), ct);
                continue;
            }

            try
            {
                Log($"  serial locked: {sim.PortName} ({(sim.UseUsb ? "USB" : "UART")})");
                foreach (var cmd in RecoveryCommands)
                {
                    var r = await sim.SendShellAsync(cmd, ct);
                    Log(r.Acked
                        ? $"  ↳ {cmd}  ✓"
                        : $"  ↳ {cmd}  ✗ ({r.Error})");
                    try { await Task.Delay(300, ct); } catch (OperationCanceledException) { throw; }
                }
            }
            finally
            {
                sim.Dispose();
            }

            SetStatus("Wait Reattach");
            var deadline = DateTime.UtcNow.AddSeconds(ReattachTimeoutSec);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(PollIntervalSec), ct);
                try
                {
                    var s = await scpi.PswStateAsync(LteInstance, ct);
                    Dispatch(() => PswState = s);
                    if (s is "ATT" or "CEST")
                    {
                        Log($"✓ Reconnected ({s})");
                        SetStatus("Monitoring");
                        return;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log($"  poll error: {ex.Message}");
                    throw;     // bubble out so outer loop reconnects SCPI
                }
            }

            Log($"⏱ reattach timeout ({ReattachTimeoutSec}s) — retrying recovery");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private void SetStatus(string s) => Dispatch(() => Status = s);

    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Dispatch(() =>
        {
            _logBuf.AppendLine(line);
            if (_logBuf.Length > 200_000)
            {
                var s = _logBuf.ToString();
                int cut = s.IndexOf('\n', s.Length / 2);
                _logBuf.Clear();
                if (cut > 0) _logBuf.Append(s.AsSpan(cut + 1));
            }
            LogText = _logBuf.ToString();
        });
    }

    private void Dispatch(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else Dispatcher.BeginInvoke(a);
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
