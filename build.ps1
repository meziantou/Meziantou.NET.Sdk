Push-Location -Path $PSScriptRoot/src
try {
    Get-ChildItem *.csproj | ForEach-Object -Parallel {
        dotnet pack $_ --output $using:PSScriptRoot/artifacts @using:args
    }
}
finally {
    Pop-Location
}