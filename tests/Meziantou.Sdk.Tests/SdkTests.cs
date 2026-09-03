using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using NuGet.Packaging;
using Task = System.Threading.Tasks.Task;
using static Meziantou.Sdk.Tests.Helpers.PackageFixture;
using Meziantou.Sdk.Tests.Helpers;
using Meziantou.Framework;
using System.Reflection.Metadata;
using NuGet.Packaging.Licenses;

namespace Meziantou.Sdk.Tests;

public sealed class Sdk10_0_Root_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : SdkTests(fixture, testOutputHelper, NetSdkVersion.Net10_0);

public sealed class Sdk11_0_Root_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : SdkTests(fixture, testOutputHelper, NetSdkVersion.Net11_0);

public abstract class SdkTests(PackageFixture fixture, ITestOutputHelper testOutputHelper, NetSdkVersion dotnetSdkVersion)
{
    // note: don't simplify names as they are used in the Renovate regex
    private static readonly NuGetReference[] XUnit3References =
    [
        new NuGetReference("xunit.v3", "4.0.0"),
    ];
    private static readonly NuGetReference[] XUnit3MTP2References =
    [
        new NuGetReference("xunit.v3.mtp-v2", "4.0.0"),
        new NuGetReference("xunit.runner.visualstudio", "4.0.0"),
    ];

    private ProjectBuilder CreateProjectBuilder(string defaultSdkName = SdkName)
    {
        var builder = new ProjectBuilder(fixture, testOutputHelper, defaultSdkName);
        builder.SetDotnetSdkVersion(dotnetSdkVersion);
        return builder;
    }

    [Fact]
    public void PackageReferenceAreValid()
    {
        var root = PathHelpers.GetRootDirectory() / "src";
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(FullPath.FromPath);
        foreach (var file in files)
        {
            if (file.Extension is ".props" or ".targets")
            {
                var doc = XDocument.Load(file);
                var nodes = doc.Descendants("PackageReference");
                foreach (var node in nodes)
                {
                    // 'Update' items only change the metadata of an existing reference, so they don't need to be flagged as implicit
                    if (node.Attribute("Include") is null)
                    {
                        continue;
                    }

                    var attr = node.Attribute("IsImplicitlyDefined");
                    if (attr is null || attr.Value != "true")
                    {
                        Assert.Fail("Missing IsImplicitlyDefined=\"true\" on " + node.ToString());
                    }
                }
            }
        }
    }

