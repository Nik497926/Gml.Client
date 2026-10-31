using System.Security.Cryptography;
using Gml.Client.Helpers;
using Gml.Dto.Files;
using Gml.Dto.Profile;
using Gml.Web.Api.Domains.System;

namespace Gml.Client.Tests;

public class FileValidationTests
{
    private string _root = null!;
    private const string ProfileRoot = "clients/Test";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "gml-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    private string WriteLocal(string relative, string content)
    {
        var full = Path.Combine(_root, SystemIoProcedures.NormalizePath(relative));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static ProfileFileReadDto ServerFile(string relative, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new ProfileFileReadDto
        {
            Name = Path.GetFileName(relative),
            Directory = relative,
            Size = bytes.Length,
            Hash = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant()
        };
    }

    private ProfileReadInfoDto Profile(
        IEnumerable<ProfileFileReadDto> files,
        IEnumerable<ProfileFileReadDto>? whiteListFiles = null,
        IEnumerable<string>? whiteListFolders = null)
    {
        return new ProfileReadInfoDto
        {
            ProfileName = "Test",
            ReleativePath = ProfileRoot,
            Files = files.ToList(),
            WhiteListFiles = (whiteListFiles ?? []).ToList(),
            WhiteListFolders = (whiteListFolders ?? []).Select(p => new ProfileFolderReadDto { Path = p }).ToList()
        };
    }

    private Task<(ProfileFileReadDto[] ToUpdate, ProfileFileReadDto[] ToDelete)> Validate(ProfileReadInfoDto profile) =>
        new SystemIoProcedures(_root, OsType.Windows).ValidateFilesAsync(profile, _root);

    [Test]
    public async Task WhiteListedFile_WithLocalChanges_IsNotOverwritten()
    {
        var server = ServerFile($"{ProfileRoot}/options.txt", "server");
        WriteLocal($"{ProfileRoot}/options.txt", "player settings");

        var (toUpdate, toDelete) = await Validate(Profile([server], whiteListFiles: [server]));

        Assert.That(toUpdate, Is.Empty);
        Assert.That(toDelete, Is.Empty);
    }

    [Test]
    public async Task WhiteListedFile_Missing_IsDownloaded()
    {
        var server = ServerFile($"{ProfileRoot}/options.txt", "server");

        Directory.CreateDirectory(Path.Combine(_root, ProfileRoot));
        var (toUpdate, _) = await Validate(Profile([server], whiteListFiles: [server]));

        Assert.That(toUpdate.Select(f => f.Directory), Is.EquivalentTo(new[] { server.Directory }));
    }

    [TestCase("config")]
    [TestCase("/config/")]
    [TestCase(ProfileRoot + "/config")]
    public async Task WhiteListedFolder_LocalFilesAreKept(string folder)
    {
        var server = ServerFile($"{ProfileRoot}/config/mod.cfg", "server");
        WriteLocal($"{ProfileRoot}/config/mod.cfg", "edited by player");
        WriteLocal($"{ProfileRoot}/config/player-only.cfg", "player");

        var (toUpdate, toDelete) = await Validate(Profile([server], whiteListFolders: [folder]));

        Assert.That(toUpdate, Is.Empty);
        Assert.That(toDelete, Is.Empty);
    }

    [TestCase("game data/beta/options.txt")]
    [TestCase("/game data/beta/options.txt")]
    [TestCase("\\game data\\beta\\options.txt")]
    [TestCase("options.txt")]
    public async Task WhiteListedFile_GameDataStylePaths_AreKept(string whiteListPath)
    {
        const string root = "game data/beta";
        var server = new ProfileFileReadDto
        {
            Name = "options.txt",
            Directory = $"/{root}/options.txt",
            Size = 6,
            Hash = Convert.ToHexString(SHA1.HashData("server"u8.ToArray())).ToLowerInvariant()
        };
        WriteLocal($"{root}/options.txt", "player");

        var profile = new ProfileReadInfoDto
        {
            ProfileName = "beta",
            ReleativePath = root,
            Files = [server],
            WhiteListFiles =
            [
                new ProfileFileReadDto { Name = "options.txt", Directory = whiteListPath }
            ],
            WhiteListFolders = []
        };

        var (toUpdate, toDelete) = await Validate(profile);

        Assert.That(toUpdate, Is.Empty);
        Assert.That(toDelete, Is.Empty);
    }

    [TestCase("game data/beta/xaero")]
    [TestCase("/game data/beta/xaero")]
    [TestCase("xaero")]
    public async Task WhiteListedFolder_GameDataStylePaths_AreKept(string folder)
    {
        const string root = "game data/beta";
        var server = new ProfileFileReadDto
        {
            Name = "waypoints.txt",
            Directory = $"{root}/xaero/waypoints.txt",
            Size = 6,
            Hash = Convert.ToHexString(SHA1.HashData("server"u8.ToArray())).ToLowerInvariant()
        };
        WriteLocal($"{root}/xaero/waypoints.txt", "player");
        WriteLocal($"{root}/xaero/extra.txt", "extra");

        var profile = new ProfileReadInfoDto
        {
            ProfileName = "beta",
            ReleativePath = root,
            Files = [server],
            WhiteListFiles = [],
            WhiteListFolders = [new ProfileFolderReadDto { Path = folder }]
        };

        var (toUpdate, toDelete) = await Validate(profile);

        Assert.That(toUpdate, Is.Empty);
        Assert.That(toDelete, Is.Empty);
    }

    [Test]
    public async Task NotWhiteListed_ChangedAndExtraFiles_AreFixed()
    {
        var server = ServerFile($"{ProfileRoot}/mods/a.jar", "original");
        WriteLocal($"{ProfileRoot}/mods/a.jar", "tampered");
        WriteLocal($"{ProfileRoot}/mods/cheat.jar", "cheat");

        var (toUpdate, toDelete) = await Validate(Profile([server]));

        Assert.That(toUpdate.Select(f => f.Directory), Is.EquivalentTo(new[] { server.Directory }));
        Assert.That(toDelete.Select(f => SystemIoProcedures.NormalizePath(f.Directory)),
            Is.EquivalentTo(new[] { SystemIoProcedures.NormalizePath($"{ProfileRoot}/mods/cheat.jar") }));
    }

    [Test]
    public async Task DisabledOptionalMod_IsNotRedownloadedOrDeleted()
    {
        var server = ServerFile($"{ProfileRoot}/mods/map-optional-mod.jar", "mod");
        WriteLocal($"{ProfileRoot}/mods/map-optional-mod.jar.disabled", "mod");

        var (toUpdate, toDelete) = await Validate(Profile([server]));

        Assert.That(toUpdate, Is.Empty);
        Assert.That(toDelete, Is.Empty);
    }

    [Test]
    public async Task SecondRun_UsesHashCache_AndStillDetectsChanges()
    {
        var server = ServerFile($"{ProfileRoot}/mods/a.jar", "original");
        var local = WriteLocal($"{ProfileRoot}/mods/a.jar", "original");

        var first = await Validate(Profile([server]));
        Assert.That(first.ToUpdate, Is.Empty);
        Assert.That(File.Exists(Path.Combine(_root, ".gml-hash-cache.json")), Is.True);

        // Тот же размер, другое содержимое и время изменения — должно быть обнаружено
        File.WriteAllText(local, "0riginal");
        File.SetLastWriteTimeUtc(local, DateTime.UtcNow.AddMinutes(1));

        var second = await Validate(Profile([server]));
        Assert.That(second.ToUpdate.Select(f => f.Directory), Is.EquivalentTo(new[] { server.Directory }));
    }
}
