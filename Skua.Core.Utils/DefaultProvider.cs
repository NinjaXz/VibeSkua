namespace Skua.Core.Utils;

public class DefaultProvider
{
    public static object? GetDefault(Type type)
    {
        if (type == null)
            return null;
        if (type == typeof(string))
            return string.Empty;
        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType() ?? typeof(object), 0);
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return null;
    }

    public static object? GetDefault<T>(Type type)
    {
        return type == null
            ? default(T)
            : type.IsArray
            ? Array.Empty<T>()
            : type == typeof(string) ? string.Empty : typeof(IEnumerable<>).IsAssignableFrom(type) ? Enumerable.Empty<T>() : default(T);
    }
}