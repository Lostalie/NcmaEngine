using System.Text.Json;
using Microsoft.CodeAnalysis;
using Ncma.ComponentContracts;

namespace Ncma.CodeGen;

internal static class ComponentFields
{
    internal sealed record Field(string Id, string MemberPath, string JsonPath, int Flags, string Kind);
    internal static Field[] Read(INamedTypeSymbol type, string schema)
    {
        using var document = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 32 });
        return FieldContract.Prepare<ITypeSymbol>(type, new SymbolAdapter(), Declarations(type), document.RootElement)
            .Select(f => new Field(f.Declaration.Id, f.Declaration.MemberPath, f.Declaration.JsonPath, f.Declaration.Flags, f.Kind)).ToArray();
    }

    private static IEnumerable<FieldDeclaration> Declarations(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == "Ncma.Runtime.ComponentFieldAttribute")) {
            var args = attribute.ConstructorArguments;
            if (args.Length != 3 || args[0].Value is not string id || args[1].Value is not string member || args[2].Value is not string path)
                throw new ArgumentException("ComponentField requires explicit FieldId, member path and JSON path.");
            int flags = 5;
            foreach (var arg in attribute.NamedArguments) if (arg.Key == "Flags") flags = arg.Value.Value is int value ? value : throw new ArgumentException("Invalid field flags.");
            yield return new(id, member, path, flags);
        }
    }

    private sealed class SymbolAdapter : IFieldTypeAdapter<ITypeSymbol>
    {
        // Metadata names avoid C# aliases (including nint/nuint) and match reflection.
        private static string TypeName(ITypeSymbol type) => type.ContainingType is not null ?
            TypeName(type.ContainingType) + "+" + type.MetadataName :
            (type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." : "") + type.MetadataName;
        public FieldTypeFacts Describe(ITypeSymbol type) => new(TypeName(type),
            type.TypeKind == TypeKind.Enum, type.IsValueType, type.IsRefLikeType,
            type.AllInterfaces.Any(i => TypeName(i) == "System.Collections.IEnumerable"));
        public IEnumerable<string> AttributeNamespaces(ITypeSymbol type) => Namespaces(type);
        private static IEnumerable<string> Namespaces(ISymbol member) => member.GetAttributes()
            .Select(a => a.AttributeClass?.ContainingNamespace.ToDisplayString() ?? "");
        public IEnumerable<FieldMemberFacts<ITypeSymbol>> Members(ITypeSymbol type)
        {
            foreach (var member in type.GetMembers()) {
                if (member is IFieldSymbol f) yield return new(f.Name, f.Type) {
                    IsField = true, IsStatic = f.IsStatic, IsPublic = f.DeclaredAccessibility == Accessibility.Public,
                    IsReadOnly = f.IsReadOnly, IsConst = f.IsConst,
                    AttributeNamespaces = Namespaces(f)
                };
                else if (member is IPropertySymbol p) yield return new(p.Name, p.Type) {
                    IsStatic = p.IsStatic, IsPublic = p.DeclaredAccessibility == Accessibility.Public,
                    GetterPublic = p.GetMethod?.DeclaredAccessibility == Accessibility.Public,
                    SetterPublic = p.SetMethod?.DeclaredAccessibility == Accessibility.Public,
                    HasSetter = p.SetMethod is not null, IsIndexer = p.IsIndexer, AttributeNamespaces = Namespaces(p)
                };
            }
        }
    }
}
