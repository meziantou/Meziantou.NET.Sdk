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
                        File.Copy(file, _packageDirectory.FullPath / Path.GetFileName(file));
                    }

                    return;
                }

                Assert.Fail("No file found in " + path);
            }

            Assert.Fail("NuGetDirectory environment variable not set");
        }

        // Build NuGet packages
        var buildFiles = Directory.GetFiles(PathHelpers.GetRootDirectory() / "src", "*.csproj").Select(FullPath.FromPath);
        Assert.NotEmpty(buildFiles);
        await Parallel.ForEachAsync(buildFiles, async (nuspecPath, _) =>
        {
            var psi = new ProcessStartInfo("dotnet");
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.ArgumentList.AddRange(["pack", nuspecPath, "-p:NuspecProperties=version=" + Version, "--output", _packageDirectory.FullPath]);
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
