using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StrideBrowser.Engine;
using StrideBrowser.Services.Extensions;
using StrideBrowser.Services.UI;

namespace StrideBrowser.ViewModels;

public sealed partial class ExtensionsPanelViewModel : ObservableObject
{
    private readonly ExtensionLibrary _library;
    private readonly ExtensionActions _actions;
    private readonly TabEngine _engine;
    private bool _refreshing;
    private bool _refreshAgain;
    [ObservableProperty] private string _error = "";
    public ObservableCollection<ExtensionToolbarItem> Extensions { get; } = [];
    public ObservableCollection<ExtensionToolbarItem> PinnedExtensions { get; } = [];
    public event Action? Dismiss;
    public ExtensionsPanelViewModel(ExtensionLibrary library, ExtensionActions actions, TabEngine engine)
    { _library = library; _actions = actions; _engine = engine; }

    public async Task RefreshAsync()
    {
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            var snapshot = await _actions.SnapshotAsync();
            Extensions.Clear(); PinnedExtensions.Clear();
            foreach (var extension in snapshot.Extensions)
            {
                var item = new ExtensionToolbarItem(extension, this);
                Extensions.Add(item);
                if (extension.IsPinned && extension.IsEnabled && extension.HasPopup && PinnedExtensions.Count < ExtensionLibrary.MaxPinnedExtensions)
                    PinnedExtensions.Add(item);
            }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { _refreshing = false; }
        if (_refreshAgain) { _refreshAgain = false; await RefreshAsync(); }
    }
    [RelayCommand]
    private async Task ManageAsync()
    { Dismiss?.Invoke(); await Run(_engine.OpenExtensionsAsync); }
    private async Task Run(Func<Task> action)
    {
        Error = "";
        try { await action(); }
        catch (Exception ex) { Error = ex.Message; }
        await RefreshAsync();
    }

    /// <summary>Returns the base64 payload of an extension icon data URI.</summary>
    internal static byte[] IconBytes(string dataUri)
    {
        var comma = dataUri.IndexOf(',');
        if (comma < 0 || comma == dataUri.Length - 1)
            throw new FormatException("The extension icon data URI has no base64 payload.");
        return Convert.FromBase64String(dataUri[(comma + 1)..]);
    }
    public sealed class ExtensionToolbarItem
    {
        public InstalledExtension Extension { get; }
        public ImageSource? Icon { get; }
        public string Name => Extension.Name;
        public string PinLabel => Extension.IsPinned ? "Unpin " + Name : "Pin " + Name;
        public string ToggleLabel => Extension.IsEnabled ? "Disable" : "Enable";
        public IAsyncRelayCommand OpenCommand { get; }
        public IAsyncRelayCommand PinCommand { get; }
        public IAsyncRelayCommand OptionsCommand { get; }
        public IAsyncRelayCommand ToggleCommand { get; }
        public IAsyncRelayCommand RemoveCommand { get; }
        public IAsyncRelayCommand ManageCommand { get; }
        public ExtensionToolbarItem(InstalledExtension extension, ExtensionsPanelViewModel vm)
        {
            Extension = extension;
            if (BuiltInExtensions.Contains(extension.Id))
            {
                var path = extension.Id switch
                {
                    "stride:youtube" or "stride:unhook" => "M3,5 L21,5 21,19 3,19 Z M10,9 L15,12 10,15 Z",
                    "stride:tint" => "M3,4 L21,4 21,20 3,20 Z M3,9 L21,9 M7,6 L8,6 M11,6 L12,6",
                    "stride:focus" => "M12,3 A9,9 0 1 0 12,21 A9,9 0 0 0 12,3 M6,6 L18,18",
                    _ => "M20,15 A9,9 0 0 1 9,4 A9,9 0 1 0 20,15"
                };
                var drawing = new DrawingImage(new GeometryDrawing(null, new Pen(Brushes.Gray, 1.5), Geometry.Parse(path)));
                drawing.Freeze(); Icon = drawing;
            }
            if (!string.IsNullOrEmpty(extension.IconDataUri))
                try
                {
                    using var stream = new MemoryStream(IconBytes(extension.IconDataUri));
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 32; image.StreamSource = stream; image.EndInit(); image.Freeze(); Icon = image;
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Extension icon for {extension.Name} could not be read: {ex.Message}"); }
            OpenCommand = new AsyncRelayCommand(() => vm.Run(async () =>
            {
                vm.Dismiss?.Invoke();
                if (BuiltInExtensions.Contains(extension.Id) || extension.HasPopup && extension.IsEnabled) await vm._actions.OpenAsync(extension.Id);
                else await vm._engine.OpenExtensionsAsync();
            }));
            PinCommand = new AsyncRelayCommand(() => vm.Run(async () =>
                await vm._library.PinAsync(await vm._actions.ReadyAsync(), extension.Id, !extension.IsPinned)),
                () => extension.IsPinned || extension.HasPopup && extension.IsEnabled);
            OptionsCommand = new AsyncRelayCommand(() => vm.Run(async () =>
            { vm.Dismiss?.Invoke(); await vm._actions.OpenAsync(extension.Id, true); }), () => extension.HasOptions && (extension.IsEnabled || BuiltInExtensions.Contains(extension.Id)));
            ToggleCommand = new AsyncRelayCommand(() => vm.Run(() => vm._actions.ToggleAsync(extension.Id, !extension.IsEnabled)), () => extension.CanToggle);
            RemoveCommand = new AsyncRelayCommand(() => vm.Run(async () =>
            {
                if (MessageBox.Show(vm._actions.Owner!, "Remove " + extension.Name + "? Its source folder will be kept.",
                    "Remove extension", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    await vm._library.RemoveAsync(await vm._actions.ReadyAsync(), extension.Id);
            }), () => !extension.IsBuiltIn);
            ManageCommand = vm.ManageCommand;
        }
    }
}
