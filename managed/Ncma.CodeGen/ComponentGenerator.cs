using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ncma.CodeGen;

[Generator(LanguageNames.CSharp)]
public sealed class ComponentGenerator : IIncrementalGenerator
{
    private sealed record Declaration(INamedTypeSymbol Type, AttributeData Attribute, Location Location);
    private static readonly DiagnosticDescriptor Invalid = new("NCMA001", "Invalid generated component", "{0}", "Ncma.CodeGen", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor Duplicate = new("NCMA002", "Duplicate persistent component ID", "Duplicate component TypeId '{0}' in this compilation", "Ncma.CodeGen", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor Shape = new("NCMA003", "Unsupported generated component shape", "{0}", "Ncma.CodeGen", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor Members = new("NCMA004", "Invalid generated component members", "{0}", "Ncma.CodeGen", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor Schema = new("NCMA005", "Invalid component schema", "{0}", "Ncma.CodeGen", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor Fields = new("NCMA006", "Invalid component field metadata", "{0}", "Ncma.CodeGen", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ncma.Runtime.GenerateComponentAttribute", static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => new Declaration((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0], ctx.TargetNode.GetLocation()));
        context.RegisterSourceOutput(declarations.Collect().Combine(context.CompilationProvider),
            static (ctx, input) => Emit(ctx, input.Left, input.Right));
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Declaration> declarations, Compilation compilation)
    {
        // Global ID diagnostics require this cold collection; no runtime parallel catalog.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var d in declarations) {
            if (d.Attribute.ConstructorArguments.Length != 5 || d.Attribute.ConstructorArguments[0].Value is not string id) continue;
            ids[id] = (ids.TryGetValue(id, out int count) ? count : 0) + 1;
        }
        var referencedIds = ComponentIdentities.ValidateReferences(context, compilation);
        var aggregated = new List<INamedTypeSymbol>();
        bool invalidLocal = false;
        foreach (var d in declarations.OrderBy(d => d.Type.ToDisplayString(), StringComparer.Ordinal)) {
            context.CancellationToken.ThrowIfCancellationRequested();
            void Error(DiagnosticDescriptor descriptor, string message) { invalidLocal = true; context.ReportDiagnostic(Diagnostic.Create(descriptor, d.Location, message)); }
            var args = d.Attribute.ConstructorArguments;
            if (args.Length != 5 || args[0].Value is not string id || string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
                args[1].Value is not int version || version < 1 || args[2].Value is not string schema ||
                args[3].Value is not string validator || args[4].Value is not string defaults) {
                Error(Invalid, "Provide an explicit bounded TypeId, positive schema version, schema, validator and default property."); continue;
            }
            if (ids[id] > 1) { Error(Duplicate, id); continue; }
            if (referencedIds.TryGetValue(id, out var owner)) {
                invalidLocal = true;
                context.ReportDiagnostic(Diagnostic.Create(ComponentIdentities.Conflict, d.Location, id,
                    compilation.AssemblyName + ":" + d.Type.ToDisplayString(), owner));
                continue;
            }
            var type = d.Type;
            if (type.TypeKind != TypeKind.Struct || !type.IsReadOnly || !type.IsUnmanagedType || type.IsRefLikeType ||
                type.IsGenericType || type.ContainingType is not null || type.IsFileLocal ||
                !type.AllInterfaces.Any(i => i.ToDisplayString() == "Ncma.Runtime.IComponent") ||
                !type.DeclaringSyntaxReferences.All(r => r.GetSyntax() is TypeDeclarationSyntax syntax && syntax.Modifiers.Any(SyntaxKind.PartialKeyword)) ||
                !SafeValue(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), 0)) {
                Error(Shape, "B1 requires a top-level readonly partial unmanaged IComponent with no Entity, address or pointer fields (including nested fields)."); continue;
            }
            if (type.GetMembers("Generated").Length != 0 ||
                !type.GetMembers(validator).OfType<IMethodSymbol>().Any(m => m.IsStatic && !m.IsGenericMethod && m.Parameters.Length == 1 &&
                    m.Parameters[0].RefKind == RefKind.None && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, type) && SymbolEqualityComparer.Default.Equals(m.ReturnType, type)) ||
                !type.GetMembers(defaults).OfType<IPropertySymbol>().Any(p => p.IsStatic && p.GetMethod is not null && !p.IsIndexer && SymbolEqualityComparer.Default.Equals(p.Type, type))) {
                Error(Members, "Reserve member name Generated; validator must be static T Method(T), default must be a static readable T property."); continue;
            }
            try {
                if (Encoding.UTF8.GetByteCount(schema) > 65536) throw new ArgumentException("Schema exceeds 64KiB.");
                using var json = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 32 });
                if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("type", out var kind) || kind.GetString() != "object")
                    throw new ArgumentException("Schema root must declare type object.");
            } catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { Error(Schema, e.Message); continue; }
            bool attachable = !d.Attribute.NamedArguments.Any(a => a.Key == "RuntimeAttachable" && a.Value.Value is false);
            ComponentFields.Field[] fields;
            try { fields = ComponentFields.Read(type, schema); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException) { Error(Fields, e.Message); continue; }
            string full = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string ns = type.ContainingNamespace.IsGlobalNamespace ? "" : "namespace " + type.ContainingNamespace.ToDisplayString() + ";\n";
            string access = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
            string kindName = type.IsRecord ? "record struct" : "struct";
            string literal(string value) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true);
            string fieldSource = string.Join(",\n", fields.Select(f => $"new global::Ncma.Runtime.ComponentFieldDefinition({literal(f.Id)}, {literal(f.MemberPath)}, {literal(f.JsonPath)}, (global::Ncma.Runtime.ComponentFieldFlags){f.Flags})"));
            string source = $$"""
                // <auto-generated/>
                #nullable enable
                [assembly: global::Ncma.Runtime.GeneratedComponentIdentityAttribute({{literal(id)}}, {{version}}, typeof({{full}}))]
                {{ns}}{{access}} readonly partial {{kindName}} @{{type.Name}}
                {
                    public static class Generated
                    {
                        public const string TypeId = {{literal(id)}};
                        public const int SchemaVersion = {{version}};
                        public static {{full}} Default => {{full}}.@{{validator}}({{full}}.@{{defaults}});
                        // Fresh cold definitions; registry freezes a private validated copy.
                        public static global::Ncma.Runtime.ComponentFieldDefinition[] DescribeFields() => [{{fieldSource}}];
                        public static void Register(global::Ncma.Runtime.ComponentRegistry registry) =>
                            registry.Register<{{full}}>(TypeId, SchemaVersion, {{literal(schema)}}, {{full}}.@{{validator}}, {{(attachable ? "true" : "false")}}, DescribeFields(), static () => {{full}}.@{{defaults}});
                        public static {{full}} Read(global::Ncma.Runtime.World world, global::Ncma.Runtime.Entity entity) => world.Get<{{full}}>(entity);
                        public static void Write(global::Ncma.Runtime.World world, global::Ncma.Runtime.Entity entity, {{full}} value) => world.Set(entity, value);
                        // Query construction is cold; callers must retain the original guarded plan.
                        public static global::Ncma.Runtime.EntityQuery Query(global::Ncma.Runtime.World world) => world.Query<{{full}}>();
                        public static global::Ncma.Runtime.ScheduleAccess ReadAccess(global::Ncma.Runtime.ComponentRegistry registry)
                        {
                            var access = global::Ncma.Runtime.ScheduleAccess.Read<{{full}}>(registry);
                            if (access.Id != TypeId || access.Version != SchemaVersion) throw new global::System.InvalidOperationException("Generated component identity does not match registry.");
                            return access;
                        }
                        public static global::Ncma.Runtime.ScheduleAccess WriteAccess(global::Ncma.Runtime.ComponentRegistry registry) => ReadAccess(registry) with { Mode = global::Ncma.Runtime.ScheduleAccessMode.Write };
                        public static global::Ncma.Runtime.ScheduleComponent Channel() => global::Ncma.Runtime.ScheduleComponent.Of<{{full}}>();
                        public static {{full}} ReadScheduled(global::Ncma.Runtime.ScheduleContext context, int row) => context.Read<{{full}}>(row);
                        public static void WriteScheduled(global::Ncma.Runtime.ScheduleContext context, int row, {{full}} value) => context.Write(row, value);
                    }
                }
                """;
            // Fixed-size identity avoids MAX_PATH failures for long fully-qualified names.
            string hint = PortableRuntime.SourceHash(full) + ".Component.g.cs";
            context.AddSource(hint, Microsoft.CodeAnalysis.Text.SourceText.From(source, Encoding.UTF8));
            aggregated.Add(type);
        }
        if (!invalidLocal) ComponentAggregation.Emit(context, compilation, aggregated);
    }

    private static bool SafeValue(ITypeSymbol type, HashSet<ITypeSymbol> visited, int depth)
    {
        if (depth > 32 || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer || type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr ||
            type.ToDisplayString() == "Ncma.Runtime.Entity") return false;
        if (type.SpecialType != SpecialType.None || type.TypeKind == TypeKind.Enum) return type.IsUnmanagedType;
        if (!visited.Add(type)) return true;
        return type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic).All(f => SafeValue(f.Type, visited, depth + 1));
    }
}
