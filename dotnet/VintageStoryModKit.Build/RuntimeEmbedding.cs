using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace VintageStoryModKit.Build;

internal sealed record StagedFile(string Path, string Destination, string PackageId);

internal sealed record EmbeddingPlan(
    StagedFile? Target,
    IReadOnlyList<StagedFile> Inputs,
    IReadOnlyList<StagedFile> Excluded
);

internal static class RuntimeEmbedding
{
    public static EmbeddingPlan Plan(
        IReadOnlyList<StagedFile> files,
        IReadOnlySet<string> embeddedPackages,
        IReadOnlySet<string> omittedPackages
    )
    {
        var runtime = files.Where(file => embeddedPackages.Contains(file.PackageId)).ToList();
        var omitted = files.Where(file => omittedPackages.Contains(file.PackageId)).ToList();
        var others = files.Except(runtime).Except(omitted).ToList();
        var references = others.ToDictionary(file => file, file => ReferencedAssemblies(file.Path));

        // Satellite resource assemblies stay behind, and the merged neutral resources serve every culture.
        Dictionary<string, StagedFile> mergeable = new(StringComparer.OrdinalIgnoreCase);
        foreach (StagedFile file in runtime.Where(file => !file.Destination.Contains('/')))
        {
            if (AssemblyName(file.Path) is string name)
            {
                mergeable[name] = file;
            }
        }

        // Package libraries built on the runtime's dependencies would lose them to the merge, so they merge too.
        List<StagedFile> dependents = [];
        List<StagedFile> found;
        do
        {
            found = others
                .Where(file =>
                    file.PackageId.Length > 0
                    && !file.Destination.Contains('/')
                    && !dependents.Contains(file)
                    && references[file].Any(mergeable.ContainsKey)
                )
                .ToList();
            foreach (StagedFile file in found)
            {
                dependents.Add(file);
                mergeable[AssemblyName(file.Path)!] = file;
            }
        } while (found.Count > 0);

        var users = others
            .Where(file =>
                file.PackageId.Length == 0 && references[file].Any(mergeable.ContainsKey)
            )
            .ToList();
        if (users.Count > 1)
        {
            IEnumerable<string> uses = users.Select(file =>
                $"{file.Destination} ({string.Join(", ", references[file].Where(mergeable.ContainsKey))})"
            );
            throw new InvalidOperationException(
                $"VSMK merges its runtime into one assembly per mod, but several assemblies reference it, its dependencies or libraries built on them: {string.Join(", ", uses)}. Keep that code in one assembly."
            );
        }

        var referenced = references
            .Values.SelectMany(names => names)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var neededPackages = omitted
            .Where(file => AssemblyName(file.Path) is string name && referenced.Contains(name))
            .Select(file => file.PackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unneeded = omitted.Where(file => !neededPackages.Contains(file.PackageId)).ToList();

        if (users.Count == 0)
        {
            // Without a mod assembly to merge into, dependent libraries keep the runtime files they load.
            HashSet<StagedFile> kept =
            [
                .. Reachable(dependents.SelectMany(file => references[file]), mergeable),
            ];
            return new EmbeddingPlan(
                null,
                [],
                [.. runtime.Where(file => !kept.Contains(file)), .. unneeded]
            );
        }

        List<StagedFile> inputs = Reachable(references[users[0]], mergeable);
        // Other files of a merged library's package can still be loaded by name, so only its satellites go.
        var satellites = inputs
            .Where(dependents.Contains)
            .Select(file => AssemblyName(file.Path) + ".resources.dll")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new EmbeddingPlan(
            users[0],
            inputs,
            [
                users[0],
                .. runtime,
                .. others.Where(file =>
                    inputs.Contains(file)
                    || (
                        file.Destination.Contains('/')
                        && satellites.Contains(System.IO.Path.GetFileName(file.Destination))
                    )
                ),
                .. unneeded,
            ]
        );
    }

    public static void Merge(
        StagedFile target,
        IReadOnlyList<StagedFile> inputs,
        IEnumerable<string> libraryDirectories,
        string output,
        string log,
        string dotnet,
        string ilRepack
    )
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        ProcessStartInfo start = new(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(ilRepack);
        start.ArgumentList.Add("/internalize");
        start.ArgumentList.Add("/noRepackRes");
        start.ArgumentList.Add("/ndebug");
        start.ArgumentList.Add($"/out:{output}");
        foreach (string directory in libraryDirectories)
        {
            start.ArgumentList.Add(
                $"/lib:{System.IO.Path.TrimEndingDirectorySeparator(directory)}"
            );
        }
        start.ArgumentList.Add(target.Path);
        foreach (StagedFile input in inputs)
        {
            start.ArgumentList.Add(input.Path);
        }

        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string text = process.StandardOutput.ReadToEnd() + error.Result;
        process.WaitForExit();
        File.WriteAllText(log, text);
        if (process.ExitCode != 0)
        {
            // MSBuild shows only the first line of tool output as the error, so it names the cause and the full log.
            string cause =
                text.Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                    .LastOrDefault(line =>
                        !line.StartsWith("at ", StringComparison.Ordinal)
                        && !line.StartsWith("---", StringComparison.Ordinal)
                    )
                ?? $"ILRepack exited with code {process.ExitCode}.";
            throw new InvalidOperationException(
                $"ILRepack could not merge the VSMK runtime into {target.Destination}. {cause} See {log}."
            );
        }
    }

    private static List<StagedFile> Reachable(
        IEnumerable<string> roots,
        Dictionary<string, StagedFile> mergeable
    )
    {
        List<StagedFile> reachable = [];
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> pending = new(roots);
        while (pending.TryDequeue(out string? name))
        {
            if (visited.Add(name) && mergeable.TryGetValue(name, out StagedFile? file))
            {
                reachable.Add(file);
                foreach (string reference in ReferencedAssemblies(file.Path))
                {
                    pending.Enqueue(reference);
                }
            }
        }

        return reachable;
    }

    private static string? AssemblyName(string path)
    {
        using PEReader? reader = OpenAssembly(path);
        if (reader is null)
        {
            return null;
        }

        MetadataReader metadata = reader.GetMetadataReader();
        return metadata.IsAssembly
            ? metadata.GetString(metadata.GetAssemblyDefinition().Name)
            : null;
    }

    private static string[] ReferencedAssemblies(string path)
    {
        using PEReader? reader = OpenAssembly(path);
        if (reader is null)
        {
            return [];
        }

        MetadataReader metadata = reader.GetMetadataReader();
        return metadata
            .AssemblyReferences.Select(handle =>
                metadata.GetString(metadata.GetAssemblyReference(handle).Name)
            )
            .ToArray();
    }

    // Mods can ship native libraries, which have no metadata.
    private static PEReader? OpenAssembly(string path)
    {
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        PEReader reader = new(File.OpenRead(path));
        if (reader.HasMetadata)
        {
            return reader;
        }

        reader.Dispose();
        return null;
    }
}
