# Meziantou.NET.Sdk

- [![Meziantou.NET.Sdk on NuGet](https://img.shields.io/nuget/v/Meziantou.NET.Sdk.svg)](https://www.nuget.org/packages/Meziantou.NET.Sdk/)

MSBuild SDK that helps standardize build and quality settings across repositories. It provides:
- Opinionated defaults and naming conventions for .NET projects
- Best practices for build, CI, and test workflows
- A static analysis baseline with Roslyn analyzers
- Set `ContinuousIntegrationBuild` based on the context
- dotnet test features
  - xUnit.net v3 and Microsoft Testing Platform (MTP) by default
  - Dump on crash or hang
  - Loggers when running on GitHub
  - Annotations and job summary when running on GitHub Actions
  - Disable Roslyn analyzers to speed up build
- Relevant NuGet packages based on the project type

Blog post: [Creating a custom MSBuild SDK to reduce boilerplate in dotnet projects](https://www.meziantou.net/creating-a-custom-msbuild-sdk-to-reduce-boilerplate-in-dotnet-projects.htm)

# Usage

## Method 1

To use it, create a `global.json` file at the solution root with the following content:

````json
{
  "sdk": {
    "version": "9.0.304"
  },
  "msbuild-sdks": {
    "Meziantou.NET.Sdk": "1.0.16",
    "Meziantou.NET.Sdk.BlazorWebAssembly": "1.0.16",
    "Meziantou.NET.Sdk.Razor": "1.0.16",
    "Meziantou.NET.Sdk.Test": "1.0.16",
    "Meziantou.NET.Sdk.Web": "1.0.16",
    "Meziantou.NET.Sdk.WindowsDesktop": "1.0.16"
  }
}
````

And reference the SDK in your project file:

````xml
<Project Sdk="Meziantou.NET.Sdk">
</Project>
````

## Method 2

You can the SDK by specifying the version inside the `csproj` file:

````xml
<Project Sdk="Meziantou.NET.Sdk/1.0.16">
</Project>
````

## Method 3

````xml
<Project Sdk="Microsoft.NET.SDK">
    <Sdk Name="Meziantou.NET.Sdk" Version="1.0.16" />
</Project>
````

## File-based apps (.NET 10+)

You can use the SDK with [file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps?WT.mc_id=DT-MVP-5003978) using the `#:sdk` directive:

````csharp
#:sdk Meziantou.NET.Sdk@1.0.16
Console.WriteLine("Hello from a file-based app!");
````

Then run with:

````shell
dotnet run Program.cs
````

You can also use it as an additional SDK alongside `Microsoft.NET.Sdk`:

````csharp
#:sdk Microsoft.NET.Sdk
#:sdk Meziantou.NET.Sdk@1.0.16
Console.WriteLine("Hello!");
````

# Build configuration properties

Set these properties in your project file or a directory-level props file. Unless stated otherwise, defaults apply only when the property is empty.

## General build

| Property | Default | Description |
| --- | --- | --- |
| `ContinuousIntegrationBuild` | Auto-detected | Set to `true` or `false` to force CI behavior (warnings as errors, code style enforcement, SBOM, code coverage, npm locked mode). |
| `TargetFramework` | `net$(NETCoreAppMaximumVersion)` | Used when both `TargetFramework` and `TargetFrameworks` are empty. |
| `GenerateSBOM` | `true` on CI | Controls SBOM generation on CI builds. |
| `RollForward` | `LatestMajor` | Applied for non-test projects when unset. |
| `SuppressNETCoreSdkPreviewMessage` | `true` | Suppresses preview SDK message. |
| `PublishRepositoryUrl` | `true` | Publishes repository URL in packages. |
| `DebugType` | `embedded` | Embeds PDBs in the output. |
| `EmbedUntrackedSources` | `true` | Embeds untracked sources in PDBs. |
| `ImplicitUsings` | `enable` | Enables implicit global usings. |
| `Nullable` | `enable` | Enables nullable reference types. |
| `GenerateDocumentationFile` | `true` | Generates XML docs. |
| `DisableDocumentationWarnings` | `true` | When `false`, enables CS1573 and CS1591 warnings for undocumented public members. |
| `RestoreUseStaticGraphEvaluation` | `true` | Enables static graph restore. |
| `RestoreSerializeGlobalProperties` | `true` | Serializes global properties for restore. |
| `ReportAnalyzer` | `true` | Enables analyzer reporting. |
| `Features` | `strict` | Enables strict compiler features. |
| `Deterministic` | `true` | Enables deterministic builds. |
| `EnableNETAnalyzers` | `true` | Enables .NET analyzers. |
| `AnalysisLevel` | `latest-all` | Uses the latest analyzer rules. |
| `AllowUnsafeBlocks` | `true` | Allows `unsafe` code blocks. |
| `LangVersion` | `latest` | Uses the latest C# language version. |
| `MSBuildTreatWarningsAsErrors` | `true` on CI, Release, or AI agent runtime | Treats MSBuild warnings as errors. |
| `TreatWarningsAsErrors` | `true` on CI, Release, or AI agent runtime | Treats compiler warnings as errors. |
| `EnforceCodeStyleInBuild` | `true` on CI or Release | Enforces analyzer code style during builds. |
| `AccelerateBuildsInVisualStudio` | `true` | Enables faster builds in Visual Studio. |

## Package validation and auditing

| Property | Default | Description |
| --- | --- | --- |
| `EnablePackageValidation` | `true` | Enables package validation when unset. |
| `NuGetAudit` | `true` | Enables NuGet vulnerability auditing. |
| `NuGetAuditMode` | `all` | Audits all dependency types. |
| `NuGetAuditLevel` | `low` | Minimum severity level to report. |
| `WarningsAsErrors` | Adds `NU1900`–`NU1904` on CI, Release, or AI agent runtime | Promotes NuGet audit warnings to errors. |

## Banned symbols and analyzers

The SDK also blocks selected NuGet packages by default:
- `YamlDotNet` (use `Meziantou.Framework.Yaml`)
- `CliWrap` (use `Meziantou.Framework.ProcessWrapper`)
- `Testcontainers` (use `Meziantou.Framework.TemporaryContainers`)
- `Meziantou.Xunit.ParallelTestFramework` (use the built-in parallelization of xunit.v3)
- `Meziantou.Xunit.v3.ParallelTestFramework` (use the built-in parallelization of xunit.v3)

| Property | Default | Description |
| --- | --- | --- |
| `IncludeDefaultBannedSymbols` | `true` | Includes the default banned API list. |
| `BannedNewtonsoftJsonSymbols` | `true` | Includes banned Newtonsoft.Json APIs. |
| `AllowPackage_YamlDotNet` | unset (`false`) | Allows `YamlDotNet` when set to `true`; otherwise the build fails and suggests `Meziantou.Framework.Yaml`. |
| `AllowPackage_CliWrap` | unset (`false`) | Allows `CliWrap` when set to `true`; otherwise the build fails and suggests `Meziantou.Framework.ProcessWrapper`. |
| `AllowPackage_Testcontainers` | unset (`false`) | Allows `Testcontainers` when set to `true`; otherwise the build fails and suggests `Meziantou.Framework.TemporaryContainers`. |
| `AllowPackage_Meziantou_Xunit_ParallelTestFramework` | unset (`false`) | Allows `Meziantou.Xunit.ParallelTestFramework` when set to `true`; otherwise the build fails and suggests the built-in parallelization of xunit.v3. |
| `AllowPackage_Meziantou_Xunit_v3_ParallelTestFramework` | unset (`false`) | Allows `Meziantou.Xunit.v3.ParallelTestFramework` when set to `true`; otherwise the build fails and suggests the built-in parallelization of xunit.v3. |
| `Disable_SponsorLink` | `true` | Removes SponsorLink and Moq analyzers when not set to `false`. |

## Web SDK and containers

| Property | Default | Description |
| --- | --- | --- |
| `AutoRegisterServiceDefaults` | `true` | Adds ServiceDefaults auto-registration for web projects unless set to `false`. |
| `EnableSdkContainerSupport` | `true` on GitHub Actions | Enables container support for web projects on GitHub Actions. |
| `ContainerRegistry` | `ghcr.io` | Default container registry. |
| `ContainerRepository` | From GitHub repository | Default repository name when running on GitHub Actions. |
| `ContainerImageTagsMainVersionPrefix` | `1.0` | Prefix used to generate tags on the main branch. |
| `ContainerImageTagsIncludeLatest` | `true` | Appends `latest` tag on main. |
| `ContainerImageTags` | Computed | Uses build number on main and `0.0.1-preview.$(GITHUB_SHA)` elsewhere when unset. |

## Packaging metadata

| Property | Default | Description |
| --- | --- | --- |
| `SearchReadmeFileAbove` | `false` | When `true`, searches parent directories for a README to pack. |
| `PackageIcon` | icon.png for Meziantou projects | Default icon when the project name starts with Meziantou and no value is set. |
| `Authors` | `meziantou` for Meziantou projects | Default authors when the project name starts with Meziantou and no value is set. |
| `Company` | `meziantou` for Meziantou projects | Default company when the project name starts with Meziantou and no value is set. |
| `PackageLicenseExpression` | `MIT` for Meziantou projects | Default license expression when the project name starts with Meziantou and no value is set. |
| `PackageReadmeFile` | README.md when found | Default README packing behavior when a README exists. |

## npm restore

| Property | Default | Description |
| --- | --- | --- |
| `EnableDefaultNpmPackageFile` | Enabled when unset | Enables automatic package.json inclusion as `NpmPackageFile` (set to `false` to disable). |
| `NpmIgnoreScripts` | `true` | Adds `--ignore-scripts` to `npm install` / `npm ci` when `true` (set to `false` to allow lifecycle scripts). |
| `NpmRestoreLockedMode` | `true` on CI or when `RestoreLockedMode` is `true` | Uses `npm ci` when `true`, otherwise `npm install`. |

## Testing

A project using `Meziantou.NET.Sdk.Test` needs no `PackageReference` to run tests:

````xml
<Project Sdk="Meziantou.NET.Sdk.Test">
</Project>
````

````csharp
public class Tests
{
    [Fact]
    public void Test1() { }
}
````

It gets xUnit.net v3 on Microsoft Testing Platform, a TRX report, crash and hang dumps, code coverage
on CI, and GitHub Actions annotations and job summary when running on GitHub Actions. Add the
following to `global.json` so `dotnet test` runs in MTP mode:

````json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
````

`dotnet test` only reports failures by default. Use `dotnet test --output Detailed` to also list the
tests that passed — the option belongs to `dotnet test` itself, so it cannot be set from the project
file.

To use another test framework, reference it: the default framework is not added when the project
already references `xunit`, `xunit.v3*`, `TUnit`, `MSTest`, or `NUnit`. Set
`EnableDefaultTestFramework` to `false` for full control, which is also required for a test project
that must stay a library (VSTest).

The assertions come from
[`Meziantou.Framework.Assertions`](https://www.nuget.org/packages/Meziantou.Framework.Assertions)
instead of xUnit.net. The SDK adds the package and generates the
`global using Assert = Meziantou.Framework.Assertions.Assert;` alias, so `Assert` refers to it in any test
— a using alias takes precedence over the namespaces imported in the same scope, so it wins over the
global `using Xunit;` directive:

````csharp
[Fact]
public void Sample() => Assert.HasCount(2, new[] { 1, 2 });
````

Only the meaning of `Assert` changes: the assertions of the test framework stay available under their
full name, such as `Xunit.Assert`. This happens when the SDK adds the default test framework itself and
the project targets .NET 10 or later, as the package supports no older framework. Set
`EnableMeziantouAssertions` to `true` to add it whatever the referenced packages and the target framework
are, or to `false` to opt out and keep the assertions of the test framework:

````xml
<PropertyGroup>
  <EnableMeziantouAssertions>false</EnableMeziantouAssertions>
</PropertyGroup>
````

xUnit.net v3 runs test collections in parallel, but the tests inside a collection run one after the
other. When the project resolves xUnit.net v3 4.0 or later, the SDK generates a source file that opts
the whole assembly into
[full parallelization](https://xunit.net/docs/running-tests-in-parallel#changing-default-behaviors):

````csharp
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.All)]
````

Tests that share mutable state with another test of the same class must therefore be synchronized, or
be moved to a collection that disables parallelization. Set `EnableXunitFullParallelization` to
`false` to not generate the file at all, which is also required when the assembly already declares the
attribute itself, as the compiler reports `CS0579` for the duplicate:

````xml
<PropertyGroup>
  <EnableXunitFullParallelization>false</EnableXunitFullParallelization>
</PropertyGroup>
````

Set `XunitParallelizationMode` to generate the attribute with another
[`Xunit.Sdk.ParallelMode`](https://xunit.net/docs/running-tests-in-parallel#changing-default-behaviors)
value: `None` (no parallelization at all), `Collections` (the xUnit.net default: only test collections
run in parallel) or `All` (the SDK default). Setting the property also generates the file whatever the
resolved references are, like `EnableXunitFullParallelization` set to `true`:

````xml
<PropertyGroup>
  <XunitParallelizationMode>None</XunitParallelizationMode>
</PropertyGroup>
````

When the project references xUnit.net v3, the SDK also generates a source file with static helpers and a
global `using static` directive for them, so `TestContext.Current.CancellationToken` can be written as
`XunitCancellationToken` in any test:

````csharp
[Fact]
public async Task Sample() => await httpClient.GetAsync("https://example.com", XunitCancellationToken);
````

The class is `partial`, so the project can declare another part of it to add its own helpers, which
are then usable without any using directive too.

Set `EnableXunitStaticHelpers` to `false` to opt out, which is also required when the project already
declares a non-partial `Meziantou.NET.Sdk.Test.XUnitStaticHelpers` type, as the compiler reports `CS0260` for
the missing `partial` modifier:

````xml
<PropertyGroup>
  <EnableXunitStaticHelpers>false</EnableXunitStaticHelpers>
</PropertyGroup>
````

The SDK also defines the `XUNIT_ENTRYPOINT_DISABLE_WARNINGS` compilation constant in every test project:

````csharp
#if XUNIT_ENTRYPOINT_DISABLE_WARNINGS
// ...
#endif
````

Set `EnableXunitEntryPointDisableWarnings` to `false` to not define it:

````xml
<PropertyGroup>
  <EnableXunitEntryPointDisableWarnings>false</EnableXunitEntryPointDisableWarnings>
</PropertyGroup>
````

| Property | Default | Description |
| --- | --- | --- |
| `EnableDefaultTestFramework` | `true` | Adds `xunit.v3.mtp-v2` when no test framework is referenced, sets `OutputType` to `Exe` (required by xUnit.net v3) and `UseMicrosoftTestingPlatformRunner` to `true`. |
| `EnableMeziantouAssertions` | Auto | Adds `Meziantou.Framework.Assertions` and aliases `Assert` to `Meziantou.Framework.Assertions.Assert`. Added when the SDK adds the default test framework and the project targets .NET 10 or later, when set to `true` whatever the referenced packages and the target framework are, and never when set to `false`. |
| `EnableXunitFullParallelization` | Auto | Generates a source file with `[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.All)]` so every test runs in parallel, not just test collections. Generated when xUnit.net v3 4.0 or later is resolved, when set to `true` whatever the resolved references are, and never when set to `false`. |
| `XunitParallelizationMode` | `All` | Sets the `Xunit.Sdk.ParallelMode` value used by the generated attribute: `None`, `Collections` or `All`. Setting it also generates the source file whatever the resolved references are. The file is not generated when `EnableXunitFullParallelization` is `false`. |
| `EnableXunitStaticHelpers` | Auto | Generates the `Meziantou.NET.Sdk.Test.XUnitStaticHelpers` static class exposing `XunitCancellationToken` (`TestContext.Current.CancellationToken`). Generated when an xUnit.net v3 package is referenced, when set to `true` whatever the referenced packages are, and never when set to `false`. The global `using static` directive requires `ImplicitUsings`. |
| `EnableXunitEntryPointDisableWarnings` | `true` | Defines the `XUNIT_ENTRYPOINT_DISABLE_WARNINGS` compilation constant. Set it to `false` to not define the constant. |
| `EnableGitHubActionsReport` | `true` | Adds `Microsoft.Testing.Extensions.GitHubActionsReport` and `--report-gh --report-gh-slow-test-notices off`. The extension is inert unless the build runs on GitHub Actions. Slow-test notices are disabled as they are mostly noise on CI machines with varying performance. |
| `EnableCodeCoverage` | `true` on CI | Enables code coverage collection on CI. |
| `MinimumExpectedTests` | `1` | Sets `--minimum-expected-tests`, the number of tests that must run for the test run to succeed. Set it to `0` to not set the argument, as Microsoft.Testing.Platform only accepts a non-zero positive value. Note that the platform still expects at least one test to run when the argument is not set. |
| `OptimizeVsTestRun` | `true` | Disables analyzers during `dotnet test` unless set to `false`. |
| `UseMicrosoftTestingPlatform` | Auto | Uses MTP when set to `true` or when `xunit.v3`, `xunit.v3.mtp-v2`, `xunit.v3.core.mtp-v2`, or `TUnit` is referenced. `Microsoft.NET.Test.Sdk` is added only when MTP is not used. |
| `EnableDefaultTestSettings` | `true` | Adds default crash/hang dumps and loggers. |
| `TestingPlatformCommandLineArguments` | Appended | Adds MTP arguments such as `--report-trx`, `--report-gh` and `--coverage` when enabled. |
| `VSTestBlame` | `true` | Enables VSTest blame. |
| `VSTestBlameCrash` | `true` | Enables crash dumps. |
| `VSTestBlameCrashDumpType` | `mini` | Sets crash dump type. |
| `VSTestBlameHang` | `true` | Enables hang dumps. |
| `VSTestBlameHangDumpType` | `mini` | Sets hang dump type. |
| `VSTestBlameHangTimeout` | `10min` | Sets hang dump timeout. |
| `VSTestCollect` | `Code Coverage` when enabled | Enables VSTest code coverage. |
| `VSTestSetting` | Default runsettings when enabled | Uses the default runsettings file for coverage. |
| `VSTestLogger` | `trx;console%3bverbosity=normal` | Appends loggers. |
