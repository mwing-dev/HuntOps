using System.Globalization;
using System.Text;

namespace HuntOps.Domain.Common;

/// <summary>Normalization used for uniqueness keys, codes and URL slugs.</summary>
public static class TextKeys
{
    /// <summary>
    /// Case- and whitespace-insensitive key used for uniqueness ("Kansas  Dept." and "kansas dept." collide).
    /// </summary>
    public static string NameKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Normalize(NormalizationForm.FormKC).Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    /// <summary>Trims and upper-cases a short code such as "KS" or "US".</summary>
    public static string Code(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Trim().ToUpperInvariant();
    }

    /// <summary>Lowercase ASCII slug: letters and digits separated by single hyphens.</summary>
    public static string Slug(string value, int maxLength = 100)
    {
        ArgumentNullException.ThrowIfNull(value);
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingHyphen = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingHyphen = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = builder.ToString();
        if (slug.Length > maxLength)
        {
            slug = slug[..maxLength].TrimEnd('-');
        }

        return slug;
    }
}
