using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BeastsV2;

internal sealed class SessionStoreV2
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public SessionStoreV2(string directory)
    {
        _directory = directory ?? string.Empty;
    }

    public string DirectoryPath => _directory;

    public bool Save(SavedSessionDataV2 session, string nameHint, out string fileName)
    {
        fileName = string.Empty;

        if (session == null || string.IsNullOrWhiteSpace(_directory))
            return false;

        try
        {
            if (!IsValidIdentifier(session.SaveId) || !IsValidIdentifier(session.SessionId))
                return false;

            Directory.CreateDirectory(_directory);

            var slug = AnalyticsEngineV2.BuildSlug(nameHint);
            var candidate = AnalyticsEngineV2.BuildSessionFileName(session.SavedAtUtc, slug);
            var path = Path.Combine(_directory, candidate);
            var suffix = 1;
            while (File.Exists(path))
            {
                candidate = AnalyticsEngineV2.BuildSessionFileName(session.SavedAtUtc, $"{slug}-{suffix++}");
                path = Path.Combine(_directory, candidate);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(session, _jsonOptions));
            fileName = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<SessionFileEntryV2> ReadAll()
    {
        try
        {
            if (!Directory.Exists(_directory))
                return [];

            return EnumerateSessionFiles()
                .Select(ReadByPath)
                .Where(x => x != null)
                .OrderByDescending(x => x.Data.SavedAtUtc)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public SessionFileEntryV2 ReadBySessionId(string sessionId)
    {
        if (!IsValidIdentifier(sessionId))
            return null;

        try
        {
            foreach (var file in EnumerateSessionFiles())
            {
                var entry = ReadByPath(file);
                if (entry?.Data?.SessionId != null &&
                    entry.Data.SessionId.EqualsIgnoreCase(sessionId))
                {
                    return entry;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public bool DeleteBySessionId(string sessionId)
    {
        if (!IsValidIdentifier(sessionId))
            return false;

        try
        {
            var entry = ReadBySessionId(sessionId);
            if (entry == null)
                return false;

            var path = Path.Combine(_directory, entry.FileName);
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public SessionFileEntryV2 ReadBySaveId(string saveId)
    {
        if (!IsValidIdentifier(saveId))
            return null;

        try
        {
            foreach (var file in EnumerateSessionFiles())
            {
                var entry = ReadByPath(file);
                if (entry?.Data?.SaveId != null &&
                    entry.Data.SaveId.EqualsIgnoreCase(saveId))
                {
                    return entry;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public bool DeleteBySaveId(string saveId)
    {
        if (!IsValidIdentifier(saveId))
            return false;

        try
        {
            var entry = ReadBySaveId(saveId);
            if (entry == null)
                return false;

            var path = Path.Combine(_directory, entry.FileName);
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private SessionFileEntryV2 ReadByPath(string path)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<SavedSessionDataV2>(json, _jsonOptions);
            if (data == null || data.SchemaVersion != 2 || !IsValidIdentifier(data.SaveId) || !IsValidIdentifier(data.SessionId))
                return null;

            return new SessionFileEntryV2(fileName, data);
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> EnumerateSessionFiles()
    {
        return Directory
            .EnumerateFiles(_directory, "????-??-??_??-??-??-*.json")
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.All(ch => char.IsLetterOrDigit(ch) || ch == '-');
    }
}

internal sealed record SessionFileEntryV2(string FileName, SavedSessionDataV2 Data);