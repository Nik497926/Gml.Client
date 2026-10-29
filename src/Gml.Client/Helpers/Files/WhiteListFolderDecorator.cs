using System.Linq;
using System.Threading.Tasks;
using Gml.Dto.Files;
using Gml.Dto.Profile;

namespace Gml.Client.Helpers.Files;

/// <summary>
/// Папки из белого списка: локальные файлы внутри не удаляются, а существующие файлы не перезаписываются.
/// Раньше для удаления путь сравнивался без префикса папки профиля, из-за чего файлы
/// из whitelist-папок удалялись и скачивались заново (выглядело как «перезапись»).
/// </summary>
public class WhiteListFolderDecorator : IFileUpdateHandler
{
    private readonly IFileUpdateHandler _handler;

    public WhiteListFolderDecorator(IFileUpdateHandler handler)
    {
        _handler = handler;
    }

    public async Task<FileValidationResult> ValidateFilesAsync(ProfileReadInfoDto profileInfo, string rootDirectory)
    {
        var result = await _handler.ValidateFilesAsync(profileInfo, rootDirectory);
        var matcher = new WhiteListMatcher(profileInfo);

        result.FilesToDelete = result.FilesToDelete
            .Where(file => !matcher.IsInWhiteListedFolder(file.Directory))
            .ToList();

        result.FilesToUpdate = result.FilesToUpdate
            .Where(file => !matcher.IsInWhiteListedFolder(file.Directory)
                           || !WhiteListFileDecorator.LocalFileExists(file, rootDirectory))
            .ToList();

        return result;
    }
}
