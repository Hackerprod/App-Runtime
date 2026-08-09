using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core;

public enum AndroidActivityState
{
    Constructed,
    Created,
    Started,
    Resumed,
    Paused,
    Stopped,
    Destroyed,
    Faulted
}

/// <summary>Owns one constructed Activity and its bounded forward lifecycle state machine.</summary>
public sealed class AndroidActivitySession
{
    private readonly DexInterpreter _interpreter;
    private AndroidActivityState _checkpoint = AndroidActivityState.Constructed;

    public AndroidActivitySession(DexInterpreter interpreter, DexObject constructedActivity)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        Activity = constructedActivity ?? throw new ArgumentNullException(nameof(constructedActivity));
    }

    public DexObject Activity { get; }
    public AndroidActivityState State { get; private set; } = AndroidActivityState.Constructed;

    public void Create() => Transition(
        AndroidActivityState.Constructed,
        AndroidActivityState.Created,
        "onCreate",
        "(Landroid/os/Bundle;)V",
        new object[] { null! });

    public void Start() => Transition(
        AndroidActivityState.Created,
        AndroidActivityState.Started,
        "onStart",
        "()V",
        Array.Empty<object>());

    public void Resume() => Transition(
        AndroidActivityState.Started,
        AndroidActivityState.Resumed,
        "onResume",
        "()V",
        Array.Empty<object>());

    public void Pause() => Transition(AndroidActivityState.Resumed, AndroidActivityState.Paused, "onPause", "()V", Array.Empty<object>());

    public void Stop() => Transition(AndroidActivityState.Paused, AndroidActivityState.Stopped, "onStop", "()V", Array.Empty<object>());

    public void Destroy() => Transition(AndroidActivityState.Stopped, AndroidActivityState.Destroyed, "onDestroy", "()V", Array.Empty<object>());

    public void Terminate()
    {
        if (State == AndroidActivityState.Destroyed) return;
        switch (_checkpoint)
        {
            case AndroidActivityState.Resumed: Pause(); goto case AndroidActivityState.Paused;
            case AndroidActivityState.Paused: Stop(); goto case AndroidActivityState.Stopped;
            case AndroidActivityState.Started: Transition(AndroidActivityState.Started, AndroidActivityState.Stopped, "onStop", "()V", Array.Empty<object>()); goto case AndroidActivityState.Stopped;
            case AndroidActivityState.Stopped: Destroy(); break;
            case AndroidActivityState.Created: Transition(AndroidActivityState.Created, AndroidActivityState.Destroyed, "onDestroy", "()V", Array.Empty<object>()); break;
            case AndroidActivityState.Constructed: State = AndroidActivityState.Destroyed; _checkpoint = AndroidActivityState.Destroyed; break;
            case AndroidActivityState.Destroyed: State = AndroidActivityState.Destroyed; break;
        }
    }

    private void Transition(
        AndroidActivityState requiredState,
        AndroidActivityState completedState,
        string methodName,
        string methodDescriptor,
        object[] arguments)
    {
        if (State != requiredState)
            throw new InvalidOperationException(
                $"Cannot execute {methodName}{methodDescriptor} while Activity session is {State}; expected {requiredState}.");

        try
        {
            _interpreter.InvokeActivityLifecycleExact(Activity, methodName, methodDescriptor, arguments);
            State = completedState;
            _checkpoint = completedState;
        }
        catch
        {
            State = AndroidActivityState.Faulted;
            throw;
        }
    }
}
