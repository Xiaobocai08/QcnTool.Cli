using System.Globalization;

namespace QcnTool.Cli;

internal enum AppLanguage
{
    Auto,
    Zh,
    En
}

internal sealed class Localizer
{
    public Localizer(AppLanguage language)
    {
        Language = language == AppLanguage.Auto ? DetectSystemLanguage() : language;
    }

    public AppLanguage Language { get; }

    public string T(string zh, string en)
    {
        return Language == AppLanguage.Zh ? zh : en;
    }

    public static AppLanguage DetectSystemLanguage()
    {
        var uiCulture = CultureInfo.CurrentUICulture;
        return uiCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Zh
            : AppLanguage.En;
    }

    public static bool TryParse(string raw, out AppLanguage language)
    {
        language = AppLanguage.Auto;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var token = raw.Trim().ToLowerInvariant();
        if (token is "auto")
        {
            language = AppLanguage.Auto;
            return true;
        }

        if (token.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            language = AppLanguage.Zh;
            return true;
        }

        if (token.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            language = AppLanguage.En;
            return true;
        }

        return false;
    }
}
