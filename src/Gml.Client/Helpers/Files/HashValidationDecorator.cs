using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gml.Dto.Files;
using Gml.Dto.Profile;

namespace Gml.Client.Helpers.Files;

public class HashValidationDecorator : IFileUpdateHandler
{
    private readonly IFileUpdateHandler _handler;

    public HashValidationDecorator(IFileUpdateHandler handler)
    {
        _handler = handler;
    }

    public async Task<FileValidationResult> ValidateFilesAsync(ProfileReadInfoDto profileInfo, string rootDirectory)
    {
        var result = await _handler.ValidateFilesAsync(profileInfo, rootDirectory);
        var filesToUpdate = new ConcurrentBag<ProfileFileReadDto>();
        var filesToDelete = new ConcurrentDictionary<string, ProfileFileReadDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in result.FilesToDelete)
            filesToDelete[SystemIoProcedures.NormalizePath(file.Directory)] = file;

        var serverFiles = result.FilesToUpdate.ToList();
        var cache = FileHashCache.Load(rootDirectory);

        // Раньше здесь был Task.WhenAll с синхронной лямбдой — фактически последовательное
        // чтение всех файлов. Теперь проверка идёт параллельно и с кэшем хешей.
        await Task.Run(() => Parallel.ForEach(
            serverFiles,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) },
            serverFile =>
            {
                var key = SystemIoProcedures.NormalizePath(serverFile.Directory);
                var localPath = Path.Combine(rootDirectory, key);

                // Отключённый опциональный мод лежит как *.jar.disabled — это тот же файл,
                // его не нужно ни удалять, ни перекачивать при каждом запуске
                if (!File.Exists(localPath)
                    && ApiProcedures.IsOptionalMod(localPath)
                    && File.Exists(localPath + ".disabled"))
                {
                    localPath += ".disabled";
                    filesToDelete.TryRemove(key + ".disabled", out _);
                }

                var info = new FileInfo(localPath);
                if (!info.Exists)
                {
                    filesToUpdate.Add(serverFile);
                    return;
                }

                // Файл есть на сервере — удалять его не нужно: он либо актуален, либо будет перезаписан
                filesToDelete.TryRemove(key, out _);

                if (info.Length != serverFile.Size)
                {
                    filesToUpdate.Add(serverFile);
                    return;
                }

                if (string.IsNullOrEmpty(serverFile.Hash))
                    return;

                try
                {
                    var localHash = cache.GetOrCompute(info);
                    if (!string.Equals(localHash, serverFile.Hash, StringComparison.OrdinalIgnoreCase))
                        filesToUpdate.Add(serverFile);
                }
                catch (IOException)
                {
                    // Файл занят (например, запущенной игрой) — пробуем перекачать
                    filesToUpdate.Add(serverFile);
                }
                catch (UnauthorizedAccessException)
                {
                    filesToUpdate.Add(serverFile);
                }
            }));

        cache.Save();

        result.FilesToUpdate = filesToUpdate.ToList();
        result.FilesToDelete = filesToDelete.Values.ToList();
        return result;
    }
}
