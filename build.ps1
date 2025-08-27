Push-Location -Path $PSScriptRoot
foreach($Nuspec in (Get-ChildItem *.nuspec)){
    nuget pack $Nuspec -OutputDirectory nupkgs
}
Pop-Location