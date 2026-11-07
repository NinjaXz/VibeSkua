using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;

namespace Skua.Core.Options;

public class OptionContainer : ObservableObject, IOptionContainer
{
    private readonly IDialogService _dialogService;

    public List<IOption> Options { get; } = new List<IOption>();

    public Dictionary<string, List<IOption>> MultipleOptions { get; } = new();

    public Dictionary<IOption, string> OptionValues { get; } = new();
    public virtual string OptionsFile { get; set; } = string.Empty;

    private readonly List<string> _resetNotices = new();
    public IReadOnlyList<string> ResetNotices => _resetNotices;

    public OptionContainer(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public void SetDefaults()
    {
        Options.ForEach(o => OptionValues[o] = o.DefaultValue?.ToString() ?? string.Empty);
        foreach (IOption option in MultipleOptions.SelectMany(kvp => kvp.Value))
            OptionValues[option] = option.DefaultValue?.ToString() ?? string.Empty;
    }

    public T? Get<T>(string name) where T : IConvertible
    {
        return Get<T>(Options.Find(o => o.Name == name));
    }

    public T? Get<T>(string category, string name) where T : IConvertible
    {
        return category == "Options" ? Get<T>(name) : Get<T>(MultipleOptions[category].Find(o => o.Name == name));
    }

    public T? Get<T>(IOption? option) where T : IConvertible
    {
        return option is null
            ? default
            : OptionValues.TryGetValue(option, out string? value)
            ? string.IsNullOrEmpty(value)
                ? default
                : typeof(T).IsEnum
                ? (T)Enum.Parse(typeof(T), value.Replace(' ', '_'))
                : (T)Convert.ChangeType(value, typeof(T))
            : default;
    }

    public string GetDirect(IOption? option)
    {
        if (option is null)
            return string.Empty;
        if (OptionValues.TryGetValue(option, out string? value) && value is not null)
            return value;
        return option.DefaultValue?.ToString() ?? string.Empty;
    }

    public void Set(string name, object value)
    {
        Set(Options.Find(o => o.Name == name), value);
    }

    public void Set(string category, string name, object value)
    {
        Set(MultipleOptions[category].Find(o => o.Name == name), value);
    }

    public void Set<T>(IOption? option, T value)
    {
        if (option is null)
            return;
        OptionValues[option] = value?.ToString() ?? string.Empty;
        if (!option.Transient)
            Save();
    }

    public void Configure()
    {
        SetDefaults();
        Load();
        _dialogService.ShowDialog(this, SaveOptions);
    }

    private void SaveOptions(IOptionContainerViewModel vm)
    {
        foreach (IOptionContainerItemViewModel optionViewModel in vm.Options)
        {
            if (optionViewModel.Type.IsEnum)
            {
                string enumStr = optionViewModel.SelectedValue?.Replace(' ', '_') ?? string.Empty;
                Set(optionViewModel.Option, enumStr);
            }
            else
            {
                Set(optionViewModel.Option, optionViewModel.Value);
            }
        }
        Save();
    }

    public void Load()
    {
        _resetNotices.Clear();
        if (string.IsNullOrWhiteSpace(OptionsFile) || !File.Exists(OptionsFile))
        {
            return;
        }

        try
        {
            foreach (string line in File.ReadLines(OptionsFile))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Trim().Split(new char[] { ':', '=' }, 3);
                switch (parts.Length)
                {
                    case 3:
                        {
                            IOption? option = parts[0] switch
                            {
                                "Options" => Options.Find(o => o.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase)),
                                _ => MultipleOptions.TryGetValue(parts[0], out var list) ? list.Find(o => o.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase)) : null,
                            };
                            if (option is not null)
                                OptionValues[option] = parts[2];
                            break;
                        }
                    case 2:
                        {
                            IOption? option = Options.Find(o => o.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                            if (option is not null)
                                OptionValues[option] = parts[1];
                            break;
                        }
                }
            }
            ResetRemovedEnumChoices();
            OnPropertyChanged(nameof(MultipleOptions));
            OnPropertyChanged(nameof(OptionValues));
            OnPropertyChanged(nameof(Options));
        }
        catch { }
    }

    // A saved enum choice the script no longer declares (removed or renamed) would make Get<T> throw.
    // Put that option back to its default in memory only; the file changes when the user saves.
    private void ResetRemovedEnumChoices()
    {
        foreach (IOption option in Options.Concat(MultipleOptions.Values.SelectMany(l => l)))
        {
            if (!option.Type.IsEnum
                || !OptionValues.TryGetValue(option, out string? saved)
                || string.IsNullOrEmpty(saved)
                || Enum.GetNames(option.Type).Contains(saved.Replace(' ', '_')))
                continue;

            string defaultValue = option.DefaultValue?.ToString() ?? string.Empty;
            OptionValues[option] = defaultValue;
            string name = string.IsNullOrWhiteSpace(option.DisplayName) ? option.Name : option.DisplayName;
            _resetNotices.Add($"{name}: \"{saved}\" is no longer a choice, reset to the default \"{defaultValue}\".");
        }
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(OptionsFile))
            return;

        try
        {
            string? dir = Path.GetDirectoryName(OptionsFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            List<string> linesToSave = new();
            linesToSave.AddRange(Options.Where(o => !o.Transient).Select(o => $"Options:{o.Name}={OptionValues.GetValueOrDefault(o, string.Empty)}"));
            foreach (KeyValuePair<string, List<IOption>> item in MultipleOptions)
                linesToSave.AddRange(item.Value.Where(o => !o.Transient).Select(o => $"{item.Key}:{o.Name}={OptionValues.GetValueOrDefault(o, string.Empty)}"));
            File.WriteAllLines(OptionsFile, linesToSave);
        }
        catch { }
    }
}