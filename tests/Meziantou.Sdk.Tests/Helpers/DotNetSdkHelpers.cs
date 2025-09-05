#nullable enable
using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Meziantou.Framework;
using Meziantou.Framework.Threading;

namespace Meziantou.Sdk.Tests.Helpers;

public static class DotNetSdkHelpers
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<NetSdkVersion, FullPath> Values = new();
    private static readonly KeyedAsyncLock<NetSdkVersion> KeyedAsyncLock = new();

    public static async Task<FullPath> Get(NetSdkVersion version)
    {
        if (Values.TryGetValue(version, out var result))
            return result;

        using (await KeyedAsyncLock.LockAsync(version))
        {
            if (Values.TryGetValue(version, out result))
                return result;

            var versionString = version switch
            {
                NetSdkVersion.Net9_0 => "9.0",
                NetSdkVersion.Net10_0 => "10.0",
                _ => throw new NotSupportedException(),
            };

            var products = await Microsoft.Deployment.DotNet.Releases.ProductCollection.GetAsync();
            var product = products.Single(a => a.ProductName == ".NET" && a.ProductVersion == versionString);
            var releases = await product.GetReleasesAsync();
            var latestRelease = releases.Single(r => r.Version == product.LatestReleaseVersion);
            var latestSdk = latestRelease.Sdks.MaxBy(sdk => sdk.Version);

            var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
            var file = latestSdk!.Files.Single(file => file.Rid == runtimeIdentifier && Path.GetExtension(file.Name) is ".zip" or ".gz");
            var finalFolderPath = FullPath.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) / "meziantou" / "dotnet" / latestSdk.Version.ToString();
            var finalDotnetPath = finalFolderPath / (OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(finalDotnetPath))
            {
                Values[version] = finalDotnetPath;
                return finalDotnetPath;
            }

            var tempFolder = FullPath.GetTempPath() / "dotnet" / Guid.NewGuid().ToString("N");

            var bytes = await HttpClient.GetByteArrayAsync(file.Address);
            if (Path.GetExtension(file.Name) is ".zip")
            {
                using var ms = new MemoryStream(bytes);
                var zip = new ZipArchive(ms);
                zip.ExtractToDirectory(tempFolder, overwriteFiles: true);
            }
            else
            {
                // .tar.gz
                using var ms = new MemoryStream(bytes);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var tar = new TarReader(gz);
                while (tar.GetNextEntry() is { } entry)
                {
                    var destinationPath = tempFolder / entry.Name;
                    if (entry.EntryType is TarEntryType.Directory)
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    else if (entry.EntryType == TarEntryType.RegularFile)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                        var entryStream = entry.DataStream;
                        using var outputStream = File.Create(destinationPath);
                        if (entryStream is not null)
                        {
                            await entryStream.CopyToAsync(outputStream);
                        }
                    }
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                var tempDotnetPath = tempFolder / "dotnet";
                File.SetUnixFileMode(tempDotnetPath, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            try
            {
                finalFolderPath.CreateParentDirectory();
                Directory.Move(tempFolder, finalFolderPath);
            }
            catch
            {
                Directory.Delete(tempFolder, recursive: true);
            }

            Assert.True(File.Exists(finalDotnetPath));
            Values[version] = finalDotnetPath;
            return finalDotnetPath;
        }
    }
}
