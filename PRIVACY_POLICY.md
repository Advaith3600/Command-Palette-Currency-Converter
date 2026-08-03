# Privacy Policy

**Currency Converter for Command Palette** (“this extension”) respects your privacy. This policy describes what data is handled on your device and what is sent over the network when you use the extension.

**Last updated: 2026-08-03**  
**Publisher:** Advaith A J

## Summary

This extension does **not** collect, sell, or upload personal information to the publisher. There is no account system, analytics, advertising, or telemetry built into the extension.

## Data stored on your device

The following may be stored locally so the extension can work:

| Data | Purpose |
| --- | --- |
| Settings (local currency, quick currencies, decimal separator, cache duration, selected API, optional API key) | Remember your preferences |
| Custom currency aliases | Your alias list |
| Pinned conversions | Pins and dock items you save |
| In-memory exchange-rate cache | Reduce repeat network requests during a session |

Settings are saved under Command Palette’s settings location. Aliases and pins are saved in the extension’s app data folder. Windows may sync roaming app data with your Microsoft account if that feature is enabled on your device; the extension itself does not upload this data.

You can clear or change this data by editing settings, removing pins/aliases, or uninstalling the extension.

## Network requests

To fetch exchange rates, the extension contacts the rate provider you select in Settings:

1. **Default** — [fawazahmed0/currency-api](https://github.com/fawazahmed0/exchange-api) via jsDelivr CDN and a Cloudflare Pages fallback  
2. **ExchangeRate-API** — [exchangerate-api.com](https://www.exchangerate-api.com/)  
3. **CurrencyAPI** — [currencyapi.com](https://currencyapi.com/)

These requests typically include currency codes (for example, a base currency) and, for non-default providers, the API key you enter in Settings. Like any HTTPS request, the provider also receives standard connection metadata such as your IP address.

None of these providers are affiliated with this extension. Their own privacy policies apply to data they receive:

- [ExchangeRate-API privacy](https://www.exchangerate-api.com/terms)  
- [CurrencyAPI privacy](https://currencyapi.com/privacy-policy/)  
- Default API / CDN hosts: see the [fawazahmed0/exchange-api](https://github.com/fawazahmed0/exchange-api) project and the jsDelivr / Cloudflare policies for their respective services

The extension does not send your search queries, pins, aliases, or settings to the publisher.

## Clipboard

When you choose a conversion result, the converted amount may be copied to the system clipboard so you can paste it elsewhere. Clipboard contents stay on your device unless you paste them into another app.

## Host platforms

This extension runs inside Windows Command Palette / PowerToys. The Microsoft Store, Winget, Windows, and Microsoft account services have separate privacy policies that apply when you install or use those platforms.

## Contact

Questions about this policy: open an issue on the [GitHub repository](https://github.com/Advaith3600/Command-Palette-Currency-Converter).

## Changes

If this policy is updated, the “Last updated” date above will change. Continued use of the extension after an update means you accept the revised policy.
