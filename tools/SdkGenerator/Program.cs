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
    var nuspecPath = sdkRootPath / $"{sdkName}.nuspec";

    propsPath.CreateParentDirectory();
    targetsPath.CreateParentDirectory();
    nuspecPath.CreateParentDirectory();


    File.WriteAllText(propsPath, $$"""
        <Project>
            <PropertyGroup>
                <MeziantouSdkName>{{sdkName}}</MeziantouSdkName>
                <_MustImportMicrosoftNETSdk Condition="'$(UsingMicrosoftNETSdk)' != 'true'">true</_MustImportMicrosoftNETSdk>

                <!-- If Microsoft SDK is already imported, we want to execute our targets before theirs.
                     So, we use the extension point. Also, if there was already a target registered, we want to make sure to execute it.
                -->
                <_MeziantouBeforeMicrosoftNETSdkTargets>$(BeforeMicrosoftNETSdkTargets)</_MeziantouBeforeMicrosoftNETSdkTargets>
                <BeforeMicrosoftNETSdkTargets>$(MSBuildThisFileDirectory)/../common/Common.targets</BeforeMicrosoftNETSdkTargets>
            </PropertyGroup>

            <Import Project="Sdk.props" Sdk="{{baseSdkName}}" Condition="'$(_MustImportMicrosoftNETSdk)' == 'true'" />
            <Import Project="$(MSBuildThisFileDirectory)/../common/Common.props" />
        </Project>
        """);


    File.WriteAllText(targetsPath, $$"""
        <Project>
            <Import Project="Sdk.targets" Sdk="{{baseSdkName}}" Condition="'$(_MustImportMicrosoftNETSdk)' == 'true'" />
        </Project>
        """);

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
            <file src="Sdk/{{sdkName}}/Sdk.props" target="Sdk/Sdk.props" />
            <file src="Sdk/{{sdkName}}/Sdk.targets" target="Sdk/Sdk.targets" />
            <file src="common/**/*" target="" />
            <file src="configuration/**/*" target="" />
            <file src="icon.png" target="" />
            <file src="icon.svg" target="" />
            <file src="../LICENSE.txt" target="" />
            <file src="../README.md" target="" />
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
        if (Directory.Exists(path / ".git"))
            return path;

        path = path.Parent;
    }

    if (path.IsEmpty)
        throw new InvalidOperationException("Cannot find the root folder");

    return path;
}