    [Fact]
    public async Task ValidateDefaultProperties()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("OutputType", "Library")]);
        var data = await project.BuildAndGetOutput();
        //data.AssertMSBuildPropertyValue("LangVersion", "latest");
        data.AssertMSBuildPropertyValue("PublishRepositoryUrl", "true");
        data.AssertMSBuildPropertyValue("DebugType", "embedded");
        data.AssertMSBuildPropertyValue("EmbedUntrackedSources", "true");
        data.AssertMSBuildPropertyValue("EnableNETAnalyzers", "true");
        data.AssertMSBuildPropertyValue("AnalysisLevel", "latest-all");
        data.AssertMSBuildPropertyValue("EnablePackageValidation", "true");
        data.AssertMSBuildPropertyValue("RestoreUseStaticGraphEvaluation", "true");
        data.AssertMSBuildPropertyValue("RollForward", "LatestMajor");
    }

    [Fact]
    public async Task ValidateDefaultProperties_Test()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile();
        var data = await project.BuildAndGetOutput();
        data.AssertMSBuildPropertyValue("RollForward", expectedValue: null);
    }

    [Fact]
    public async Task CanOverrideLangVersion()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("LangVersion", "preview")]);
        project.AddFile("sample.cs", "Console.WriteLine();");
        var data = await project.BuildAndGetOutput();
        data.AssertMSBuildPropertyValue("LangVersion", "preview");
    }

    [Fact]
    public async Task CanOverrideRollForward()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("RollForward", "Minor")]);
        project.AddFile("sample.cs", "Console.WriteLine();");
        var data = await project.BuildAndGetOutput();
        data.AssertMSBuildPropertyValue("RollForward", "Minor");
    }

    [Fact]
    public async Task RollForwardIsCompatibleWithClassLibraries()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("OutputType", "Library")]);
        var data = await project.BuildAndGetOutput();
        data.AssertMSBuildPropertyValue("RollForward", "LatestMajor");
    }

    [Fact]
    public async Task PackAsTool_IsSetForExe()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", "Console.WriteLine();");
        var data = await project.BuildAndGetOutput();
        data.AssertMSBuildPropertyValue("PackAsTool", "true");
    }

    [Fact]
    public async Task PackAsTool_IsNotSetForLibrary()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("OutputType", "Library")]);
        var data = await project.BuildAndGetOutput();
        data.AssertMSBuildPropertyValue("PackAsTool", expectedValue: null);
    }

    [Fact]
    public async Task PackAsTool_CanBeOverridden()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("PackAsTool", "false")]);
        project.AddFile("Program.cs", "Console.WriteLine();");
        var data = await project.BuildAndGetOutput();
        data.AssertMSBuildPropertyValue("PackAsTool", "false");
    }

    [Fact]
    public async Task CanOverrideLangVersionInDirectoryBuildProps()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddDirectoryBuildPropsFile("""
            <PropertyGroup>
                <LangVersion>preview</LangVersion>
            </PropertyGroup>
            """);
        project.AddFile("sample.cs", "Console.WriteLine();");
        var data = await project.BuildAndGetOutput();
        data.AssertMSBuildPropertyValue("LangVersion", "preview");
    }

    [Fact]
    public async Task AllowUnsafeBlock()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """
            unsafe
            {
                int* p = null;
            }
            """);

        var data = await project.BuildAndGetOutput();
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task StrictModeEnabled()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """
            var o = new object();
            if (o is Math) // Error CS7023 The second operand of an 'is' or 'as' operator may not be static type 'Math'
            {
            }
            """);

        var data = await project.BuildAndGetOutput();
        Assert.True(data.HasWarning("CS7023"));
    }

    [Fact]
    public async Task BannedSymbolsAreReported()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var data = await project.BuildAndGetOutput();
        Assert.True(data.HasWarning("RS0030"));

        var files = data.GetBinLogFiles();
        Assert.Contains(files, f => f.EndsWith("BannedSymbols.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BannedSymbols_NewtonsoftJson_AreReported()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(nuGetPackages: [new NuGetReference("Newtonsoft.Json", "13.0.4")]);
        project.AddFile("sample.cs", """_ = Newtonsoft.Json.JsonConvert.SerializeObject("test");""");
        var data = await project.BuildAndGetOutput();
        Assert.True(data.HasWarning("RS0030"));
    }

    [Fact]
    public async Task BannedSymbols_NewtonsoftJson_Disabled_AreNotReported()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("BannedNewtonsoftJsonSymbols", "false")], nuGetPackages: [new NuGetReference("Newtonsoft.Json", "13.0.4")]);
        project.AddFile("sample.cs", """_ = Newtonsoft.Json.JsonConvert.SerializeObject("test");""");
        var data = await project.BuildAndGetOutput();
        Assert.False(data.HasWarning("RS0030"));
    }

    [Fact]
    public async Task EditorConfigsAreInBinlog()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var localFile = project.AddFile(".editorconfig", "");
        TestContext.Current.TestOutputHelper.WriteLine("Local editorconfig path: " + localFile);

        var data = await project.BuildAndGetOutput();

        var files = data.GetBinLogFiles();
        foreach (var file in files)
        {
            TestContext.Current.TestOutputHelper.WriteLine("Binlog file: " + file);
        }

        // macos may prefix the path with /private
        var localFileWithPrivatePrefix = FullPath.FromPath("/private" + localFile);

        Assert.Contains(files, f => f.EndsWith(".editorconfig", StringComparison.Ordinal));
        Assert.Contains(files, f => FullPathComparer.Default.Equals(FullPath.FromPath(f), localFile) || FullPathComparer.Default.Equals(FullPath.FromPath(f), localFileWithPrivatePrefix));
    }

    [Fact]
    public async Task SingleFileAppEditorConfig_NotIncludedByDefault()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        var files = data.GetBinLogFiles();
        Assert.DoesNotContain(files, f => f.EndsWith("Meziantou.NET.Sdk.SingleFileApp.editorconfig", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SingleFileAppEditorConfig_IncludedWhenMeziantouSingleFileAppIsTrue()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("MeziantouSingleFileApp", "true")]);
        project.AddFile("Sample.cs", """
            Console.WriteLine();

            class Foo { }
            """);
        var data = await project.BuildAndGetOutput();

        var files = data.GetBinLogFiles();
        Assert.Contains(files, f => f.EndsWith("Meziantou.NET.Sdk.SingleFileApp.editorconfig", StringComparison.Ordinal));
        Assert.False(data.HasWarning("MA0048"));
    }

    [Fact]
    public async Task SingleFileAppEditorConfig_MA0048IsReportedByDefault()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Sample.cs", """
            Console.WriteLine();

            class Foo { }
            """);
        var data = await project.BuildAndGetOutput();
        Assert.True(data.HasWarning("MA0048"));
    }

    [Fact]
    public async Task NetStandard20MultiTargetEditorConfig_IncludedAndDisablesMA0110()
    {
        var commonPropsPath = PathHelpers.GetRootDirectory() / "src" / "common" / "Common.props";
        var commonTargetsPath = PathHelpers.GetRootDirectory() / "src" / "common" / "Common.targets";

        await using var project = CreateProjectBuilder("Microsoft.NET.Sdk");
        project.AddDirectoryBuildPropsFile($"""<Import Project="{commonPropsPath}" />""");
        project.AddFile("Sample.cs", """
            using System.Text.RegularExpressions;

            public static class Sample
            {
                public static Regex Create() => new("sample", RegexOptions.Compiled);
            }
            """);

        void AddProjectFile(params (string Name, string Value)[] properties)
        {
            project.AddCsprojFile(properties: properties, additionalProjectElements:
            [
                new XElement("Import", new XAttribute("Project", commonTargetsPath)),
            ]);
        }

        AddProjectFile(
            ("TargetFramework", "net10.0"),
            ("OutputType", "Library"));

        var singleTargetData = await project.BuildAndGetOutput();
        Assert.True(singleTargetData.HasNote("MA0110"));

        AddProjectFile(
            ("TargetFrameworks", "net10.0;netstandard2.0"),
            ("OutputType", "Library"));

        var multiTargetData = await project.BuildAndGetOutput(["-p:TargetFramework=netstandard2.0"]);
        var editorConfigFiles = multiTargetData.GetBinLogFiles();

        Assert.Contains(editorConfigFiles, f => f.EndsWith("Meziantou.NET.Sdk.NetStandard2_0.MultiTarget.editorconfig", StringComparison.Ordinal));
        Assert.False(multiTargetData.HasNote("MA0110"));
    }

    [Fact]
    public async Task WarningsAsErrorOnGitHubActions()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var data = await project.BuildAndGetOutput(environmentVariables: [.. project.GitHubEnvironmentVariables]);
        Assert.True(data.HasError("RS0030"));
    }

    [Fact]
    public async Task WarningsAsErrorInLLMContext()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var data = await project.BuildAndGetOutput(environmentVariables: [("CLAUDECODE", "1")]);
        Assert.True(data.HasError("RS0030"));
        Assert.NotEqual("true", data.GetMSBuildPropertyValue("ContinuousIntegrationBuild"));
    }

    [Theory]
    [InlineData("CLAUDECODE", "1")]
    [InlineData("CLAUDE_CODE_ENTRYPOINT", "1")]
    [InlineData("CURSOR_EDITOR", "1")]
    [InlineData("CURSOR_AI", "1")]
    [InlineData("GEMINI_CLI", "true")]
    [InlineData("GITHUB_COPILOT_CLI_MODE", "yes")]
    [InlineData("GH_COPILOT_WORKING_DIRECTORY", "1")]
    [InlineData("COPILOT_CLI", "1")]
    [InlineData("COPILOT_AGENT", "1")]
    [InlineData("CODEX_CLI", "1")]
    [InlineData("CODEX_SANDBOX", "1")]
    [InlineData("OR_APP_NAME", "Aider")]
    [InlineData("OR_APP_NAME", "plandex")]
    [InlineData("AMP_HOME", "1")]
    [InlineData("QWEN_CODE", "1")]
    [InlineData("DROID_CLI", "on")]
    [InlineData("OPENCODE_AI", "1")]
    [InlineData("ZED_ENVIRONMENT", "1")]
    [InlineData("ZED_TERM", "1")]
    [InlineData("KIMI_CLI", "TRUE")]
    [InlineData("OR_APP_NAME", "OpenHands")]
    [InlineData("GOOSE_TERMINAL", "1")]
    [InlineData("CLINE_TASK_ID", "1")]
    [InlineData("ROO_CODE_TASK_ID", "1")]
    [InlineData("WINDSURF_SESSION", "1")]
    [InlineData("AGENT_CLI", "1")]
    public async Task LLMContextEnvironmentVariables_EnableWarningsAsErrors(string environmentVariableName, string environmentVariableValue)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables: [(environmentVariableName, environmentVariableValue)]);

        data.AssertMSBuildPropertyValue("IsLLMContext", "true");
        data.AssertMSBuildPropertyValue("_EnableWarningsAsErrors", "true");
        data.AssertMSBuildPropertyValue("MSBuildTreatWarningsAsErrors", "true");
        data.AssertMSBuildPropertyValue("TreatWarningsAsErrors", "true");
        Assert.Contains("NU1903", data.GetMSBuildPropertyValue("WarningsAsErrors"), StringComparison.Ordinal);
        Assert.NotEqual("true", data.GetMSBuildPropertyValue("ContinuousIntegrationBuild"));
    }

    [Theory]
    [InlineData("GEMINI_CLI", "false")]
    [InlineData("GITHUB_COPILOT_CLI_MODE", "0")]
    [InlineData("DROID_CLI", "disabled")]
    [InlineData("KIMI_CLI", "no")]
    [InlineData("AGENT_CLI", "false")]
    [InlineData("OR_APP_NAME", "Unknown")]
    public async Task UnknownLLMContextEnvironmentVariableValues_DoNotEnableWarningsAsErrors(string environmentVariableName, string environmentVariableValue)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables: [(environmentVariableName, environmentVariableValue)]);

        Assert.NotEqual("true", data.GetMSBuildPropertyValue("IsLLMContext"));
        Assert.NotEqual("true", data.GetMSBuildPropertyValue("_EnableWarningsAsErrors"));
        Assert.NotEqual("true", data.GetMSBuildPropertyValue("MSBuildTreatWarningsAsErrors"));
        Assert.NotEqual("true", data.GetMSBuildPropertyValue("TreatWarningsAsErrors"));
        Assert.DoesNotContain("NU1903", data.GetMSBuildPropertyValue("WarningsAsErrors"), StringComparison.Ordinal);
        Assert.NotEqual("true", data.GetMSBuildPropertyValue("ContinuousIntegrationBuild"));
    }

    [Fact]
    public async Task Override_WarningsAsErrors()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("TreatWarningsAsErrors", "false")]);
        project.AddFile("sample.cs", """
            _ = "";

            class Sample
            {
                private readonly int field;

                public Sample(int a) => field = a;

                public int A() => field;
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.True(data.HasWarning("IDE1006"));
    }

    [Fact]
    public async Task NamingConvention_Invalid()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """
            _ = "";

            class Sample
            {
                private readonly int field;

                public Sample(int a) => field = a;

                public int A() => field;
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.True(data.HasError("IDE1006"));
    }

    [Fact]
    public async Task NamingConvention_Valid()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("sample.cs", """
            _ = "";

            class Sample
            {
                private int _field;
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.False(data.HasError("IDE1006"));
        Assert.False(data.HasWarning("IDE1006"));
    }

    [Fact]
    public async Task CodingStyle_UseExpression()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            A();

            static void A()
            {
                System.Console.WriteLine();
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.False(data.HasWarning());
        Assert.False(data.HasError());
    }

    [Fact]
    public async Task CodingStyle_ExpressionIsNeverUsed()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();

            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.False(data.HasWarning());
        Assert.False(data.HasError());
    }

    [Fact]
    public async Task LocalEditorConfigCanOverrideSettings()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            _ = "";

            class Sample
            {
                public static void A()
                {
                    B();

                    static void B()
                    {
                        System.Console.WriteLine();
                    }
                }
            }

            """);
        project.AddFile(".editorconfig", """
            [*.cs]
            csharp_style_expression_bodied_local_functions = true:warning
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Debug"]);
        Assert.True(data.HasWarning());
        Assert.False(data.HasError());
    }

    [Fact]
    public async Task WebEditorConfig_DisablesCA1002()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(rootSdk: "Microsoft.NET.Sdk.Web");
        project.AddFile("Sample.cs", """
            using System.Collections.Generic;

            public sealed class Sample
            {
                public List<int> Items { get; } = new();
            }
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Debug"]);
        Assert.False(data.HasWarning("CA1002"));
        Assert.False(data.HasError("CA1002"));
    }

    [Fact]
    public async Task DefaultEditorConfig_ReportsCA1002()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Sample.cs", """
            using System.Collections.Generic;

            public sealed class Sample
            {
                public List<int> Items { get; } = new();
            }
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Debug"]);
        Assert.True(data.HasWarning("CA1002"));
    }

    [Fact]
    public async Task DefaultEditorConfig_MA0015_ConsidersMemberAccessAsParameter()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Sample.cs", """
            class Request
            {
                public string? Definition { get; set; }
            }

            class Sample
            {
                public void Test(Request request)
                {
                    System.ArgumentNullException.ThrowIfNull(request.Definition);
                }
            }
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Debug"]);
        Assert.False(data.HasWarning("MA0015"));
        Assert.False(data.HasError("MA0015"));
    }

    [Fact]
    public async Task DefaultEditorConfig_MA0015_ReportsThrowIfLessThanWithNameofMemberAccess()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Sample.cs", """
            class Options
            {
                public int ModuleSize { get; set; }
            }

            class Sample
            {
                public void Test(Options options)
                {
                    System.ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleSize, 1, nameof(options.ModuleSize));
                }
            }
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Debug"]);
        Assert.True(data.HasWarning("MA0015"));
        Assert.False(data.HasError("MA0015"));
    }

    [Fact]
    public async Task NuGetAuditIsReportedAsErrorOnGitHubActions()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(nuGetPackages: [new NuGetReference("System.Net.Http", "4.3.3")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput(environmentVariables: [.. project.GitHubEnvironmentVariables]);
        Assert.True(data.OutputContains("error NU1903", StringComparison.Ordinal));
        Assert.Equal(1, data.ExitCode);
    }

    [Fact]
    public async Task NuGetAuditIsReportedAsWarning()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(nuGetPackages: [new NuGetReference("System.Net.Http", "4.3.3")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();
        Assert.True(data.OutputContains("warning NU1903", StringComparison.Ordinal));
        Assert.True(data.OutputDoesNotContain("error NU1903", StringComparison.Ordinal));
        Assert.Equal(0, data.ExitCode);
    }

    [Theory]
    [InlineData("YamlDotNet", "16.3.0", "'Meziantou.Framework.Yaml'", "AllowPackage_YamlDotNet")]
    [InlineData("CliWrap", "3.7.0", "'Meziantou.Framework.ProcessWrapper'", "AllowPackage_CliWrap")]
    [InlineData("Testcontainers", "4.13.0", "'Meziantou.Framework.TemporaryContainers'", "AllowPackage_Testcontainers")]
    [InlineData("Meziantou.Xunit.ParallelTestFramework", "2.3.0", "the built-in parallelization of xunit.v3", "AllowPackage_Meziantou_Xunit_ParallelTestFramework")]
    [InlineData("Meziantou.Xunit.v3.ParallelTestFramework", "1.0.6", "the built-in parallelization of xunit.v3", "AllowPackage_Meziantou_Xunit_v3_ParallelTestFramework")]
    public async Task BannedPackageReference_DirectReference_IsReported(string packageName, string packageVersion, string suggestion, string allowProperty)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("TargetFramework", "net10.0")], nuGetPackages: [new NuGetReference(packageName, packageVersion)]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(1, data.ExitCode);
        Assert.True(data.OutputContains($"Package '{packageName}' is not allowed.", StringComparison.Ordinal));
        Assert.True(data.OutputContains($"Use {suggestion} instead.", StringComparison.Ordinal));
        Assert.True(data.OutputContains($"{allowProperty}=true", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("YamlDotNet", "16.3.0", "'Meziantou.Framework.Yaml'", "AllowPackage_YamlDotNet")]
    [InlineData("CliWrap", "3.7.0", "'Meziantou.Framework.ProcessWrapper'", "AllowPackage_CliWrap")]
    [InlineData("Testcontainers", "4.13.0", "'Meziantou.Framework.TemporaryContainers'", "AllowPackage_Testcontainers")]
    [InlineData("Meziantou.Xunit.ParallelTestFramework", "2.3.0", "the built-in parallelization of xunit.v3", "AllowPackage_Meziantou_Xunit_ParallelTestFramework")]
    [InlineData("Meziantou.Xunit.v3.ParallelTestFramework", "1.0.6", "the built-in parallelization of xunit.v3", "AllowPackage_Meziantou_Xunit_v3_ParallelTestFramework")]
    public async Task BannedPackageReference_TransitiveReference_IsReported(string packageName, string packageVersion, string suggestion, string allowProperty)
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Dependency/Dependency.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{{packageName}}" Version="{{packageVersion}}" />
              </ItemGroup>
            </Project>
            """);
        project.AddFile("Dependency/Class1.cs", "public sealed class Class1 {}\n");
        project.AddCsprojFile(properties: [("TargetFramework", "net10.0")], additionalProjectElements:
        [
            new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "Dependency/Dependency.csproj"))),
        ]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(1, data.ExitCode);
        Assert.True(data.OutputContains($"Package '{packageName}' is not allowed.", StringComparison.Ordinal));
        Assert.True(data.OutputContains($"Use {suggestion} instead.", StringComparison.Ordinal));
        Assert.True(data.OutputContains($"{allowProperty}=true", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("YamlDotNet", "16.3.0", "AllowPackage_YamlDotNet")]
    [InlineData("CliWrap", "3.7.0", "AllowPackage_CliWrap")]
    [InlineData("Testcontainers", "4.13.0", "AllowPackage_Testcontainers")]
    [InlineData("Meziantou.Xunit.ParallelTestFramework", "2.3.0", "AllowPackage_Meziantou_Xunit_ParallelTestFramework")]
    [InlineData("Meziantou.Xunit.v3.ParallelTestFramework", "1.0.6", "AllowPackage_Meziantou_Xunit_v3_ParallelTestFramework")]
    public async Task BannedPackageReference_CanBeAllowedPerPackage(string packageName, string packageVersion, string allowProperty)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("TargetFramework", "net10.0"), (allowProperty, "true")], nuGetPackages: [new NuGetReference(packageName, packageVersion)]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task PackageIncludeAssets_IsRestrictedByDefault()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(nuGetPackages: [new NuGetReference("Newtonsoft.Json", "13.0.4")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Equal("runtime;compile", data.GetMSBuildItemMetadata("PackageReference", "Newtonsoft.Json", "IncludeAssets"));
    }

    [Theory]
    [InlineData("Microsoft.Extensions.Logging", "9.0.0")]
    [InlineData("Meziantou.Framework", "6.0.2")]
    [InlineData("xunit.v3", "4.0.0")]
    public async Task PackageIncludeAssets_IsNotRestrictedForExcludedPackages(string packageName, string packageVersion)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(nuGetPackages: [new NuGetReference(packageName, packageVersion)]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Null(data.GetMSBuildItemMetadata("PackageReference", packageName, "IncludeAssets"));
    }

    [Theory]
    [InlineData("IncludeAssets", "all")]
    [InlineData("ExcludeAssets", "none")]
    [InlineData("PrivateAssets", "all")]
    public async Task PackageIncludeAssets_IsNotRestrictedWhenTheReferenceConfiguresItsAssets(string metadataName, string metadataValue)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(additionalProjectElements:
        [
            new XElement("ItemGroup",
                new XElement("PackageReference",
                    new XAttribute("Include", "Newtonsoft.Json"),
                    new XAttribute("Version", "13.0.4"),
                    new XElement(metadataName, metadataValue))),
        ]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Equal(metadataName is "IncludeAssets" ? metadataValue : null, data.GetMSBuildItemMetadata("PackageReference", "Newtonsoft.Json", "IncludeAssets"));
    }

    [Fact]
    public async Task PackageIncludeAssets_CanBeDisabled()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("EnableDefaultPackageIncludeAssets", "false")], nuGetPackages: [new NuGetReference("Newtonsoft.Json", "13.0.4")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Null(data.GetMSBuildItemMetadata("PackageReference", "Newtonsoft.Json", "IncludeAssets"));
    }

    [Fact]
    public async Task PackageIncludeAssets_CanBeConfigured()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("DefaultPackageIncludeAssets", "compile")], nuGetPackages: [new NuGetReference("Newtonsoft.Json", "13.0.4")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Equal("compile", data.GetMSBuildItemMetadata("PackageReference", "Newtonsoft.Json", "IncludeAssets"));
    }

    [Fact]
    public async Task PackageIncludeAssets_ExcludedPackagePatternCanBeConfigured()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("DefaultPackageIncludeAssetsExcludedPackagePattern", "^Newtonsoft\\.")], nuGetPackages: [new NuGetReference("Newtonsoft.Json", "13.0.4")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Null(data.GetMSBuildItemMetadata("PackageReference", "Newtonsoft.Json", "IncludeAssets"));
    }

    [Fact]
    public async Task PackageIncludeAssets_AnalyzersFromPackagesAreNotImported()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(nuGetPackages: [new NuGetReference("Roslynator.Analyzers", "4.14.1")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain("Roslynator", data.GetCompilerCommandLineArguments(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackageIncludeAssets_AnalyzersFromPackagesAreImportedWhenDisabled()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("EnableDefaultPackageIncludeAssets", "false")], nuGetPackages: [new NuGetReference("Roslynator.Analyzers", "4.14.1")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Contains("Roslynator", data.GetCompilerCommandLineArguments(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackageIncludeAssets_IsRestrictedWithCentralPackageManagement()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="13.0.4" />
              </ItemGroup>
            </Project>
            """);
        project.AddCsprojFile(additionalProjectElements:
        [
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Newtonsoft.Json"))),
        ]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Equal("runtime;compile", data.GetMSBuildItemMetadata("PackageReference", "Newtonsoft.Json", "IncludeAssets"));
    }

    [Fact]
    public async Task PackageIncludeAssets_IsNotRestrictedForExcludedPackagesWithCentralPackageManagement()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Microsoft.Extensions.Logging" Version="9.0.0" />
              </ItemGroup>
            </Project>
            """);
        project.AddCsprojFile(additionalProjectElements:
        [
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Microsoft.Extensions.Logging"))),
        ]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Null(data.GetMSBuildItemMetadata("PackageReference", "Microsoft.Extensions.Logging", "IncludeAssets"));
    }

    [Fact]
    public async Task PackageIncludeAssets_AnalyzersFromPackagesAreNotImportedWithCentralPackageManagement()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Roslynator.Analyzers" Version="4.14.1" />
              </ItemGroup>
            </Project>
            """);
        project.AddCsprojFile(additionalProjectElements:
        [
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Roslynator.Analyzers"))),
        ]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain("Roslynator", data.GetCompilerCommandLineArguments(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackageIncludeAssets_BuildAssetsFromPackagesAreNotImported()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(nuGetPackages: [new NuGetReference("coverlet.msbuild", "6.0.4")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain(data.GetBinLogFiles(), file => file.EndsWith("coverlet.msbuild.props", StringComparison.OrdinalIgnoreCase) || file.EndsWith("coverlet.msbuild.targets", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageIncludeAssets_BuildAssetsFromPackagesAreImportedWhenDisabled()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("EnableDefaultPackageIncludeAssets", "false")], nuGetPackages: [new NuGetReference("coverlet.msbuild", "6.0.4")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Contains(data.GetBinLogFiles(), file => file.EndsWith("coverlet.msbuild.targets", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageIncludeAssets_BuildAssetsFromPackagesAreNotImportedWithCentralPackageManagement()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="coverlet.msbuild" Version="6.0.4" />
              </ItemGroup>
            </Project>
            """);
        project.AddCsprojFile(additionalProjectElements:
        [
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "coverlet.msbuild"))),
        ]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain(data.GetBinLogFiles(), file => file.EndsWith("coverlet.msbuild.props", StringComparison.OrdinalIgnoreCase) || file.EndsWith("coverlet.msbuild.targets", StringComparison.OrdinalIgnoreCase));
    }

    // 'GlobalPackageReference' is meant for the packages providing analyzers and MSBuild logic, so it must not be restricted
    [Fact]
    public async Task PackageIncludeAssets_GlobalPackageReferenceIsNotRestricted()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <GlobalPackageReference Include="Roslynator.Analyzers" Version="4.14.1" />
              </ItemGroup>
            </Project>
            """);
        project.AddCsprojFile();
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Contains("Roslynator", data.GetCompilerCommandLineArguments(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MSBuildWarningsAsError()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Program.cs", """
            System.Console.WriteLine();

            """);
        project.AddCsprojFile(additionalProjectElements: [
            new XElement("Target", new XAttribute("Name", "Custom"), new XAttribute("BeforeTargets", "Build"),
                new XElement("Warning", new XAttribute("Text", "CustomWarning")))]);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);

        Assert.True(data.OutputContains("error : CustomWarning"));
    }

    [Fact]
    public async Task MSBuildWarningsAsError_NotEnableOnDebug()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        project.AddCsprojFile(additionalProjectElements: [
            new XElement("Target", new XAttribute("Name", "Custom"), new XAttribute("BeforeTargets", "Build"),
                new XElement("Warning", new XAttribute("Text", "CustomWarning")))]);
        var data = await project.BuildAndGetOutput(["--configuration", "Debug"]);

        Assert.True(data.OutputContains("warning : CustomWarning"));
    }

    [Fact]
    public async Task CA1708_NotReportedForFileLocalTypes()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Sample1.cs", """
            System.Console.WriteLine();

            class A {}

            file class Sample
            {
            }
            """);
        project.AddFile("Sample2.cs", """
            class B {}

            file class sample
            {
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.False(data.HasError("CA1708"));
        Assert.False(data.HasWarning("CA1708"));
    }

    [Fact]
    public async Task PdbShouldBeEmbedded_Dotnet_Build()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            Console.WriteLine();
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);

        var outputFiles = Directory.GetFiles(project.RootFolder / "bin", "*", SearchOption.AllDirectories);
        await AssertPdbIsEmbedded(outputFiles);
    }

    [Fact]
    public async Task Dotnet_Pack_ClassLibrary()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("OutputType", "Library")]);
        var data = await project.PackAndGetOutput(["--configuration", "Release"]);

        var extractedPath = project.RootFolder / "extracted";
        var files = Directory.GetFiles(project.RootFolder / "bin" / "Release");
        Assert.Single(files); // Only the .nupkg should be generated
        var nupkg = files.Single(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        ZipFile.ExtractToDirectory(nupkg, extractedPath);

        var outputFiles = Directory.GetFiles(extractedPath, "*", SearchOption.AllDirectories);
        await AssertPdbIsEmbedded(outputFiles);
        Assert.Contains(outputFiles, f => f.EndsWith(".xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PdbShouldBeEmbedded_Dotnet_Pack()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            Console.WriteLine();

            """);

        var data = await project.PackAndGetOutput(["--configuration", "Release"]);

        var extractedPath = project.RootFolder / "extracted";
        var files = Directory.GetFiles(project.RootFolder / "bin" / "Release");
        Assert.Single(files); // Only the .nupkg should be generated
        var nupkg = files.Single(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        ZipFile.ExtractToDirectory(nupkg, extractedPath);

        var outputFiles = Directory.GetFiles(extractedPath, "*", SearchOption.AllDirectories);
        await AssertPdbIsEmbedded(outputFiles);
        Assert.Contains(outputFiles, f => f.EndsWith(".xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PackageShouldContainsXmlDocumentation()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            Console.WriteLine();
            """);

        var data = await project.PackAndGetOutput();

        var extractedPath = project.RootFolder / "extracted";
        var files = Directory.GetFiles(project.RootFolder / "bin" / "Release");
        Assert.Single(files); // Only the .nupkg should be generated
        var nupkg = files.Single(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        ZipFile.ExtractToDirectory(nupkg, extractedPath);

        var outputFiles = Directory.GetFiles(extractedPath, "*", SearchOption.AllDirectories);
        Assert.Contains(outputFiles, f => f.EndsWith(".xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DocumentationWarnings_SuppressedByDefault()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("OutputType", "Library")]);
        project.AddFile("Sample.cs", """
            public class Sample
            {
                /// <param name="undocumented">Missing param doc triggers CS1573</param>
                public static void Method(int undocumented, int alsoUndocumented) { }

                public int UndocumentedProperty { get; set; }
            }
            """);
        var data = await project.BuildAndGetOutput();
        Assert.False(data.HasWarning("CS1573"));
        Assert.False(data.HasWarning("CS1591"));
    }

    [Fact]
    public async Task DocumentationWarnings_ReportedWhenEnabled()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("OutputType", "Library"), ("DisableDocumentationWarnings", "false")]);
        project.AddFile("Sample.cs", """
            public class Sample
            {
                public int UndocumentedProperty { get; set; }
            }
            """);
        var data = await project.BuildAndGetOutput();
        Assert.True(data.HasWarning("CS1591"));
    }

    [Theory]
    [InlineData("readme.md")]
    [InlineData("Readme.md")]
    [InlineData("ReadMe.md")]
    [InlineData("README.md")]
    public async Task Pack_ReadmeFromCurrentFolder(string readmeFileName)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile(readmeFileName, "sample");

        var data = await project.PackAndGetOutput(["--configuration", "Release"]);

        var extractedPath = project.RootFolder / "extracted";
        var files = Directory.GetFiles(project.RootFolder / "bin" / "Release");
        Assert.Single(files); // Only the .nupkg should be generated
        var nupkg = files.Single(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        ZipFile.ExtractToDirectory(nupkg, extractedPath);
        var allFiles = Directory.GetFiles(extractedPath);
        Assert.Contains("README.md", allFiles.Select(Path.GetFileName));
        Assert.Equal("sample", File.ReadAllText(extractedPath / "README.md"));
    }

    [Fact]
    public async Task Pack_ReadmeFromAboveCurrentFolder_SearchReadmeFileAbove_True()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            filename: "dir/Test.csproj",
            properties: [("SearchReadmeFileAbove", "true")]);
        project.AddFile("dir/Program.cs", "Console.WriteLine();");
        project.AddFile("README.md", "sample");

        var data = await project.PackAndGetOutput(["dir", "--configuration", "Release"]);

        var extractedPath = project.RootFolder / "extracted";
        var files = Directory.GetFiles(project.RootFolder / "dir" / "bin" / "Release");
        Assert.Single(files); // Only the .nupkg should be generated
        var nupkg = files.Single(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        ZipFile.ExtractToDirectory(nupkg, extractedPath);

        Assert.Equal("sample", File.ReadAllText(extractedPath / "README.md"));
    }

    [Fact]
    public async Task Pack_ReadmeFromAboveCurrentFolder_SearchReadmeFileAbove_False()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(filename: "dir/Test.csproj");
        project.AddFile("dir/Program.cs", "Console.WriteLine();");
        project.AddFile("README.md", "sample");

        var data = await project.PackAndGetOutput(["dir", "--configuration", "Release"]);

        var extractedPath = project.RootFolder / "extracted";
        var files = Directory.GetFiles(project.RootFolder / "dir" / "bin" / "Release");
        Assert.Single(files); // Only the .nupkg should be generated
        var nupkg = files.Single(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        ZipFile.ExtractToDirectory(nupkg, extractedPath);

        Assert.False(File.Exists(extractedPath / "README.md"));
    }

    [Theory]
    [InlineData("THIRD-PARTY-NOTICES.TXT")]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    public async Task Pack_ThirdPartyNotices(string noticesFileName)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile(noticesFileName, "sample");

        var data = await project.PackAndGetOutput(["--configuration", "Release"]);

        var extractedPath = project.RootFolder / "extracted";
        var files = Directory.GetFiles(project.RootFolder / "bin" / "Release");
        var nupkg = files.Single(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        ZipFile.ExtractToDirectory(nupkg, extractedPath);

        Assert.Equal("sample", File.ReadAllText(extractedPath / noticesFileName));
    }

    [Fact]
    public async Task NonMeziantouCsproj_DoesNotIncludePackageProperties()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(filename: "sample.csproj");
        project.AddFile("Program.cs", """Console.WriteLine();""");
        project.AddFile("LICENSE.txt", """dummy""");
        var data = await project.PackAndGetOutput();
        Assert.Equal(0, data.ExitCode);

        var package = Directory.GetFiles(project.RootFolder, "*.nupkg", SearchOption.AllDirectories).Single();
        using var packageReader = new PackageArchiveReader(package);
        var nuspecReader = await packageReader.GetNuspecReaderAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual("meziantou", nuspecReader.GetAuthors());
        Assert.NotEqual("icon.png", nuspecReader.GetIcon());
        Assert.DoesNotContain("icon.png", packageReader.GetFiles());
    }

    [Fact]
    public async Task MeziantouCsproj_DoesIncludePackageProperties()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", """Console.WriteLine();""");
        project.AddFile("LICENSE.txt", """dummy""");
        var data = await project.PackAndGetOutput();
        Assert.Equal(0, data.ExitCode);

        var package = Directory.GetFiles(project.RootFolder, "*.nupkg", SearchOption.AllDirectories).Single();
        using var packageReader = new PackageArchiveReader(package);
        var nuspecReader = await packageReader.GetNuspecReaderAsync(TestContext.Current.CancellationToken);
        Assert.Equal("meziantou", nuspecReader.GetAuthors());
        Assert.Equal("icon.png", nuspecReader.GetIcon());
        Assert.Contains("icon.png", packageReader.GetFiles());
        Assert.Contains("LICENSE.txt", packageReader.GetFiles());
    }

    [Fact]
    public async Task MeziantouAnalyzerCsproj()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(filename: "Meziantou.Analyzer.csproj");
        project.AddFile("Program.cs", """Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task MTP_DotnetTestSkipAnalyzers()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("UseMicrosoftTestingPlatform", "true")],
            nuGetPackages: [.. XUnit3MTP2References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                    _ = System.DateTime.Now; // This should not be reported as an error
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        Assert.False(data.HasWarning("RS0030"));
        Assert.True(data.IsMSBuildTargetExecuted("_MTPBuild"));
    }

    [Fact]
    public async Task MTP_DotnetTestSkipAnalyzers_OptOut()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("OptimizeTestRun", "false")],
            nuGetPackages: [.. XUnit3MTP2References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                    _ = System.DateTime.Now; // This should be reported as the analyzers are not disabled
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        Assert.True(data.HasWarning("RS0030"));
        Assert.True(data.IsMSBuildTargetExecuted("_MTPBuild"));
    }

    [Fact]
    public async Task MTP_OnUnknownContextShouldNotAddCustomLogger()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("UseMicrosoftTestingPlatform", "true")],
            nuGetPackages: [.. XUnit3MTP2References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                    Assert.Fail("failure message");
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(2, data.ExitCode);
        Assert.True(data.OutputContains("failure message", StringComparison.Ordinal));
        Assert.Empty(project.GetGitHubStepSummaryContent());
        Assert.NotEmpty(Directory.GetFiles(project.RootFolder, "*.trx", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(project.RootFolder, "*.coverage", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MTP_SuccessTests(bool addUseMicrosoftTestingPlatformProperty)
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: addUseMicrosoftTestingPlatformProperty ? [("UseMicrosoftTestingPlatform", "true")] : [],
            nuGetPackages: [.. XUnit3MTP2References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.NotEmpty(Directory.GetFiles(project.RootFolder, "*.trx", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MTP_NoTest()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("UseMicrosoftTestingPlatform", "true")],
            nuGetPackages: [.. XUnit3MTP2References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(8, data.ExitCode);
    }

    [Fact]
    public async Task MTP_DefaultTestFramework_AddsXunit()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.True(data.IsMSBuildTargetExecuted("_MTPBuild"));

        var packageReferences = data.GetMSBuildItems("PackageReference");
        Assert.Contains("xunit.v3.mtp-v2", packageReferences, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.NET.Test.Sdk", packageReferences, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(Directory.GetFiles(project.RootFolder, "*.trx", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MTP_DefaultTestFramework_NotAddedWhenATestFrameworkIsReferenced()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            nuGetPackages: [.. XUnit3References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain("xunit.v3.mtp-v2", data.GetMSBuildItems("PackageReference"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MTP_DefaultTestFramework_OptOut()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableDefaultTestFramework", "false")],
            nuGetPackages: [.. XUnit3References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Xunit.Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);

        var packageReferences = data.GetMSBuildItems("PackageReference");
        Assert.DoesNotContain("xunit.v3.mtp-v2", packageReferences, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.NET.Test.Sdk", packageReferences, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MTP_MeziantouAssertions_AssertRefersToMeziantouAssertions()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        // 'Assert' must be resolved to the Meziantou.Framework.Assertions type without any using directive
        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1() => Assert.Equal(typeof(Meziantou.Framework.Assertions.Assert), typeof(Assert));
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Contains("Meziantou.Framework.Assertions", data.GetMSBuildItems("PackageReference"), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Meziantou.Framework.Assertions.Assert", data.GetMSBuildItems("Using"), StringComparer.Ordinal);
    }

    // Only the meaning of 'Assert' changes: the assertions of the test framework stay available
    [Fact]
    public async Task MTP_MeziantouAssertions_XunitAssertIsStillAvailable()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1() => Xunit.Assert.True(true);
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task MTP_MeziantouAssertions_OptOut()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableMeziantouAssertions", "false")]
            );

        // 'Assert' must still be resolved to the xUnit.net type
        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1() => Assert.Equal(typeof(Xunit.Assert), typeof(Assert));
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain("Meziantou.Framework.Assertions", data.GetMSBuildItems("PackageReference"), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Meziantou.Framework.Assertions.Assert", data.GetMSBuildItems("Using"), StringComparer.Ordinal);
    }

    // The package must not replace the assertions of a test framework referenced by the project itself
    [Fact]
    public async Task MTP_MeziantouAssertions_NotAddedWhenATestFrameworkIsReferenced()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            nuGetPackages: [.. XUnit3References]
            );

        // 'Assert' must still be resolved to the xUnit.net type
        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1() => Assert.Equal(typeof(Xunit.Assert), typeof(Assert));
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain("Meziantou.Framework.Assertions", data.GetMSBuildItems("PackageReference"), StringComparer.OrdinalIgnoreCase);
    }

    // The package only supports .NET 10 and later, so it must not break projects targeting an older framework
    [Fact]
    public async Task MTP_MeziantouAssertions_NotAddedForUnsupportedTargetFramework()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("TargetFramework", "net8.0")]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1() => Assert.Equal(typeof(Xunit.Assert), typeof(Assert));
            }
            """);

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain("Meziantou.Framework.Assertions", data.GetMSBuildItems("PackageReference"), StringComparer.OrdinalIgnoreCase);
    }

    // Setting the property explicitly adds the package even when the project references the test framework itself
    [Fact]
    public async Task MTP_MeziantouAssertions_ExplicitlyEnabled()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableMeziantouAssertions", "true")],
            nuGetPackages: [.. XUnit3References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1() => Assert.Equal(typeof(Meziantou.Framework.Assertions.Assert), typeof(Assert));
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Contains("Meziantou.Framework.Assertions", data.GetMSBuildItems("PackageReference"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MTP_XunitFullParallelization_RunsTestsOfSameClassInParallelByDefault()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        // Both tests can only complete when they run at the same time
        project.AddFile("Program.cs", """
            public class Tests
            {
                private static readonly System.Threading.Barrier Barrier = new(participantCount: 2);

                [Fact]
                public void Test1() => Assert.True(Barrier.SignalAndWait(System.TimeSpan.FromSeconds(60)));

                [Fact]
                public void Test2() => Assert.True(Barrier.SignalAndWait(System.TimeSpan.FromSeconds(60)));
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Contains(data.GetMSBuildItems("Compile"), item => item.EndsWith("Meziantou.NET.Sdk.XunitParallelization.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MTP_XunitFullParallelization_OptOut()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableXunitFullParallelization", "false")]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.False(data.IsMSBuildTargetExecuted("GenerateXunitParallelizationSourceFile"));
        Assert.DoesNotContain(data.GetMSBuildItems("Compile"), item => item.Contains("XunitParallelization", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "All")]
    [InlineData("All", "All")]
    [InlineData("Collections", "Collections")]
    [InlineData("None", "None")]
    [InlineData("collections", "Collections")]
    public async Task MTP_XunitParallelizationMode(string mode, string expectedMode)
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: mode.Length == 0 ? null : [("XunitParallelizationMode", mode)]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);

        var generatedFile = Directory.GetFiles(project.RootFolder, "Meziantou.NET.Sdk.XunitParallelization.g.cs", SearchOption.AllDirectories).Single();
        Assert.Contains($"[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.{expectedMode})]", File.ReadAllText(generatedFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MTP_XunitParallelizationMode_InvalidValueIsReported()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("XunitParallelizationMode", "Collection")]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        var data = await project.BuildAndGetOutput();

        Assert.Equal(1, data.ExitCode);
        Assert.True(data.OutputContains("'XunitParallelizationMode' has an invalid value: 'Collection'.", StringComparison.Ordinal));
    }

    // 'Xunit.v3.ParallelizationAttribute' is only available when xUnit.net v3 is referenced, so the file must not be generated
    [Fact]
    public async Task MTP_XunitFullParallelization_NotGeneratedWhenXunitIsNotReferenced()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableDefaultTestFramework", "false")]
            );

        project.AddFile("Program.cs", """System.Console.WriteLine();""");

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain(data.GetMSBuildItems("Compile"), item => item.Contains("XunitParallelization", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MTP_XunitStaticHelpers_XunitCancellationTokenIsAvailableByDefault()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        // 'XunitCancellationToken' must be usable without any using directive
        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public async Task Test1()
                {
                    Assert.False(XunitCancellationToken.IsCancellationRequested);
                    await Task.Delay(1, XunitCancellationToken);
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Contains(data.GetMSBuildItems("Compile"), item => item.EndsWith("Meziantou.NET.Sdk.XunitStaticHelpers.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MTP_XunitStaticHelpers_ClassIsPartial()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        // The project can add its own helpers to the generated class
        project.AddFile("Program.cs", """
            namespace Meziantou.NET.Sdk.Test
            {
                internal static partial class XUnitStaticHelpers
                {
                    public static int SampleHelper => 42;
                }
            }

            public class Tests
            {
                [Fact]
                public void Test1() => Assert.Equal(42, SampleHelper);
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task MTP_XunitStaticHelpers_OptOut()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableXunitStaticHelpers", "false")]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.False(data.IsMSBuildTargetExecuted("GenerateXunitStaticHelpersSourceFile"));
        Assert.DoesNotContain(data.GetMSBuildItems("Compile"), item => item.Contains("XunitStaticHelpers", StringComparison.Ordinal));
    }

    // 'Xunit.TestContext' is only available when xUnit.net v3 is referenced, so the file must not be generated
    [Fact]
    public async Task MTP_XunitStaticHelpers_NotGeneratedWhenXunitIsNotReferenced()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableDefaultTestFramework", "false")]
            );

        project.AddFile("Program.cs", """System.Console.WriteLine();""");

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.False(data.IsMSBuildTargetExecuted("GenerateXunitStaticHelpersSourceFile"));
        Assert.DoesNotContain(data.GetMSBuildItems("Compile"), item => item.Contains("XunitStaticHelpers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MTP_XunitEntryPointDisableWarningsConstantIsDefined()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        project.AddFile("Program.cs", """
            #if !XUNIT_ENTRYPOINT_DISABLE_WARNINGS
            #error XUNIT_ENTRYPOINT_DISABLE_WARNINGS is not defined
            #endif

            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task MTP_XunitEntryPointDisableWarningsConstant_OptOut()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableXunitEntryPointDisableWarnings", "false")]
            );

        project.AddFile("Program.cs", """
            #if XUNIT_ENTRYPOINT_DISABLE_WARNINGS
            #error XUNIT_ENTRYPOINT_DISABLE_WARNINGS is defined
            #endif

            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
    }

    // 'dotnet test' renders the results itself in MTP mode, so '--output Detailed' must be set on the command line.
    // The default arguments added by the SDK must not conflict with it.
    [Fact]
    public async Task MTP_DetailedOutputListsSucceededTests()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            nuGetPackages: [.. XUnit3References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void SampleSucceedingTest()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput(["--output", "Detailed"]);

        Assert.Equal(0, data.ExitCode);
        Assert.True(data.OutputContains("SampleSucceedingTest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MTP_GitHubActionsReport()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput(environmentVariables: [.. project.GitHubEnvironmentVariables]);

        Assert.Equal(0, data.ExitCode);
        Assert.NotEmpty(project.GetGitHubStepSummaryContent());
    }

    [Fact]
    public async Task MTP_GitHubActionsReport_OptOut()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("EnableGitHubActionsReport", "false")]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput(environmentVariables: [.. project.GitHubEnvironmentVariables]);

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain("Microsoft.Testing.Extensions.GitHubActionsReport", data.GetMSBuildItems("PackageReference"), StringComparer.OrdinalIgnoreCase);
        Assert.Empty(project.GetGitHubStepSummaryContent());
    }

    [Fact]
    public async Task MTP_MinimumExpectedTests()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        var data = await project.BuildAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.Contains("--minimum-expected-tests 1", data.GetMSBuildPropertyValue("TestingPlatformCommandLineArguments"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MTP_MinimumExpectedTests_CustomValue()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("MinimumExpectedTests", "2")]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(9, data.ExitCode);
        Assert.Contains("--minimum-expected-tests 2", data.GetMSBuildPropertyValue("TestingPlatformCommandLineArguments"), StringComparison.Ordinal);
    }

    // Microsoft.Testing.Platform reports an invalid command line when '--minimum-expected-tests' is set to '0',
    // so the argument must not be set at all
    [Fact]
    public async Task MTP_MinimumExpectedTests_Zero()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [("MinimumExpectedTests", "0")]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("global.json", """
            {
                "test": {
                    "runner": "Microsoft.Testing.Platform"
                }
            }
            """);

        var data = await project.TestAndGetOutput();

        Assert.Equal(0, data.ExitCode);
        Assert.DoesNotContain("--minimum-expected-tests", data.GetMSBuildPropertyValue("TestingPlatformCommandLineArguments"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CentralPackageManagement()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            sdk: SdkTestName,
            filename: "Sample.Tests.csproj"
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                }
            }
            """);

        project.AddFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
              </PropertyGroup>
              <ItemGroup>
              </ItemGroup>
            </Project>
            """);

        var data = await project.BuildAndGetOutput();
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task SuppressNuGetAudit_NoSuppression_Fails()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            nuGetPackages: [new NuGetReference("System.Net.Http", "4.3.3")],
            properties: [("NOWARN", "$(NOWARN);NU1510")]);

        project.AddFile("Program.cs", """
            Console.WriteLine();
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.Equal(1, data.ExitCode);
    }

    [Fact]
    public async Task SuppressNuGetAudit_Suppressed()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            nuGetPackages: [new NuGetReference("System.Net.Http", "4.3.3")],
            additionalProjectElements: [new XElement("ItemGroup", new XElement("NuGetAuditSuppress", new XAttribute("Include", "https://github.com/advisories/GHSA-7jgj-8wvc-jh57")))],
            properties: [("NOWARN", "$(NOWARN);NU1510")]);

        project.AddFile("Program.cs", """
            Console.WriteLine();
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task Pack_ContainsMetadata()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            sdk: SdkName,
            filename: "Meziantou.Sample.csproj",
            properties: [("OutputType", "library")]
            );

        project.AddFile("Class1.cs", """
            namespace Meziantou.Sample;

            public static class Class1
            {
            }
            """);

        await project.ExecuteGitCommand("init");
        await project.ExecuteGitCommand("add", ".");
        await project.ExecuteGitCommand("commit", "-m", "sample");
        await project.ExecuteGitCommand("remote", "add", "origin", "https://github.com/meziantou/sample.git");

        var data = await project.PackAndGetOutput(environmentVariables: [.. project.GitHubEnvironmentVariables]);
        Assert.Equal(0, data.ExitCode);

        // Validate nupkg
        var package = Directory.GetFiles(project.RootFolder, "*.nupkg", SearchOption.AllDirectories).Single();
        using var packageReader = new PackageArchiveReader(package);
        var nuspecReader = await packageReader.GetNuspecReaderAsync(TestContext.Current.CancellationToken);
        Assert.Equal("meziantou", nuspecReader.GetAuthors());
        Assert.Equal("icon.png", nuspecReader.GetIcon());
        Assert.Equal(LicenseType.Expression, nuspecReader.GetLicenseMetadata().Type);
        Assert.Equal(LicenseExpressionType.License, nuspecReader.GetLicenseMetadata().LicenseExpression.Type);
        Assert.Equal("MIT", ((NuGetLicense)nuspecReader.GetLicenseMetadata().LicenseExpression).Identifier);
        Assert.Equal("git", nuspecReader.GetRepositoryMetadata().Type);
        Assert.Equal("https://github.com/meziantou/sample.git", nuspecReader.GetRepositoryMetadata().Url);
        Assert.Equal("refs/heads/main", nuspecReader.GetRepositoryMetadata().Branch);
        Assert.NotEmpty(nuspecReader.GetRepositoryMetadata().Commit);
    }

    [Fact]
    public async Task Web_HasServiceDefaults()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(rootSdk: "Microsoft.NET.Sdk.Web");

        project.AddFile("Program.cs", """
            using Meziantou.AspNetCore.ServiceDefaults;

            var builder = WebApplication.CreateBuilder();
            builder.UseMeziantouConventions();
            """);

        var data = await project.BuildAndGetOutput(environmentVariables: [.. project.GitHubEnvironmentVariables]);
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task Web_ServiceDefaultsIsRegisteredAutomatically()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(rootSdk: "Microsoft.NET.Sdk.Web");

        project.AddFile("Program.cs", """
            using Meziantou.AspNetCore.ServiceDefaults;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();
            return app.Services.GetService<MeziantouServiceDefaultsOptions>() is not null ? 0 : 1;
            """);

        var data = await project.RunAndGetOutput();
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task Web_ServiceDefaultsIsRegisteredAutomatically_Disabled()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(
            rootSdk: "Microsoft.NET.Sdk.Web",
            properties: [("AutoRegisterServiceDefaults", "false")]);

        project.AddFile("Program.cs", """
            using Meziantou.AspNetCore.ServiceDefaults;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();
            return app.Services.GetService<MeziantouServiceDefaultsOptions>() is not null ? 0 : 1;
            """);

        var data = await project.RunAndGetOutput();
        Assert.NotEqual(0, data.ExitCode);
    }

    [Fact]
    public async Task Web_ContainerDefaultsOnGitHubActions_UsePreviewTags()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(rootSdk: "Microsoft.NET.Sdk.Web");
        project.AddFile("Program.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables:
        [
            .. project.GitHubEnvironmentVariables,
            ("GITHUB_REPOSITORY", "meziantou/Meziantou.SampleProject"),
            ("GITHUB_SHA", "0123456789abcdef"),
            ("GITHUB_REF_NAME", "feature/test"),
        ]);

        data.AssertMSBuildPropertyValue("EnableSdkContainerSupport", "true");
        data.AssertMSBuildPropertyValue("ContainerRegistry", "ghcr.io");
        data.AssertMSBuildPropertyValue("ContainerRepository", "meziantou/meziantou-sample-project");
        data.AssertMSBuildPropertyValue("ContainerImageTags", "0.0.1-preview.0123456789abcdef");
    }

    [Fact]
    public async Task Web_ContainerDefaultsOnGitHubActions_UseMainTags()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(rootSdk: "Microsoft.NET.Sdk.Web");
        project.AddFile("Program.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables:
        [
            .. project.GitHubEnvironmentVariables,
            ("GITHUB_REPOSITORY", "meziantou/Meziantou.SampleProject"),
            ("GITHUB_SHA", "fedcba9876543210"),
            ("GITHUB_REF_NAME", "main"),
            ("GITHUB_RUN_NUMBER", "42"),
        ]);

        data.AssertMSBuildPropertyValue("EnableSdkContainerSupport", "true");
        data.AssertMSBuildPropertyValue("ContainerRegistry", "ghcr.io");
        data.AssertMSBuildPropertyValue("ContainerRepository", "meziantou/meziantou-sample-project");
        data.AssertMSBuildPropertyValue("ContainerImageTags", "1.0.42;latest");
    }

    [Fact]
    public async Task Web_ContainerDefaultsOnGitHubActions_MainTagsPrefixCanBeOverridden()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(
            rootSdk: "Microsoft.NET.Sdk.Web",
            properties: [("ContainerImageTagsMainVersionPrefix", "2.5")]);
        project.AddFile("Program.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables:
        [
            .. project.GitHubEnvironmentVariables,
            ("GITHUB_REPOSITORY", "meziantou/Meziantou.SampleProject"),
            ("GITHUB_REF_NAME", "main"),
            ("GITHUB_RUN_NUMBER", "7"),
        ]);

        data.AssertMSBuildPropertyValue("ContainerImageTags", "2.5.7;latest");
    }

    [Fact]
    public async Task Web_ContainerDefaultsOnGitHubActions_LatestTagCanBeDisabled()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(
            rootSdk: "Microsoft.NET.Sdk.Web",
            properties: [("ContainerImageTagsIncludeLatest", "false")]);
        project.AddFile("Program.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables:
        [
            .. project.GitHubEnvironmentVariables,
            ("GITHUB_REPOSITORY", "meziantou/Meziantou.SampleProject"),
            ("GITHUB_REF_NAME", "main"),
            ("GITHUB_RUN_NUMBER", "13"),
        ]);

        data.AssertMSBuildPropertyValue("ContainerImageTags", "1.0.13");
    }

    [Fact]
    public async Task GitHubVersion_TagWithVPrefix_UsesTagVersion()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("Version", "9.9.9")]);
        project.AddFile("Program.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables:
        [
            .. project.GitHubEnvironmentVariables,
            ("GITHUB_REF_TYPE", "tag"),
            ("GITHUB_REF_NAME", "v2.3.4"),
            ("GITHUB_SHA", "0123456789abcdef"),
        ]);

        data.AssertMSBuildPropertyValue("Version", "2.3.4");
    }

    [Fact]
    public async Task GitHubVersion_InvalidTag_UsesBuildSuffix()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("Version", "1.0.0")]);
        project.AddFile("Program.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables:
        [
            .. project.GitHubEnvironmentVariables,
            ("GITHUB_REF_TYPE", "tag"),
            ("GITHUB_REF_NAME", "release-2026-02-13"),
            ("GITHUB_SHA", "abcdef0123456789"),
        ]);

        data.AssertMSBuildPropertyValue("Version", "1.0.0-build-abcdef0123456789");
    }

    [Fact]
    public async Task GitHubVersion_MainBranch_UsesBaseVersion()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("Version", "3.2.1")]);
        project.AddFile("Program.cs", "Console.WriteLine();");

        var data = await project.BuildAndGetOutput(environmentVariables:
        [
            .. project.GitHubEnvironmentVariables,
            ("GITHUB_REF_NAME", "main"),
            ("GITHUB_SHA", "1111111111111111"),
        ]);

        data.AssertMSBuildPropertyValue("Version", "3.2.1");
    }

    [Theory]
    [InlineData(SdkName)]
    [InlineData(SdkTestName)]
    [InlineData(SdkWebName)]
    public async Task AssemblyContainsMetadataAttributeWithSdkName(string sdkName)
    {
        await using var project = CreateProjectBuilder(sdkName);
        project.AddCsprojFile(filename: "Sample.Tests.csproj", properties: [("EnableDefaultTestFramework", "false")]);

        project.AddDirectoryBuildPropsFile(postSdkContent: "");

        project.AddFile("Program.cs", """
            Console.WriteLine();
            """);

        var data = await project.BuildAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        var dllPath = Directory.GetFiles(project.RootFolder / "bin" / "Debug", "Sample.Tests.dll", SearchOption.AllDirectories).Single();

        await using var assembly = File.OpenRead(dllPath);
        using var reader = new PEReader(assembly);
        var metadata = reader.GetMetadataReader();
        foreach (var attrHandle in metadata.CustomAttributes)
        {
            var customAttribute = metadata.GetCustomAttribute(attrHandle);
            var attributeType = customAttribute.Constructor;
            var typeName = metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)metadata.GetMemberReference(((MemberReferenceHandle)attributeType)).Parent).Name);
            if (typeName is "AssemblyMetadataAttribute")
            {
                var blobReader = metadata.GetBlobReader(customAttribute.Value);
                _ = blobReader.ReadSerializedString();
                var key = blobReader.ReadSerializedString();
                var value = blobReader.ReadSerializedString();

                Assert.Equal("Meziantou.Sdk.Name", key);
                Assert.Equal(sdkName, value);
                return;
            }
        }

        Assert.Fail("Attribute not found");
    }

    [Theory]
    [InlineData("TargetFramework", "")]
    [InlineData("TargetFrameworks", "")]
    [InlineData("TargetFramework", "net10.0")]
    [InlineData("TargetFrameworks", "net10.0")]
    public async Task SetTargetFramework(string propName, string version)
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            properties: [(propName, version)]);

        project.AddFile("Program.cs", """
            Console.WriteLine();
            """);

        var data = await project.BuildAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        var dllPath = Directory.GetFiles(project.RootFolder / "bin" / "Debug", "Sample.Tests.dll", SearchOption.AllDirectories).Single();

        var expectedVersion = version;
        if (string.IsNullOrEmpty(expectedVersion))
        {
            expectedVersion = propName switch
            {
                "TargetFramework" or "TargetFrameworks" => dotnetSdkVersion switch
                {
                    NetSdkVersion.Net10_0 => "net10.0",
                    NetSdkVersion.Net11_0 => "net11.0",
                    _ => throw new NotSupportedException(),
                },
                _ => throw new NotSupportedException(),
            };
        }

        await using var assembly = File.OpenRead(dllPath);
        using var reader = new PEReader(assembly);
        var metadata = reader.GetMetadataReader();
        foreach (var attrHandle in metadata.CustomAttributes)
        {
            var customAttribute = metadata.GetCustomAttribute(attrHandle);
            var attributeType = customAttribute.Constructor;
            var typeName = metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)metadata.GetMemberReference(((MemberReferenceHandle)attributeType)).Parent).Name);
            if (typeName is "TargetFrameworkAttribute")
            {
                var blobReader = metadata.GetBlobReader(customAttribute.Value);
                _ = blobReader.ReadSerializedString();
                var key = blobReader.ReadSerializedString();

                Assert.Contains(expectedVersion.Replace("net", "v", StringComparison.Ordinal), key);
                return;
            }
        }

        Assert.Fail("Attribute not found");
    }

    private static string[] GetNpmStampFiles(FullPath projectFolder)
    {
        var nodeModules = projectFolder / "node_modules";
        return Directory.Exists(nodeModules) ? Directory.GetFiles(nodeModules, ".npm-install-stamp-*") : [];
    }

    private static string GetSingleNpmStampFileName(FullPath projectFolder)
    {
        return Path.GetFileName(Assert.Single(GetNpmStampFiles(projectFolder)));
    }

    private static void AssertNpmStampFileExists(FullPath projectFolder)
    {
        // The stamp file is environment-specific, so the same node_modules folder can be used from multiple environments
        // (e.g. a Windows host and a dev container) without skipping the npm restore
        var expectedPrefix = ".npm-install-stamp-" + (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux");
        var stampFile = Assert.Single(GetNpmStampFiles(projectFolder));
        Assert.StartsWith(expectedPrefix, Path.GetFileName(stampFile), StringComparison.Ordinal);
    }

    private static void AssertNpmStampFileDoesNotExist(FullPath projectFolder)
    {
        Assert.Empty(GetNpmStampFiles(projectFolder));
    }

    [Fact]
    public async Task NpmInstall()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile();

        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile("package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);

        var data = await project.BuildAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        Assert.True(File.Exists(project.RootFolder / "package-lock.json"));
        AssertNpmStampFileExists(project.RootFolder);
        var files = data.GetBinLogFiles();
        Assert.Contains(files, f => f.EndsWith("package-lock.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GitHubActionsEnvironmentVariablesAreEmbeddedInBinLog()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", "Console.WriteLine();");

        var environmentVariables = new (string Name, string Value)[]
        {
            ("GITHUB_ACTIONS", "true"),
            ("GITHUB_JOB", "build-job"),
            ("GITHUB_WORKFLOW", "Build Workflow"),
            ("GITHUB_ACTION", "build-action"),
            ("GITHUB_RUN_ID", "789012"),
            ("GITHUB_RUN_NUMBER", "99"),
            ("GITHUB_RUN_ATTEMPT", "2"),
            ("GITHUB_REPOSITORY", "meziantou/test-repo"),
            ("GITHUB_REPOSITORY_OWNER", "meziantou"),
            ("GITHUB_REF", "refs/heads/main"),
            ("GITHUB_REF_NAME", "main"),
            ("GITHUB_SHA", "abc123def456"),
            ("GITHUB_ACTOR", "testuser"),
            ("RUNNER_NAME", "Runner-1"),
            ("RUNNER_OS", "Windows"),
            ("RUNNER_ARCH", "X64")
        };

        var data = await project.BuildAndGetOutput(environmentVariables: environmentVariables);
        Assert.Equal(0, data.ExitCode);

        data.AssertMSBuildPropertyValue("_GitHubJobId", "build-job");
        data.AssertMSBuildPropertyValue("_GitHubWorkflow", "Build Workflow");
        data.AssertMSBuildPropertyValue("_GitHubAction", "build-action");
        data.AssertMSBuildPropertyValue("_GitHubRunId", "789012");
        data.AssertMSBuildPropertyValue("_GitHubRunNumber", "99");
        data.AssertMSBuildPropertyValue("_GitHubRunAttempt", "2");
        data.AssertMSBuildPropertyValue("_GitHubRepository", "meziantou/test-repo");
        data.AssertMSBuildPropertyValue("_GitHubRepositoryOwner", "meziantou");
        data.AssertMSBuildPropertyValue("_GitHubRef", "refs/heads/main");
        data.AssertMSBuildPropertyValue("_GitHubRefName", "main");
        data.AssertMSBuildPropertyValue("_GitHubSha", "abc123def456");
        data.AssertMSBuildPropertyValue("_GitHubActor", "testuser");
        data.AssertMSBuildPropertyValue("_RunnerName", "Runner-1");
        data.AssertMSBuildPropertyValue("_RunnerOs", "Windows");
        data.AssertMSBuildPropertyValue("_RunnerArch", "X64");
    }

    [Fact]
    public async Task NpmRestore()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile();

        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile("package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);

        var data = await project.RestoreAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        Assert.True(File.Exists(project.RootFolder / "package-lock.json"));
        AssertNpmStampFileExists(project.RootFolder);
    }

    [Fact]
    public async Task NpmRestore_StampFileIsEnvironmentSpecific()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile();

        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile("package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);

        var data = await project.RestoreAndGetOutput(["/p:NpmStampFileIdentifier=env1"]);
        Assert.Equal(0, data.ExitCode);
        Assert.True(data.IsMSBuildTargetExecuted("NpmRestore"));
        Assert.Equal(".npm-install-stamp-env1", GetSingleNpmStampFileName(project.RootFolder));

        // The packages must not be installed again when restoring from the same environment
        data = await project.RestoreAndGetOutput(["/p:NpmStampFileIdentifier=env1"]);
        Assert.Equal(0, data.ExitCode);
        Assert.False(data.IsMSBuildTargetExecuted("NpmRestore"));
        Assert.Equal(".npm-install-stamp-env1", GetSingleNpmStampFileName(project.RootFolder));

        // The packages must be installed again when restoring the same folder from another environment.
        // The stamp file of the first environment must be removed as its packages are replaced by the new ones.
        data = await project.RestoreAndGetOutput(["/p:NpmStampFileIdentifier=env2"]);
        Assert.Equal(0, data.ExitCode);
        Assert.True(data.IsMSBuildTargetExecuted("NpmRestore"));
        Assert.Equal(".npm-install-stamp-env2", GetSingleNpmStampFileName(project.RootFolder));

        // The packages of the first environment were replaced by the packages of the second environment,
        // so they must be installed again when restoring from the first environment
        data = await project.RestoreAndGetOutput(["/p:NpmStampFileIdentifier=env1"]);
        Assert.Equal(0, data.ExitCode);
        Assert.True(data.IsMSBuildTargetExecuted("NpmRestore"));
        Assert.Equal(".npm-install-stamp-env1", GetSingleNpmStampFileName(project.RootFolder));
    }

    [Fact]
    public async Task NpmRestore_DisabledWhenEnableDefaultNpmPackageFileIsFalse()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(properties: [("EnableDefaultNpmPackageFile", "false")]);

        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile("package.json", """
                        {
                            "name": "sample",
                            "version": "1.0.0",
                            "private": true,
                            "devDependencies": {
                                "is-number": "7.0.0"
                            }
                        }
                        """);

        var data = await project.RestoreAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        Assert.False(File.Exists(project.RootFolder / "package-lock.json"));
        AssertNpmStampFileDoesNotExist(project.RootFolder);
    }

    [Fact]
    public async Task Npm_Dotnet_Build_sln()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(filename: "sample.csproj");

        var csprojFile = project.AddFile("Program.cs", "Console.WriteLine();");
        var slnFile = project.AddFile("sample.slnx", """
            <Solution>
                <Project Path="sample.csproj" />
            </Solution>
            """);
        project.AddFile("package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);

        var data = await project.BuildAndGetOutput([slnFile]);
        Assert.Equal(0, data.ExitCode);
        Assert.True(File.Exists(project.RootFolder / "package-lock.json"));
        AssertNpmStampFileExists(project.RootFolder);
    }

    [Theory]
    [InlineData("publish")]
    public async Task Npm_Dotnet_sln(string command)
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(filename: "sample.csproj");

        var csprojFile = project.AddFile("Program.cs", "Console.WriteLine();");
        var slnFile = project.AddFile("sample.slnx", """
            <Solution>
                <Project Path="sample.csproj" />
            </Solution>
            """);
        project.AddFile("package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);
        project.AddFile("package-lock.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "lockfileVersion": 3,
              "requires": true,
              "packages": {
                "": {
                  "name": "sample",
                  "version": "1.0.0",
                  "devDependencies": {
                    "is-number": "7.0.0"
                  }
                },
                "node_modules/is-number": {
                  "version": "7.0.0",
                  "resolved": "https://registry.npmjs.org/is-number/-/is-number-7.0.0.tgz",
                  "integrity": "sha512-41Cifkg6e8TylSpdtTpeLVMqvSBEVzTttHvERD741+pnZ8ANv0004MRL43QKPDlK9cGvNp6NZWZUBlbGXYxxng==",
                  "dev": true,
                  "license": "MIT",
                  "engines": {
                    "node": ">=0.12.0"
                  }
                }
              }
            }

            """);

        var data = await project.ExecuteDotnetCommandAndGetOutput(command, [slnFile]);
        Assert.Equal(0, data.ExitCode);

        Assert.True(File.Exists(project.RootFolder / "package-lock.json"));
        AssertNpmStampFileExists(project.RootFolder);
    }

    [Fact]
    public async Task NpmRestore_MultipleFiles()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile(
            additionalProjectElements: [
                new XElement("ItemGroup",
                    new XElement("NpmPackageFile", new XAttribute("Include", "a/package.json")),
                    new XElement("NpmPackageFile", new XAttribute("Include", "b/package.json")))
                ]);

        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile("a/package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);
        project.AddFile("b/package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);

        var data = await project.RestoreAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        Assert.True(File.Exists(project.RootFolder / "a" / "package-lock.json"));
        AssertNpmStampFileExists(project.RootFolder / "a");
        Assert.True(File.Exists(project.RootFolder / "b" / "package-lock.json"));
        AssertNpmStampFileExists(project.RootFolder / "b");
    }

    [Fact]
    public async Task Npm_Dotnet_Build_RestoreLockedMode_Fail()
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile();

        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile("package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);

        var data = await project.BuildAndGetOutput(["/p:RestoreLockedMode=true"]);
        Assert.Equal(1, data.ExitCode);
    }

    [Theory]
    [InlineData("/p:RestoreLockedMode=true")]
    [InlineData("/p:ContinuousIntegrationBuild=true")]
    public async Task Npm_Dotnet_Build_Ci_Success(string command)
    {
        await using var project = CreateProjectBuilder(SdkWebName);
        project.AddCsprojFile();

        project.AddFile("Program.cs", "Console.WriteLine();");
        project.AddFile("package.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "private": true,
              "devDependencies": {
                "is-number": "7.0.0"
              }
            }
            """);
        project.AddFile("package-lock.json", """
            {
              "name": "sample",
              "version": "1.0.0",
              "lockfileVersion": 3,
              "requires": true,
              "packages": {
                "": {
                  "name": "sample",
                  "version": "1.0.0",
                  "devDependencies": {
                    "is-number": "7.0.0"
                  }
                },
                "node_modules/is-number": {
                  "version": "7.0.0",
                  "resolved": "https://registry.npmjs.org/is-number/-/is-number-7.0.0.tgz",
                  "integrity": "sha512-41Cifkg6e8TylSpdtTpeLVMqvSBEVzTttHvERD741+pnZ8ANv0004MRL43QKPDlK9cGvNp6NZWZUBlbGXYxxng==",
                  "dev": true,
                  "license": "MIT",
                  "engines": {
                    "node": ">=0.12.0"
                  }
                }
              }
            }

            """);

        var data = await project.BuildAndGetOutput([command]);
        Assert.Equal(0, data.ExitCode);
    }

    private static async Task AssertPdbIsEmbedded(string[] outputFiles)
    {
        Assert.DoesNotContain(outputFiles, f => f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
        var dllPath = outputFiles.Single(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        await using var stream = File.OpenRead(dllPath);
        var peReader = new PEReader(stream);
        var debug = peReader.ReadDebugDirectory();
        Assert.Contains(debug, entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
    }
}
