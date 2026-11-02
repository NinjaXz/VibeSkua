using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Messaging;
using Skua.Manager.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.Manager.Avalonia.UserControls;

public partial class AccountManagerUserControl : UserControl
{
    private readonly AccountManagerViewModel _viewModel;
    private readonly IDialogService _dialogService;
    private bool _isPasswordVisible;

    public AccountManagerUserControl()
    {
        InitializeComponent();
        _viewModel = Ioc.Default.GetRequiredService<AccountManagerViewModel>();
        _dialogService = Ioc.Default.GetRequiredService<IDialogService>();
        DataContext = _viewModel;

        StrongReferenceMessenger.Default.Register<AccountManagerUserControl, ClearPasswordBoxMessage>(this, ClearPasswordBox);
        WeakReferenceMessenger.Default.Register<AccountManagerUserControl, EditAccountMessage>(this, (r, m) => r.EditAccount(m.Account));
        WeakReferenceMessenger.Default.Register<AccountManagerUserControl, AddAccountToGroupMessage>(this, (r, m) => r.AddAccountToGroup(m.Account));
        WeakReferenceMessenger.Default.Register<AccountManagerUserControl, AddTagsMessage>(this, (r, m) => r.EditAccountTags(m.Account));
        AttachedToVisualTree += (s, e) => _viewModel.ReloadAccountsAndGroups();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        StrongReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Unloaded -= OnUnloaded;
    }

    private void ClearPasswordBox(AccountManagerUserControl recipient, ClearPasswordBoxMessage message)
    {
        recipient.PswBox.Clear();
        recipient._isPasswordVisible = false;
        recipient.PswBox.PasswordChar = '*';
        recipient.PswVisibilityIcon.Kind = Material.Icons.MaterialIconKind.EyeOutline;
    }

