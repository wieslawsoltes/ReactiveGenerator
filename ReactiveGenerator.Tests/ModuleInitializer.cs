using System.Runtime.CompilerServices;
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
