Push-Location -Path $PSScriptRoot/src/Sdk
try {
    Get-ChildItem *.nuspec | ForEach-Object -Parallel {
        nuget pack $_ -OutputDirectory nupkgs -BasePath $using:PSScriptRoot/src/ -OutputDirectory $using:PSScriptRoot/artifacts @using:args
    }
}
finally {
    Pop-Location
}