using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace OptimizeCsprojReferences;

public class Project
{
    public Project(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }
    public string Path { get; }
    
    public bool IsSdkProj { get; set; } = true;
    
    public List<IPackageSearchMetadata> PackageRefs { get; } = new();
    public List<Project> ProjectRefs { get; } = new();

    public override string ToString()
    {
        return $"{Name} (Projs: {ProjectRefs.Count}, Pkgs: {PackageRefs.Count})";
    }
}