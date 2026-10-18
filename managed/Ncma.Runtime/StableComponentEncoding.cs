using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Ncma.Runtime;

// Deliberately conservative: arbitrary converters, JSON attributes and user getters use the
// bounded cold path. This is not a user-provided "trust my cache" flag or an authority grant.
internal static class StableComponentEncoding
{
    internal static bool IsStable(Type type) => Check(type, []);
    private static bool Check(Type type, HashSet<Type> visiting)
    {
        if (type.GetCustomAttributes<JsonAttribute>(inherit: true).Any()) return false;
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(Guid)) return true;
        if (!type.IsValueType || type.IsByRefLike || type.IsPointer) return false;
        if (!visiting.Add(type)) return true;
        // JsonInclude may opt non-public fields/properties into the serializer too.
        foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (member is FieldInfo or PropertyInfo && member.GetCustomAttributes<JsonAttribute>(inherit: true).Any()) return false;
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            if (field.GetCustomAttributes<JsonAttribute>(inherit: true).Any() || !Check(field.FieldType, visiting)) return false;
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            // System.Text.Json ignores indexers. IgnoreReadOnlyProperties is fixed in SceneJson.Options.
            if (property.GetIndexParameters().Length != 0) continue;
            if (property.GetCustomAttributes<JsonAttribute>(inherit: true).Any()) return false;
            if (property.GetMethod is null || !property.GetMethod.IsPublic || property.SetMethod is null) continue;
            if (!property.GetMethod.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
                !ReadsOneField(property.GetMethod, type, property.PropertyType) ||
                !Check(property.PropertyType, visiting)) return false;
        }
        return true;
    }
    private static bool ReadsOneField(MethodInfo getter, Type declaringType, Type propertyType)
    {
        // An attribute alone is not proof: extensions can manually apply CompilerGenerated.
        // Only ldarg.0; ldfld <own instance field>; ret is admitted. Unknown IL is cold.
        byte[]? il = getter.GetMethodBody()?.GetILAsByteArray();
        if (il is not { Length: 7 } || il[0] != 0x02 || il[1] != 0x7b || il[6] != 0x2a) return false;
        var field = getter.Module.ResolveField(BitConverter.ToInt32(il, 2), declaringType.GetGenericArguments(), null);
        return field is not null && !field.IsStatic && field.DeclaringType == declaringType && field.FieldType == propertyType;
    }
}
