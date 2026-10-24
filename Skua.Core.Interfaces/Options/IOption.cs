namespace Skua.Core.Interfaces;

public interface IOption
{
    string Category { get; set; }
    string Name { get; }
    string DisplayName { get; }
    string Description { get; }
    object? DefaultValue { get; }
    bool Transient { get; }
    Type Type { get; }

    /// <summary>
    /// For enum options: show a dropdown that filters as you type instead of a plain one.
    /// Off unless an option turns it on.
    /// </summary>
    bool Searchable => false;
}