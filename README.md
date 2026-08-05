# Currency Converter

[![Microsoft](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9PC2T04G3V9C)

![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/advaith3600/Command-Palette-Currency-Converter/total)
![GitHub Release](https://img.shields.io/github/v/release/advaith3600/Command-Palette-Currency-Converter)

Convert fiat and crypto currencies without leaving [Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview). Type natural language, pin the pairs you care about, and keep live rates on the dock — ready the moment you open your launcher.

![Home](screenshots/home.png)

![Settings](screenshots/settings.png)

## Why you'll love it

- **Natural language** — codes, symbols, and mixed styles all work (`100 inr to usd`, `$100 to €`, `₽100`)
- **Fiat and crypto** — convert in either direction with the free default API
- **Math built in** — evaluate expressions with BODMAS/PEMDAS, then convert
- **Pins + dock** — save conversions and see live amounts on the Command Palette dock
- **Details at a glance** — unit rate, inverse rate, and when the rate was last updated
- **Aliases** — rich built-in currency symbols, plus your own custom aliases
- **Free by default** — unlimited daily rates with no API key required

## Installation

This extension supports both **x64** and **ARM** architectures for all installation methods below.

### Method 1: Microsoft Store

Install Currency Converter directly from the Microsoft Store. Click the badge below to open the store page:

[![Microsoft](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9PC2T04G3V9C)

### Method 2: Winget

If you have [Winget](https://learn.microsoft.com/windows/package-manager/winget/) (Windows Package Manager) installed, run:

```
winget install advaith.CurrencyConverterCommandPalette
```

### Method 3: MSIX from GitHub Releases

Download the MSIX package from the [Releases](https://github.com/advaith3600/Command-Palette-Currency-Converter/releases) page. Choose the file that matches your architecture (**x64** or **ARM**) and install it manually.

## Quick start

1. Open Command Palette and run **Currency Converter** (or type a conversion query on the home list).
2. Type a query, for example `100 usd to eur`.
3. Press **Enter** on a result to copy the converted amount to the clipboard.
4. Open the details pane on a selected result to see the unit rate, inverse rate, and last update time.

![Conversion with details](screenshots/conversion.png)

## Usage

Type a conversion in natural language. Currency codes, symbols, and mixed styles all work:

```
100 inr to usd
eur 100 in usd

$100
100R$
100€
100₽
₹100
$100 to eur
100$ to euro
```

Conversion titles always show both source and target (for example `2 USD → 1.86 EUR`). Values use dynamic precision: when an amount is less than 1, the number of non-zero decimal places shown follows your system configuration.

### Crypto and other currencies

Convert between fiat and cryptocurrencies in either direction. See the [full list of supported currencies](https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies.json).

```
1 btc to usd
1209 btc to usd
```

![Crypto conversion](screenshots/conversion-crypto.png)

### Quick conversions

Type just a number to convert from your local currency into the other currencies configured in Settings. You can change both the local currency and the list of target currencies there.

```
102.2
$1209
```

![Quick conversion](screenshots/conversion-quick.png)

### Mathematical calculations

Type a math expression and the extension evaluates it with BODMAS/PEMDAS before converting. Supported operators are `+`, `-`, `*`, and `/`, including brackets:

```
(12.4 - 34) / 3.3 + 43.3 * 2.22
```

![Math expression conversion](screenshots/conversion-math.png)

## Pins, Today's rates & dock

### Today's rates

Open **Today's rates** from the main page (when the search box is empty) to see live conversions of `1` unit of your local currency into each of your other currencies from Settings.

If your local currency matches every currency in the other-currencies list, a warning is shown instead of the default rates — press **Enter** on it to open Settings and add a different currency.

![Today's rates](screenshots/todays-rates.png)

### Pinning conversions

Keep the pairs you check often at the top of Today's rates and on the dock.

| Where                             | Pin                           | Unpin                         | Enter                       |
| --------------------------------- | ----------------------------- | ----------------------------- | --------------------------- |
| Main converter results            | Context menu (`Ctrl + Enter`) | Context menu (`Ctrl + Enter`) | Copies the converted amount |
| Today's rates (search for a pair) | **Enter**                     | Context menu (`Ctrl + Enter`) | Pins the conversion         |

Example: on Today's rates, search `34 btc to aed` and press **Enter** to pin it. Pinned conversions appear at the top with live rates the next time you open the page.

### Currency pins on the dock

Pinned conversions also show up in the Command Palette **Currency pins** dock band — so your favorite rates are visible without opening the extension.

- Press **Enter** on a dock pin to copy the converted amount
- Use the context menu to **Refresh** rates for that pin's base currency, or **Unpin** it
- Dock rates refresh automatically on a new local calendar day, and whenever you change your pins

![Dock pins](screenshots/dock-pins.png)

## Aliases

Open **Manage currency aliases** to view, create, and remove currency aliases. Hundreds of built-in symbol aliases (`$`, `€`, `₹`, `£`, and more) ship with the extension so everyday typing just works.

You can also:

- **Export** your alias configuration to your Downloads folder
- **Reset** all aliases to the built-in defaults

Remove an alias by selecting it and pressing `Ctrl + Enter`, then confirming the prompt.

![Manage aliases](screenshots/aliases.png)

![Create alias](screenshots/aliases-create.png)

## Settings

Open Settings from the Currency Converter command (context menu → Settings), or from the warning on Today's rates when your currency list needs attention.

| Setting                             | What it does                                                      |
| ----------------------------------- | ----------------------------------------------------------------- |
| **Quick Conversion Local Currency** | Base currency for number-only quick conversions and Today's rates |
| **Quick Conversion Currencies**     | Comma-separated targets (e.g. `USD, EUR, BTC`)                    |
| **Decimal format separator**        | System default, always dots, or always commas                     |
| **Conversion Cache duration**       | How long rates stay cached, in hours (min `0.5`, max `24`)        |
| **Conversion API**                  | Rate provider (see below)                                         |
| **Conversion API Key**              | Required only for ExchangeRateAPI or CurrencyAPI                  |

## Conversion API

This extension uses third-party APIs for the latest conversion rates:

1. **Default: [fawazahmed0/exchange-api](https://github.com/fawazahmed0/exchange-api)**
   - Refreshed every day at midnight.
   - **Free** and **unlimited** — no API key required.
   - Supports fiat and cryptocurrency conversions.
   - **Important:** Keep the default API unless you have a strong reason to switch. It updates daily and needs no extra setup.

2. **[Frankfurter](https://frankfurter.dev/)**
   - Free, open-source exchange rates from central banks — no API key required.
   - No request quotas (abuse rate limits may still apply).
   - Does **not** support cryptocurrency conversions.
   - Uses the public Frankfurter [v2 rates API](https://frankfurter.dev/).

3. **[ExchangeRateAPI](https://www.exchangerate-api.com/)**
   - Updated frequently throughout the day ([pricing](https://www.exchangerate-api.com/#pricing)).
   - Free tier: 1,500 requests per month.
   - Does **not** support cryptocurrency conversions.
   - Requires an API key in Settings.

4. **[CurrencyAPI](https://currencyapi.com)**
   - Updated frequently throughout the day ([pricing](https://currencyapi.com/pricing/)).
   - See their documentation for update frequency, pricing, and supported features.
   - Requires an API key in Settings.

None of these APIs are affiliated with this extension. To use a different rate provider, or to suggest a new one, open a pull request.

## Privacy

This extension has no analytics or telemetry. Settings, aliases, and pins stay on your device; exchange rates are fetched from the third-party API you select. See the [Privacy Policy](PRIVACY_POLICY.md) for details.
