#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
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
                NetSdkVersion.Net10_0 => "10.0",
                NetSdkVersion.Net11_0 => "11.0",
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
            Directory.CreateDirectory(tempFolder);

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
                var tempArchivePath = FullPath.GetTempPath() / "dotnet" / (Guid.NewGuid().ToString("N") + ".tar.gz");
                tempArchivePath.CreateParentDirectory();
                await File.WriteAllBytesAsync(tempArchivePath, bytes);

                try
                {
                    var psi = new ProcessStartInfo("tar")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    psi.ArgumentList.Add("-xzf");
                    psi.ArgumentList.Add(tempArchivePath);
                    psi.ArgumentList.Add("-C");
                    psi.ArgumentList.Add(tempFolder);

                    var extractionResult = await psi.RunAsTaskAsync();
                    if (extractionResult.ExitCode != 0)
                        throw new InvalidOperationException($"Failed to extract SDK archive: {extractionResult.Output}");
                }
                finally
                {
                    if (File.Exists(tempArchivePath))
                    {
                        File.Delete(tempArchivePath);
                    }
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                var tempDotnetPath = tempFolder / "dotnet";

                Console.WriteLine("Updating permissions of " + tempDotnetPath);
                File.SetUnixFileMode(tempDotnetPath, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

                foreach (var cscPath in Directory.GetFiles(tempFolder, "csc", SearchOption.AllDirectories))
                {
                    Console.WriteLine("Updating permissions of " + cscPath);
                    File.SetUnixFileMode(cscPath, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }

            try
            {
                finalFolderPath.CreateParentDirectory();
                Directory.Move(tempFolder, finalFolderPath);
            }
            catch
            {
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, recursive: true);
                }
            }

            Assert.True(File.Exists(finalDotnetPath));
            Values[version] = finalDotnetPath;
            return finalDotnetPath;
        }
    }
}
