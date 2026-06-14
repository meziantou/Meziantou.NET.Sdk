using Task = System.Threading.Tasks.Task;
using static Meziantou.Sdk.Tests.Helpers.PackageFixture;
using Meziantou.Sdk.Tests.Helpers;

namespace Meziantou.Sdk.Tests;

public sealed class FileBasedApp10_0_Root_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : FileBasedAppTests(fixture, testOutputHelper, NetSdkVersion.Net10_0, useAsRootSdk: true);

public sealed class FileBasedApp11_0_Root_Tests(PackageFixture fixture, ITestOutputHelper testOutputHelper)
    : FileBasedAppTests(fixture, testOutputHelper, NetSdkVersion.Net11_0, useAsRootSdk: true);

/// <summary>
/// Tests for file-based apps using the <c>#:sdk</c> directive.
/// </summary>
public abstract class FileBasedAppTests(PackageFixture fixture, ITestOutputHelper testOutputHelper, NetSdkVersion dotnetSdkVersion, bool useAsRootSdk)
{
    private ProjectBuilder CreateProjectBuilder()
    {
        var builder = new ProjectBuilder(fixture, testOutputHelper, SdkName);
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
            #:package Meziantou.Framework.FullPath@2.1.4
            _ = Meziantou.Framework.FullPath.Empty;
            Console.WriteLine("done");
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

    [Fact]
    public async Task FileBasedApp_SingleFileAppEditorConfigIsIncluded()
    {
        await using var project = CreateProjectBuilder();
        project.AddFile("Program.cs", $$"""
            {{GetSdkDirectives()}}
            Console.WriteLine("Hello");
            """);

        var data = await project.BuildFileAndGetOutput("Program.cs");
        Assert.Equal(0, data.ExitCode);
        data.AssertMSBuildPropertyValue("MeziantouSingleFileApp", "true");

        var files = data.GetBinLogFiles();
        Assert.Contains(files, f => f.EndsWith("Meziantou.NET.Sdk.SingleFileApp.editorconfig", StringComparison.Ordinal));
    }
}
