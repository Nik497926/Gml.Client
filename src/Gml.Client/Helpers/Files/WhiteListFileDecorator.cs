using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gml.Dto.Files;
using Gml.Dto.Profile;

namespace Gml.Client.Helpers.Files;

/// <summary>
/// Файлы из белого списка: если файл уже есть у игрока — не удаляем и не перезаписываем его,
/// даже если хеш отличается от серверного (это и есть смысл белого списка — локальные настройки игрока).
/// Если файла нет — скачиваем серверную версию как начальную.
/// </summary>
public class WhiteListFileDecorator : IFileUpdateHandler
{
    private readonly IFileUpdateHandler _handler;

    public WhiteListFileDecorator(IFileUpdateHandler handler)
    {
        _handler = handler;
    }

    public async Task<FileValidationResult> ValidateFilesAsync(ProfileReadInfoDto profileInfo, string rootDirectory)
    {
        var result = await _handler.ValidateFilesAsync(profileInfo, rootDirectory);
        var matcher = new WhiteListMatcher(profileInfo);

        result.FilesToDelete = result.FilesToDelete
            .Where(file => !matcher.IsWhiteListedFile(file.Directory))
            .ToList();

        result.FilesToUpdate = result.FilesToUpdate
            .Where(file => !matcher.IsWhiteListedFile(file.Directory) || !LocalFileExists(file, rootDirectory))
            .ToList();

        return result;
    }

    internal static bool LocalFileExists(ProfileFileReadDto file, string rootDirectory)
    {
        var relative = SystemIoProcedures.NormalizePath(file.Directory);
        var fullPath = Path.Combine(rootDirectory, relative);
        // Для опциональных модов файл может быть сохранен с суффиксом ".disabled"
        if (File.Exists(fullPath) || File.Exists(fullPath + ".disabled"))
            return true;

        // На всякий случай: путь с другим разделителем / ведущим слэшем
        var alt = Path.Combine(rootDirectory, relative.Replace('\\', '/').TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        return !string.Equals(alt, fullPath, StringComparison.OrdinalIgnoreCase)
               && (File.Exists(alt) || File.Exists(alt + ".disabled"));
    }
}
