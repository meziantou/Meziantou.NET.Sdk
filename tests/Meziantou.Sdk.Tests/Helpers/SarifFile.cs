using System.Text.Json.Serialization;

namespace Meziantou.Sdk.Tests.Helpers;

internal sealed class SarifFile
{
    [JsonPropertyName("runs")]
    public SarifFileRun[] Runs { get; set; }

    public IEnumerable<SarifFileRunResult> AllResults() => Runs.SelectMany(r => r.Results);
}
