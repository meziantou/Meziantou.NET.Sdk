using Microsoft.Build.Logging.StructuredLogger;

namespace Meziantou.Sdk.Tests.Helpers;

internal sealed record BuildResult(int ExitCode, IReadOnlyList<string> OutputLines, SarifFile SarifFile, byte[] BinaryLogContent)
{
    // MSBuild.StructuredLogger uses mutable static state, so reading several binary logs concurrently can produce an
    // incomplete tree, such as a project evaluation with no property. Tests run in parallel, so the reads must be
    // serialized. The result is cached because a single test reads the same binary log multiple times.
    private static readonly Lock BinaryLogLock = new();

    private Build _build;

    public bool OutputContains(string value, StringComparison stringComparison = StringComparison.Ordinal) => OutputLines.Any(line => line.Contains(value, stringComparison));
    public bool OutputDoesNotContain(string value, StringComparison stringComparison = StringComparison.Ordinal) => !OutputLines.Any(line => line.Contains(value, stringComparison));

    public bool HasError() => SarifFile.AllResults().Any(r => r.Level == "error");
    public bool HasError(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "error" && r.RuleId == ruleId);
    public bool HasWarning() => SarifFile.AllResults().Any(r => r.Level == "warning");
    public bool HasWarning(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "warning" && r.RuleId == ruleId);
    public bool HasNote(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "note" && r.RuleId == ruleId);

    private Build GetBuild()
    {
        lock (BinaryLogLock)
        {
            if (_build is null)
            {
                using var stream = new MemoryStream(BinaryLogContent);
                _build = Serialization.ReadBinLog(stream);
            }

            return _build;
        }
    }

    public IReadOnlyCollection<string> GetBinLogFiles()
    {
        var build = GetBuild();
        return [.. build.SourceFiles.Select(file => file.FullPath)];
    }

    public List<string> GetMSBuildItems(string name)
    {
        var result = new List<string>();
        var build = GetBuild();
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
        var build = GetBuild();
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
        var build = GetBuild();
        var task = build.FindLastDescendant<Microsoft.Build.Logging.StructuredLogger.Task>(task => task.Name is "Csc" or "Vbc" or "Fsc");
        return task?.FindChild<Property>(property => property.Name is "CommandLineArguments")?.Value;
    }

    public string GetMSBuildPropertyValue(string name)
    {
        var build = GetBuild();
        return build.FindLastDescendant<Property>(e => e.Name == name)?.Value;
    }

    public void AssertMSBuildPropertyValue(string name, string expectedValue, bool ignoreCase = true)
    {
        var actual = GetMSBuildPropertyValue(name);

        Assert.Equal(expectedValue, actual, ignoreCase: ignoreCase);
    }

    public bool IsMSBuildTargetExecuted(string name)
    {
        var build = GetBuild();
        var target = build.FindLastDescendant<Target>(e => e.Name == name);
        if (target is null)
            return false;

        if (target.Skipped)
            return false;

        return true;
    }
}
