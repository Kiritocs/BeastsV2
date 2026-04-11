using System;
using ImGuiNET;

namespace BeastsV2;

public partial class Main
{
    private void ApplyPauseMenuTimerState(DateTime now)
    {
        var pauseMenuOpen = IsPauseMenuOpen();

        if (pauseMenuOpen)
        {
            _pauseMenuSessionStartUtc ??= now;
            if (_isCurrentAreaTrackable) PauseCurrentMapTimer(now);
            return;
        }

        if (_pauseMenuSessionStartUtc.HasValue)
        {
            var paused = now - _pauseMenuSessionStartUtc.Value;
            if (paused > TimeSpan.Zero) _sessionPausedDuration += paused;
            _pauseMenuSessionStartUtc = null;
        }

        if (_isCurrentAreaTrackable && !_currentMapStartUtc.HasValue)
            _currentMapStartUtc = now;
    }

    private bool IsPauseMenuOpen() => GameController?.Game?.IsEscapeState == true;

    private void PauseCurrentMapTimer(DateTime now)
    {
        if (!_currentMapStartUtc.HasValue) return;
        var elapsed = now - _currentMapStartUtc.Value;
        if (elapsed > TimeSpan.Zero) _currentMapElapsed += elapsed;
        _currentMapStartUtc = null;
    }

    private void FinalizePausedMap()
    {
        if (_currentMapElapsed > TimeSpan.Zero)
        {
            _completedMapsDuration += _currentMapElapsed;
            _completedMapCount++;
        }

        _currentMapElapsed = TimeSpan.Zero;
    }

    private void ResetSessionAnalytics()
    {
        if (!ImGui.GetIO().KeyShift) return;

        _sessionStartUtc = DateTime.UtcNow;
        _sessionPausedDuration = TimeSpan.Zero;
        _loadedSessionsDuration = TimeSpan.Zero;
        _pauseMenuSessionStartUtc = null;
        _sessionBeastsFound = 0;
        _totalRedBeastsSession = 0;
        _completedMapsDuration = TimeSpan.Zero;
        _completedMapCount = 0;
        _currentMapElapsed = TimeSpan.Zero;
        _currentMapStartUtc = _isCurrentAreaTrackable ? DateTime.UtcNow : null;
        _mapHistory.Clear();
        ResetCurrentMapAnalytics();

        foreach (var tracked in AllRedBeasts)
            _valuableBeastCounts[tracked.Name] = 0;
    }

    private void ResetMapAverageAnalytics()
    {
        if (!ImGui.GetIO().KeyShift) return;
        _completedMapsDuration = TimeSpan.Zero;
        _completedMapCount = 0;
        _mapHistory.Clear();
        ResetCurrentMapAnalytics();
    }
}



