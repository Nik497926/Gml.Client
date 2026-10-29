using System;
using System.IO;
using System.Security.Cryptography;

namespace Gml.Client.Helpers;

public class SystemHelper
{
    private const int HashBufferSize = 1024 * 1024;

    public static string CalculateFileHash(string filePath, HashAlgorithm algorithm)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            HashBufferSize, FileOptions.SequentialScan);
        var hashBytes = algorithm.ComputeHash(fileStream);

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}
