// See https://aka.ms/new-console-template for more information

using System.Text.RegularExpressions;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using OptimizeCsprojReferences;

if (Environment.GetCommandLineArgs().Length != 2)
{
    Console.WriteLine("Specify solution file");
    Environment.Exit(1);
}

var sln = Environment.GetCommandLineArgs()[1];
var pathRoot = Path.GetDirectoryName(sln);

SourceCacheContext cache = new();
List<PackageMetadataResource> repositories = new();

XElement xml = XDocument.Load(Path.Combine(pathRoot, "nuget.config")).Root;
var credentials = xml.Descendants("packageSourceCredentials");
foreach (var pkgSource in xml.Descendants("packageSources").Descendants())
{
    var repository = Repository.Factory.GetCoreV3(pkgSource.Attribute("value").Value);
    if (credentials.Any())
    {
        var key = pkgSource.Attribute("key").Value.Replace(" ", "_x0020_");
        string? user = null, password = null;
        foreach (var node in credentials.Descendants(key).Descendants())
        {
            var value = node.Attribute("value").Value;
            if (node.Attribute("key").Value == "Username")
                user = value;
            else
                password = value;
        }

        if (user != null)
            repository.PackageSource.Credentials =
                PackageSourceCredential.FromUserInput(key, user, password, false, null);
    }
    
    var resource = await repository.GetResourceAsync<PackageMetadataResource>();
    repositories.Add(resource);
}


List<Project> projects = new();
List<IPackageSearchMetadata> packages = new();
foreach (var line in await File.ReadAllLinesAsync(sln))
{
    var match = Regex.Match(line,
        @"Project\(""\{[0-9A-F\-]{36}\}""\) = ""(?<name>[\w\.]+)"", ""(?<path>[\w\.\\]+\.csproj)"", ""\{[0-9A-F\-]{36}\}""",
        RegexOptions.IgnoreCase);
    if (!match.Success)
        continue;
    
    projects.Add(new Project(match.Groups["name"].Value, Path.Combine(pathRoot, match.Groups["path"].Value)));
}

foreach (var proj in projects)
{
    bool firstLine = true;
    foreach (var line in await File.ReadAllLinesAsync(proj.Path))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        if (firstLine && !line.TrimStart().StartsWith("<Project Sdk=\""))
        {
            proj.IsSdkProj = false;
            continue;
        }

        firstLine = false;

        var match = Regex.Match(line,
            @"<PackageReference\s+Include=""(?<name>[\w\.]+)""\s+Version=""(?<version>[\d\.]+)""\s+/>",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            proj.PackageRefs.Add(await FindPackage(match.Groups["name"].Value, match.Groups["version"].Value));
            continue;
        }

        match = Regex.Match(line, @"<ProjectReference\s+Include=""(?<path>[\w\.\\]+\.csproj)""\s+/>",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            continue;

        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(proj.Path), match.Groups["path"].Value));
        var projRef = projects.Single(p => p.Path == path);
        proj.ProjectRefs.Add(projRef);
    }
}

foreach (var proj in projects)
{
    await RemoveUnnecessary(proj.ProjectRefs, IsProjectRequired, proj.Path, "ProjectReference", x => x.Name);
    await RemoveUnnecessary(proj.PackageRefs, IsPackageRequired, proj.Path, "PackageReference", x => x.Identity.Id);
}

    
Console.WriteLine("Hello, World!");

async Task<IPackageSearchMetadata> FindPackage(string name, string version)
{
    var pkg = packages.SingleOrDefault(p => p.Identity.Id == name);
    if (pkg != null)
        return pkg;

    PackageIdentity identity = new(name, NuGetVersion.Parse(version));
    foreach (var repo in repositories)
    {
        var package = await repo.GetMetadataAsync(identity, cache, new NullLogger(), CancellationToken.None);
        if (package != null)
        {
            packages.Add(package);
            return package;
        }
    }

    throw new KeyNotFoundException(name);
}

static async Task RemoveUnnecessary<T>(IList<T> list, Func<IList<T>, T, bool> isRequired, string csproj, string element, Func<T, string> name)
{
    for (var i = 0; i < list.Count;)
    {
        if (isRequired(list, list[i]))
            i++;
        else
        {
            var refName = name(list[i]);
            await RemoveLine(csproj, element, refName);
            Console.WriteLine($"Removing {refName} from {Path.GetFileName(csproj)}");
            list.RemoveAt(i);
        }
    }
}

static async Task RemoveLine(string csproj, string element, string name)
{
    var lines = (await File.ReadAllLinesAsync(csproj)).ToList();
    for (var i = 0; i < lines.Count;)
    {
        var match = Regex.Match(lines[i], @$"<{element}\s+Include=""{name}"".*/>", RegexOptions.IgnoreCase);
        if (match.Success)
            lines.RemoveAt(i);
        else
            i++;
    }

    await File.WriteAllLinesAsync(csproj, lines);
}

bool IsProjectRequired(IEnumerable<Project> list, Project project)
{
    return list.Except(new[] { project }).SelectMany(p => projects.Single(c => c == p).ProjectRefs).All(c => c != project);
}

bool IsPackageRequired(IEnumerable<IPackageSearchMetadata> list, IPackageSearchMetadata package)
{
    return list.Except(new[] { package }).SelectMany(p => GetPackageDependencies(packages.Single(c => c == p))).All(dep => dep != package.Identity.Id);
}

static IEnumerable<string> GetPackageDependencies(IPackageSearchMetadata package)
{
    throw new NotImplementedException();
}