using System.Xml.Linq;

namespace GeoSurvey.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void RepositoryProjects_RespectDependencyRules()
    {
        var graph = ProjectDependencies.Read(FindRepositoryRoot());
        Assert.NotEmpty(graph);
        Assert.Empty(ProjectDependencies.Validate(graph));
    }

    [Fact]
    public void BackendDomainProjects_HaveNoDeclaredPackageOrProjectReferences()
    {
        var violations = ProjectDependencies.ReadDomainViolations(FindRepositoryRoot());
        Assert.Contains("src/Backend/Modules/Organizations/GeoSurvey.Organizations.Domain/GeoSurvey.Organizations.Domain.csproj", violations.Keys);
        foreach (var (project, errors) in violations)
            Assert.True(errors.Length == 0, $"{project}: {string.Join(Environment.NewLine, errors)}");
    }

    [Theory]
    [InlineData("PackageReference", "Include")]
    [InlineData("ProjectReference", "Include")]
    [InlineData("PackageReference", "Update")]
    [InlineData("ProjectReference", "Update")]
    public void DomainReferences_AreRejectedEvenWhenConditional(string kind, string attribute)
    {
        var project = XDocument.Parse($"""
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <{kind} {attribute}="ForbiddenDependency" />
              </ItemGroup>
            </Project>
            """);

        var error = Assert.Single(ProjectDependencies.ValidateDomainReferences(project));
        Assert.Contains(kind, error);
        Assert.Contains("ForbiddenDependency", error);
    }

    [Fact]
    public void DomainProject_WithOnlyFrameworkSdkAndProperties_IsAllowed()
    {
        var project = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        Assert.Empty(ProjectDependencies.ValidateDomainReferences(project));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoSurvey.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }

    [Theory]
    [InlineData("src/Backend/Api/Api.csproj", "tests/Unit/Tests.csproj", "Production cannot reference tests")]
    [InlineData("src/Backend/Api/Api.csproj", "src/Mobile/App/App.csproj", "Backend/mobile boundary crossed")]
    [InlineData("src/Mobile/App/App.csproj", "src/Backend/Api/Api.csproj", "Backend/mobile boundary crossed")]
    public void ForbiddenReference_IsDetected(string source, string target, string message)
    {
        var graph = new Dictionary<string, string[]> { [source] = [target], [target] = [] };
        Assert.Contains(ProjectDependencies.Validate(graph), error => error.StartsWith(message, StringComparison.Ordinal));
    }

    [Fact]
    public void AllowedDependencies_AreAccepted()
    {
        var graph = new Dictionary<string, string[]>
        {
            ["tests/Integration/Tests.csproj"] = ["src/Backend/Api/Api.csproj"],
            ["src/Backend/Api/Api.csproj"] = ["src/Backend/Domain/Domain.csproj"],
            ["src/Backend/Domain/Domain.csproj"] = []
        };
        Assert.Empty(ProjectDependencies.Validate(graph));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CircularDependency_IsDetected(bool selfReference)
    {
        var graph = new Dictionary<string, string[]>
        {
            ["A"] = [selfReference ? "A" : "B"],
            ["B"] = ["C"],
            ["C"] = ["A"]
        };
        Assert.Contains(ProjectDependencies.Validate(graph), error => error.StartsWith("Circular", StringComparison.Ordinal));
    }
}
