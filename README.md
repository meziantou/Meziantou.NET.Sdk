# Meziantou.NET.Sdk

- [![Meziantou.NET.Sdk on NuGet](https://img.shields.io/nuget/v/Meziantou.NET.Sdk.svg)](https://www.nuget.org/packages/Meziantou.NET.Sdk/)
- [![Meziantou.NET.Sdk.Web on NuGet](https://img.shields.io/nuget/v/Meziantou.NET.Sdk.Web.svg)](https://www.nuget.org/packages/Meziantou.NET.Sdk.Web/)
- [![Meziantou.NET.Sdk.Test on NuGet](https://img.shields.io/nuget/v/Meziantou.NET.Sdk.Test.svg)](https://www.nuget.org/packages/Meziantou.NET.Sdk.Test/)

MSBuild SDK that provides:
- Opinionated defaults for .NET projects
- Naming conventions
- Static analysis with Roslyn analyzers
- Set `ContinuousIntegrationBuild` based on the context
- dotnet test features
  - dump on crash or hang
  - loggers when running on GitHub
  - Disable Roslyn analyzers
- Relevant NuGet packages based on the project type

To use it, create a `global.json` file at the solution root with the following content:

````json
{
  "sdk": {
    "version": "9.0.304"
  },
  "msbuild-sdks": {
    "Meziantou.NET.Sdk": "1.0.4",
    "Meziantou.NET.Sdk.Test": "1.0.4",
    "Meziantou.NET.Sdk.Web": "1.0.4"
  }
}
````

And reference the SDK in your project file:

````xml
<Project Sdk="Meziantou.NET.Sdk">
</Project>
````

Or you can the SDK by specifying the version inside the `csproj` file:

````xml
<Project Sdk="Meziantou.NET.Sdk/1.0.4">
</Project>
````
