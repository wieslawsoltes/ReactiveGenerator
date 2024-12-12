using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using VerifyTests;
using DiffEngine;

namespace ReactiveGenerator.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifySourceGenerators.Initialize();
        
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) => new(
            directory: Path.Combine(projectDirectory, "Snapshots"),
            typeName: type.Name,
            methodName: method.Name));

        DiffTools.UseOrder(DiffTool.VisualStudio);
    }
}

// Add this class to help with verification of the generated source
public static class TestHelpers
{
    public static Task Verify(GeneratorDriver driver)
    {
        var runResults = driver.GetRunResult();

        // Get the generated sources from the compilation
        var generatedSources = runResults.GeneratedTrees
            .Select(tree => new
            {
                FileName = Path.GetFileName(tree.FilePath),
                Source = tree.ToString()
            })
            .OrderBy(f => f.FileName)
            .ToList();

        if (!generatedSources.Any())
        {
            throw new Exception("No source was generated!");
        }

        // Return results in a verifiable format
        return Verifier.Verify(new
        {
            Sources = generatedSources,
            Diagnostics = runResults.Diagnostics
        });
    }
}
