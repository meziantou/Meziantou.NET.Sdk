using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using NuGet.Packaging;
using Task = System.Threading.Tasks.Task;
using static Meziantou.Sdk.Tests.Helpers.PackageFixture;
using Meziantou.Sdk.Tests.Helpers;

namespace Meziantou.Sdk.Tests;

// TODO use artifact folder

// TODO test with Central Package Management (Set IsImplicitlyDefined=true if needed)
// TODO test with xunit v3
public sealed class SdkTests(PackageFixture fixture, ITestOutputHelper testOutputHelper) : IClassFixture<PackageFixture>
{
    [Theory]
    [InlineData(SdkName)]
    [InlineData(SdkWebName)]
    [InlineData(SdkTestName)]
    public async Task ImplicitUsings(string sdk)
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile(sdk: sdk);
        project.AddFile("sample.cs", """_ = new StringBuilder();""");
        var data = await project.BuildAndGetOutput();
        Assert.False(data.HasError());
    }

    [Fact]
    public async Task BannedSymbolsAreReported()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var data = await project.BuildAndGetOutput();
        Assert.True(data.HasWarning("RS0030"));

        var files = data.GetBinLogFiles();
        Assert.Contains(files, f => f.EndsWith("BannedSymbols.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EditorConfigsAreInBinlog()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var localFile = project.AddFile(".editorconfig", "");
        var data = await project.BuildAndGetOutput();

        var files = data.GetBinLogFiles();
        Assert.Contains(files, f => f.EndsWith(".editorconfig", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f == localFile);
    }

    [Fact]
    public async Task WarningsAsErrorOnGitHubActions()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var data = await project.BuildAndGetOutput(["/p:GITHUB_ACTIONS=true"]);
        Assert.True(data.HasError("RS0030"));
    }

    [Fact]
    public async Task NamingConvention_Invalid()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
    public async Task NuGetAuditIsReportedAsErrorOnGitHubActions()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile(nuGetPackages: [("System.Net.Http", "4.3.3")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput(["/p:GITHUB_ACTIONS=true"]);
        Assert.True(data.OutputContains("error NU1903", StringComparison.Ordinal));
        Assert.Equal(1, data.ExitCode);
    }

    [Fact]
    public async Task NuGetAuditIsReportedAsWarning()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile(nuGetPackages: [("System.Net.Http", "4.3.3")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();
        Assert.True(data.OutputContains("warning NU1903", StringComparison.Ordinal));
        Assert.True(data.OutputDoesNotContain("error NU1903", StringComparison.Ordinal));
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task MSBuildWarningsAsError()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            Console.WriteLine();

            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);

        var outputFiles = Directory.GetFiles(project.RootFolder / "bin", "*", SearchOption.AllDirectories);
        await AssertPdbIsEmbedded(outputFiles);
    }

    [Fact]
    public async Task PdbShouldBeEmbedded_Dotnet_Pack()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
    }

    [Fact]
    public async Task DotnetTestSkipAnalyzers()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile(
            properties: [("IsTestProject", "true")],
            nuGetPackages: [("Microsoft.NET.Test.Sdk", "17.14.1"), ("xunit", "2.9.3"), ("xunit.runner.visualstudio", "3.1.1")]
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile(
            properties: [("IsTestProject", "true"), ("OptimizeVsTestRun", "false")],
            nuGetPackages: [("Microsoft.NET.Test.Sdk", "17.14.1"), ("xunit", "2.9.3"), ("xunit.runner.visualstudio", "3.1.1")]
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
    public async Task NonMeziantouCsproj()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
    public async Task MeziantouCsproj()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
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
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile(filename: "Meziantou.Analyzer.csproj");
        project.AddFile("Program.cs", """Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();
        Assert.Equal(0, data.ExitCode);
    }

    // TODO fail if no tests?
    [Fact] // TODO same test without GitHubActions
    public async Task RunningTestsOnGitHubActionsShouldAddCustomLogger()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile(
            sdk: SdkTestName,
            filename: "Sample.Tests.csproj",
            nuGetPackages: [("Microsoft.NET.Test.Sdk", "17.14.1"), ("xunit", "2.9.3"), ("xunit.runner.visualstudio", "3.1.1")]
            );

        project.AddFile("Program.cs", """
            using Xunit;
            public class Tests
            {
                [Fact]
                public void Test1()
                {
                    Assert.Fail("failure message");
                }
            }
            """);

        var summary = project.AddFile("gh_summary.txt", "");
        var data = await project.TestAndGetOutput(environmentVariables: [("GITHUB_ACTIONS", "true"), ("GITHUB_STEP_SUMMARY", summary)]);

        Assert.Equal(1, data.ExitCode);
        Assert.True(data.OutputContains("failure message", StringComparison.Ordinal));
        Assert.NotEmpty(File.ReadAllText(summary));
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