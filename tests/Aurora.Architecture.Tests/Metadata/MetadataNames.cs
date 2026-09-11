using System.Reflection.Metadata;

namespace Aurora.Architecture.Tests.Metadata;

/// <summary>
/// Full names for the two ways a type appears in a metadata table: defined here
/// (<see cref="TypeDefinitionHandle"/>) or referenced from elsewhere (<see cref="TypeReferenceHandle"/>).
/// </summary>
/// <remarks>
/// Names keep the arity suffix metadata itself carries - <c>ITenantDbContextFactory`1</c>, not
/// <c>ITenantDbContextFactory</c> - and nested types are joined with <c>+</c>, exactly as
/// <see cref="System.Type.FullName"/> renders them. Rules therefore compare against the same
/// spelling a developer sees in a stack trace.
/// </remarks>
internal static class MetadataNames
{
    public static string Of(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        string name = reader.GetString(definition.Name);

        if (definition.IsNested)
        {
            return Of(reader, definition.GetDeclaringType()) + "+" + name;
        }

        string @namespace = reader.GetString(definition.Namespace);
        return @namespace.Length == 0 ? name : @namespace + "." + name;
    }

    public static string Of(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference reference = reader.GetTypeReference(handle);
        string name = reader.GetString(reference.Name);

        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return Of(reader, (TypeReferenceHandle)reference.ResolutionScope) + "+" + name;
        }

        string @namespace = reader.GetString(reference.Namespace);
        return @namespace.Length == 0 ? name : @namespace + "." + name;
    }

    /// <summary>The assembly a type reference resolves to, or <c>null</c> when it is not an assembly.</summary>
    public static string? AssemblyOf(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference reference = reader.GetTypeReference(handle);
        return reference.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference =>
                reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name),
            HandleKind.TypeReference => AssemblyOf(reader, (TypeReferenceHandle)reference.ResolutionScope),
            _ => null,
        };
    }
}
