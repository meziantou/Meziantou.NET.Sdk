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
        await Parallel.ForEachAsync(buildFiles, async (projectPath, ct) =>
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
            var result = await ProcessWrapper.Create("dotnet")
                .WithArguments("pack", "--disable-build-servers", projectPath, "-p:Version=" + Version, "--output", _packageDirectory.FullPath)
                .WithEnvironmentVariables(env => env
                    .Set("MSBUILDDISABLENODEREUSE", "1")
                    .Set("DOTNET_CLI_USE_MSBUILDNOINPROCNODE", "1"))
                .WithValidation(ProcessValidationMode.None)
                .ExecuteBufferedAsync(linkedCts.Token);
            if (!result.ExitCode.IsSuccess)
            {
                Assert.Fail($"NuGet pack failed with exit code {result.ExitCode}. Output: {result.Output}");
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _packageDirectory.DisposeAsync();
    }
}
