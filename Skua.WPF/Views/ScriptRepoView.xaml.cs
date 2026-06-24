using Skua.Core.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Skua.WPF.Views;

/// <summary>
/// Interaction logic for ScriptRepoView.xaml
/// </summary>
public partial class ScriptRepoView : UserControl
{
    private ICollectionView? _collectionView;
    private System.Threading.Timer? _debounceTimer;

    public ScriptRepoView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        
        // Ensure we clean up the timer when the control is unloaded to prevent memory leaks
        this.Unloaded += ScriptRepoView_Unloaded;
    }

    private readonly object _syncLock = new();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ScriptRepoViewModel vm)
        {
            BindingOperations.EnableCollectionSynchronization(vm.Scripts, _syncLock);
            _collectionView = CollectionViewSource.GetDefaultView(vm.Scripts);
        }
    }

    private void ScriptRepoView_Unloaded(object sender, RoutedEventArgs e)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    private bool Search(object obj)
    {
        string searchScript = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(searchScript))
            return true;

        if (obj is not ScriptInfoViewModel script)
            return false;

        // Using native string.Contains with OrdinalIgnoreCase is SIMD accelerated
        // and avoids KMP algorithm memory allocations and multiple .ToLower() calls.
        if (script.Info.Name?.Contains(searchScript, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (script.Info.FileName?.Contains(searchScript, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (script.Info.FilePath?.Contains(searchScript, StringComparison.OrdinalIgnoreCase) == true)
            return true;
        
        if (script.Info.Description?.Contains(searchScript, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return script.InfoTags.Any(tag => tag.Contains(searchScript, StringComparison.OrdinalIgnoreCase));
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_collectionView is null)
            return;

        _debounceTimer?.Change(System.Threading.Timeout.Infinite, 0);
        _debounceTimer = new System.Threading.Timer((state) =>
        {
            Dispatcher.Invoke(() =>
            {
                _collectionView.Filter = Search;
                _collectionView.Refresh();
            });
        }, null, 250, System.Threading.Timeout.Infinite);
    }
}