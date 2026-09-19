using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IptvRecorder;

/// <summary>
/// Evita que una grabación se pierda porque el equipo se suspende.
///
/// Mientras hay una grabación en curso se le pide a Windows que no suspenda el sistema
/// (la pantalla sí puede apagarse). Para las grabaciones programadas se arma además un
/// temporizador capaz de despertar el equipo unos minutos antes de la hora.
/// </summary>
public static class PowerManager
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
        AwayModeRequired = 0x00000040,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(
        IntPtr attributes, string? name, uint flags, uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr completionRoutine, IntPtr argToCompletionRoutine,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelWaitableTimer(SafeWaitHandle timer);

    private const uint CreateWaitableTimerManualReset = 0x1;
    private const uint TimerAllAccess = 0x1F0003;

    private static bool _awake;
    private static SafeWaitHandle? _timer;
    private static DateTime _wakeAt = DateTime.MinValue;

    /// <summary>
    /// Impide o vuelve a permitir la suspensión del sistema. Debe llamarse siempre desde
    /// el mismo hilo (el de la interfaz): la petición va asociada al hilo que la hace.
    /// </summary>
    public static void KeepAwake(bool on)
    {
        if (on == _awake) return;

        if (on)
        {
            // "Away mode" mantiene el equipo trabajando con la pantalla apagada. No todos
            // los equipos lo admiten, así que si falla se pide solo no suspender.
            var r = SetThreadExecutionState(
                ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.AwayModeRequired);
            if (r == 0)
                r = SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);
            _awake = r != 0;
        }
        else
        {
            SetThreadExecutionState(ExecutionState.Continuous);
            _awake = false;
        }
    }

    /// <summary>
    /// Programa el despertar del equipo a la hora indicada. Devuelve false si Windows no
    /// admite el temporizador, por ejemplo con los temporizadores de reactivación
    /// desactivados en el plan de energía.
    /// </summary>
    public static bool ScheduleWake(DateTime when)
    {
        if (when <= DateTime.Now)
        {
            CancelWake();
            return false;
        }

        // Ya armado para esa misma hora: no hace falta rehacerlo.
        if (_timer is { IsInvalid: false, IsClosed: false } && _wakeAt == when) return true;

        CancelWake();

        SafeWaitHandle handle;
        try
        {
            handle = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerManualReset, TimerAllAccess);
        }
        catch { return false; }

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return false;
        }

        var due = when.ToFileTime();
        if (!SetWaitableTimer(handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, resume: true))
        {
            handle.Dispose();
            return false;
        }

        _timer = handle;
        _wakeAt = when;
        return true;
    }

    public static void CancelWake()
    {
        if (_timer == null) return;
        try { CancelWaitableTimer(_timer); } catch { }
        try { _timer.Dispose(); } catch { }
        _timer = null;
        _wakeAt = DateTime.MinValue;
    }

    /// <summary>Devuelve todo a su estado normal. Se llama al salir de la aplicación.</summary>
    public static void Release()
    {
        KeepAwake(false);
        CancelWake();
    }
}
