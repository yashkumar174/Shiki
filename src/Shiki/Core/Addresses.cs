using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shiki.Core;

public enum AddressKind { Phone, Email }

/// <summary>
/// A WhatsApp number or an email address the person types (or says) for someone new: checked,
/// and put the way WhatsApp and Gmail want it. India first: "98765 43210" is +91.
/// </summary>
public static class Addresses
{
    /// <summary>What goes in the box to start with.</summary>
    public static string Start(AddressKind kind) => kind == AddressKind.Phone ? "+91 " : "";

    /// <summary>The address ready to use (a phone as digits with the country code), or null if it isn't one yet.</summary>
    public static string? Clean(AddressKind kind, string typed) => kind == AddressKind.Phone ? Phone(typed) : Email(typed);

    /// <summary>A short line under the box saying what's missing, or null when it's fine.</summary>
    public static string? Problem(AddressKind kind, string typed)
    {
        if (Clean(kind, typed) != null) return null;
        if (kind == AddressKind.Email)
            return typed.Trim().Length == 0 ? "Like name@gmail.com" : "That isn't a full email address yet (like name@gmail.com)";
        var digits = Digits(typed);
        bool plus = typed.TrimStart().StartsWith('+');
        if (plus && digits.Length > 2 && !digits.StartsWith("91")) return "That doesn't look like a phone number";
        // What comes after +91 (or all of it, typed without the country code).
        int local = plus ? Math.Max(0, digits.Length - 2) : digits.TrimStart('0').Length;
        if (local == 0) return "The 10-digit mobile number";
        if (local < 10) return $"{local} digit{(local == 1 ? "" : "s")} so far: a mobile number has 10";
        if (local == 10) return "Indian mobile numbers start with 6, 7, 8 or 9";
        return "That's more than 10 digits";
    }

    static string Digits(string typed) => new(typed.Where(char.IsAsciiDigit).ToArray());

    /// <summary>Digits with the country code (919876543210), or null.</summary>
    public static string? Phone(string typed)
    {
        var digits = Digits(typed);
        bool plus = typed.TrimStart().StartsWith('+');
        // "+91 098765…": the trunk zero doesn't belong after the country code.
        if (plus && digits.StartsWith("910") && digits.Length == 13) digits = "91" + digits[3..];
        if (!plus && digits.Length == 11 && digits[0] == '0') digits = "91" + digits[1..];
        else if (!plus && digits.Length == 10) digits = "91" + digits;
        if (digits.StartsWith("91") && (plus || digits.Length == 12))
            return digits.Length == 12 && digits[2] is >= '6' and <= '9' ? digits : null;
        return plus && digits.Length is >= 8 and <= 15 ? digits : null;
    }

    public static string? Email(string typed)
    {
        var email = typed.Trim().Trim('<', '>', '.', ',').ToLowerInvariant();
        return Regex.IsMatch(email, @"^[a-z0-9._%+\-']+@[a-z0-9\-]+(\.[a-z0-9\-]+)*\.[a-z]{2,}$") ? email : null;
    }

    static readonly string[] Numbers = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    /// <summary>
    /// An address in something said aloud ("nine eight seven…", "98765 43210", "tausif dot ansari at
    /// gmail dot com"), as it would be typed; null when there isn't one.
    /// </summary>
    public static string? FromSpeech(AddressKind kind, string said)
    {
        var text = said.ToLowerInvariant().Trim().TrimEnd('.', '!', '?');
        if (kind == AddressKind.Phone)
        {
            // Number words become digits ("double nine" is 99); everything else but digits and + goes.
            var words = Regex.Split(text, @"[\s,\-]+").Where(w => w.Length > 0).ToList();
            var built = new System.Text.StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                int repeat = w is "double" ? 2 : w is "triple" ? 3 : 1;
                if (repeat > 1 && i + 1 < words.Count) w = words[++i];
                var digit = w == "oh" ? 0 : Array.IndexOf(Numbers, w);
                var piece = digit >= 0 ? digit.ToString() : new string(w.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
                for (int r = 0; r < repeat; r++) built.Append(piece);
            }
            var typed = built.ToString();
            // Said with the country code ("plus nine one…") or without: either way it's checked as typed.
            if (text.Contains("plus") && !typed.StartsWith('+')) typed = "+" + typed;
            // A few digits already count (cut off mid-number): they go in the box, which says what's missing.
            return Digits(typed).Length >= 3 ? typed : null;
        }
        if (Email(text) is { } plain) return plain;
        var spoken = Regex.Replace(" " + text + " ", @"\s+(at the rate( of)?|at)\s+", "@");
        spoken = Regex.Replace(spoken, @"\s+(dot|point)\s+", ".");
        spoken = Regex.Replace(spoken, @"\s+underscore\s+", "_");
        spoken = Regex.Replace(spoken, @"\s+(dash|hyphen)\s+", "-");
        spoken = Regex.Replace(spoken, @"\s+", "");
        return Email(spoken);
    }
}
