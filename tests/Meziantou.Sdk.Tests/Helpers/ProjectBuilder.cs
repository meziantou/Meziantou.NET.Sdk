#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Meziantou.Framework;

namespace Meziantou.Sdk.Tests.Helpers;

internal sealed class ProjectBuilder : IAsyncDisposable
{
    private const string SarifFileName = "BuildOutput.sarif";

    private readonly TemporaryDirectory _directory;
    private readonly PackageFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly FullPath _githubStepSummaryFile;
    private NetSdkVersion _sdkVersion = NetSdkVersion.Net10_0;
    private int _buildCount;

    public FullPath RootFolder => _directory.FullPath;

    public ProjectBuilder(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
        _directory = TemporaryDirectory.Create();
        _directory.CreateTextFile("NuGet.config", $"""
            <configuration>
                <config>
                <add key="globalPackagesFolder" value="{_fixture.PackageDirectory}/packages" />
                </config>
                <packageSources>
                <clear />    
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <add key="TestSource" value="{_fixture.PackageDirectory}" />
                </packageSources>
                <packageSourceMapping>
                <packageSource key="nuget.org">
                    <package pattern="*" />
                </packageSource>
                <packageSource key="TestSource">
                    <package pattern="Meziantou.NET.Sdk*" />
                </packageSource>
                </packageSourceMapping>
            </configuration>
            """);

        _githubStepSummaryFile = _directory.CreateEmptyFile("GITHUB_STEP_SUMMARY.txt");
    }

    public IEnumerable<(string Name, string Value)> GitHubEnvironmentVariables
    {
        get
        {
            yield return ("GITHUB_ACTIONS", "true");
            yield return ("GITHUB_STEP_SUMMARY", _githubStepSummaryFile);
        }
    }

    public string? GetGitHubStepSummaryContent()
    {
        if (File.Exists(_githubStepSummaryFile))
        {
            return File.ReadAllText(_githubStepSummaryFile);
        }

        return null;
    }

    public FullPath AddFile(string relativePath, string content)
    {
        var path = _directory.FullPath / relativePath;
        File.WriteAllText(path, content);
        return path;
    }

    public void SetDotnetSdkVersion(NetSdkVersion dotnetSdkVersion) => _sdkVersion = dotnetSdkVersion;

    public ProjectBuilder AddCsprojFile((string Name, string Value)[]? properties = null, (string Name, string Version)[]? nuGetPackages = null, XElement[]? additionalProjectElements = null, string sdk = "Meziantou.NET.Sdk", string filename = "Meziantou.TestProject.csproj")
    {
        var propertiesElement = new XElement("PropertyGroup");
        if (properties != null)
        {
            foreach (var prop in properties)
            {
                propertiesElement.Add(new XElement(prop.Name, prop.Value));
            }
        }

        var packagesElement = new XElement("ItemGroup");
        if (nuGetPackages != null)
        {
            foreach (var package in nuGetPackages)
            {
                packagesElement.Add(new XElement("PackageReference", new XAttribute("Include", package.Name), new XAttribute("Version", package.Version)));
            }
        }

        var content = $"""
            <Project Sdk="{sdk}/{_fixture.Version}">
                <PropertyGroup>
                <OutputType>exe</OutputType>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <ErrorLog>{SarifFileName},version=2.1</ErrorLog>
                </PropertyGroup>
                {propertiesElement}
                {packagesElement}
                {string.Join('\n', additionalProjectElements?.Select(e => e.ToString()) ?? [])}
            </Project>
            """;

        var fullPath = _directory.FullPath / filename;
        fullPath.CreateParentDirectory();
        File.WriteAllText(fullPath, content);
        return this;
    }

