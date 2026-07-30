using System.Reflection;
using AnalysisService.Worker.Messaging;
using DocumentIntelligence.Contracts.Contracts;
using DocumentService.Api.Controllers;
using NetArchTest.Rules;

namespace DocumentIntelligence.ArchitectureTests;

// The boundaries this repo relies on are conventions: nothing in the compiler stops
// someone from referencing EF Core out of a controller, or the API out of the worker.
// These tests turn those conventions into build failures.
public class ModuleBoundaryTests
{
    private static readonly Assembly ContractsAssembly = typeof(AnalyzeDocumentCommand).Assembly;
    private static readonly Assembly ApiAssembly = typeof(DocumentsController).Assembly;
    private static readonly Assembly WorkerAssembly = typeof(AnalyzeDocumentCommandHandler).Assembly;

    // Namespace prefixes of infrastructure the shared contracts must stay clear of.
    private static readonly string[] ForbiddenInContracts =
    {
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Azure"
    };

    [Fact]
    public void Contracts_do_not_depend_on_infrastructure()
    {
        // Contracts are a published language: every service has to be able to reference
        // them, so they must not drag in a persistence, web or cloud stack.
        var result = Types.InAssembly(ContractsAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenInContracts)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Contracts_reference_no_infrastructure_assemblies()
    {
        // Belt and braces for the rule above: catches a package reference that has been
        // added to the project but is not used by any type yet.
        var referenced = ContractsAssembly.GetReferencedAssemblies().Select(a => a.Name ?? "");

        var offenders = referenced
            .Where(name => ForbiddenInContracts.Any(
                prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Contracts must not reference infrastructure assemblies: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Api_does_not_reference_the_worker()
    {
        AssertDoesNotReference(ApiAssembly, WorkerAssembly);
    }

    [Fact]
    public void Worker_does_not_reference_the_api()
    {
        AssertDoesNotReference(WorkerAssembly, ApiAssembly);
    }

    [Fact]
    public void Only_api_infrastructure_touches_ef_core()
    {
        // Persistence stays behind the repository. Program.cs is deliberately exempt: the
        // composition root is where DbContext registration belongs, and its top-level
        // statements compile into the global namespace, outside the filter below.
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("DocumentService.Api")
            .And()
            .DoNotResideInNamespace("DocumentService.Api.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static void AssertDoesNotReference(Assembly source, Assembly forbidden)
    {
        var forbiddenName = forbidden.GetName().Name;

        var references = source.GetReferencedAssemblies().Select(a => a.Name);

        Assert.False(
            references.Contains(forbiddenName),
            $"{source.GetName().Name} must not reference {forbiddenName}. "
            + "The two services communicate over messages only.");
    }

    private static string Describe(TestResult result) =>
        "Offending types: "
        + string.Join(", ", result.FailingTypeNames ?? new List<string>());
}
