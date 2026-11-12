using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Api.Features.Users;

/// <summary>Turns a user's name and company into a DNS label ("Jane", "Doe", "Acme Corp." -> "jane-doe-acme-corp"). Same rule as the frontend preview.</summary>
public static partial class TenantSlug
{
    public const int MaxLength = 30;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    public static string From(string? name, string? lastname, string? company)
    {
        var withoutAccents = new StringBuilder();
        foreach (var c in string.Join(' ', name, lastname, company).Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                withoutAccents.Append(c);

        var slug = NonAlphanumeric().Replace(withoutAccents.ToString().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > MaxLength) slug = slug[..MaxLength].TrimEnd('-');
        return slug.Length == 0 ? "tenant" : slug;
    }

    /// <summary>The n-th alternative when the slug is taken: "acme", "acme-2", "acme-3", ... (still at most 30 chars).</summary>
    public static string Candidate(string slug, int attempt)
    {
        if (attempt <= 1) return slug;
        var suffix = $"-{attempt}";
        return slug[..Math.Min(slug.Length, MaxLength - suffix.Length)].TrimEnd('-') + suffix;
    }
}
