using Skua.Core.Interfaces;

namespace Skua.Core.Options;

public class Option<T> : IOption where T : IConvertible
{
    public string Category { get; set; } = "Options";
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public object? DefaultValue { get; set; }
    public bool Transient { get; set; }

    /// <summary>For enum options: a dropdown that filters as you type, e.g. <c>new Option&lt;MyEnum&gt;(...) { Searchable = true }</c>.</summary>
    public bool Searchable { get; set; }

    public Type Type => typeof(T);

    /// <summary>
    /// Constructs an option definition with the given attributes.
    /// </summary>
    public Option(string name, string displayName, string description, T? defaultValue = default, bool transient = false)
    {
        Name = name;
        DisplayName = displayName;
        Description = description;
        DefaultValue = defaultValue;
        Transient = transient;
    }

    public override string ToString()
    {
        return (DisplayName == string.Empty ? Name : DisplayName) ?? Name;
    }
}