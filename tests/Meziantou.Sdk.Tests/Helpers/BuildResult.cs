using Meziantou.Framework;
using Microsoft.Build.Logging.StructuredLogger;

namespace Meziantou.Sdk.Tests.Helpers;

internal sealed record BuildResult(int ExitCode, ProcessOutputCollection ProcessOutput, SarifFile SarifFile, byte[] BinaryLogContent, string VSTestDiagnosticFileContent)
{
    public bool OutputContains(string value, StringComparison stringComparison = StringComparison.Ordinal) => ProcessOutput.Any(line => line.Text.Contains(value, stringComparison));
    public bool OutputDoesNotContain(string value, StringComparison stringComparison = StringComparison.Ordinal) => !ProcessOutput.Any(line => line.Text.Contains(value, stringComparison));

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
}
