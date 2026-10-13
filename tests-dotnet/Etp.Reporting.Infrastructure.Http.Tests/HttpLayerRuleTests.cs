using Etp.Reporting.Infrastructure.Http;

namespace Etp.Reporting.Infrastructure.Http.Tests;

public sealed class HttpLayerRuleTests
{
    [Fact]
    public void Http_infrastructure_references_only_the_application_layer()
    {
        var references = typeof(HttpInfrastructureAssembly).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? "")
            .Where(name => name.StartsWith("Etp.Reporting.", StringComparison.Ordinal))
            .ToArray();
        // Compiler-pruned references mean an unused Application reference may be absent; anything else Etp is a layer fault.
        Assert.All(references, name => Assert.Equal("Etp.Reporting.Application", name));
    }

    [Fact]
    public void Http_infrastructure_takes_no_package_dependency()
    {
        var packages = typeof(HttpInfrastructureAssembly).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? "")
            .Where(name => name.StartsWith("Microsoft.Data.", StringComparison.Ordinal) || name.StartsWith("DocumentFormat.", StringComparison.Ordinal) || name.StartsWith("Newtonsoft.", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(packages);
    }
}
