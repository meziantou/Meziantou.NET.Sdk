#nullable enable
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Meziantou.Framework;

namespace Meziantou.Sdk.Tests.Helpers;

public enum SdkImportStyle
{
    Default,
    ProjectElement,
    SdkElement,
}

internal sealed class ProjectBuilder : IAsyncDisposable
{
    private const string SarifFileName = "BuildOutput.sarif";

    private readonly TemporaryDirectory _directory;
    private readonly PackageFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly SdkImportStyle _defaultSdkImportStyle;
    private readonly string _defaultSdkName;
    private readonly FullPath _githubStepSummaryFile;
    private NetSdkVersion _sdkVersion = NetSdkVersion.Default;
    private int _buildCount;

    public FullPath RootFolder => _directory.FullPath;

    public ProjectBuilder(PackageFixture fixture, ITestOutputHelper testOutputHelper, SdkImportStyle defaultSdkImportStyle, string defaultSdkName)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
        _defaultSdkImportStyle = defaultSdkImportStyle;
        _defaultSdkName = defaultSdkName;
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
        path.CreateParentDirectory();

        // Ensure source files end with a newline to satisfy the insert_final_newline editorconfig rule.
        // .NET 11+ enforces this as IDE0055.
        if (Path.GetExtension(relativePath) is ".cs" or ".vb" or ".fs"
            && content.Length > 0
            && content[^1] is not '\n')
        {
            content += '\n';
        }

