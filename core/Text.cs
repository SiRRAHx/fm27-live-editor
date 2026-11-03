// Search that ignores case and accents, so "kone" finds Koné and "odegaard" finds Ødegaard.
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FM27LiveEditor;

public static class Text
{
    static readonly ConcurrentDictionary<string, string> folded = new();

    // Letters that don't split into a base letter and an accent.
    static string Special(char c) => c switch
    {
        'ø' => "o", 'ł' => "l", 'đ' => "d", 'ð' => "d", 'þ' => "th", 'ß' => "ss", 'æ' => "ae", 'œ' => "oe", 'ı' => "i",
        _ => null,
    };

    public static string Fold(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return folded.GetOrAdd(s, x =>
        {
            var sb = new StringBuilder(x.Length);
            foreach (char c in x.Normalize(NormalizationForm.FormD).ToLowerInvariant())
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (Special(c) is { } r) sb.Append(r); else sb.Append(c);
            }
            return sb.ToString();
        });
    }

    // Every word of the query is in one of the texts.
    public static bool Matches(string[] words, params string[] texts) => words.All(w => texts.Any(t => Fold(t).Contains(w)));

    public static string[] Words(string q) => Fold(q).Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
}
