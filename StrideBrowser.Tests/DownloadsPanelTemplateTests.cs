using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Markup;
using System.IO;
using System.Xml.Linq;
using StrideBrowser.Controls;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

[Collection("ThemeManagerResources")]
public sealed class DownloadsPanelTemplateTests(ThemeAppFixture fixture)
{
    [Theory]
    [InlineData(AppThemeMode.Dark)]
    [InlineData(AppThemeMode.Light)]
    public void LiveItemTemplate_LoadsAndUpdatesReadOnlyProgressWithoutWritingBack(AppThemeMode mode)
    {
        fixture.RunOnAppThread(() =>
        {
            _ = new ThemeManager(new BrowserSettings { AppTheme = mode });
            var item = new DownloadItem { FileName = "example.zip", ReceivedBytes = 50, TotalBytes = 100 };
            var panel = new DownloadsPanel
            {
                Width = 380,
                DataContext = new { RecentDownloads = new ObservableCollection<DownloadItem> { item } }
            };
            panel.Measure(new Size(380, 600));
            panel.Arrange(new Rect(0, 0, 380, panel.DesiredSize.Height));
            panel.UpdateLayout();
            var progress = Assert.Single(Descendants(panel).OfType<ProgressBar>());
            Assert.Equal(50, progress.Value);
            item.ReceivedBytes = 75;
            progress.GetBindingExpression(ProgressBar.ValueProperty)!.UpdateTarget();
            Assert.Equal(75, progress.Value);
            Assert.Equal(75, item.ProgressPercent);
        });
    }
    [Theory]
    [InlineData(AppThemeMode.Dark)]
    [InlineData(AppThemeMode.Light)]
    public void RowActionsUseAccessibleIconsAndChangeWhenDownloadCompletes(AppThemeMode mode)
    {
        fixture.RunOnAppThread(() =>
        {
            _ = new ThemeManager(new BrowserSettings { AppTheme = mode });
            var item = new DownloadItem { FileName = "a-long-download-file-name.zip", TotalBytes = 100 };
            var panel = new DownloadsPanel
            {
                Width = 360,
                DataContext = new { RecentDownloads = new ObservableCollection<DownloadItem> { item } }
            };
            panel.Measure(new Size(360, 600));
            panel.Arrange(new Rect(0, 0, 360, panel.DesiredSize.Height));
            panel.UpdateLayout();
            var actions = Descendants(panel).OfType<Button>()
                .Where(button => ReferenceEquals(button.DataContext, item)).ToArray();
            Assert.Equal(new[] { "Pause download", "Cancel download" }, actions
                .Where(button => button.Visibility == Visibility.Visible).Select(AutomationProperties.GetName));
            Assert.All(actions, button =>
            {
                Assert.IsType<System.Windows.Shapes.Path>(button.Content);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.NotNull(button.ToolTip);
            });
            item.State = DownloadState.Completed;
            foreach (var button in actions) button.GetBindingExpression(UIElement.VisibilityProperty)!.UpdateTarget();
            panel.UpdateLayout();
            var visible = actions.Where(button => button.Visibility == Visibility.Visible).ToArray();
            Assert.Equal(new[] { "Open file", "Show in folder" }, visible.Select(AutomationProperties.GetName));
            Assert.All(visible, button =>
            {
                var position = button.TranslatePoint(new Point(), panel);
                Assert.InRange(position.X, 0, panel.ActualWidth - button.ActualWidth);
            });
            Assert.True(panel.ActualHeight < 160);
        });
    }
    [Theory]
    [InlineData(AppThemeMode.Dark)]
    [InlineData(AppThemeMode.Light)]
    public void DownloadsTabSelectionUsesNeutralThemeSurfaceWithAPurpleAccent(AppThemeMode mode)
    {
        fixture.RunOnAppThread(() =>
        {
            _ = new ThemeManager(new BrowserSettings { AppTheme = mode });
            var resources = Application.Current.Resources;
            var previous = resources["AccentWash"];
            var previousColor = resources["AccentWashColor"];
            try
            {
                resources["AccentWash"] = new SolidColorBrush(Colors.Magenta);
                resources["AccentWashColor"] = Colors.Magenta;
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root is not null && !File.Exists(Path.Combine(root.FullName, "Stride.csproj"))) root = root.Parent;
                Assert.NotNull(root);
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var styleXml = XDocument.Load(Path.Combine(root!.FullName, "App.xaml")).Descendants()
                    .Single(element => (string?)element.Attribute(x + "Key") == "FaviconTabListStyle");
                styleXml.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                var style = (Style)XamlReader.Parse(styleXml.ToString());
                var tab = new BrowserTab(InternalUrls.Downloads) { Title = "Downloads" };
                var list = new ListBox { Style = style, Width = 240, Height = 40 };
                list.Items.Add(tab); list.SelectedItem = tab;
                list.Measure(new Size(240, 40)); list.Arrange(new Rect(0, 0, 240, 40)); list.UpdateLayout();
                var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(tab);
                var border = (Border)container.Template.FindName("Bd", container);
                var neutral = (SolidColorBrush)Application.Current.FindResource("NeutralSelectedTabBrush");
                Assert.Equal(neutral.Color, ((SolidColorBrush)border.Background).Color);
                tab.Url = "https://example.com/";
                list.UpdateLayout();
                var selected = (SolidColorBrush)Application.Current.FindResource("SelectedTabBrush");
                Assert.Equal(selected.Color, ((SolidColorBrush)border.Background).Color);
            }
            finally
            {
                if (previous is null) resources.Remove("AccentWash"); else resources["AccentWash"] = previous;
                if (previousColor is null) resources.Remove("AccentWashColor"); else resources["AccentWashColor"] = previousColor;
            }
        });
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
