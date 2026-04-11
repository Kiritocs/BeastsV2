using System;

namespace BeastsV2.Runtime.Analytics;

internal sealed record AnalyticsWebServerCoordinatorCallbacks(
    Func<DateTime, SessionCurrentResponseV2> BuildSnapshot,
    Func<AnalyticsWebServer> GetServer,
    Action<AnalyticsWebServer> SetServer,
    Func<SessionCurrentResponseV2> GetLatestSnapshot,
    Action<SessionCurrentResponseV2> SetLatestSnapshot,
    Func<bool> GetEnabled,
    Func<int> GetPort,
    Func<bool> GetAllowNetwork,
    Func<int, bool, AnalyticsWebServer> CreateServer,
    Func<int> GetCurrentPort,
    Action<int> SetCurrentPort,
    Func<bool> GetCurrentAllowNetwork,
    Action<bool> SetCurrentAllowNetwork,
    Action<string> LogDebug,
    Action<string, Exception> LogError);

internal sealed class AnalyticsWebServerCoordinator
{
    private readonly AnalyticsWebServerCoordinatorCallbacks _callbacks;

    public AnalyticsWebServerCoordinator(AnalyticsWebServerCoordinatorCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void RefreshSnapshot(DateTime now)
    {
        _callbacks.SetLatestSnapshot(_callbacks.BuildSnapshot(now));
    }

    public void EnsureServerState()
    {
        if (!_callbacks.GetEnabled())
        {
            if (_callbacks.GetServer()?.IsRunning == true)
            {
                _callbacks.GetServer().Stop();
            }

            return;
        }

        var server = _callbacks.GetServer();
        if (server == null)
        {
            server = _callbacks.CreateServer(_callbacks.GetPort(), _callbacks.GetAllowNetwork());
            _callbacks.SetServer(server);
        }

        var targetPort = _callbacks.GetPort();
        var allowNetwork = _callbacks.GetAllowNetwork();
        if (server.IsRunning &&
            _callbacks.GetCurrentPort() == targetPort &&
            _callbacks.GetCurrentAllowNetwork() == allowNetwork)
        {
            return;
        }

        try
        {
            server.Start(targetPort, allowNetwork);
            _callbacks.SetCurrentPort(targetPort);
            _callbacks.SetCurrentAllowNetwork(allowNetwork);
        }
        catch (Exception ex)
        {
            _callbacks.LogError("Failed to start analytics web server", ex);
        }
    }

    public string GetServerUrl()
    {
        var server = _callbacks.GetServer();
        return server?.IsRunning == true
            ? server.Url
            : $"http://localhost:{_callbacks.GetPort()}/";
    }

    public void DisposeServer()
    {
        _callbacks.GetServer()?.Dispose();
        _callbacks.SetServer(null);
    }
}