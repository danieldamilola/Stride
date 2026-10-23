using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using UmberBrowser.Models;
using UmberBrowser.Services;
using UmberBrowser.ViewModels;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class SuggestionSelectionTests : IDisposable
{
    private readonly DirectoryInfo _cache = Directory.CreateTempSubdirectory("umber-suggestion-selection");
    private readonly HttpClient _http = new();
    private BrowserViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _http.Dispose();
        _cache.Delete(true);
    }

    private sealed class Downloads : IDownloadStore
    {
        public ObservableCollection<DownloadItem> Items { get; } = [];
        public void Add(DownloadItem item) => Items.Add(item);
        public void Remove(string id) { }
        public void ClearCompleted() { }
        public void ClearAll() => Items.Clear();
        public DownloadItem? Get(string id) => null;
    }

    private sealed class Suggestions : ISuggestionProvider
    {
        public List<SuggestionItem> Local { get; set; } = [];
        public Task<List<SuggestionItem>> Web { get; set; } = Task.FromResult(new List<SuggestionItem>());
        public List<SuggestionItem> GetLocalSuggestions(string query) => Local.ToList();
        public Task<List<SuggestionItem>> GetWebSuggestionsAsync(string query, CancellationToken ct) => Web;
        public Task<List<SuggestionItem>> GetSuggestionsAsync(string query, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static SuggestionItem First() => new() { DisplayText = "Car simulator", Url = "https://first.example/car" };
    private static SuggestionItem Second() => new() { DisplayText = "Cartoons", Url = "https://second.example/car" };

    private BrowserViewModel Create(Suggestions suggestions)
    {
        var settings = new BrowserSettings();
        return _viewModel = new BrowserViewModel(settings, new NavigationService(settings), new Downloads(),
            null!, null!, null!, new UpdateService(_http), suggestions, new FaviconLoader(settings, _http, _cache.FullName))
        { AddressText = "car" };
    }

    [Fact]
    public async Task FirstLocalResultIsSelectedWithoutReplacingTypedText()
    {
        var vm = Create(new Suggestions { Local = [First(), Second()] });

        await vm.UpdateSuggestionsAsync("car");

        Assert.True(vm.ShowSuggestions);
        Assert.Equal(0, vm.SelectedSuggestionIndex);
        Assert.Equal("https://first.example/car", vm.TakeHighlightedInput());
        Assert.Equal("car", vm.AddressText);
        Assert.False(vm.IsSuggestionTextDisplayed);
    }

    [Fact]
    public async Task ArrowsMoveFromTheFirstResultAndUpRestoresTypedText()
    {
        var vm = Create(new Suggestions { Local = [First(), Second()] });
        await vm.UpdateSuggestionsAsync("car");

        Assert.True(vm.MoveSuggestionHighlight(1));
        Assert.Equal(1, vm.SelectedSuggestionIndex);
        Assert.Equal("Cartoons", vm.AddressText);
        Assert.True(vm.IsSuggestionTextDisplayed);
        Assert.Equal("https://second.example/car", vm.TakeHighlightedInput());

        vm.MoveSuggestionHighlight(-1);
        vm.MoveSuggestionHighlight(-1);

        Assert.Equal(-1, vm.SelectedSuggestionIndex);
        Assert.Equal("car", vm.AddressText);
        Assert.False(vm.IsSuggestionTextDisplayed);
        Assert.Null(vm.TakeHighlightedInput());
    }

    [Theory]
    [InlineData("")]
    [InlineData("no matches")]
    public async Task EmptyResultsClearThePreviousSelection(string query)
    {
        var suggestions = new Suggestions { Local = [First()] };
        var vm = Create(suggestions);
        await vm.UpdateSuggestionsAsync("car");
        suggestions.Local.Clear();

        await vm.UpdateSuggestionsAsync(query);

        Assert.False(vm.ShowSuggestions);
        Assert.Equal(-1, vm.SelectedSuggestionIndex);
        Assert.Null(vm.TakeHighlightedInput());
    }

    [Fact]
    public async Task FirstWebResultIsSelectedWhenThereAreNoLocalMatches()
    {
        var response = new TaskCompletionSource<List<SuggestionItem>>();
        var vm = Create(new Suggestions { Web = response.Task });
        var update = vm.UpdateSuggestionsAsync("car");
        Assert.Equal(-1, vm.SelectedSuggestionIndex);

        response.SetResult([new() { DisplayText = "car games", Kind = SuggestionKind.Search }]);
        await update;

        Assert.Equal(0, vm.SelectedSuggestionIndex);
        Assert.Equal("car games", vm.TakeHighlightedInput());
        Assert.Equal("car", vm.AddressText);
        Assert.False(vm.IsSuggestionTextDisplayed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task LateWebResultsPreserveTheUsersArrowSelection(int delta)
    {
        var response = new TaskCompletionSource<List<SuggestionItem>>();
        var vm = Create(new Suggestions { Local = [First(), Second()], Web = response.Task });
        var update = vm.UpdateSuggestionsAsync("car");
        vm.MoveSuggestionHighlight(delta);
        var selected = vm.SelectedSuggestionIndex;
        var text = vm.AddressText;

        response.SetResult([new() { DisplayText = "car games", Kind = SuggestionKind.Search }]);
        await update;

        Assert.Equal(3, vm.Suggestions.Count);
        Assert.Equal(selected, vm.SelectedSuggestionIndex);
        Assert.Equal(text, vm.AddressText);
    }

    [Fact]
    public async Task FurtherTypingResetsArrowNavigationAndSelectsTheNewFirstMatch()
    {
        var suggestions = new Suggestions { Local = [First(), Second()] };
        var vm = Create(suggestions);
        await vm.UpdateSuggestionsAsync("car");
        vm.MoveSuggestionHighlight(1);
        suggestions.Local = [First()];
        vm.AddressText = "cars";

        await vm.UpdateSuggestionsAsync("cars");

        Assert.Equal(0, vm.SelectedSuggestionIndex);
        Assert.Equal("cars", vm.AddressText);
        Assert.False(vm.IsSuggestionTextDisplayed);
    }

    [Fact]
    public async Task ClosingSuggestionsClearsSelectionAndIgnoresLateResults()
    {
        var response = new TaskCompletionSource<List<SuggestionItem>>();
        var vm = Create(new Suggestions { Local = [First()], Web = response.Task });
        var update = vm.UpdateSuggestionsAsync("car");

        vm.CloseSuggestions();
        response.SetResult([Second()]);
        await update;

        Assert.False(vm.ShowSuggestions);
        Assert.Equal(-1, vm.SelectedSuggestionIndex);
        Assert.Null(vm.TakeHighlightedInput());
    }
}
