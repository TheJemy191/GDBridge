using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GDParser;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SourceGeneratorUtils;

namespace GDBridge.Generator;

[Generator]
public class GDBridgeIncrementalSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var scriptSources = context.AdditionalTextsProvider
            .Where(t => t.Path.EndsWith(".gd"))
            .Select((t, ct) => t.GetText(ct)?.ToString()!)
            .Where(t => t is not null)
            .Collect();

        context.RegisterSourceOutput(scriptSources.Combine(context.CompilationProvider), (c, p) => Generate(c, p.Right, p.Left));
    }

    static void Generate(SourceProductionContext context, Compilation compilation, ImmutableArray<string> scripts)
    {
        var godotAssembly = compilation.SourceModule.ReferencedAssemblySymbols
            .FirstOrDefault(e => e.Name == "GodotSharp");
        
        var availableGodotTypes =
            godotAssembly?.GlobalNamespace
            .GetNamespaceMembers().First(n => n.Name == "Godot")
            .GetMembers()
            .Where(m => m.IsType)
            .Select(m => m.Name) ?? new List<string>();

        var availableTypes = compilation.Assembly.TypeNames
            .Concat(availableGodotTypes)
            .ToList();
        
        foreach (var script in scripts)
        {
            var gdClass = ClassParser.Parse(script);
            if (gdClass?.ClassName is null)
                continue;

            var className = $"{gdClass.ClassName}Bridge";
            var source = GenerateClass(gdClass, className, availableTypes);
            
            context.AddSource(className, source);
        }
    }
    static string GenerateClass(GdClass gdClass, string className, ICollection<string> availableTypes)
    {
        var source = new SourceWriter();
        var bridgeWriter = new BridgeWriter(availableTypes, source);
        
        source.WriteLine(
                $"""
                  using Godot;
                  using GDBridge;

                  [GlobalClass]
                  public partial class {className} : GDScriptBridge
                  """)
            .OpenBlock()
            .WriteLine($"""public const string ClassName = "{className}";"""); 
        bridgeWriter.Variables(gdClass.Variables)
            .WriteLine($"public {className}(GodotObject gdObject) : base(gdObject) {{}}");
        bridgeWriter.Functions(gdClass.Functions)
            .CloseBlock();

        return source.ToString();
    }

}