using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Models;

namespace Skua.Core.Interfaces;

public interface IDialogService
{
bool? ShowDialog<TViewModel>(TViewModel viewModel)
        where TViewModel : class;

    bool? ShowDialog<TViewModel>(TViewModel viewModel, string Title)
        where TViewModel : class;

    bool? ShowDialog<TViewModel>(TViewModel viewModel, Action<TViewModel> callback)
        where TViewModel : class;

    void ShowDialog(IOptionContainer optionContainer, Action<IOptionContainerViewModel> callback);

    void ShowMessageBox(string message, string caption);

    bool? ShowMessageBox(string message, string caption, bool yesAndNo);

    DialogResult ShowMessageBox(string message, string caption, params string[] buttons);

    /// <summary>
    /// Displays a warning for a server whose measured latency exceeds the configured threshold.
    /// </summary>
    /// <param name="serverName">The server being selected.</param>
    /// <param name="pingMs">The measured handshake latency, or the unavailable sentinel.</param>
    /// <param name="thresholdMs">The configured warning threshold in milliseconds.</param>
    /// <returns>The user's connection decision and warning preference.</returns>
    ServerLatencyWarningResult ShowServerLatencyWarning(string serverName, long pingMs, int thresholdMs);

    IInputDialogViewModel CreateInputDialog(string title, string dialogHint);
}