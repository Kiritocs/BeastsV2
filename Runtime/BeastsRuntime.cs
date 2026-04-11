using System;
using BeastsV2.Runtime.Lifecycle;
using BeastsV2.Runtime.State;

namespace BeastsV2.Runtime;

internal sealed class BeastsRuntime
{
    private readonly Main _plugin;

    public BeastsRuntime(Main plugin)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        State = new BeastsRuntimeState();
        AreaTransitions = new AreaTransitionCoordinator(State);
    }

    public BeastsRuntimeState State { get; }

    public AreaTransitionCoordinator AreaTransitions { get; }

    public void Initialize(DateTime now, MainSettingsBindingTargets bindingTargets)
    {
        State.Session.SessionStartUtc = now;
        MainSettingsBindings.Bind(_plugin.Settings, bindingTargets);
    }

    public void Shutdown()
    {
        State.Automation.CancellationTokenSource?.Dispose();
        State.Automation.CancellationTokenSource = null;
    }
}