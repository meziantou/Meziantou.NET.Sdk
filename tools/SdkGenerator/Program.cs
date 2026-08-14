using Meziantou.Framework;

var rootFolder = GetRootFolderPath();
var sdkRootPath = rootFolder / "src" / "sdk";

var sdks = new (string SdkName, string BaseSdkName)[] {
    ("Meziantou.NET.Sdk", "Microsoft.NET.Sdk"),
    ("Meziantou.NET.Sdk.BlazorWebAssembly", "Microsoft.NET.Sdk.BlazorWebAssembly"),
    ("Meziantou.NET.Sdk.Razor", "Microsoft.NET.Sdk.Razor"),
    ("Meziantou.NET.Sdk.Test", "Microsoft.NET.Sdk"),
    ("Meziantou.NET.Sdk.Web", "Microsoft.NET.Sdk.Web"),
    ("Meziantou.NET.Sdk.WindowsDesktop", "Microsoft.NET.Sdk.WindowsDesktop"),
};

foreach (var (sdkName, baseSdkName) in sdks)
{
    var propsPath = sdkRootPath / sdkName / "Sdk.props";
    var targetsPath = sdkRootPath / sdkName / "Sdk.targets";
    var nuspecPath = rootFolder / "src" / $"{sdkName}.nuspec";
    var csprojPath = rootFolder / "src" / $"{sdkName}.csproj";

    propsPath.CreateParentDirectory();
    targetsPath.CreateParentDirectory();
    nuspecPath.CreateParentDirectory();
    csprojPath.CreateParentDirectory();

    File.WriteAllText(csprojPath, $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <NoBuild>true</NoBuild>
            <IncludeBuildOutput>false</IncludeBuildOutput>
            <TargetFramework>netstandard2.0</TargetFramework>
            <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
            <NoWarn>NU5128</NoWarn>
            <NuSpecFile>{{nuspecPath.Name}}</NuSpecFile>
            <Version>1.0.0</Version>
            <NuspecProperties>$(NuspecProperties);version=$(Version)</NuspecProperties>
            <NuspecProperties>$(NuspecProperties);RepositoryBranch=$(GITHUB_REF_NAME);RepositoryUrl=$(GITHUB_REPOSITORY_URL)</NuspecProperties>
            <NuspecProperties>$(NuspecProperties);RepositoryUrl=$(GITHUB_REPOSITORY_URL)</NuspecProperties>
            <NuspecProperties>$(NuspecProperties);RepositoryUrl=$(GITHUB_REPOSITORY_URL)</NuspecProperties>
          </PropertyGroup>
        </Project>
        """);

    File.WriteAllText(propsPath, $$"""
        <Project>
            <PropertyGroup>
                <MeziantouSdkName>{{sdkName}}</MeziantouSdkName>
                <_MustImportMicrosoftNETSdk Condition="'$(UsingMicrosoftNETSdk)' != 'true'">true</_MustImportMicrosoftNETSdk>

                <CustomBeforeDirectoryBuildProps>$(CustomBeforeDirectoryBuildProps);$(MSBuildThisFileDirectory)../common/Common.props</CustomBeforeDirectoryBuildProps>
                <BeforeMicrosoftNETSdkTargets>$(BeforeMicrosoftNETSdkTargets);$(MSBuildThisFileDirectory)/../common/Common.targets</BeforeMicrosoftNETSdkTargets>
            </PropertyGroup>

            <Import Project="Sdk.props" Sdk="{{baseSdkName}}" Condition="'$(_MustImportMicrosoftNETSdk)' == 'true'" />
            <Import Project="$(MSBuildThisFileDirectory)../common/Common.props" Condition="'$(_MustImportMicrosoftNETSdk)' != 'true'" />
        </Project>
        """);

    File.WriteAllText(targetsPath, $$"""
        <Project>
            <Import Project="Sdk.targets" Sdk="{{baseSdkName}}" Condition="'$(_MustImportMicrosoftNETSdk)' == 'true'" />
        </Project>
        """);

    var nuspecFiles = GetNuspecFiles(rootFolder / "src", sdkName);

    File.WriteAllText(nuspecPath, $$"""
        <?xml version="1.0"?>
        <package>
          <metadata>
            <id>{{sdkName}}</id>
            <version>1.0.0</version>
            <authors>Meziantou</authors>
            <requireLicenseAcceptance>false</requireLicenseAcceptance>
            <description>Meziantou SDK for .NET projects</description>
            <readme>README.md</readme>
            <license type="expression">MIT</license>
            <repository type="git" url="$RepositoryUrl$" commit="$RepositoryCommit$" branch="$RepositoryBranch$" />
          </metadata>
          <files>
        {{nuspecFiles}}
          </files>
        </package>
        """);

    Console.WriteLine($"Generated {sdkName}");
}

static FullPath GetRootFolderPath()
{
    var path = FullPath.CurrentDirectory();
    while (!path.IsEmpty)
    {
        if (Directory.Exists(path / ".git") || File.Exists(path / ".git"))
            return path;

        path = path.Parent;
    }

    if (path.IsEmpty)
        throw new InvalidOperationException("Cannot find the root folder");

    return path;
}

static string GetNuspecFiles(FullPath srcFolderPath, string sdkName)
{
    var files = new List<(string Source, string Target)>
    {
        ($"Sdk/{sdkName}/Sdk.props", "Sdk/Sdk.props"),
        ($"Sdk/{sdkName}/Sdk.targets", "Sdk/Sdk.targets"),
    };

    AddDirectoryFiles(files, srcFolderPath, "common");
    AddDirectoryFiles(files, srcFolderPath, "configuration");

    files.Add(("icon.png", "icon.png"));
    files.Add(("icon.svg", "icon.svg"));
    files.Add(("../LICENSE.txt", "LICENSE.txt"));
    files.Add(("../README.md", "README.md"));

    return string.Join(Environment.NewLine, files.Select(file => $"    <file src=\"{file.Source}\" target=\"{file.Target}\" />"));
}

static void AddDirectoryFiles(List<(string Source, string Target)> files, FullPath srcFolderPath, string directoryName)
{
    var directoryPath = srcFolderPath / directoryName;
    foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var relativePath = Path.GetRelativePath(srcFolderPath, filePath).Replace('\\', '/');
        files.Add((relativePath, relativePath));
    }
}
