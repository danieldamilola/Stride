using System;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Models;

namespace UmberBrowser.Engine.Handlers;

public static class TabDialogHandler
{
    public static void Wire(
        CoreWebView2 core, 
        Dispatcher dispatcher, 
        BrowserSettings settings,
        Func<System.Windows.Window?>? getOwner = null)
    {
        core.ScriptDialogOpening += (_, e) =>
        {
            var deferral = e.GetDeferral();
            dispatcher.InvokeAsync(() =>
            {
                ShowScriptDialog(e, getOwner?.Invoke());
                deferral.Complete();
            });
        };

        core.PermissionRequested += (_, e) =>
        {
            var deferral = e.GetDeferral();
            dispatcher.InvokeAsync(() =>
            {
                ShowPermissionDialog(e, getOwner?.Invoke());
                deferral.Complete();
            });
        };
    }

    private static void ShowScriptDialog(CoreWebView2ScriptDialogOpeningEventArgs e, System.Windows.Window? owner)
    {
        var host = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            ? Services.NavigationService.ToSafeAsciiHost(uri)
            : "";

        var dialog = new BaseBrowserDialogWindow
        {
            Owner = owner ?? System.Windows.Application.Current.MainWindow,
            DialogTitle = string.IsNullOrEmpty(host) ? "This page says" : $"{host} says",
            DialogMessage = e.Message
        };

        switch (e.Kind)
        {
            case CoreWebView2ScriptDialogKind.Alert:
                dialog.CancelVisibility = System.Windows.Visibility.Collapsed;
                dialog.InputVisibility = System.Windows.Visibility.Collapsed;
                break;
            case CoreWebView2ScriptDialogKind.Confirm:
                dialog.CancelVisibility = System.Windows.Visibility.Visible;
                dialog.InputVisibility = System.Windows.Visibility.Collapsed;
                break;
            case CoreWebView2ScriptDialogKind.Prompt:
                dialog.CancelVisibility = System.Windows.Visibility.Visible;
                dialog.InputVisibility = System.Windows.Visibility.Visible;
                dialog.InputText = e.DefaultText;
                break;
            case CoreWebView2ScriptDialogKind.Beforeunload:
                dialog.DialogTitle = "Leave site?";
                dialog.CancelVisibility = System.Windows.Visibility.Visible;
                dialog.InputVisibility = System.Windows.Visibility.Collapsed;
                dialog.OkButtonText = "Leave";
                dialog.CancelButtonText = "Stay";
                break;
        }

        dialog.ShowDialog();

        if (dialog.IsAccepted)
        {
            e.Accept();
            if (e.Kind == CoreWebView2ScriptDialogKind.Prompt)
                e.ResultText = dialog.InputText;
        }
    }

    private static void ShowPermissionDialog(CoreWebView2PermissionRequestedEventArgs e, System.Windows.Window? owner)
    {
        var host = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            ? Services.NavigationService.ToSafeAsciiHost(uri)
            : "";

        string permissionName = e.PermissionKind switch
        {
            CoreWebView2PermissionKind.Microphone => "your microphone",
            CoreWebView2PermissionKind.Camera => "your camera",
            CoreWebView2PermissionKind.Geolocation => "your location",
            CoreWebView2PermissionKind.Notifications => "show notifications",
            CoreWebView2PermissionKind.OtherSensors => "motion and light sensors",
            CoreWebView2PermissionKind.ClipboardRead => "read your clipboard",
            CoreWebView2PermissionKind.LocalFonts => "access local fonts",
            _ => "access a restricted feature"
        };

        var dialog = new BaseBrowserDialogWindow
        {
            Owner = owner ?? System.Windows.Application.Current.MainWindow,
            DialogTitle = string.IsNullOrEmpty(host) ? "This site" : host,
            DialogMessage = $"Wants to {permissionName}.",
            CancelVisibility = System.Windows.Visibility.Visible,
            OkButtonText = "Allow",
            CancelButtonText = "Deny"
        };

        dialog.ShowDialog();

        e.State = dialog.IsAccepted
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
    }
}