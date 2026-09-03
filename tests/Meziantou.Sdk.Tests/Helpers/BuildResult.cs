using Microsoft.Build.Logging.StructuredLogger;

namespace Meziantou.Sdk.Tests.Helpers;

internal sealed record BuildResult(int ExitCode, IReadOnlyList<string> OutputLines, SarifFile SarifFile, byte[] BinaryLogContent)
{
    public bool OutputContains(string value, StringComparison stringComparison = StringComparison.Ordinal) => OutputLines.Any(line => line.Contains(value, stringComparison));
    public bool OutputDoesNotContain(string value, StringComparison stringComparison = StringComparison.Ordinal) => !OutputLines.Any(line => line.Contains(value, stringComparison));

    public bool HasError() => SarifFile.AllResults().Any(r => r.Level == "error");
    public bool HasError(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "error" && r.RuleId == ruleId);
    public bool HasWarning() => SarifFile.AllResults().Any(r => r.Level == "warning");
    public bool HasWarning(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "warning" && r.RuleId == ruleId);
    public bool HasNote(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "note" && r.RuleId == ruleId);

    public IReadOnlyCollection<string> GetBinLogFiles()
    {
        using var stream = new MemoryStream(BinaryLogContent);
        var build = Serialization.ReadBinLog(stream);
        return [.. build.SourceFiles.Select(file => file.FullPath)];
    }

    public List<string> GetMSBuildItems(string name)
    {
        var result = new List<string>();
        using var stream = new MemoryStream(BinaryLogContent);
        var build = Serialization.ReadBinLog(stream);
        build.VisitAllChildren<Item>(item =>
        {
            if (item.Parent is AddItem parent && parent.Name == name)
            {
                result.Add(item.Name);
            }
        });

        return result;
    }

    public string GetMSBuildItemMetadata(string itemName, string itemSpec, string metadataName)
    {
        string result = null;
        using var stream = new MemoryStream(BinaryLogContent);
        var build = Serialization.ReadBinLog(stream);
        build.VisitAllChildren<Item>(item =>
        {
            if (item.Parent is AddItem parent && parent.Name == itemName && string.Equals(item.Name, itemSpec, StringComparison.OrdinalIgnoreCase))
            {
                var metadata = item.Children.OfType<Metadata>().LastOrDefault(metadata => string.Equals(metadata.Name, metadataName, StringComparison.OrdinalIgnoreCase));
                if (metadata is not null)
                {
                    result = metadata.Value;
                }
            }
        });

        return result;
    }

    public string GetCompilerCommandLineArguments()
    {
        using var stream = new MemoryStream(BinaryLogContent);
        var build = Serialization.ReadBinLog(stream);
        var task = build.FindLastDescendant<Microsoft.Build.Logging.StructuredLogger.Task>(task => task.Name is "Csc" or "Vbc" or "Fsc");
        return task?.FindChild<Property>(property => property.Name is "CommandLineArguments")?.Value;
    }

    public string GetMSBuildPropertyValue(string name)
    {
        using var stream = new MemoryStream(BinaryLogContent);
        var build = Serialization.ReadBinLog(stream);
        return build.FindLastDescendant<Property>(e => e.Name == name)?.Value;
    }

    public void AssertMSBuildPropertyValue(string name, string expectedValue, bool ignoreCase = true)
    {
        using var stream = new MemoryStream(BinaryLogContent);
        var build = Serialization.ReadBinLog(stream);
        var actual = build.FindLastDescendant<Property>(e => e.Name == name)?.Value;

        Assert.Equal(expectedValue, actual, ignoreCase: ignoreCase);
    }

    public bool IsMSBuildTargetExecuted(string name)
    {
        using var stream = new MemoryStream(BinaryLogContent);
        var build = Serialization.ReadBinLog(stream);
        var target = build.FindLastDescendant<Target>(e => e.Name == name);
        if (target is null)
            return false;

        if (target.Skipped)
            return false;

        return true;
    }
}
