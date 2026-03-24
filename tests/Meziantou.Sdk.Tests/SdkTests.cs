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
    : SdkTests(fixture, testOutputHelper, NetSdkVersion.Net10_0, SdkImportStyle.ProjectElement);

public sealed class Sdk10_0_Inner_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : SdkTests(fixture, testOutputHelper, NetSdkVersion.Net10_0, SdkImportStyle.SdkElement);

public sealed class Sdk11_0_Root_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : SdkTests(fixture, testOutputHelper, NetSdkVersion.Net11_0, SdkImportStyle.ProjectElement);

public sealed class Sdk11_0_Inner_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : SdkTests(fixture, testOutputHelper, NetSdkVersion.Net11_0, SdkImportStyle.SdkElement);

public abstract class SdkTests(PackageFixture fixture, ITestOutputHelper testOutputHelper, NetSdkVersion dotnetSdkVersion, SdkImportStyle sdkImportStyle)
{
    // note: don't simplify names as they are used in the Renovate regex
    private static readonly NuGetReference[] XUnit2References =
    [
        new NuGetReference("xunit", "2.9.3"),
        new NuGetReference("xunit.runner.visualstudio", "3.1.5"),
    ];
    private static readonly NuGetReference[] XUnit3References =
    [
        new NuGetReference("xunit.v3", "3.2.0"),
        new NuGetReference("xunit.runner.visualstudio", "3.1.5"),
    ];
    private static readonly NuGetReference[] XUnit3MTP2References =
    [
        new NuGetReference("xunit.v3.mtp-v2", "3.2.0"),
        new NuGetReference("xunit.runner.visualstudio", "3.1.5"),
    ];

    private ProjectBuilder CreateProjectBuilder(string defaultSdkName = SdkName)
    {
        var builder = new ProjectBuilder(fixture, testOutputHelper, sdkImportStyle, defaultSdkName);
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
        data.AssertMSBuildPropertyValue("LangVersion", "latest");
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
    public async Task GenerateSbom_IsSetWhenContinuousIntegrationBuildIsSet()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(properties: [("ContinuousIntegrationBuild", "true")]);
        project.AddFile("Program.cs", "Console.WriteLine();");
        var data = await project.PackAndGetOutput();
        data.AssertMSBuildPropertyValue("GenerateSBOM", "true");

        var nupkg = Directory.GetFiles(project.RootFolder, "*.nupkg", SearchOption.AllDirectories).Single();
        using var archive = ZipFile.OpenRead(nupkg);
        Assert.Contains(archive.Entries, e => e.FullName.EndsWith("manifest.spdx.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateSbom_IsNotSetWhenContinuousIntegrationBuildIsNotSet()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile();
        project.AddFile("Program.cs", "Console.WriteLine();");
        var data = await project.PackAndGetOutput();

        var nupkg = Directory.GetFiles(project.RootFolder, "*.nupkg", SearchOption.AllDirectories).Single();
        using var archive = ZipFile.OpenRead(nupkg);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.EndsWith("manifest.spdx.json", StringComparison.Ordinal));
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
        if (sdkImportStyle is SdkImportStyle.SdkElement)
        {
            Assert.Skip("Directory.Build.props is not supported with SdkImportStyle.SdkElement");
        }

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

        Assert.Contains(files, f => f.EndsWith(".editorconfig", StringComparison.Ordinal));
        Assert.Contains(files, f => f == localFile || f == "/private" + localFile); // macos may prefix it with /private
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

    [Fact]
    public async Task DotnetTestSkipAnalyzers()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            properties: [("IsTestProject", "true")],
            nuGetPackages: [.. XUnit2References]
        );
        project.AddFile("sample.cs", """
            public class Sample
            {
                [Xunit.Fact]
                public void Test()
                {
                    _ = System.DateTime.Now; // This should not be reported as an error
                }
            }
            """);
        var data = await project.TestAndGetOutput();
        Assert.False(data.HasWarning("RS0030"));
    }

