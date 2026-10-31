using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gml.Dto.Profile;

namespace Gml.Client.Helpers.Files;

/// <summary>
/// Сопоставление путей с белым списком.
/// Пути в панели и у локальных/серверных файлов часто в разном виде:
/// с/без ведущего слэша, относительно профиля ("config") или от корня установки ("game data/beta/config").
/// </summary>
internal sealed class WhiteListMatcher
{
    private readonly HashSet<string> _files;
    private readonly string[] _folderPrefixes;
    private readonly string _profileRoot;

    public WhiteListMatcher(ProfileReadInfoDto profileInfo)
    {
        _profileRoot = Normalize(profileInfo.ReleativePath ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar);

        _files = new HashSet<string>(
            (profileInfo.WhiteListFiles ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.Directory))
            .SelectMany(f => FileVariants(_profileRoot, f.Directory)),
            StringComparer.OrdinalIgnoreCase);

        _folderPrefixes = (profileInfo.WhiteListFolders ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .SelectMany(f => FolderVariants(_profileRoot, f.Path))
            .Select(p => p.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsWhiteListedFile(string directory)
    {
        var path = Normalize(directory);
        if (string.IsNullOrEmpty(path))
            return false;

        if (_files.Contains(path))
            return true;

        // Суффикс: whitelist = "game data/beta/options.txt", file = "options.txt" или наоборот
        foreach (var white in _files)
        {
            if (IsSameOrSuffix(path, white) || IsSameOrSuffix(white, path))
                return true;
        }

        return false;
    }

    public bool IsInWhiteListedFolder(string directory)
    {
        var path = Normalize(directory);
        if (string.IsNullOrEmpty(path))
            return false;

        if (_folderPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Файл относительно профиля, папка в whitelist — абсолютная (и наоборот)
        if (!string.IsNullOrEmpty(_profileRoot)
            && !path.StartsWith(_profileRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var underProfile = Normalize(Path.Combine(_profileRoot, path));
            if (_folderPrefixes.Any(prefix => underProfile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        if (!string.IsNullOrEmpty(_profileRoot)
            && path.StartsWith(_profileRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var relative = path[(_profileRoot.Length)..].TrimStart(Path.DirectorySeparatorChar);
            if (_folderPrefixes.Any(prefix =>
                    relative.StartsWith(prefix.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    || (prefix.Length > 1 && relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
                return true;

            // prefix без profileRoot: whitelist "xaero/" vs path "game data/beta/xaero/a"
            foreach (var prefix in _folderPrefixes)
            {
                var folder = prefix.TrimEnd(Path.DirectorySeparatorChar);
                if (path.IndexOf(Path.DirectorySeparatorChar + folder + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) >= 0
                    || path.EndsWith(Path.DirectorySeparatorChar + folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public bool IsProtected(string directory) =>
        IsWhiteListedFile(directory) || IsInWhiteListedFolder(directory);

    private static IEnumerable<string> FileVariants(string profileRoot, string path)
    {
        foreach (var v in BaseVariants(profileRoot, path))
            yield return v;
    }

    private static IEnumerable<string> FolderVariants(string profileRoot, string path)
    {
        foreach (var v in BaseVariants(profileRoot, path))
            yield return v;
    }

    private static IEnumerable<string> BaseVariants(string profileRoot, string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
            yield break;

        yield return normalized;

        if (string.IsNullOrEmpty(profileRoot))
            yield break;

        var sep = Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(profileRoot + sep, StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals(profileRoot, StringComparison.OrdinalIgnoreCase))
        {
            yield return Normalize(Path.Combine(profileRoot, normalized));
        }

        if (normalized.StartsWith(profileRoot + sep, StringComparison.OrdinalIgnoreCase))
        {
            var relative = normalized[(profileRoot.Length)..].TrimStart(sep);
            if (!string.IsNullOrEmpty(relative))
                yield return relative;
        }
    }

    private static bool IsSameOrSuffix(string full, string suffix)
    {
        if (full.Equals(suffix, StringComparison.OrdinalIgnoreCase))
            return true;

        // "...\suffix" — чтобы "a\b.txt" не матчил "b.txt" через EndsWith без сепаратора ошибочно для "ab.txt"
        return full.EndsWith(Path.DirectorySeparatorChar + suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = SystemIoProcedures.NormalizePath(path.Trim());
        // Опциональные моды лежат локально с суффиксом .disabled — для белого списка это тот же файл
        return normalized.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^(".disabled".Length)]
            : normalized;
    }
}
