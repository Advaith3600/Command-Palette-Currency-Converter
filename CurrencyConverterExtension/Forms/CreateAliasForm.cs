using System.Text.Json.Nodes;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension.Forms
{
    internal sealed partial class CreateAliasForm : FormContent
    {
        internal readonly AliasManager _aliasManager;

        public CreateAliasForm(AliasManager aliasManager)
        {
            _aliasManager = aliasManager;

            TemplateJson = $$"""
{
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "type": "AdaptiveCard",
    "version": "1.6",
    "body": [
        {
            "type": "TextBlock",
            "size": "medium",
            "weight": "bolder",
            "text": "Create a new currency alias",
            "horizontalAlignment": "left",
            "wrap": true,
            "style": "heading"
        },
        {
            "type": "Input.Text",
            "label": "Your new alias",
            "style": "text",
            "id": "alias",
            "isRequired": true,
            "errorMessage": "Alias is required",
            "placeholder": "Enter your new alias (eg. $, eur, etc.)"
        },
        {
            "type": "Input.Text",
            "label": "Mapped currency",
            "style": "text",
            "id": "currency",
            "isRequired": true,
            "errorMessage": "Mapped currency is required",
            "placeholder": "Enter a valid currency unit"
        }
    ],
    "actions": [
        {
            "type": "Action.Submit",
            "title": "Create a new alias"
        }
    ]
}
""";
        }

        public override CommandResult SubmitForm(string payload)
        {
            var formInput = JsonNode.Parse(payload)?.AsObject();
            if (formInput == null)
            {
                return CommandResult.KeepOpen();
            }

            string? alias = formInput["alias"]?.ToString();
            string? currency = formInput["currency"]?.ToString();

            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(currency))
            {
                new ToastStatusMessage("Alias and currency are required").Show();
                return CommandResult.KeepOpen();
            }

            if (!_aliasManager.ValidateKeyFormat(alias))
            {
                new ToastStatusMessage("Alias Key is invalid").Show();
                return CommandResult.KeepOpen();
            }

            if (_aliasManager.GetAlias(alias) != null)
            {
                new ToastStatusMessage("Alias already exists").Show();
                return CommandResult.KeepOpen();
            }

            if (!_aliasManager.ValidateKeyFormat(currency))
            {
                new ToastStatusMessage("Currency Key is invalid").Show();
                return CommandResult.KeepOpen();
            }

            _aliasManager.SetAliasAsync(alias, currency).GetAwaiter().GetResult();
            new ToastStatusMessage($"Alias '{alias}' => '{currency}' created").Show();
            return CommandResult.KeepOpen();
        }
    }
}