    private void PasswordBox_PasswordChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            _viewModel.PasswordInput = tb.Text ?? string.Empty;
    }

    private void TogglePasswordVisibility_Click(object? sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        PswBox.PasswordChar = _isPasswordVisible ? '\0' : '*';
        PswVisibilityIcon.Kind = _isPasswordVisible ? Material.Icons.MaterialIconKind.EyeOffOutline : Material.Icons.MaterialIconKind.EyeOutline;
    }

    private void ShowAddAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowAddAccountButton.IsVisible = false;
        AddAccountForm.IsVisible = true;
    }

    private void HideAddAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowAddAccountButton.IsVisible = true;
        AddAccountForm.IsVisible = false;
        PswBox.Clear();
        _isPasswordVisible = false;
        PswBox.PasswordChar = '*';
        PswVisibilityIcon.Kind = Material.Icons.MaterialIconKind.EyeOutline;
    }

    private void AddGroup_Click(object? sender, RoutedEventArgs e)
    {
        InputDialogViewModel inputDialog = new((string)"Create Group", (string)"Enter group name", (string)"Group Name", (bool)false);
        bool? result = _dialogService.ShowDialog(inputDialog, "Create Group");
        if (result == true && !string.IsNullOrWhiteSpace(inputDialog.DialogTextInput))
            _viewModel.AddGroup(inputDialog.DialogTextInput.Trim());
    }

    private void BulkAddTags_Click(object? sender, RoutedEventArgs e)
    {
        AddTagsToSelectedAction();
    }

    public async Task StartSelectedWithScriptAsync()
    {
        List<IAccountItemViewModel> selected = _viewModel.Accounts.Where(a => a.UseCheck).ToList();
        if (selected.Count == 0)
        {
            _dialogService.ShowMessageBox("No accounts selected", "Start Selected With Script");
            return;
        }

        try
        {
            foreach (IAccountItemViewModel account in selected)
            {
                WeakReferenceMessenger.Default.Send(new StartAccountMessage(account, true));
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error in StartSelectedWithScriptAsync: {ex.Message}");
        }
    }

    public void AddSelectedToGroupAction()
    {
        List<IAccountItemViewModel> selected = _viewModel.Accounts.Where(a => a.UseCheck).ToList();
        if (selected.Count == 0)
        {
            _dialogService.ShowMessageBox("No accounts selected", "Add to Group");
            return;
        }

        SelectGroupDialogViewModel dialogVm = new(_viewModel.Groups);
        bool? result = _dialogService.ShowDialog(dialogVm, "Add to Group");
        if (result == true && dialogVm.SelectedGroup != null)
        {
            foreach (IAccountItemViewModel account in selected)
                _viewModel.AddAccountToGroup(account, dialogVm.SelectedGroup);
        }
    }

    private void BulkAddToGroup_Click(object? sender, RoutedEventArgs e)
    {
        AddSelectedToGroupAction();
    }

    public void AddTagsToSelectedAction()
    {
        List<IAccountItemViewModel> selected = _viewModel.Accounts.Where(a => a.UseCheck).ToList();
        if (selected.Count == 0)
        {
            _dialogService.ShowMessageBox("No accounts selected", "Add Tags");
            return;
        }

        InputDialogViewModel inputDialog = new((string)"Add Tags to Selected", (string)"Enter tags (comma-separated)", (string)"Tags", (bool)false);
        bool? result = _dialogService.ShowDialog(inputDialog, "Add Tags");
        if (result != true)
            return;

        List<string> tags = inputDialog.DialogTextInput.Split(',')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (AccountItemViewModel account in selected)
            foreach (string tag in tags)
                if (!account.Tags.Contains(tag))
                    account.Tags.Add(tag);

        _viewModel.SaveAccounts();
        _viewModel.RefreshTagFilters();
        _viewModel.ApplyTagFilter();
    }

    public void EditAccountTags(IAccountItemViewModel account)
    {
        string existingTags = string.Join(", ", account.Tags);
        InputDialogViewModel inputDialog = new((string)"Edit Tags", (string)"Enter tags (comma-separated)", (string)"Tags", (bool)false)
        {
            DialogTextInput = existingTags
        };
        bool? result = _dialogService.ShowDialog(inputDialog, "Edit Tags");
        if (result != true)
            return;

        List<string> tags = inputDialog.DialogTextInput.Split(',')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        account.Tags.Clear();
        foreach (string tag in tags)
            account.Tags.Add(tag);

        _viewModel.SaveAccounts();
        _viewModel.RefreshTagFilters();
        _viewModel.ApplyTagFilter();
    }

    public void EditAccount(IAccountItemViewModel account)
    {
        AccountEditorViewModel editorVm = new(account);
        bool? result = _dialogService.ShowDialog(editorVm, $"Edit Account - {account.Username}");
        if (result == true)
        {
            string oldUsername = account.Username;
            string newUsername = editorVm.Username.Trim();

            if (string.IsNullOrWhiteSpace(newUsername) || string.IsNullOrWhiteSpace(editorVm.Password))
            {
                _dialogService.ShowMessageBox("Username and password must not be empty.", "Invalid Input");
                return;
            }

            // Check if username was changed and if the new username already exists for a different account
            if (!string.Equals(oldUsername, newUsername, StringComparison.OrdinalIgnoreCase))
            {
                bool exists = _viewModel.Accounts.Any(a => !ReferenceEquals(a, account) && string.Equals(a.Username, newUsername, StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    _dialogService.ShowMessageBox($"An account with username '{newUsername}' already exists.", "Duplicate Username");
                    return;
                }
            }

            account.Username = newUsername;
            account.DisplayName = string.IsNullOrWhiteSpace(editorVm.DisplayName) ? string.Empty : editorVm.DisplayName.Trim();
            account.Password = editorVm.Password;

            account.Tags.Clear();
            foreach (string tag in editorVm.ParsedTags)
            {
                account.Tags.Add(tag);
            }

            account.RefreshDisplayName();
            _viewModel.SaveAccounts();
            _viewModel.RefreshTagFilters();
            _viewModel.ApplyTagFilter();
        }
    }

    private void AddAccountToGroup(IAccountItemViewModel account)
    {
        // When accounts are checked, use the checked selection rather than only the
        // account whose context menu was clicked.
        if (_viewModel.Accounts.Any(selectedAccount => selectedAccount.UseCheck))
        {
            AddSelectedToGroupAction();
            return;
        }

        SelectGroupDialogViewModel dialogVm = new(_viewModel.Groups);
        bool? result = _dialogService.ShowDialog(dialogVm, "Add to Group");
        if (result == true && dialogVm.SelectedGroup != null)
            _viewModel.AddAccountToGroup(account, dialogVm.SelectedGroup);
    }
}
