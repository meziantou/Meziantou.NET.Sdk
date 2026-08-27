$ErrorActionPreference = "Stop"

$PackArguments = $args

dotnet run --project $PSScriptRoot/tools/ConfigFilesGenerator/ConfigFilesGenerator.csproj
if ($LASTEXITCODE -ne 0) {
    throw "Generating the configuration files failed with exit code $LASTEXITCODE."
}

# The projects all live in the same folder, so they share the 'src/obj' and 'src/bin' folders
# (project.assets.json, project.nuget.cache, ...). Packing them in parallel makes them race on
# those files, which can make a package silently missing from the output folder.
Get-ChildItem $PSScriptRoot/src/*.csproj | ForEach-Object {
    dotnet pack $_ --output $PSScriptRoot/artifacts @PackArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$($_.Name)' failed with exit code $LASTEXITCODE."
    }
}