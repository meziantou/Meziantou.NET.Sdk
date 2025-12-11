Get-ChildItem $PSScriptRoot/src/*.csproj | ForEach-Object -Parallel {
    dotnet pack $_ --output $using:PSScriptRoot/artifacts @using:args
}