    [Fact]
    public async Task DotnetTestSkipAnalyzers_OptOut()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            properties: [("IsTestProject", "true"), ("OptimizeVsTestRun", "false")],
            nuGetPackages: [.. XUnit2References]
        );
        project.AddFile("sample.cs", """
            public class Sample
            {
                [Xunit.Fact]
                public void Test()
                {
                    _ = System.DateTime.Now; // This should not be reported as an error
                }
            }
            """);
        var data = await project.TestAndGetOutput();
        Assert.True(data.HasWarning("RS0030"));
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
    public async Task VSTests_OnGitHubActionsShouldAddCustomLogger_Xunit2()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            nuGetPackages: [.. XUnit2References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                    Assert.Equal("true", System.Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
                    Assert.NotEmpty(System.Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") ?? "");
                    Assert.Fail("failure message");
                }
            }
            """);

        var data = await project.TestAndGetOutput(environmentVariables: [.. project.GitHubEnvironmentVariables]);

        Assert.Equal(1, data.ExitCode);
        Assert.True(data.OutputContains("failure message", StringComparison.Ordinal), userMessage: "Output must contain 'failure message'");
        Assert.NotEmpty(Directory.GetFiles(project.RootFolder, "*.trx", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(project.RootFolder, "*.coverage", SearchOption.AllDirectories));
        Assert.True(data.OutputContains("::error title=Tests.Test1,", StringComparison.Ordinal), userMessage: "Output must contain '::error title=Tests.Test1'");
        Assert.NotEmpty(project.GetGitHubStepSummaryContent());
    }

    [Fact]
    public async Task VSTests_OnGitHubActionsShouldAddCustomLogger_Xunit3()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Failing, need more investigation");
        }

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
                    Assert.Equal("true", System.Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
                    Assert.NotEmpty(System.Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") ?? "");
                    Assert.Fail("failure message");
                }
            }
            """);

        var data = await project.TestAndGetOutput(environmentVariables: [.. project.GitHubEnvironmentVariables]);

        Assert.Equal(1, data.ExitCode);
        Assert.True(data.OutputContains("failure message", StringComparison.Ordinal));
        Assert.NotEmpty(Directory.GetFiles(project.RootFolder, "*.trx", SearchOption.AllDirectories));
        Assert.True(data.OutputContains("::error title=Tests.Test1,", StringComparison.Ordinal), userMessage: "Output must contain '::error title=Tests.Test1'");
        Assert.NotEmpty(project.GetGitHubStepSummaryContent());
    }

    [Fact]
    public async Task VSTests_OnUnknownContextShouldNotAddCustomLogger()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            nuGetPackages: [.. XUnit2References]
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

        var data = await project.TestAndGetOutput();

        Assert.Equal(1, data.ExitCode);
        Assert.True(data.OutputContains("failure message", StringComparison.Ordinal));
        Assert.Empty(project.GetGitHubStepSummaryContent());
        Assert.NotEmpty(Directory.GetFiles(project.RootFolder, "*.trx", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(project.RootFolder, "*.coverage", SearchOption.AllDirectories));
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
    public async Task CentralPackageManagement()
    {
        await using var project = CreateProjectBuilder();
        project.AddCsprojFile(
            sdk: SdkTestName,
            filename: "Sample.Tests.csproj"
            );

        project.AddFile("Program.cs", """
            Console.WriteLine();
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
        project.AddCsprojFile(filename: "Sample.Tests.csproj");

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
        Assert.True(File.Exists(project.RootFolder / "node_modules" / ".npm-install-stamp"));
        var files = data.GetBinLogFiles();
        Assert.Contains(files, f => f.EndsWith("package-lock.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GitHubActionsMetadataIsLoggedBeforeTests()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            nuGetPackages: [.. XUnit2References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                    Assert.True(true);
                }
            }
            """);

        var environmentVariables = new (string Name, string Value)[]
        {
            ("GITHUB_ACTIONS", "true"),
            ("GITHUB_JOB", "test-job"),
            ("GITHUB_WORKFLOW", "CI"),
            ("GITHUB_ACTION", "run-tests"),
            ("GITHUB_RUN_ID", "123456"),
            ("GITHUB_RUN_NUMBER", "42"),
            ("GITHUB_RUN_ATTEMPT", "1"),
            ("RUNNER_NAME", "GitHub Actions 1"),
            ("RUNNER_OS", "Linux"),
            ("RUNNER_ARCH", "X64"),
            ("GITHUB_STEP_SUMMARY", project.RootFolder / "step_summary.txt")
        };

        var data = await project.TestAndGetOutput(environmentVariables: environmentVariables);
        Assert.Equal(0, data.ExitCode);

        var expectedJson = """GitHub Actions Metadata: {"github_job_id":"test-job","github_workflow":"CI","github_action":"run-tests","github_run_id":"123456","github_run_number":"42","github_run_attempt":"1","runner_name":"GitHub Actions 1","runner_os":"Linux","runner_arch":"X64"}""";
        Assert.True(data.OutputContains(expectedJson, StringComparison.Ordinal));
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
    public async Task GitHubActionsMetadataNotLoggedWhenNotOnGitHub()
    {
        await using var project = CreateProjectBuilder(SdkTestName);
        project.AddCsprojFile(
            filename: "Sample.Tests.csproj",
            nuGetPackages: [.. XUnit2References]
            );

        project.AddFile("Program.cs", """
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                    Assert.True(true);
                }
            }
            """);

        var data = await project.TestAndGetOutput();
        Assert.Equal(0, data.ExitCode);
        Assert.False(data.OutputContains("GitHub Actions Metadata:", StringComparison.Ordinal));
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
        Assert.True(File.Exists(project.RootFolder / "node_modules" / ".npm-install-stamp"));
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
        Assert.False(File.Exists(project.RootFolder / "node_modules" / ".npm-install-stamp"));
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
        Assert.True(File.Exists(project.RootFolder / "node_modules" / ".npm-install-stamp"));
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

        var data = await project.ExecuteDotnetCommandAndGetOutput(command, [slnFile]);
        Assert.Equal(0, data.ExitCode);

        Assert.True(File.Exists(project.RootFolder / "package-lock.json"));
        Assert.True(File.Exists(project.RootFolder / "node_modules" / ".npm-install-stamp"));
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
        Assert.True(File.Exists(project.RootFolder / "a" / "node_modules" / ".npm-install-stamp"));
        Assert.True(File.Exists(project.RootFolder / "b" / "package-lock.json"));
        Assert.True(File.Exists(project.RootFolder / "b" / "node_modules" / ".npm-install-stamp"));
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

public sealed class FileBasedApp10_0_Root_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : FileBasedAppTests(fixture, testOutputHelper, NetSdkVersion.Net10_0, useAsRootSdk: true);

public sealed class FileBasedApp10_0_Inner_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : FileBasedAppTests(fixture, testOutputHelper, NetSdkVersion.Net10_0, useAsRootSdk: false);

public sealed class FileBasedApp11_0_Root_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : FileBasedAppTests(fixture, testOutputHelper, NetSdkVersion.Net11_0, useAsRootSdk: true);

public sealed class FileBasedApp11_0_Inner_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : FileBasedAppTests(fixture, testOutputHelper, NetSdkVersion.Net11_0, useAsRootSdk: false);

/// <summary>
/// Tests for file-based apps using the <c>#:sdk</c> directive.
/// </summary>
public abstract class FileBasedAppTests(PackageFixture fixture, ITestOutputHelper testOutputHelper, NetSdkVersion dotnetSdkVersion, bool useAsRootSdk)
{
    private ProjectBuilder CreateProjectBuilder()
    {
        var builder = new ProjectBuilder(fixture, testOutputHelper, SdkImportStyle.Default, SdkName);
        builder.SetDotnetSdkVersion(dotnetSdkVersion);
        return builder;
    }

    private string GetSdkDirectives()
    {
        if (useAsRootSdk)
        {
            return $"#:sdk {SdkName}@{fixture.Version}";
        }

        return $$"""
            #:sdk Microsoft.NET.Sdk
            #:sdk {{SdkName}}@{{fixture.Version}}
            """;
    }

    [Fact]
    public async Task FileBasedApp_Build()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Program.cs", $$"""
            {{GetSdkDirectives()}}
            Console.WriteLine("Hello from file-based app");
            """);

        var data = await project.BuildFileAndGetOutput("Program.cs");
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task FileBasedApp_Run()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Program.cs", $$"""
            {{GetSdkDirectives()}}
            Console.WriteLine("Hello from file-based app");
            """);

        var data = await project.RunFileAndGetOutput("Program.cs");
        Assert.Equal(0, data.ExitCode);
        Assert.True(data.OutputContains("Hello from file-based app"));
    }

    [Fact]
    public async Task FileBasedApp_WithPackageDirective()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Program.cs", $$"""
            {{GetSdkDirectives()}}
            #:package Humanizer.Core@2.14.1
            using Humanizer;
            Console.WriteLine("truncated: " + "Hello from file-based app".Truncate(10));
            """);

        var data = await project.RunFileAndGetOutput("Program.cs");
        Assert.Equal(0, data.ExitCode);
        Assert.True(data.OutputContains("truncated: Hello fro"));
    }

    [Fact]
    public async Task FileBasedApp_DefaultProperties()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Program.cs", $$"""
            {{GetSdkDirectives()}}
            Console.WriteLine("Hello");
            """);

        var data = await project.BuildFileAndGetOutput("Program.cs");
        Assert.Equal(0, data.ExitCode);
        data.AssertMSBuildPropertyValue("LangVersion", "latest");
        data.AssertMSBuildPropertyValue("Nullable", "enable");
        data.AssertMSBuildPropertyValue("ImplicitUsings", "enable");
        data.AssertMSBuildPropertyValue("EnableNETAnalyzers", "true");
    }
}