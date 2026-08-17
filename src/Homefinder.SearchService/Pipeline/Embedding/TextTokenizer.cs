using System.Globalization;
using System.Text;

namespace Homefinder.SearchService.Pipeline.Embedding;

/// <summary>
/// One tokeniser, used by every stage that reads free text — the lexical scorer, the
/// embedding function and the manipulation scanner. A second implementation would be
/// a second definition of "the same word", and the two would drift the day one of them
/// learns to strip diacritics and the other does not (SPEC B-7: French and German
/// listings share this corpus, and "café" and "cafe" must tokenise the same way).
/// </summary>
public static class TextTokenizer
{
    public static IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text.Normalize(NormalizationForm.FormD);
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);

            if (category == UnicodeCategory.NonSpacingMark)
            {
                // Diacritic marks are dropped, not kept: "café" and "cafe" tokenise
                // identically, which is what lets a German- or French-authored
                // listing be found by an unaccented query typed on an ordinary
                // keyboard.
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
