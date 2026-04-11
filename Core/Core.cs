using System;
using BeastsV2.Navigation;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using Graphics = ExileCore.Graphics;

namespace BeastsV2;

internal static class Core
{
    private static Navigator _navigator;
    private static string _navigatorAreaHash;

    public static Main Plugin { get; private set; }

    public static GameController GameController => Plugin?.GameController;

    public static Settings Settings => Plugin?.Settings;

    public static Graphics Graphics => Plugin?.Graphics;

    public static AreaInstance CurrentArea { get; private set; }

    public static bool IsReady => Plugin?.GameController != null;

    public static void Initialize(Main plugin)
    {
        Plugin = plugin;
        CurrentArea = plugin.GameController?.Area?.CurrentArea;
        _navigator = null;
        _navigatorAreaHash = null;
    }

    public static void AreaChanged(AreaInstance area)
    {
        CurrentArea = area;
        _navigator = null;
        _navigatorAreaHash = null;
    }

    public static Navigator GetNavigator()
    {
        var gameController = GameController;
        if (gameController?.IngameState?.Data?.Terrain == null)
        {
            return null;
        }

        var areaHash = BeastsV2Helpers.TryGetAreaHashText(CurrentArea ?? gameController.Area?.CurrentArea) ?? string.Empty;
        if (_navigator != null && string.Equals(_navigatorAreaHash, areaHash, StringComparison.Ordinal))
        {
            return _navigator;
        }

        _navigator = new Navigator(gameController);
        _navigatorAreaHash = areaHash;
        return _navigator;
    }

    public static void Shutdown()
    {
        _navigator = null;
        _navigatorAreaHash = null;
        CurrentArea = null;
        Plugin = null;
    }
}
