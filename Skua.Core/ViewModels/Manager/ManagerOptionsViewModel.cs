using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Utils;

namespace Skua.Core.ViewModels.Manager;

public class ManagerOptionsViewModel : ObservableObject
{
    public ManagerOptionsViewModel(List<DisplayOptionItemViewModelBase> options, ISettingsService settingsService, IFileDialogService fileService)
    {
        ManagerOptions = options;
        _settingsService = settingsService;
        _fileService = fileService;

        string initialDirectory = _settingsService.Get("ClientDownloadPath", string.Empty);
        if (string.IsNullOrEmpty(initialDirectory))
        {
            initialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(initialDirectory))
                _settingsService.Set("ClientDownloadPath", initialDirectory);
            else
                _settingsService.Set("ClientDownloadPath", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));
        }

        _downloadPath = _settingsService.Get("ClientDownloadPath", string.Empty);
        ChangeDownloadPathCommand = new RelayCommand(ChangeDownloadPath);
    }

    public List<DisplayOptionItemViewModelBase> ManagerOptions { get; }

    private string _downloadPath;
    private readonly ISettingsService _settingsService;
    private readonly IFileDialogService _fileService;

    public string DownloadPath
    {
        get => _downloadPath;
        set
        {
            if (SetProperty(ref _downloadPath, value))
                _settingsService.Set("ClientDownloadPath", value);
        }
    }

    public IRelayCommand ChangeDownloadPathCommand { get; }

    private void ChangeDownloadPath()
    {
        string initialDirectory = _settingsService.Get("ClientDownloadPath", string.Empty);
        if (string.IsNullOrEmpty(initialDirectory))
        {
            initialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(initialDirectory))
                _settingsService.Set("ClientDownloadPath", initialDirectory);
            else
                _settingsService.Set("ClientDownloadPath", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));
        }

        string? folderPath = _fileService.OpenFolder(initialDirectory);
        if (!string.IsNullOrEmpty(folderPath))
        {
            _settingsService.Set("ClientDownloadPath", folderPath);
            DownloadPath = folderPath;
        }
    }
}