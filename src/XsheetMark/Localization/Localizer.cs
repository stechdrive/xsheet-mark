using System;
using System.Globalization;
using System.Windows;

namespace XsheetMark.Localization;

/// <summary>
/// Loads a string ResourceDictionary for the active language and exposes
/// lookup + format helpers. XAML consumes strings via {DynamicResource Key};
/// code-behind uses Get(key) / Format(key, args).
/// Call Init() from the App constructor before any window loads its XAML.
/// </summary>
public static class Localizer
{
    private static ResourceDictionary? _current;

    public static string CurrentLanguage { get; private set; } = "en";

    public static void Init() => SetLanguage(DetectLanguage());

    public static void SetLanguage(string lang)
    {
        if (lang != "ja" && lang != "en") lang = "en";

        var dict = new ResourceDictionary
        {
            Source = new Uri($"Localization/Strings.{lang}.xaml", UriKind.Relative),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        CurrentLanguage = lang;
    }

    public static string Get(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private static string DetectLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
}