        File.WriteAllText(path, content);
        return path;
    }

    public void SetDotnetSdkVersion(NetSdkVersion dotnetSdkVersion) => _sdkVersion = dotnetSdkVersion;

    private string GetSdkElementContent(string sdkName)
    {
        return $"""<Sdk Name="{sdkName}" Version="{_fixture.Version}" />""";
    }

    public void AddDirectoryBuildPropsFile(string postSdkContent, string preSdkContent = "")
    {
        var fileContent = $"""
            <Project>
                {preSdkContent}
                {postSdkContent}
            </Project>
            """;
        var fullPath = _directory.FullPath / "Directory.Build.props";
        fullPath.CreateParentDirectory();
        File.WriteAllText(fullPath, fileContent);
    }

    public ProjectBuilder AddCsprojFile((string Name, string Value)[]? properties = null, NuGetReference[]? nuGetPackages = null, XElement[]? additionalProjectElements = null, string? sdk = null, string? rootSdk = null, string filename = "Meziantou.TestProject.csproj", SdkImportStyle importStyle = SdkImportStyle.Default)
    {
        sdk ??= _defaultSdkName;
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

        importStyle = importStyle == SdkImportStyle.Default ? _defaultSdkImportStyle : importStyle;
        var rootSdkName = importStyle == SdkImportStyle.ProjectElement ? $"{sdk}/{_fixture.Version}" : (rootSdk ?? "Microsoft.NET.Sdk");
        var innerSdkXmlElement = importStyle == SdkImportStyle.SdkElement ? GetSdkElementContent(sdk) : string.Empty;

        var content = $"""
            <Project Sdk="{rootSdkName}">
                {innerSdkXmlElement}
                <PropertyGroup>
                    <OutputType>exe</OutputType>
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

    public Task<BuildResult> RunAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("run", ["--", .. buildArguments ?? []], environmentVariables);
    }

    public Task<BuildResult> BuildFileAndGetOutput(string fileName, string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("build", [fileName, .. buildArguments ?? []], environmentVariables);
    }

    public Task<BuildResult> RunFileAndGetOutput(string fileName, string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("run", [fileName, .. buildArguments ?? []], environmentVariables);
    }

    public Task<BuildResult> TestAndGetOutput(string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        return ExecuteDotnetCommandAndGetOutput("test", buildArguments, environmentVariables);
    }

    public async Task<BuildResult> ExecuteDotnetCommandAndGetOutput(string command, string[]? buildArguments = null, (string Name, string Value)[]? environmentVariables = null)
    {
        static bool ShouldRemoveEnvironmentVariable(string key) =>
            key.StartsWith("GITHUB", StringComparison.Ordinal) ||
            key.StartsWith("GITHUB_", StringComparison.Ordinal) ||
            key.StartsWith("RUNNER_", StringComparison.Ordinal) ||
            key.StartsWith("VSTEST_", StringComparison.OrdinalIgnoreCase);

        _buildCount++;

        foreach (var file in Directory.GetFiles(_directory.FullPath, "*", SearchOption.AllDirectories))
        {
            _testOutputHelper.WriteLine("File: " + file);
            var content = await File.ReadAllTextAsync(file);
            _testOutputHelper.WriteLine(content);
        }

        _testOutputHelper.WriteLine("-------- dotnet " + command);

        var dotnetPath = await DotNetSdkHelpers.Get(_sdkVersion);
        var arguments = new List<string> { command };
        if (buildArguments != null)
        {
            arguments.AddRange(buildArguments);
        }

        arguments.Add("/bl");

        var environmentChanges = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CI"] = string.Empty,
            ["TF_BUILD"] = string.Empty,
            ["APPVEYOR"] = string.Empty,
            ["GITLAB_CI"] = string.Empty,
            ["TRAVIS"] = string.Empty,
            ["CIRCLECI"] = string.Empty,
            ["CODEBUILD_BUILD_ID"] = string.Empty,
            ["AWS_REGION"] = string.Empty,
            ["BUILD_ID"] = string.Empty,
            ["BUILD_URL"] = string.Empty,
            ["PROJECT_ID"] = string.Empty,
            ["TEAMCITY_VERSION"] = string.Empty,
            ["JB_SPACE_API_URL"] = string.Empty,
            ["GITHUB_COPILOT_RUNTIME"] = string.Empty,
            ["CLAUDECODE"] = string.Empty,
            ["CLAUDE_CODE_ENTRYPOINT"] = string.Empty,
            ["GEMINI_CLI"] = string.Empty,
            ["CODEX_SANDBOX"] = string.Empty,
            ["MSBUILDLOGALLENVIRONMENTVARIABLES"] = "true",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_CLI_USE_MSBUILDNOINPROCNODE"] = "1",
            ["DOTNET_HOST_PATH"] = dotnetPath,
            ["DOTNET_ROLL_FORWARD"] = "LatestMajor",
            ["DOTNET_ROLL_FORWARD_TO_PRERELEASE"] = "1",
            ["NUGET_HTTP_CACHE_PATH"] = _fixture.PackageDirectory / "http-cache",
            ["NUGET_PACKAGES"] = _fixture.PackageDirectory,
            ["NUGET_SCRATCH"] = _fixture.PackageDirectory / "nuget-scratch",
            ["NUGET_PLUGINS_CACHE_PATH"] = _fixture.PackageDirectory / "nuget-plugins-cache",
        };

        foreach (var key in Environment.GetEnvironmentVariables().Keys.OfType<string>().Where(ShouldRemoveEnvironmentVariable))
        {
            environmentChanges[key] = string.Empty;
        }

        var vstestdiagPath = RootFolder / "vstestdiag.txt";
        environmentChanges["VSTestDiag"] = vstestdiagPath;
        var dotnetRoot = Path.GetDirectoryName(dotnetPath) ?? throw new InvalidOperationException("Cannot get dotnet root path");
        environmentChanges["DOTNET_ROOT"] = dotnetRoot;
        if (RuntimeInformation.ProcessArchitecture is Architecture.X64)
        {
            environmentChanges["DOTNET_ROOT_X64"] = dotnetRoot;
        }
        else if (RuntimeInformation.ProcessArchitecture is Architecture.Arm64)
        {
            environmentChanges["DOTNET_ROOT_ARM64"] = dotnetRoot;
        }

        var sdkRoot = Path.Combine(dotnetRoot, "sdk");
        if (Directory.Exists(sdkRoot))
        {
            var preferredSdkPath = Path.Combine(sdkRoot, Path.GetFileName(dotnetRoot));
            string? sdkPath = Directory.Exists(preferredSdkPath)
                ? preferredSdkPath
                : Directory.GetDirectories(sdkRoot)
                    .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

            if (sdkPath is not null)
            {
                var msbuildSdksPath = Path.Combine(sdkPath, "Sdks");
                if (Directory.Exists(msbuildSdksPath))
                {
                    environmentChanges["MSBuildSDKsPath"] = msbuildSdksPath;
                }
            }
        }

        if (environmentVariables != null)
        {
            foreach (var env in environmentVariables)
            {
                environmentChanges[env.Name] = env.Value;
            }
        }

        var environmentForLog = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string key && !ShouldRemoveEnvironmentVariable(key))
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString() ?? string.Empty, StringComparer.Ordinal);
        foreach (var env in environmentChanges)
        {
            if (env.Value is null)
            {
                environmentForLog.Remove(env.Key);
            }
            else
            {
                environmentForLog[env.Key] = env.Value;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine("Executing: " + dotnetPath + " " + string.Join(' ', arguments));
        foreach (var env in environmentForLog.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"  {env.Key}={env.Value}");
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        var cancellationToken = linkedCts.Token;
        async Task<BufferedProcessResult> ExecuteProcessAsync()
        {
            return await ProcessWrapper.Create(dotnetPath)
                .WithWorkingDirectory(_directory.FullPath)
                .WithArguments(arguments)
                .WithEnvironmentVariables(env =>
                {
                    foreach (var environmentChange in environmentChanges)
                    {
                        if (environmentChange.Value is null)
                        {
                            env.Remove(environmentChange.Key);
                        }
                        else
                        {
                            env.Set(environmentChange.Key, environmentChange.Value);
                        }
                    }
                })
                .WithValidation(ProcessValidationMode.None)
                .ExecuteBufferedAsync(cancellationToken);
        }
        var result = await ExecuteProcessAsync();

        // Retry up to 5 times if MSB4236 or NETSDK1004 error occurs (SDK resolution or assets file issue)
        const int maxRetries = 5;
        for (int retry = 0; retry < maxRetries && result.ExitCode != 0; retry++)
        {
            if (result.Output.Any(line => line.Text.Contains("error MSB4236", StringComparison.Ordinal) ||
                                           line.Text.Contains("error NETSDK1004", StringComparison.Ordinal) ||
                                           line.Text.Contains("Could not resolve SDK", StringComparison.Ordinal) ||
                                           line.Text.Contains("Invalid restore input", StringComparison.Ordinal) ||
                                           line.Text.Contains("The project file may be invalid or missing targets required for restore", StringComparison.Ordinal)))
            {
                _testOutputHelper.WriteLine($"SDK resolution or restore error detected, retrying ({retry + 1}/{maxRetries})...");

                // Exponential backoff: 100ms, 200ms, 400ms, 800ms, 1600ms
                await Task.Delay(100 * (1 << retry), cancellationToken);

                result = await ExecuteProcessAsync();
            }
            else
            {
                break;
            }
        }

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

        return new BuildResult(result.ExitCode, [.. result.Output.Select(line => line.Text)], sarif, binlogContent, vstestDiagContent);
    }

    public async Task ExecuteGitCommand(params string[]? arguments)
    {
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

        var gitArguments = new List<string>();
        foreach (var param in gitParameters)
        {
            gitArguments.Add("-c");
            gitArguments.Add($"{param.Key}={param.Value}");
        }

        if (arguments != null)
        {
            foreach (var arg in arguments)
            {
                gitArguments.Add(arg);
            }
        }

        await ProcessWrapper.Create("git")
            .WithWorkingDirectory(_directory.FullPath)
            .WithArguments(gitArguments)
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        TestContext.Current.AddAttachment("GITHUB_STEP_SUMMARY", GetGitHubStepSummaryContent() ?? "");
        await _directory.DisposeAsync();
    }
}
