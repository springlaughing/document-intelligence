using System.Reflection;
using DocumentService.Api.Features.Documents.GetDocument;
using NetArchTest.Rules;

namespace DocumentIntelligence.ArchitectureTests;

// A vertical slice is only worth the folder if nothing reaches across it. Without a
// rule, one slice quietly calls another's handler and the structure decays back into
// layers with extra steps.
public class SliceIsolationTests
{
    private const string SlicesRoot = "DocumentService.Api.Features.Documents";

    private static readonly Assembly ApiAssembly = typeof(GetDocumentController).Assembly;

    private static readonly string[] SliceNames =
    {
        "GetDocument",
        "RegisterDocument",
        "RequestAnalysis",
        "RecordAnalysisResult"
    };

    public static TheoryData<string> Slices()
    {
        var data = new TheoryData<string>();
        foreach (var slice in SliceNames) data.Add(slice);
        return data;
    }

    [Theory]
    [MemberData(nameof(Slices))]
    public void Slice_does_not_reference_another_slice(string slice)
    {
        var others = SliceNames
            .Where(s => s != slice)
            .Select(s => $"{SlicesRoot}.{s}")
            .ToArray();

        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace($"{SlicesRoot}.{slice}")
            .ShouldNot()
            .HaveDependencyOnAny(others)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Slice '{slice}' reaches into another slice. Offending types: "
            + string.Join(", ", result.FailingTypeNames ?? new List<string>())
            + ". Share through Domain, Infrastructure or a route name instead.");
    }

    [Fact]
    public void Every_slice_namespace_is_covered_by_this_test()
    {
        // Guards against the rule above silently passing because a new slice was added
        // and never listed here.
        var actual = ApiAssembly.GetTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns is not null && ns.StartsWith($"{SlicesRoot}.", StringComparison.Ordinal))
            .Select(ns => ns![(SlicesRoot.Length + 1)..].Split('.')[0])
            .Distinct()
            .ToArray();

        var unlisted = actual.Except(SliceNames).ToArray();

        Assert.True(
            unlisted.Length == 0,
            "Slices not listed in SliceNames: " + string.Join(", ", unlisted));
    }
}
