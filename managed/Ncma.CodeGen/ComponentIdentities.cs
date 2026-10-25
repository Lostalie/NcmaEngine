using Microsoft.CodeAnalysis;

namespace Ncma.CodeGen;

// Cold compile-time identity index only. No component assembly loading or runtime cache.
internal static class ComponentIdentities
{
    internal static readonly DiagnosticDescriptor Conflict = new("NCMA007", "Conflicting referenced component ID",
        "Component TypeId '{0}' conflicts between '{1}' and '{2}'", "Ncma.CodeGen", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor Invalid = new("NCMA008", "Invalid component identity manifest",
        "Invalid generated component manifest in '{0}': {1}", "Ncma.CodeGen", DiagnosticSeverity.Error, true);

    internal static Dictionary<string, string> ValidateReferences(SourceProductionContext context, Compilation compilation)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IAssemblySymbol>(compilation.SourceModule.ReferencedAssemblySymbols);
        while (pending.Count != 0) {
            var assembly = pending.Pop();
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(assembly)) continue;
            foreach (var module in assembly.Modules)
                foreach (var reference in module.ReferencedAssemblySymbols) pending.Push(reference);
        }
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assembly in visited.OrderBy(a => a.Identity.ToString(), StringComparer.Ordinal)) {
            var types = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var attribute in assembly.GetAttributes().Where(a =>
                a.AttributeClass?.ToDisplayString() == "Ncma.Runtime.GeneratedComponentIdentityAttribute")) {
                context.CancellationToken.ThrowIfCancellationRequested();
                var args = attribute.ConstructorArguments;
                if (args.Length != 3 || args[0].Value is not string id || string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
                    args[1].Value is not int version || version < 1 || args[2].Value is not INamedTypeSymbol type ||
                    !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, assembly) || !types.Add(type)) {
                    Reject("Expected one bounded ID/version and an owned component type per manifest entry."); continue;
                }
                var declarations = type.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == "Ncma.Runtime.GenerateComponentAttribute").ToArray();
                var declaration = declarations.Length == 1 ? declarations[0] : null;
                if (declaration is null || declaration.ConstructorArguments.Length != 5 ||
                    declaration.ConstructorArguments[0].Value is not string declaredId || declaredId != id ||
                    declaration.ConstructorArguments[1].Value is not int declaredVersion || declaredVersion != version) {
                    Reject("Manifest identity does not match the explicit component declaration: " + type.ToDisplayString()); continue;
                }
                string owner = assembly.Identity + ":" + type.ToDisplayString();
                if (ids.ContainsKey(id))
                    context.ReportDiagnostic(Diagnostic.Create(Conflict, Location.None, id, ids[id], owner));
                else ids.Add(id, owner);
            }
            void Reject(string reason) => context.ReportDiagnostic(Diagnostic.Create(Invalid, Location.None, assembly.Identity.ToString(), reason));
        }
        return ids;
    }
}
