using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shiki.Core.Ai;

/// <summary>
/// Hides secrets in text before it leaves the PC (a document's words, what was copied): card
/// numbers, Aadhaar and PAN numbers, passwords written down, and API keys. Checked on this PC;
/// what was hidden is said, never what it was.
/// </summary>
public static class PrivacyGuard
{
    public static bool On { get; set; } = true;

    static readonly (string Name, Regex Pattern, Func<string, bool>? Check, bool KeepLabel)[] Rules =
    [
        ("card number", new Regex(@"\b(?:\d[ -]?){12,18}\d\b", RegexOptions.Compiled), Luhn, false),
        ("Aadhaar number", new Regex(@"\b[2-9]\d{3}[ -]?\d{4}[ -]?\d{4}\b", RegexOptions.Compiled), Verhoeff, false),
        ("PAN", new Regex(@"\b[A-Z]{3}[PCHFATBLJG][A-Z]\d{4}[A-Z]\b", RegexOptions.Compiled), null, false),
        ("password", new Regex(@"(?i)\b(password|passcode|passwd|pwd|pin|cvv|cvc|otp)\b(\s*(?:is\s+|[:=]\s*))\S+", RegexOptions.Compiled), null, true),
        ("secret key", new Regex(@"\b(?:sk-[A-Za-z0-9_\-]{20,}|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{36,}|xox[bpas]-[A-Za-z0-9-]{10,}|AIza[0-9A-Za-z\-_]{35})\b", RegexOptions.Compiled), null, false),
    ];

    /// <summary>The text with secrets replaced by "[card number hidden]" and the like, and what was hidden.</summary>
    public static (string Text, List<string> Hidden) Clean(string text)
    {
        var hidden = new List<string>();
        if (!On || string.IsNullOrEmpty(text)) return (text, hidden);
        foreach (var (name, pattern, check, keepLabel) in Rules)
        {
            text = pattern.Replace(text, m =>
            {
                if (check != null && !check(m.Value)) return m.Value;
                hidden.Add(name);
                // "Password: hunter2" keeps "Password: " so the sentence still reads.
                return keepLabel ? m.Groups[1].Value + m.Groups[2].Value + $"[{name} hidden]" : $"[{name} hidden]";
            });
        }
        return (text, hidden);
    }

    /// <summary>"2 card numbers and a password".</summary>
    public static string Describe(List<string> hidden) =>
        string.Join(", ", hidden.GroupBy(h => h).Select(g => g.Count() == 1 ? $"{("AEIOU".Contains(g.Key[0]) ? "an" : "a")} {g.Key}" : $"{g.Count()} {g.Key}s"));

    static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    /// <summary>Card numbers carry a check digit (Luhn): most other long numbers fail it.</summary>
    static bool Luhn(string value)
    {
        var digits = Digits(value);
        if (digits.Length is < 13 or > 19) return false;
        int sum = 0;
        bool second = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (second) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            second = !second;
        }
        return sum % 10 == 0;
    }

    static readonly int[,] VerhoeffD =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, { 1, 2, 3, 4, 0, 6, 7, 8, 9, 5 }, { 2, 3, 4, 0, 1, 7, 8, 9, 5, 6 }, { 3, 4, 0, 1, 2, 8, 9, 5, 6, 7 },
        { 4, 0, 1, 2, 3, 9, 5, 6, 7, 8 }, { 5, 9, 8, 7, 6, 0, 4, 3, 2, 1 }, { 6, 5, 9, 8, 7, 1, 0, 4, 3, 2 }, { 7, 6, 5, 9, 8, 2, 1, 0, 4, 3 },
        { 8, 7, 6, 5, 9, 3, 2, 1, 0, 4 }, { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 },
    };

    static readonly int[,] VerhoeffP =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, { 1, 5, 7, 6, 2, 8, 3, 0, 9, 4 }, { 5, 8, 0, 3, 7, 9, 6, 1, 4, 2 }, { 8, 9, 1, 6, 0, 4, 3, 5, 2, 7 },
        { 9, 4, 5, 3, 1, 2, 6, 8, 7, 0 }, { 4, 2, 8, 6, 5, 7, 3, 9, 0, 1 }, { 2, 7, 9, 3, 8, 0, 6, 4, 1, 5 }, { 7, 0, 4, 6, 9, 1, 3, 2, 5, 8 },
    };

    /// <summary>Aadhaar numbers end in a Verhoeff check digit.</summary>
    static bool Verhoeff(string value)
    {
        var digits = Digits(value);
        if (digits.Length != 12) return false;
        int c = 0;
        for (int i = 0; i < digits.Length; i++)
            c = VerhoeffD[c, VerhoeffP[i % 8, digits[digits.Length - 1 - i] - '0']];
        return c == 0;
    }
}
