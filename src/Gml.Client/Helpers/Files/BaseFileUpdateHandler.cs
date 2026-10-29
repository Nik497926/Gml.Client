using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gml.Dto.Files;
using Gml.Dto.Profile;

namespace Gml.Client.Helpers.Files;

public class BaseFileUpdateHandler : IFileUpdateHandler
{
    private static readonly string[] IgnoredFileNames =
    [
        ".gml-hash-cache.json",
        ".gml-hash-cache.json.tmp",
        "theme.json"
    ];

    public virtual Task<FileValidationResult> ValidateFilesAsync(
        ProfileReadInfoDto profileInfo,
        string rootDirectory)
    {
        var profilePath = Path.Combine(rootDirectory, profileInfo.ReleativePath);

        if (!Directory.Exists(profilePath))
        {
            return Task.FromResult(new FileValidationResult
            {
                FilesToDelete = [],
                FilesToUpdate = profileInfo.Files
            });
        }

        var roots = new List<string> { profilePath };
        var assetsPath = Path.Combine(rootDirectory, "shared data", "assets");
        var runtimePath = Path.Combine(rootDirectory, "shared data", "runtime");
        if (Directory.Exists(assetsPath)) roots.Add(assetsPath);
        if (Directory.Exists(runtimePath)) roots.Add(runtimePath);

        var localFiles = new ConcurrentBag<ProfileFileReadDto>();

        // Параллельный обход каталогов — на больших клиентах (assets/runtime) это заметно быстрее
        Parallel.ForEach(roots, root =>
        {
            foreach (var filePath in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var fileInfo = new FileInfo(filePath);
                if (ShouldIgnoreLocalFile(fileInfo))
                    continue;

                var relativePath = Path.GetRelativePath(rootDirectory, filePath);
                var directory = Path.Combine(
                    Path.GetDirectoryName(relativePath)?.Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar)
                    ?? string.Empty,
                    fileInfo.Name);

                localFiles.Add(new ProfileFileReadDto
                {
                    Name = fileInfo.Name,
                    Directory = Path.DirectorySeparatorChar + SystemIoProcedures.NormalizePath(directory),
                    Size = fileInfo.Length,
                });
            }
        });

        return Task.FromResult(new FileValidationResult
        {
            FilesToDelete = localFiles.ToList(),
            FilesToUpdate = profileInfo.Files
        });
    }

    private static bool ShouldIgnoreLocalFile(FileInfo fileInfo)
    {
        if (IgnoredFileNames.Contains(fileInfo.Name, StringComparer.OrdinalIgnoreCase))
            return true;

        // Незавершённые загрузки и disabled-моды обрабатываются отдельно
        if (fileInfo.Name.EndsWith(".gmldownload", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
