using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using VintageStoryModKit.Build;

namespace VintageStoryModKit.Tests;

public sealed class RuntimeEmbeddingTests : IDisposable
{
    private static readonly HashSet<string> Embedded = new(
        ["Runtime", "Dependency"],
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> Omitted = new(
        ["Optional"],
        StringComparer.OrdinalIgnoreCase
    );
    private readonly string directory = Path.Combine(
        Environment.GetEnvironmentVariable("VSMK_TEST_ROOT")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "scratch"),
        "embedding-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void MergesTheRuntimeItReachesIntoTheAssemblyThatUsesIt()
    {
        StagedFile optional = Compile("Optional", "Optional");
        StagedFile dependency = Compile("Dependency", "Dependency", optional);
        StagedFile runtime = Compile("Runtime", "Runtime", dependency);
        StagedFile unused = Compile("Unused", "Runtime");
        StagedFile satellite = runtime with { Destination = "de/Runtime.resources.dll" };
        StagedFile helper = Compile("Helper", "", runtime);
        StagedFile mod = Compile("Mod", "", helper);

        EmbeddingPlan plan = RuntimeEmbedding.Plan(
            [mod, helper, runtime, unused, satellite, dependency, optional],
            Embedded,
            Omitted
        );

        Assert.Equal(helper, plan.Target);
        Assert.Equal([runtime, dependency], plan.Inputs);
        Assert.Equal(
            [dependency, helper, optional, runtime, unused, satellite],
            plan.Excluded.OrderBy(file => file.Destination, StringComparer.Ordinal)
        );
    }

    [Fact]
    public void KeepsAnOmittedPackageThatTheModUsesItself()
    {
        StagedFile optional = Compile("Optional", "Optional");
        StagedFile runtime = Compile("Runtime", "Runtime", optional);
        StagedFile mod = Compile("Mod", "", runtime, optional);

        EmbeddingPlan plan = RuntimeEmbedding.Plan([mod, runtime, optional], Embedded, Omitted);

        Assert.Equal(mod, plan.Target);
        Assert.DoesNotContain(optional, plan.Excluded);
    }

    [Fact]
    public void MergesPackageLibrariesBuiltOnTheRuntimeDependencies()
    {
        StagedFile dependency = Compile("Dependency", "Dependency");
        StagedFile runtime = Compile("Runtime", "Runtime", dependency);
        StagedFile sibling = Compile("Sibling", "Library");
        StagedFile library = Compile("Library", "Library", dependency, sibling);
        StagedFile satellite = library with { Destination = "de/Library.resources.dll" };
        StagedFile mod = Compile("Mod", "", runtime, library);

        EmbeddingPlan plan = RuntimeEmbedding.Plan(
            [mod, runtime, dependency, library, satellite, sibling],
            Embedded,
            Omitted
        );

        Assert.Equal(mod, plan.Target);
        Assert.Equal(
            [dependency, library, runtime],
            plan.Inputs.OrderBy(file => file.Destination, StringComparer.Ordinal)
        );
        Assert.Contains(library, plan.Excluded);
        Assert.Contains(satellite, plan.Excluded);
        Assert.DoesNotContain(sibling, plan.Excluded);
    }

    [Fact]
    public void RejectsRuntimeUseFromSeveralAssemblies()
    {
        StagedFile runtime = Compile("Runtime", "Runtime");
        StagedFile helper = Compile("Helper", "", runtime);
        StagedFile mod = Compile("Mod", "", runtime, helper);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeEmbedding.Plan([mod, helper, runtime], Embedded, Omitted)
        );
        Assert.Contains(
            "Mod.dll (Runtime), Helper.dll (Runtime)",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private StagedFile Compile(string name, string packageId, params StagedFile[] uses)
    {
        string calls = string.Concat(
            uses.Select(file => $" + {Path.GetFileNameWithoutExtension(file.Path)}.Api.Use()")
        );
        IEnumerable<MetadataReference> references = (
            (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
        )
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Concat(uses.Select(file => MetadataReference.CreateFromFile(file.Path)));
        var compilation = CSharpCompilation.Create(
            name,
            [
                CSharpSyntaxTree.ParseText(
                    $"namespace {name}; public static class Api {{ public static int Use() => 0{calls}; }}"
                ),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        string path = Path.Combine(directory, name + ".dll");
        Directory.CreateDirectory(directory);
        EmitResult result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return new StagedFile(path, name + ".dll", packageId);
    }
}
