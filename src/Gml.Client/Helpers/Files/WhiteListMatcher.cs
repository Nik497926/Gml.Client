using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gml.Dto.Profile;

namespace Gml.Client.Helpers.Files;

/// <summary>
/// Единая логика сопоставления путей с белым списком.
/// Пути файлов приходят как относительные к каталогу установки ("clients/Profile/config/a.cfg"),
/// а пути белого списка в панели обычно вводятся относительно папки профиля ("config")
/// или уже вместе с ней ("clients/Profile/config"). Поддерживаем оба варианта.
/// </summary>
internal sealed class WhiteListMatcher
{
    private readonly HashSet<string> _files;
    private readonly string[] _folderPrefixes;

    public WhiteListMatcher(ProfileReadInfoDto profileInfo)
    {
        var profileRoot = SystemIoProcedures.NormalizePath(profileInfo.ReleativePath ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar);

        _files = new HashSet<string>(
            (profileInfo.WhiteListFiles ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.Directory))
            .SelectMany(f => Variants(profileRoot, f.Directory)),
            StringComparer.OrdinalIgnoreCase);

        _folderPrefixes = (profileInfo.WhiteListFolders ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .SelectMany(f => Variants(profileRoot, f.Path))
            .Select(p => p.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsWhiteListedFile(string directory) =>
        _files.Contains(Normalize(directory));

    public bool IsInWhiteListedFolder(string directory)
    {
        var path = Normalize(directory);
        return _folderPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsProtected(string directory) =>
        IsWhiteListedFile(directory) || IsInWhiteListedFolder(directory);

    private static IEnumerable<string> Variants(string profileRoot, string path)
    {
        var normalized = Normalize(path);
        yield return normalized;

        if (!string.IsNullOrEmpty(profileRoot)
            && !normalized.StartsWith(profileRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(profileRoot, normalized);
        }
    }

    private static string Normalize(string path)
    {
        var normalized = SystemIoProcedures.NormalizePath(path);
        // Опциональные моды лежат локально с суффиксом .disabled — для белого списка это тот же файл
        return normalized.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(0, normalized.Length - ".disabled".Length)
            : normalized;
    }
}
