using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AIUsageMonitor.Models;
using AIUsageMonitor.Services;
using WpfClipboard = System.Windows.Clipboard;

namespace AIUsageMonitor.Views;

// Signs a Claude account in without a terminal and without opening a browser by itself:
// 1. the user names the account and (optionally) gives its email,
// 2. the app shows the sign-in link to copy or open in a private window,
// 3. the user pastes the code from the sign-in page, and the app checks who signed in.
public partial class ClaudeLoginWindow : Window
{
    private static readonly TimeSpan LinkTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromMinutes(2);

    private readonly AnthropicAccountManagerService _accountManager;
    private readonly ProviderAccount? _existingAccount;
    private readonly PrivateBrowserLauncher.PrivateBrowser? _privateBrowser;
    private AnthropicAccountManagerService.PendingLogin? _pending;
    private string _url = string.Empty;

    // `existingAccount` null adds a new account; otherwise signs that account in again.
    public ClaudeLoginWindow(AnthropicAccountManagerService accountManager, ProviderAccount? existingAccount = null)
    {
        InitializeComponent();
        _accountManager = accountManager;
        _existingAccount = existingAccount;
        _privateBrowser = PrivateBrowserLauncher.Find();

        if (existingAccount is not null)
        {
            Title = "Sign in to Claude again";
            HeaderTextBlock.Text = $"Sign in again: {existingAccount.Label}";
            LabelTextBox.Text = existingAccount.Label;
            LabelTextBox.IsReadOnly = true;
            EmailTextBox.Text = existingAccount.Email;
        }

        if (_privateBrowser is null)
        {
            OpenPrivateButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            OpenPrivateButton.Content = $"Open in {_privateBrowser.Name} private window";
        }

        Loaded += (_, _) => (existingAccount is null ? LabelTextBox : EmailTextBox).Focus();
        Closing += OnClosing;
    }

    public AnthropicAccountManagerService.LoginResult? Result { get; private set; }

    private async void GetLinkButtonOnClick(object sender, RoutedEventArgs e)
    {
        var label = LabelTextBox.Text.Trim();
        var email = EmailTextBox.Text.Trim();
        if (label.Length == 0)
        {
            StatusTextBlock.Text = "Enter a name for this account.";
            LabelTextBox.Focus();
            return;
        }

        if (email.Length > 0 && !email.Contains('@'))
        {
            StatusTextBlock.Text = "That email does not look right. Fix it or leave it empty.";
            EmailTextBox.Focus();
            return;
        }

        SetDetailsEnabled(false);
        StatusTextBlock.Text = "Getting the sign-in link...";
        try
        {
            _pending = await Task.Run(() => _accountManager.StartLogin(email.Length == 0 ? null : email));
            _url = await _pending.Session.AuthorizeUrl.WaitAsync(LinkTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            CancelPending();
            SetDetailsEnabled(true);
            StatusTextBlock.Text = ex is TimeoutException
                ? "The claude CLI did not show a sign-in link. Check that Claude Code is installed and try again."
                : ex.Message;
            return;
        }

        ShowSignInStep(email);
    }

    // Offscreen README screenshot of step 2. No CLI runs and nothing is saved.
    internal void ShowSignInStepForScreenshot(string label, string email, string url)
    {
        LabelTextBox.Text = label;
        EmailTextBox.Text = email;
        SetDetailsEnabled(false);
        _url = url;
        ShowSignInStep(email);
    }

    private void ShowSignInStep(string email)
    {
        LinkTextBox.Text = _url;
        CheckAccountTextBlock.Text = email.Length > 0
            ? $"Before you approve, check that the page shows {email}. A browser that is already signed in to claude.ai uses that account, so a private window is safest."
            : "Before you approve, check which account the page shows. A browser that is already signed in to claude.ai uses that account, so a private window is safest.";
        SignInPanel.Visibility = Visibility.Visible;
        StartAgainButton.Visibility = Visibility.Visible;
        GetLinkButton.IsDefault = false;
        FinishButton.IsDefault = true;
        StatusTextBlock.Text = string.Empty;
        CopyLinkButton.Focus();
    }

    private void CopyLinkButtonOnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            WpfClipboard.SetText(_url);
            StatusTextBlock.Text = "Link copied. Paste it into a private window, sign in, then paste the code below.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            StatusTextBlock.Text = "The clipboard is busy. Select the link above and copy it with Ctrl+C.";
        }
    }

    private void OpenPrivateButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (_privateBrowser is null)
        {
            return;
        }

        try
        {
            PrivateBrowserLauncher.Open(_privateBrowser, _url);
            StatusTextBlock.Text = $"Opened a {_privateBrowser.Name} private window. Sign in, then paste the code below.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            StatusTextBlock.Text = $"Could not open {_privateBrowser.Name}: {ex.Message}. Copy the link instead.";
        }
    }

    private void OpenDefaultButtonOnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true })?.Dispose();
            StatusTextBlock.Text = "Opened your browser. Check the account on the page before you approve, then paste the code below.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            StatusTextBlock.Text = $"Could not open the browser: {ex.Message}. Copy the link instead.";
        }
    }

    private async void FinishButtonOnClick(object sender, RoutedEventArgs e)
    {
        var code = CodeTextBox.Text.Trim();
        if (code.Length == 0 || _pending is null)
        {
            StatusTextBlock.Text = "Paste the code from the sign-in page first.";
            CodeTextBox.Focus();
            return;
        }

        var pending = _pending;
        _pending = null;
        SignInPanel.IsEnabled = false;
        StartAgainButton.IsEnabled = false;
        StatusTextBlock.Text = "Checking the code...";
        using var timeout = new CancellationTokenSource(FinishTimeout);
        try
        {
            await pending.Session.SubmitCodeAsync(code);
            Result = await _accountManager.CompleteLoginAsync(
                pending,
                _existingAccount,
                LabelTextBox.Text.Trim(),
                EmailTextBox.Text.Trim(),
                timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            _accountManager.CancelLogin(pending);
            Result = AnthropicAccountManagerService.LoginResult.Failed(
                "The sign-in did not finish. Nothing was saved. Start again and paste the new code.");
        }

        if (Result.Succeeded)
        {
            DialogResult = true;
            return;
        }

        ResetToDetails(Result.Message);
    }

    private void StartAgainButtonOnClick(object sender, RoutedEventArgs e)
    {
        CancelPending();
        ResetToDetails(string.Empty);
    }

    private void CancelButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CancelPending();
    }

    private void ResetToDetails(string message)
    {
        _url = string.Empty;
        LinkTextBox.Text = string.Empty;
        CodeTextBox.Text = string.Empty;
        SignInPanel.Visibility = Visibility.Collapsed;
        SignInPanel.IsEnabled = true;
        StartAgainButton.Visibility = Visibility.Collapsed;
        StartAgainButton.IsEnabled = true;
        FinishButton.IsDefault = false;
        GetLinkButton.IsDefault = true;
        SetDetailsEnabled(true);
        StatusTextBlock.Text = message;
    }

    private void SetDetailsEnabled(bool enabled)
    {
        // Read-only and dimmed, not disabled: WPF's disabled style is a bright grey box here.
        LabelTextBox.IsReadOnly = !enabled || _existingAccount is not null;
        EmailTextBox.IsReadOnly = !enabled;
        LabelTextBox.Opacity = EmailTextBox.Opacity = enabled ? 1 : 0.6;
        GetLinkButton.IsEnabled = enabled;
    }

    private void CancelPending()
    {
        if (_pending is null)
        {
            return;
        }

        _accountManager.CancelLogin(_pending);
        _pending = null;
    }
}
