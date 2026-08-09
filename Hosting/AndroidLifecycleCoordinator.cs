#nullable enable
using AndroidRuntime.Core.ApiLayer;

namespace AndroidRuntime.Core.Hosting;

internal static class AndroidLifecycleCoordinator
{
    internal static bool RunForward(AndroidActivitySession session, AndroidFrameworkState state, IActivityWindow window, CancellationToken cancellationToken)
    {
        session.Create();
        if (state.IsFinishing) return false;
        session.Start();
        if (state.IsFinishing) return false;
        window.Show(cancellationToken);
        session.Resume();
        return !state.IsFinishing;
    }
}
