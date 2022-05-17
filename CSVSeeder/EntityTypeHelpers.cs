using System.Reflection;

namespace CSVSeeder;
public static class EntityTypeHelpers
{
    public static List<PropertyInfo> FilterDbProperties(this IEnumerable<PropertyInfo> declaredProperties)
    {
        return declaredProperties.Where(x => x.GetGetMethod()?.IsVirtual == false && (x.PropertyType.Equals(typeof(string)) || x.PropertyType.IsClass == false) &&
                !x.PropertyType.IsGenericType && !x.PropertyType.GetInterfaces().Contains(typeof(IEnumerable<>))).ToList();
    }
}
