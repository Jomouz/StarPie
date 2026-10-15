using System;
using System.Collections.Generic;
using System.Text.Json;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>Presentation only. Does not change registry labels, action IDs or loading preferences.</summary>
internal static class PluginDisplayText
{
    internal static string Name(PluginInstance instance) => Resolve(instance.Scan.Manifest?.NameTranslations,
        !string.IsNullOrWhiteSpace(instance.Entry.Name) ? instance.Entry.Name :
        !string.IsNullOrWhiteSpace(instance.Scan.Manifest?.Name) ? instance.Scan.Manifest!.Name : instance.PluginId);

    internal static string Description(PluginInstance instance) => Resolve(instance.Scan.Manifest?.DescriptionTranslations,
        !string.IsNullOrWhiteSpace(instance.Scan.Manifest?.Description) ? instance.Scan.Manifest!.Description : instance.Entry.Description);

    internal static string Resolve(IReadOnlyDictionary<string, string>? translations, string fallback)
    {
        if (translations != null)
        {
            foreach (KeyValuePair<string, string> pair in translations)
            {
                if (MatchesLanguage(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value) && pair.Value.Length <= 8192)
                    return pair.Value;
            }
        }
        return fallback;
    }

    internal static string ReadProperty(JsonElement element, string property, string fallback)
    {
        if (element.TryGetProperty(property + "Translations", out JsonElement translations) &&
            translations.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty translation in translations.EnumerateObject())
            {
                if (MatchesLanguage(translation.Name) && translation.Value.ValueKind == JsonValueKind.String)
                {
                    string text = translation.Value.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(text) && text.Length <= 8192) return text;
                }
            }
        }
        return fallback;
    }

    private static bool MatchesLanguage(string language) => I18n.CurrentLanguage switch
    {
        LanguageCode.En => language.Equals("en", StringComparison.OrdinalIgnoreCase) || language.Equals("en-US", StringComparison.OrdinalIgnoreCase),
        LanguageCode.Ja => language.Equals("ja", StringComparison.OrdinalIgnoreCase) || language.Equals("ja-JP", StringComparison.OrdinalIgnoreCase),
        LanguageCode.ZhTw => language.Equals("zh-TW", StringComparison.OrdinalIgnoreCase),
        _ => language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
    };
}
