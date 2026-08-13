using System;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;

namespace StrideBrowser.Engine.Handlers;

public static class TabDialogHandler
{
    public static void Wire(
        CoreWebView2 core, 
        Dispatcher dispatcher, 
        BrowserSettings settings)
    {
        core.ScriptDialogOpening += (_, e) =>
        {
            if (settings.AdBlockEnabled)
            {
                var msg = e.Message?.ToLowerInvariant() ?? "";
                if (msg.Contains("robot") || msg.Contains("virus") || msg.Contains("update") || 
                    msg.Contains("allow") || msg.Contains("human") || msg.Contains("vpn"))
                {
                    // Silently suppress the spam dialog
                    return;
                }
            }

            var deferral = e.GetDeferral();
            dispatcher.InvokeAsync(() =>
            {
                var host = "";
                try { host = new Uri(e.Uri).Host; } catch { }

                var dialog = new BaseBrowserDialogWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow,
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
                
                deferral.Complete();
            });
        };

        core.PermissionRequested += (_, e) =>
        {
            var deferral = e.GetDeferral();
            dispatcher.InvokeAsync(() =>
            {
                var host = "";
                try { host = new Uri(e.Uri).Host; } catch { }

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
                    Owner = System.Windows.Application.Current.MainWindow,
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
                    
                deferral.Complete();
            });
        };
    }
}
