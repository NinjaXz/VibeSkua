using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.ViewModels;
using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;

namespace Skua.WPF.Views;

/// <summary>
/// Interaction logic for PacketInterceptorView.xaml
/// </summary>
public partial class PacketInterceptorView : UserControl
{
    private readonly PacketInterceptorViewModel _vm;
    private readonly ICollectionView _collectionView;
    private readonly ICollectionView _filtersCollectionView;

    public PacketInterceptorView()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<PacketInterceptorViewModel>();
        _vm = (PacketInterceptorViewModel)DataContext;
        _collectionView = CollectionViewSource.GetDefaultView(_vm.Packets);
        _collectionView.Filter = Search;
        _filtersCollectionView = CollectionViewSource.GetDefaultView(_vm.PacketFilters);
        _filtersCollectionView.Filter = SearchFilters;

        foreach (PacketLogFilterViewModel filter in _vm.PacketFilters)
            filter.PropertyChanged += Filter_PropertyChanged;

        this.Unloaded += PacketInterceptorView_Unloaded;
    }

    private void PacketInterceptorView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        foreach (PacketLogFilterViewModel filter in _vm.PacketFilters)
            filter.PropertyChanged -= Filter_PropertyChanged;
            
        this.Unloaded -= PacketInterceptorView_Unloaded;
    }

    private void Filter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PacketLogFilterViewModel.IsChecked))
            _collectionView.Refresh();
    }

    private bool Search(object obj)
    {
        if (obj is not InterceptedPacketViewModel pkt)
            return false;

        if (!string.IsNullOrWhiteSpace(SearchBox.Text) && !pkt.Packet.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase))
            return false;

        string[] parts = new[] { pkt.Packet };
        foreach (PacketLogFilterViewModel filterVM in _vm.PacketFilters)
        {
            if (filterVM.IsChecked)
                continue;

            if (filterVM.Filter.Invoke(parts))
                return false;
        }

        return true;
    }

    private bool SearchFilters(object obj)
    {
        return string.IsNullOrEmpty(FilterSearchBox.Text) || (obj is PacketLogFilterViewModel filter && filter.Content.Contains(FilterSearchBox.Text, StringComparison.OrdinalIgnoreCase));
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _collectionView.Refresh();
    }

    private void FilterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filtersCollectionView.Refresh();
    }
}
