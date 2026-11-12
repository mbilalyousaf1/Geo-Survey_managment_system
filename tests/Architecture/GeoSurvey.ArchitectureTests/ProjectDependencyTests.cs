namespace GeoSurvey.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void RepositoryProjects_RespectDependencyRules()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoSurvey.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var graph = ProjectDependencies.Read(directory.FullName);
        Assert.NotEmpty(graph);
        Assert.Empty(ProjectDependencies.Validate(graph));
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
