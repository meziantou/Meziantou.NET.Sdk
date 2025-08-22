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
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly FullPath _githubStepSummaryFile;

    public FullPath RootFolder => _directory.FullPath;

    public ProjectBuilder(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _directory = TemporaryDirectory.Create();
        _directory.CreateTextFile("NuGet.config", $"""
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{fixture.PackageDirectory}/packages" />
                  </config>
                  <packageSources>
                    <clear />    
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                    <add key="TestSource" value="{fixture.PackageDirectory}" />
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

        _githubStepSummaryFile = _directory.CreateEmptyFile("GITHUB_STEP_SUMMARY");
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
                <Project Sdk="{sdk}/999.9.9">
                  <PropertyGroup>
                    <OutputType>exe</OutputType>
                    <TargetFramework>net$(NETCoreAppMaximumVersion)</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <ErrorLog>{SarifFileName},version=2.1</ErrorLog>
                  </PropertyGroup>
                  {propertiesElement}
                  {packagesElement}
                  {string.Join('\n', additionalProjectElements?.Select(e => e.ToString()) ?? [])}
                </Project>                
                """;

        File.WriteAllText(_directory.FullPath / filename, content);
        return this;
    }

    public Task<BuildResult> BuildAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return this.ExecuteDotnetCommandAndGetOutput("build", buildArguments, environmentVariables);
    }

    public Task<BuildResult> PackAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return this.ExecuteDotnetCommandAndGetOutput("pack", buildArguments, environmentVariables);
    }

    public Task<BuildResult> TestAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return this.ExecuteDotnetCommandAndGetOutput("test", buildArguments, environmentVariables);
    }

    private async Task<BuildResult> ExecuteDotnetCommandAndGetOutput(string command, string[]? buildArguments, (string Name, string Value)[]? environmentVariables)
    {
        var globaljsonPsi = new ProcessStartInfo("dotnet", "new global.json")
        {
            WorkingDirectory = _directory.FullPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var result = await globaljsonPsi.RunAsTaskAsync();
        _testOutputHelper.WriteLine("Process exit code: " + result.ExitCode);
        _testOutputHelper.WriteLine(result.Output.ToString());

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _directory.FullPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
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
        psi.Environment.Remove("GITHUB_ACTIONS");
        psi.Environment["MSBUILDLOGALLENVIRONMENTVARIABLES"] = "true";

        if (environmentVariables != null)
        {
            foreach (var env in environmentVariables)
            {
                psi.Environment[env.Name] = env.Value;
            }
        }

        result = await psi.RunAsTaskAsync();
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
        TestContext.Current.AddAttachment("msbuild.binlog", binlogContent);
        return new BuildResult(result.ExitCode, result.Output, sarif, binlogContent);
    }

    public ValueTask DisposeAsync() => _directory.DisposeAsync();
}
