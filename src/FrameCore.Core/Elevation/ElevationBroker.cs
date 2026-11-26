using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FrameCore.Core.Engine;

namespace FrameCore.Core.Elevation;

/// <summary>
/// UI side of the privilege separation. Starts "FrameCore.exe --worker &lt;pipe&gt; &lt;pid&gt;" through UAC only when an
/// operation needs it, owns the pipe server (ACL: current user + Administrators) and checks that the
/// connected client is exactly the process it launched.
/// </summary>
public sealed class ElevationBroker : IPrivilegedChannel, IDisposable
{
    private readonly object _gate = new();
    private NamedPipeServerStream? _pipe;
    private Process? _worker;
    private int _nextId;

    public event EventHandler? StateChanged;

    /// <summary>Called before launching UAC so the UI can explain why rights are requested.</summary>
    public Func<bool>? ConfirmElevation { get; set; }

    public bool IsConnected => _pipe is { IsConnected: true } && _worker is { HasExited: false };

    /// <summary>The channel is considered available when connected or when the process is already elevated.</summary>
    public bool IsAvailable => IsConnected || AppInfo.IsElevated;

    public bool EnsureStarted()
    {
        lock (_gate)
        {
            if (IsConnected) return true;
            Cleanup();
            if (ConfirmElevation != null && !ConfirmElevation()) throw new ElevationDeclinedException();

            var pipeName = "FrameCore-" + Guid.NewGuid().ToString("N");
            var security = new PipeSecurity();
            using (var me = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(me.User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.ReadWrite, AccessControlType.Allow));

            _pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);

            try
            {
                var psi = new ProcessStartInfo(AppInfo.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = $"--worker {pipeName} {Environment.ProcessId}",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                _worker = Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC declined
            {
                Cleanup();
                throw new ElevationDeclinedException();
            }
            if (_worker == null) { Cleanup(); throw new InvalidOperationException("Worker did not start"); }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { _pipe.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { Cleanup(); throw new TimeoutException("Elevated worker did not connect"); }

            if (!GetNamedPipeClientProcessId(_pipe.SafePipeHandle, out var clientPid) || clientPid != (uint)_worker.Id)
            {
                Log.Error("Elevation", $"Unexpected pipe client {clientPid} (expected {_worker.Id}); closing");
                Cleanup();
                throw new UnauthorizedAccessException("Unexpected pipe client");
            }
            Log.Info("Elevation", $"Elevated worker connected (pid {_worker.Id})");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }

    public T Send<T>(string operation, object payload, TimeSpan? timeout = null)
    {
        lock (_gate)
        {
            EnsureStarted();
            var request = new PipeRequest
            {
                Id = Interlocked.Increment(ref _nextId),
                Op = operation,
                Payload = PipeProtocol.ToElement(payload),
                UserSid = WindowsIdentity.GetCurrent().User?.Value,
            };
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(5));
            try
            {
                PipeProtocol.WriteAsync(_pipe!, request, cts.Token).GetAwaiter().GetResult();
                var response = PipeProtocol.ReadAsync<PipeResponse>(_pipe!, cts.Token).GetAwaiter().GetResult()
                               ?? throw new EndOfStreamException("Worker closed the channel");
                if (response.Id != request.Id) throw new InvalidDataException("Out-of-order response");
                if (!response.Ok) throw new PrivilegedOperationException(response.Error ?? "Privileged operation failed", response.ErrorType);
                return PipeProtocol.FromElement<T>(response.Payload)!;
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException)
            {
                Log.Error("Elevation", $"Channel failure during {operation}", ex);
                Cleanup();
                throw;
            }
        }
    }

    private void Cleanup()
    {
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
        _worker = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (IsConnected)
            {
                try
                {
                    var req = new PipeRequest { Id = ++_nextId, Op = "shutdown", Payload = PipeProtocol.ToElement(new { }) };
                    PipeProtocol.WriteAsync(_pipe!, req).Wait(TimeSpan.FromSeconds(2));
                }
                catch { }
            }
            Cleanup();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);
}
