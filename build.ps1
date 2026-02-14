dotnet run --project  $PSScriptRoot/tools/ConfigFilesGenerator/ConfigFilesGenerator.csproj
Get-ChildItem $PSScriptRoot/src/*.csproj | ForEach-Object -Parallel {
    dotnet pack $_ --output $using:PSScriptRoot/artifacts @using:args
}