    public Task<BuildResult> BuildAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("build", buildArguments, environmentVariables);
    }
    

    public Task<BuildResult> RestoreAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("restore", buildArguments, environmentVariables);
    }
    
    public Task<BuildResult> CleanAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("clean", buildArguments, environmentVariables);
    }

    public Task<BuildResult> PackAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("pack", buildArguments, environmentVariables);
    }

    public Task<BuildResult> TestAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("test", buildArguments, environmentVariables);
    }

    private async Task<BuildResult> ExecuteDotnetCommandAndGetOutput(string command, string[]? buildArguments, (string Name, string Value)[]? environmentVariables)
    {
        _buildCount++;
        _testOutputHelper.WriteLine("-------- dotnet " + command);
        var psi = new ProcessStartInfo(await DotNetSdkHelpers.Get(_sdkVersion))
        {
            WorkingDirectory = _directory.FullPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(command);
        if (buildArguments != null)
        {
            foreach (var arg in buildArguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        psi.ArgumentList.Add("/bl");

        // Remove parent environment variables
        psi.Environment.Remove("CI");
        foreach (var kvp in psi.Environment.ToArray())
        {
            if (kvp.Key.StartsWith("GITHUB", StringComparison.Ordinal) || kvp.Key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase) || kvp.Key.StartsWith("GITHUB_", StringComparison.Ordinal) || kvp.Key.StartsWith("RUNNER_", StringComparison.Ordinal))
            {
                psi.Environment.Remove(kvp.Key);
            }
        }

        psi.Environment["MSBUILDLOGALLENVIRONMENTVARIABLES"] = "true";
        var vstestdiagPath = RootFolder / "vstestdiag.txt";
        psi.Environment["VSTestDiag"] = vstestdiagPath;
        psi.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(psi.FileName);
        psi.Environment["DOTNET_HOST_PATH"] = psi.FileName;

        if (environmentVariables != null)
        {
            foreach (var env in environmentVariables)
            {
                psi.Environment[env.Name] = env.Value;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine("Executing: " + psi.FileName + " " + string.Join(' ', psi.ArgumentList));
        foreach (var env in psi.Environment.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"  {env.Key}={env.Value}");
        }

        var result = await psi.RunAsTaskAsync();
        _testOutputHelper.WriteLine("Process exit code: " + result.ExitCode);
        _testOutputHelper.WriteLine(result.Output.ToString());

        FullPath sarifPath = _directory.FullPath / SarifFileName;
        SarifFile? sarif = null;
        if (File.Exists(sarifPath))
        {
            var bytes = File.ReadAllBytes(sarifPath);
            sarif = JsonSerializer.Deserialize<SarifFile>(bytes);
            _testOutputHelper.WriteLine("Sarif result:\n" + string.Join("\n", sarif!.AllResults().Select(r => r.ToString())));
        }
        else
        {
            _testOutputHelper.WriteLine("Sarif file not found: " + sarifPath);
        }

        var binlogContent = File.ReadAllBytes(_directory.FullPath / "msbuild.binlog");
        TestContext.Current.AddAttachment($"msbuild{_buildCount}.binlog", binlogContent);

        string? vstestDiagContent = null;
        if (File.Exists(vstestdiagPath))
        {
            vstestDiagContent = File.ReadAllText(vstestdiagPath);
            TestContext.Current.AddAttachment(vstestdiagPath.Name, vstestDiagContent);
        }

        if (result.Output.Any(line => line.Text.Contains("Could not resolve SDK")))
        {
            Assert.Fail("The SDK cannot be found, expected version: " + _fixture.Version);
        }

        return new BuildResult(result.ExitCode, result.Output, sarif, binlogContent, vstestDiagContent);
    }

    public Task ExecuteGitCommand(params string[]? arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _directory.FullPath,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        ICollection<KeyValuePair<string, string>> gitParameters =
        [
            KeyValuePair.Create("user.name", "sample"),
            KeyValuePair.Create("user.username", "sample"),
            KeyValuePair.Create("user.email", "sample@example.com"),
            KeyValuePair.Create("commit.gpgsign", "false"),
            KeyValuePair.Create("pull.rebase", "true"),
            KeyValuePair.Create("fetch.prune", "true"),
            KeyValuePair.Create("core.autocrlf", "false"),
            KeyValuePair.Create("core.longpaths", "true"),
            KeyValuePair.Create("rebase.autoStash", "true"),
            KeyValuePair.Create("submodule.recurse", "false"),
            KeyValuePair.Create("init.defaultBranch", "main"),
        ];

        foreach (var param in gitParameters)
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"{param.Key}={param.Value}");
        }

        if (arguments != null)
        {
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        return psi.RunAsTaskAsync();
    }

    public async ValueTask DisposeAsync()
    {
        TestContext.Current.AddAttachment("GITHUB_STEP_SUMMARY", GetGitHubStepSummaryContent() ?? "");
        await _directory.DisposeAsync();
    }
}
