using System.Xml.Linq;

namespace GeoSurvey.ArchitectureTests;

internal static class ProjectDependencies
{
    public static Dictionary<string, string[]> ReadDomainViolations(string root)
    {
        return FindProjects(Path.Combine(root, "src", "Backend"))
            .Where(project => Path.GetFileName(project).EndsWith(".Domain.csproj", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(project => Relative(root, project),
                project => ValidateDomainReferences(XDocument.Load(project)), StringComparer.OrdinalIgnoreCase);
    }

    public static string[] ValidateDomainReferences(XDocument project)
    {
        // Inspect all declarations, including conditional and Update/Remove entries.
        // Imports and SDK-provided framework references are not evaluated here.
        return project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => $"Domain projects cannot declare {element.Name.LocalName}: "
                + (element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value
                    ?? element.Attribute("Remove")?.Value ?? "(unspecified)"))
            .ToArray();
    }

    public static Dictionary<string, string[]> Read(string root)
    {
        var graph = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { "src", "tests" })
        {
            foreach (var project in FindProjects(Path.Combine(root, folder)))
            {
                var references = XDocument.Load(project).Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference")
                    .Select(element => element.Attribute("Include")?.Value
                        ?? throw new InvalidOperationException($"Missing ProjectReference Include: {project}"))
                    .Select(reference => ResolveReference(root, project, reference))
                    .ToArray();
                graph.Add(Relative(root, project), references);
            }
        }

        return graph;
    }

    public static string[] Validate(IReadOnlyDictionary<string, string[]> graph)
    {
        var errors = new List<string>();
        foreach (var (project, references) in graph)
        {
            foreach (var reference in references)
            {
                if (!graph.ContainsKey(reference))
                    errors.Add($"Unknown project reference: {project} -> {reference}");

                if (project.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
                    && reference.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Production cannot reference tests: {project} -> {reference}");

                if ((IsBackend(project) && IsMobile(reference)) || (IsMobile(project) && IsBackend(reference)))
                    errors.Add($"Backend/mobile boundary crossed: {project} -> {reference}");
            }
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in graph.Keys)
            Visit(project);

        return errors.ToArray();

        void Visit(string project)
        {
            if (visiting.Contains(project))
            {
                errors.Add($"Circular project dependency detected at: {project}");
                return;
            }

            if (!visited.Add(project))
                return;

            visiting.Add(project);
            foreach (var reference in graph[project].Where(graph.ContainsKey))
                Visit(reference);
            visiting.Remove(project);
        }
    }

    private static IEnumerable<string> FindProjects(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.csproj"))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (new[] { "bin", "obj", "node_modules", ".angular", "dist", ".git" }
                .Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                continue;

            foreach (var project in FindProjects(child))
                yield return project;
        }
    }

    private static string ResolveReference(string root, string project, string reference)
    {
        // Static inspection intentionally rejects expressions rather than silently missing edges.
        if (reference.IndexOfAny(['$', '@', '*', '?', ';']) >= 0)
            throw new InvalidOperationException($"Unsupported project reference in {project}: {reference}");

        var path = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Relative(root, Path.GetFullPath(path, Path.GetDirectoryName(project)!));
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static bool IsBackend(string path) => path.StartsWith("src/Backend/", StringComparison.OrdinalIgnoreCase);
    private static bool IsMobile(string path) => path.StartsWith("src/Mobile/", StringComparison.OrdinalIgnoreCase);
}
