using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using CurrencyConverterExtension.Commands;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Windows.Management.Deployment;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterAliasPage : DynamicListPage
{
    internal readonly AliasManager _aliasManager;

    public CurrencyConverterAliasPage(AliasManager aliasManager)
    {
        Icon = IconManager.Icon;
        Title = "Currency Converter";
        Name = "Alias";

        _aliasManager = aliasManager;
    }

    public override IListItem[] GetItems()
    {
        string[] createAlias = SearchText.ToLower().Replace(" ", "").Split("=>");
        
       ListItem createCommand = new ListItem(new NoOpCommand())
       { 
            Title = $"Create"
       };

        return _aliasManager
            .GetAllAliases()
            .Where(kvp => kvp.Key.Contains(SearchText) || kvp.Value.Contains(SearchText))
            .Select(kvp => {
                ClearAliasCommand command = new(_aliasManager, kvp.Key);
                command.ItemsChanged += OnAliasClear;
                return new ListItem(new NoOpCommand())
                {
                    Title = $"{kvp.Key} => {kvp.Value}",
                    Icon = IconManager.Icon,
                    MoreCommands = [
                        new CommandContextItem(command)
                    ]
                };
            })
            .ToArray();
    }

    private void OnAliasClear() => RaiseItemsChanged();

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch == newSearch)
        {
            DebounceSearch();
        }
    }

    private CancellationTokenSource? _debounceCts;

    private void DebounceSearch()
    {
        // Cancel any ongoing debounce task
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();

        var token = _debounceCts.Token;

        Task.Delay(300, token) // 300ms debounce delay
            .ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    // Trigger the items update after debounce delay
                    RaiseItemsChanged();
                }
            }, TaskScheduler.Default);
    }
}