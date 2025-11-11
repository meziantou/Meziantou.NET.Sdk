using System.Diagnostics;
using Meziantou.Framework;
using Meziantou.Sdk.Tests.Helpers;

[assembly: AssemblyFixture(typeof(PackageFixture))]

namespace Meziantou.Sdk.Tests.Helpers;

public sealed class PackageFixture : IAsyncLifetime
{
    public const string SdkName = "Meziantou.NET.Sdk";
    public const string SdkWebName = "Meziantou.NET.Sdk.Web";
    public const string SdkTestName = "Meziantou.NET.Sdk.Test";

    private readonly TemporaryDirectory _packageDirectory = TemporaryDirectory.Create();

    public FullPath PackageDirectory => _packageDirectory.FullPath;

    public string Version { get; } = Environment.GetEnvironmentVariable("PACKAGE_VERSION") ?? "999.9.9";

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("CI") != null)
        {
            if (Environment.GetEnvironmentVariable("NUGET_DIRECTORY") is { } path)
            {
                var files = Directory.GetFiles(path, "*.nupkg", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    foreach (var file in files)
                    {
                        Console.WriteLine("Copying NuGet package from " + file + " to " + (_packageDirectory.FullPath / Path.GetFileName(file)));
                        File.Copy(file, _packageDirectory.FullPath / Path.GetFileName(file));
                    }

                    return;
                }

                Assert.Fail("No file found in " + path);
            }

            Assert.Fail("NuGetDirectory environment variable not set");
        }

        var nugetPath = FullPath.GetTempPath() / $"meziantou.sdk.tests-nuget.exe";
        if (!File.Exists(nugetPath))
        {
            var tempNugetPath = FullPath.GetTempPath() / $"nuget-{Guid.NewGuid()}.exe";
            await DownloadFileAsync("https://dist.nuget.org/win-x86-commandline/latest/nuget.exe", tempNugetPath);
            try
            {
                File.Move(tempNugetPath, nugetPath);
            }
            catch (Exception) when (File.Exists(nugetPath))
            {
            }
        }

        // Build NuGet packages
        var nuspecFiles = Directory.GetFiles(PathHelpers.GetRootDirectory() / "src" / "Sdk", "*.nuspec").Select(FullPath.FromPath);
        Assert.NotEmpty(nuspecFiles);
        await Parallel.ForEachAsync(nuspecFiles, async (nuspecPath, _) =>
        {
            var psi = new ProcessStartInfo(nugetPath);
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.ArgumentList.AddRange(["pack", nuspecPath, "-ForceEnglishOutput", "-BasePath", PathHelpers.GetRootDirectory() / "src", "-Version", Version, "-OutputDirectory", _packageDirectory.FullPath]);
            var result = await psi.RunAsTaskAsync();
            if (result.ExitCode != 0)
            {
                Assert.Fail($"NuGet pack failed with exit code {result.ExitCode}. Output: {result.Output}");
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _packageDirectory.DisposeAsync();
    }

    private static async Task DownloadFileAsync(string url, FullPath path)
    {
        path.CreateParentDirectory();
        await using var nugetStream = await SharedHttpClient.Instance.GetStreamAsync(url);
        await using var fileStream = File.Create(path);
        await nugetStream.CopyToAsync(fileStream);
    }
}
