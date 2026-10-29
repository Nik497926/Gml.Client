using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace Gml.Client.Helpers.Files;

/// <summary>
/// Кэш SHA1-хешей локальных файлов. Хеш пересчитывается только если у файла изменились
/// размер, время изменения или время создания. Без кэша при каждом запуске читались
/// все файлы клиента (ассеты, библиотеки, моды, runtime) — это и давало запуск по 10–30 минут.
/// </summary>
internal sealed class FileHashCache
{
    private const string CacheFileName = ".gml-hash-cache.json";
    private const int CacheVersion = 1;
    private const int HashBufferSize = 1024 * 1024;

    private readonly string _cachePath;
    private readonly string _rootDirectory;
    private readonly ConcurrentDictionary<string, Entry> _entries;
    private volatile bool _dirty;

    private FileHashCache(string rootDirectory, ConcurrentDictionary<string, Entry> entries)
    {
        _rootDirectory = rootDirectory;
        _cachePath = Path.Combine(rootDirectory, CacheFileName);
        _entries = entries;
    }

    public static FileHashCache Load(string rootDirectory)
    {
        var entries = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
        var path = Path.Combine(rootDirectory, CacheFileName);

        try
        {
            if (File.Exists(path))
            {
                var state = JsonConvert.DeserializeObject<State>(File.ReadAllText(path));
                if (state?.Version == CacheVersion && state.Entries is not null)
                {
                    foreach (var pair in state.Entries)
                        entries[pair.Key] = pair.Value;
                }
            }
        }
        catch
        {
            // Повреждённый кэш просто пересобирается
        }

        return new FileHashCache(rootDirectory, entries);
    }

    public string GetOrCompute(FileInfo file)
    {
        var key = Path.GetRelativePath(_rootDirectory, file.FullName).Replace('\\', '/');
        var size = file.Length;
        var modified = file.LastWriteTimeUtc.Ticks;
        var created = file.CreationTimeUtc.Ticks;

        if (_entries.TryGetValue(key, out var cached)
            && cached.Size == size
            && cached.Modified == modified
            && cached.Created == created
            && !string.IsNullOrEmpty(cached.Hash))
        {
            return cached.Hash;
        }

        var hash = ComputeSha1(file.FullName);
        _entries[key] = new Entry { Size = size, Modified = modified, Created = created, Hash = hash };
        _dirty = true;
        return hash;
    }

    public void Save()
    {
        if (!_dirty)
            return;

        try
        {
            // Удаляем записи о файлах, которых больше нет
            foreach (var key in _entries.Keys)
            {
                if (!File.Exists(Path.Combine(_rootDirectory, key)))
                    _entries.TryRemove(key, out _);
            }

            var tmp = _cachePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(new State
            {
                Version = CacheVersion,
                Entries = new Dictionary<string, Entry>(_entries)
            }));

            if (File.Exists(_cachePath))
                File.Delete(_cachePath);
            File.Move(tmp, _cachePath);
            _dirty = false;
        }
        catch
        {
            // кэш не критичен
        }
    }

    public static string ComputeSha1(string filePath)
    {
        using var algorithm = SHA1.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            HashBufferSize, FileOptions.SequentialScan);
        var hashBytes = algorithm.ComputeHash(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private sealed class State
    {
        public int Version { get; set; }
        public Dictionary<string, Entry>? Entries { get; set; }
    }

    internal sealed class Entry
    {
        public long Size { get; set; }
        public long Modified { get; set; }
        public long Created { get; set; }
        public string Hash { get; set; } = string.Empty;
    }
}
