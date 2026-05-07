# Meziantou.NET.Sdk

- [![Meziantou.NET.Sdk on NuGet](https://img.shields.io/nuget/v/Meziantou.NET.Sdk.svg)](https://www.nuget.org/packages/Meziantou.NET.Sdk/)

MSBuild SDK that helps standardize build and quality settings across repositories. It provides:
- Opinionated defaults and naming conventions for .NET projects
- Best practices for build, CI, and test workflows
- A static analysis baseline with Roslyn analyzers
- Set `ContinuousIntegrationBuild` based on the context
- dotnet test features
  - Dump on crash or hang
  - Loggers when running on GitHub
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
| `MSBuildTreatWarningsAsErrors` | `true` on CI or Release | Treats MSBuild warnings as errors. |
| `TreatWarningsAsErrors` | `true` on CI or Release | Treats compiler warnings as errors. |
| `EnforceCodeStyleInBuild` | `true` on CI or Release | Enforces analyzer code style during builds. |
| `AccelerateBuildsInVisualStudio` | `true` | Enables faster builds in Visual Studio. |

## Package validation and auditing

| Property | Default | Description |
| --- | --- | --- |
| `EnablePackageValidation` | `true` | Enables package validation when unset. |
| `NuGetAudit` | `true` | Enables NuGet vulnerability auditing. |
| `NuGetAuditMode` | `all` | Audits all dependency types. |
| `NuGetAuditLevel` | `low` | Minimum severity level to report. |
| `WarningsAsErrors` | Adds `NU1900`–`NU1904` on CI or Release | Promotes NuGet audit warnings to errors. |

## Banned symbols and analyzers

| Property | Default | Description |
| --- | --- | --- |
| `IncludeDefaultBannedSymbols` | `true` | Includes the default banned API list. |
| `BannedNewtonsoftJsonSymbols` | `true` | Includes banned Newtonsoft.Json APIs. |
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
| `NpmRestoreLockedMode` | `true` on CI or when `RestoreLockedMode` is `true` | Uses `npm ci` when `true`, otherwise `npm install`. |

## Testing

| Property | Default | Description |
| --- | --- | --- |
| `EnableCodeCoverage` | `true` on CI | Enables code coverage collection on CI. |
| `OptimizeVsTestRun` | `true` | Disables analyzers during `dotnet test` unless set to `false`. |
| `UseMicrosoftTestingPlatform` | Auto | Uses MTP when set to `true` or when `xunit.v3.mtp-v2` or `TUnit` is referenced. |
| `EnableDefaultTestSettings` | `true` | Adds default crash/hang dumps and loggers. |
| `TestingPlatformCommandLineArguments` | Appended | Adds MTP arguments such as `--report-trx` and `--coverage` when enabled. |
| `VSTestBlame` | `true` | Enables VSTest blame. |
| `VSTestBlameCrash` | `true` | Enables crash dumps. |
| `VSTestBlameCrashDumpType` | `mini` | Sets crash dump type. |
| `VSTestBlameHang` | `true` | Enables hang dumps. |
| `VSTestBlameHangDumpType` | `mini` | Sets hang dump type. |
| `VSTestBlameHangTimeout` | `10min` | Sets hang dump timeout. |
| `VSTestCollect` | `Code Coverage` when enabled | Enables VSTest code coverage. |
| `VSTestSetting` | Default runsettings when enabled | Uses the default runsettings file for coverage. |
| `VSTestLogger` | `trx;console%3bverbosity=normal` | Appends loggers, including GitHub Actions on CI. |
