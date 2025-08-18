Push-Location -Path $PSScriptRoot
nuget pack Meziantou.NET.Sdk.nuspec -OutputDirectory nupkgs
nuget pack Meziantou.NET.Sdk.Web.nuspec -OutputDirectory nupkgs
Pop-Location