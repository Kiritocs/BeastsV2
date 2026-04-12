using System;
using System.Collections.Generic;
using System.IO;

namespace BeastsV2.Runtime.Analytics;

internal sealed class AnalyticsPersistenceState
{
    public HashSet<string> LoadedSaveIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SavedSessionDataV2> LoadedSaveCacheById { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SessionStoreV2 SessionStore { get; } = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "BeastsV2Sessions"));

    public SessionStoreV2 AutoSaveSessionStore { get; } = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "BeastsV2Sessions", "AutoSaves"));
}