using System.Xml.Linq;

namespace ServerPilot.UnitTests.Architecture;

public sealed class ProjectDependencyRulesTests
{
    [Theory]
    [InlineData("ServerPilot.Domain", new string[] { })]
    [InlineData("ServerPilot.Application", new[] { "ServerPilot.Domain" })]
    [InlineData(
        "ServerPilot.Infrastructure",
        new[] { "ServerPilot.Application", "ServerPilot.Domain" })]
    [InlineData(
        "ServerPilot.Api",
        new[] { "ServerPilot.Application", "ServerPilot.Infrastructure" })]
    [InlineData("ServerPilot.Agent", new string[] { })]
    public void ProductionProjectReferencesMatchArchitectureRules(
        string projectName,
        string[] expectedReferences)
    {
        string projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            projectName,
            $"{projectName}.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] actualReferences = project
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
    }

    [Fact]
    public void DomainDoesNotReferenceInfrastructureFrameworks()
    {
        string projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ServerPilot.Domain",
            "ServerPilot.Domain.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] packages = project
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .ToArray();

        Assert.DoesNotContain(packages, package =>
            package.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
            package.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase) ||
            package.Contains("Npgsql", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the ServerPilot repository root.");
    }
}
