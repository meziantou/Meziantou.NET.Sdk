using Meziantou.Framework;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Meziantou.Sdk.Tests.Helpers;

/// <summary>
/// Creates the NuGet packages used by the tests on the fly, so the tests don't need to download packages from nuget.org.
/// The generated packages are copied to the test package source and mapped to it (<see cref="ProjectBuilder"/>), so they
/// shadow any package with the same identity on nuget.org.
/// </summary>
internal static class TestPackages
{
    private const string TargetFramework = "netstandard2.0";
    private const string RoslynVersion = "4.8.0";

    public const string Version = "1.0.0";

    /// <summary>Assembly contained in the library packages.</summary>
    public const string LibraryAssemblyName = "Meziantou.Sdk.TestLibrary";

    /// <summary>Assembly contained in <see cref="Analyzer"/>.</summary>
    public const string AnalyzerAssemblyName = "Meziantou.Sdk.TestAnalyzer";

    /// <summary>Library package whose name is not excluded from the default 'IncludeAssets' restriction.</summary>
    public const string Library = "TestPackage.Library";

    // Library packages whose names are excluded from the default 'IncludeAssets' restriction
    public const string MicrosoftLibrary = "Microsoft.TestPackage";
    public const string MeziantouLibrary = "Meziantou.TestPackage";
    public const string XunitLibrary = "xunit.TestPackage";

    /// <summary>Package providing a Roslyn analyzer.</summary>
    public const string Analyzer = "TestPackage.Analyzer";

    /// <summary>Package providing MSBuild props and targets.</summary>
    public const string BuildAssets = "TestPackage.BuildAssets";

    // Packages banned by the SDK. The SDK only validates the package identity, so the content of the packages doesn't matter
    public const string YamlDotNet = "YamlDotNet";
    public const string CliWrap = "CliWrap";
    public const string Testcontainers = "Testcontainers";
    public const string MeziantouXunitParallelTestFramework = "Meziantou.Xunit.ParallelTestFramework";
    public const string MeziantouXunitV3ParallelTestFramework = "Meziantou.Xunit.v3.ParallelTestFramework";

    private static string[] BannedPackages => [YamlDotNet, CliWrap, Testcontainers, MeziantouXunitParallelTestFramework, MeziantouXunitV3ParallelTestFramework];

    private static string[] LibraryPackages => [Library, MicrosoftLibrary, MeziantouLibrary, XunitLibrary, .. BannedPackages];

    public static IEnumerable<string> PackageIds => [.. LibraryPackages, Analyzer, BuildAssets];

    public static async Task CreateAsync(FullPath outputDirectory, CancellationToken cancellationToken)
    {
        await using var sources = TemporaryDirectory.Create();

        var libraryOutput = sources.FullPath / "library" / "bin";
        var analyzerOutput = sources.FullPath / "analyzer" / "bin";

        CreateLibraryProject(sources.FullPath / "library");
        CreateAnalyzerProject(sources.FullPath / "analyzer");

        await Task.WhenAll(
            BuildAsync(sources.FullPath / "library" / "library.csproj", libraryOutput, cancellationToken),
            BuildAsync(sources.FullPath / "analyzer" / "analyzer.csproj", analyzerOutput, cancellationToken));

        foreach (var packageId in LibraryPackages)
        {
            CreatePackage(outputDirectory, packageId, [(libraryOutput / (LibraryAssemblyName + ".dll"), $"lib/{TargetFramework}/{LibraryAssemblyName}.dll")]);
        }

        CreatePackage(outputDirectory, Analyzer, [(analyzerOutput / (AnalyzerAssemblyName + ".dll"), $"analyzers/dotnet/cs/{AnalyzerAssemblyName}.dll")]);

        var buildAssets = sources.FullPath / "build-assets";
        Directory.CreateDirectory(buildAssets);
        File.WriteAllText(buildAssets / (BuildAssets + ".props"), "<Project />");
        File.WriteAllText(buildAssets / (BuildAssets + ".targets"), "<Project />");
        CreatePackage(outputDirectory, BuildAssets,
        [
            (buildAssets / (BuildAssets + ".props"), $"build/{BuildAssets}.props"),
            (buildAssets / (BuildAssets + ".targets"), $"build/{BuildAssets}.targets"),
        ]);
    }

    private static void CreateLibraryProject(FullPath directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(directory / "library.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{TargetFramework}</TargetFramework>
                <AssemblyName>{LibraryAssemblyName}</AssemblyName>
                <RootNamespace>{LibraryAssemblyName}</RootNamespace>
                <LangVersion>latest</LangVersion>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(directory / "TestClass.cs", $$"""
            namespace {{LibraryAssemblyName}};

            public static class TestClass
            {
                public static string GetValue() => "{{LibraryAssemblyName}}";
            }

            """);
    }

    private static void CreateAnalyzerProject(FullPath directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(directory / "analyzer.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{TargetFramework}</TargetFramework>
                <AssemblyName>{AnalyzerAssemblyName}</AssemblyName>
                <RootNamespace>{AnalyzerAssemblyName}</RootNamespace>
                <LangVersion>latest</LangVersion>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="{RoslynVersion}" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);

        // The analyzer must be a valid Roslyn analyzer, otherwise the compiler reports CS8033 when the assembly is loaded.
        // The diagnostic is disabled by default, so the analyzer never reports anything.
        File.WriteAllText(directory / "TestAnalyzer.cs", $$"""
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Diagnostics;

            namespace {{AnalyzerAssemblyName}};

            [DiagnosticAnalyzer(LanguageNames.CSharp)]
            public sealed class TestAnalyzer : DiagnosticAnalyzer
            {
                private static readonly DiagnosticDescriptor Rule = new(
                    id: "TESTPKG0001",
                    title: "Test analyzer",
                    messageFormat: "Test analyzer",
                    category: "Test",
                    defaultSeverity: DiagnosticSeverity.Warning,
                    isEnabledByDefault: false);

                public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

                public override void Initialize(AnalysisContext context)
                {
                    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                    context.EnableConcurrentExecution();
                }
            }

            """);
    }

    private static async Task BuildAsync(FullPath projectPath, FullPath outputPath, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var result = await ProcessWrapper.Create("dotnet")
            .WithArguments("build", "--disable-build-servers", projectPath, "--configuration", "Release", "--output", outputPath)
            .WithEnvironmentVariables(env => env
                .Set("MSBUILDDISABLENODEREUSE", "1")
                .Set("DOTNET_CLI_USE_MSBUILDNOINPROCNODE", "1"))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync(linkedCts.Token);
        if (!result.ExitCode.IsSuccess)
        {
            Assert.Fail($"Building the test package '{projectPath}' failed with exit code {result.ExitCode}. Output: {result.Output}");
        }
    }

    private static void CreatePackage(FullPath outputDirectory, string packageId, (FullPath SourcePath, string TargetPath)[] files)
    {
        var builder = new PackageBuilder
        {
            Id = packageId,
            Version = NuGetVersion.Parse(Version),
            Description = "Package generated by the tests",
        };
        builder.Authors.Add("Meziantou.Sdk.Tests");
        builder.DependencyGroups.Add(new PackageDependencyGroup(NuGetFramework.Parse(TargetFramework), []));

        foreach (var (sourcePath, targetPath) in files)
        {
            builder.Files.Add(new PhysicalPackageFile { SourcePath = sourcePath, TargetPath = targetPath });
        }

        using var stream = File.Create(outputDirectory / $"{packageId}.{Version}.nupkg");
        builder.Save(stream);
    }
}
