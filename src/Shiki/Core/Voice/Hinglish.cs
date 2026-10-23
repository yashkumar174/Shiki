using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Shiki.Core.Voice;

/// <summary>
/// Hindi and Hinglish for the quick commands: Devanagari written the Roman way people type it
/// ("आवाज़ कम करो" → "aawaaz kam karo"), and Hinglish commands put into the English the quick
/// commands know ("awaaz kam karo" → "volume down", "chrome kholo" → "open chrome").
/// </summary>
public static class Hinglish
{
    static readonly Dictionary<char, string> Consonants = new()
    {
        ['क'] = "k", ['ख'] = "kh", ['ग'] = "g", ['घ'] = "gh", ['ङ'] = "n", ['च'] = "ch", ['छ'] = "chh", ['ज'] = "j", ['झ'] = "jh", ['ञ'] = "n",
        ['ट'] = "t", ['ठ'] = "th", ['ड'] = "d", ['ढ'] = "dh", ['ण'] = "n", ['त'] = "t", ['थ'] = "th", ['द'] = "d", ['ध'] = "dh", ['न'] = "n",
        ['प'] = "p", ['फ'] = "ph", ['ब'] = "b", ['भ'] = "bh", ['म'] = "m", ['य'] = "y", ['र'] = "r", ['ल'] = "l", ['व'] = "v", ['श'] = "sh",
        ['ष'] = "sh", ['स'] = "s", ['ह'] = "h", ['ळ'] = "l", ['\u0958'] = "q", ['\u0959'] = "kh", ['\u095A'] = "gh", ['\u095B'] = "z", ['\u095C'] = "r", ['\u095D'] = "rh", ['\u095E'] = "f", ['\u095F'] = "y",
    };

    // With a nukta written as a separate mark: क + ़ and the like.
    static readonly Dictionary<char, string> Nukta = new() { ['क'] = "q", ['ख'] = "kh", ['ग'] = "gh", ['ज'] = "z", ['ड'] = "r", ['ढ'] = "rh", ['फ'] = "f" };

    static readonly Dictionary<char, string> Vowels = new()
    {
        ['अ'] = "a", ['आ'] = "aa", ['इ'] = "i", ['ई'] = "ee", ['उ'] = "u", ['ऊ'] = "oo", ['ऋ'] = "ri", ['ए'] = "e", ['ऐ'] = "ai", ['ओ'] = "o", ['औ'] = "au", ['ऑ'] = "o",
    };

    static readonly Dictionary<char, string> Signs = new()
    {
        ['ा'] = "aa", ['ि'] = "i", ['ी'] = "ee", ['ु'] = "u", ['ू'] = "oo", ['ृ'] = "ri", ['े'] = "e", ['ै'] = "ai", ['ो'] = "o", ['ौ'] = "au", ['ॉ'] = "o",
    };

    public static bool HasDevanagari(string text) => text.Any(c => c is >= 'ऀ' and <= 'ॿ');

    /// <summary>Devanagari in Roman letters, the way Hinglish is usually typed; everything else is left alone.</summary>
    public static string ToRoman(string text)
    {
        if (!HasDevanagari(text)) return text;
        return Regex.Replace(text, @"[ऀ-ॿ]+", m => Word(m.Value));
    }

    // One syllable: its consonants (maybe none) and its vowel; Schwa is the "a" every consonant carries until something else follows.
    sealed class Syllable
    {
        public string Consonant = "";
        public string Vowel = "";
        public bool Schwa;
        public string After = "";
    }

    // The everyday words, spelled the way people type them (the rules alone give "naheen", "kyaa").
    static readonly Dictionary<string, string> Common = new()
    {
        ["है"] = "hai", ["हैं"] = "hain", ["में"] = "mein", ["मैं"] = "main", ["नहीं"] = "nahi", ["नही"] = "nahi", ["क्या"] = "kya", ["यह"] = "yeh", ["ये"] = "ye", ["वह"] = "woh", ["वो"] = "woh", ["और"] = "aur", ["तो"] = "toh", ["हाँ"] = "haan", ["हां"] = "haan", ["कि"] = "ki", ["की"] = "ki", ["के"] = "ke", ["का"] = "ka", ["को"] = "ko", ["से"] = "se", ["पर"] = "par", ["भी"] = "bhi", ["ही"] = "hi", ["था"] = "tha", ["थी"] = "thi", ["थे"] = "the", ["हूँ"] = "hoon", ["हूं"] = "hoon", ["हो"] = "ho", ["कर"] = "kar", ["करो"] = "karo", ["करना"] = "karna", ["करके"] = "karke", ["ठीक"] = "theek", ["अच्छा"] = "accha", ["बहुत"] = "bahut", ["मुझे"] = "mujhe", ["आप"] = "aap", ["तुम"] = "tum", ["मेरा"] = "mera", ["मेरी"] = "meri", ["मेरे"] = "mere", ["तुम्हारा"] = "tumhara", ["आपका"] = "aapka", ["क्यों"] = "kyun", ["कैसे"] = "kaise", ["कैसा"] = "kaisa", ["कब"] = "kab", ["कहाँ"] = "kahan", ["कहां"] = "kahan", ["अभी"] = "abhi", ["आज"] = "aaj", ["कल"] = "kal", ["भाई"] = "bhai", ["यार"] = "yaar", ["चलो"] = "chalo", ["ज़रा"] = "zara", ["जरा"] = "zara", ["सब"] = "sab", ["कुछ"] = "kuch", ["एक"] = "ek", ["दो"] = "do", ["वाला"] = "wala", ["वाली"] = "wali", ["वाले"] = "wale", ["आवाज़"] = "awaaz", ["आवाज"] = "awaaz", ["खोलो"] = "kholo", ["बंद"] = "band", ["चालू"] = "chalu", ["कम"] = "kam", ["ज़्यादा"] = "zyada", ["ज्यादा"] = "zyada", ["थोड़ा"] = "thoda", ["बताओ"] = "batao", ["बता"] = "bata", ["दे"] = "de", ["देना"] = "dena", ["लो"] = "lo", ["जाओ"] = "jao", ["शुरू"] = "shuru", ["मतलब"] = "matlab", ["प्लीज़"] = "please", ["प्लीज"] = "please", ["धन्यवाद"] = "dhanyavaad", ["शुक्रिया"] = "shukriya", ["बस"] = "bas", ["चाहिए"] = "chahiye", ["सकते"] = "sakte", ["सकता"] = "sakta", ["सकती"] = "sakti", ["रहा"] = "raha", ["रही"] = "rahi", ["रहे"] = "rahe", ["गया"] = "gaya", ["गई"] = "gayi", ["होगा"] = "hoga", ["वाट्सएप"] = "WhatsApp", ["व्हाट्सएप"] = "WhatsApp", ["क्रोम"] = "Chrome", ["जीमेल"] = "Gmail", ["यूट्यूब"] = "YouTube", ["स्पॉटिफाई"] = "Spotify",
    };

    static string Word(string word)
    {
        if (Common.TryGetValue(word, out var known)) return known;
        var syllables = new List<Syllable>();
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if (Consonants.TryGetValue(c, out var consonant))
            {
                if (i + 1 < word.Length && word[i + 1] == '़' && Nukta.TryGetValue(c, out var withDot)) { consonant = withDot; i++; }
                syllables.Add(new Syllable { Consonant = consonant, Vowel = "a", Schwa = true });
            }
            else if (Vowels.TryGetValue(c, out var vowel)) syllables.Add(new Syllable { Vowel = vowel });
            else if (Signs.TryGetValue(c, out var sign) && syllables.Count > 0) { syllables[^1].Vowel = sign; syllables[^1].Schwa = false; }
            else if (c == '्' && syllables.Count > 0) { syllables[^1].Vowel = ""; syllables[^1].Schwa = false; } // virama: no vowel
            else if (c is 'ं' or 'ँ' && syllables.Count > 0) syllables[^1].After += "n"; // anusvara, chandrabindu
            else if (c == 'ः' && syllables.Count > 0) syllables[^1].After += "h";
            else if (c is >= '०' and <= '९') syllables.Add(new Syllable { After = ((char)('0' + c - '०')).ToString() });
            else if (c is '।' or '॥') syllables.Add(new Syllable { After = "." });
        }
        // Hindi drops the last schwa ("कम" is "kam", not "kama"), and one between a vowel and a consonant
        // that has a vowel of its own ("करना" is "karnaa").
        if (syllables.Count > 1 && syllables[^1].Schwa && syllables[^1].After.Length == 0) syllables[^1].Vowel = "";
        for (int i = 1; i + 1 < syllables.Count; i++)
        {
            var s = syllables[i];
            if (s.Schwa && s.After.Length == 0 && syllables[i - 1].Vowel.Length > 0 && syllables[i + 1].Consonant.Length > 0 && syllables[i + 1].Vowel.Length > 0 && !syllables[i + 1].Schwa)
            {
                s.Vowel = "";
                i++; // never two in a row
            }
        }
        var roman = new StringBuilder();
        for (int i = 0; i < syllables.Count; i++)
        {
            var s = syllables[i];
            var after = s.After;
            // An anusvara before p, b or m sounds like m ("संबंध" → "sambandh").
            if (after.StartsWith('n') && i + 1 < syllables.Count && syllables[i + 1].Consonant is "p" or "ph" or "b" or "bh" or "m") after = "m" + after[1..];
            roman.Append(s.Consonant).Append(s.Vowel).Append(after);
        }
        var result = roman.ToString();
        // व is usually written "w" in Hinglish ("awaaz", "hawa"), but "v" before i and e ("vivek", "ved").
        result = System.Text.RegularExpressions.Regex.Replace(result, "v(?![ie])", "w");
        // Typed Hinglish ends words in "a" and "i", not "aa" and "ee" ("karna", "nahi").
        if (result.Length > 3 && result.EndsWith("aa")) result = result[..^1];
        if (result.Length > 3 && result.EndsWith("ee")) result = result[..^2] + "i";
        if (result.Length > 4 && result.EndsWith("een")) result = result[..^3] + "in"; // "nahin", not "naheen"
        return result;
    }

    // App names as they come out of Devanagari ("क्रोम" is "krom").
    static readonly Dictionary<string, string> AppNames = new()
    {
        ["krom"] = "chrome", ["kroam"] = "chrome", ["gugal krom"] = "chrome", ["spotiphai"] = "spotify", ["spotiphaai"] = "spotify", ["spotifai"] = "spotify",
        ["notaipaid"] = "notepad", ["notpaid"] = "notepad", ["notped"] = "notepad", ["vard"] = "word", ["eksel"] = "excel", ["ekasel"] = "excel",
        ["kailkuletar"] = "calculator", ["kailkulettar"] = "calculator", ["vhaatsaep"] = "whatsapp", ["vhatsaep"] = "whatsapp", ["vhaatsep"] = "whatsapp",
        ["yootyoob"] = "youtube", ["yutyub"] = "youtube", ["paint"] = "paint", ["pent"] = "paint", ["setings"] = "settings", ["seting"] = "settings",
        ["kaimra"] = "camera", ["kaimara"] = "camera", ["teems"] = "teams", ["zoom"] = "zoom", ["jum"] = "zoom",
    };

    // Hinglish ways of saying what the quick commands know, longest first.
    static readonly (string Pattern, string English)[] Phrases =
    [
        (@"\b(aa?waa?z|aawaaz|avaaz|aavaaz|sound|volume)\s+(band|bandh|off|mute)(\s+(karo|kar do|kar dijiye|kardo))?\b", "mute"),
        (@"\b(aa?waa?z|aawaaz|avaaz|aavaaz|sound|volume)\s+(chalu|on|wapas|unmute)(\s+(karo|kar do|kardo))?\b", "unmute"),
        (@"\bmute\s+(karo|kar do|kardo)\b", "mute"),
        (@"\bunmute\s+(karo|kar do|kardo)\b", "unmute"),
        (@"\b(aa?waa?z|aawaaz|avaaz|aavaaz|sound|volume)\s+(thoda\s+)?(badhao|badha do|badhaao|tez karo|tez kar do|zyada karo|jyada karo|upar karo|up karo|badhaiye)\b", "volume up"),
        (@"\b(aa?waa?z|aawaaz|avaaz|aavaaz|sound|volume)\s+(thoda\s+)?(kam karo|kam kar do|kam kijiye|ghatao|dheere karo|dheemi karo|neeche karo|down karo)\b", "volume down"),
        (@"\b(roshni|brightness|light)\s+(thoda\s+)?(badhao|badha do|tez karo|zyada karo|jyada karo|up karo)\b", "brightness up"),
        (@"\b(roshni|brightness|light)\s+(thoda\s+)?(kam karo|kam kar do|ghatao|dheemi karo|down karo)\b", "brightness down"),
        (@"\bdark mode\s+(on|chalu|laga do|lagao)(\s+(karo|kar do))?\b", "dark mode on"),
        (@"\bdark mode\s+(off|band|hata do|hatao)(\s+(karo|kar do))?\b", "dark mode off"),
        (@"\b(do not disturb|dnd)\s+(on|chalu|laga do|lagao)(\s+(karo|kar do))?\b", "do not disturb on"),
        (@"\b(do not disturb|dnd)\s+(off|band|hata do|hatao)(\s+(karo|kar do))?\b", "do not disturb off"),
        (@"\b(captions?|subtitles?)\s+(on|chalu|dikhao|laga do)(\s+(karo|kar do))?\b", "captions on"),
        (@"\b(captions?|subtitles?)\s+(off|band|hata do|hatao)(\s+(karo|kar do))?\b", "captions off"),
        (@"\b(notes?|nots?)\s+(lo|le lo|lena shuru karo|lena shuroo karo|likho|banao|start karo|shuru karo|shuroo karo)\b", "take notes"),
        (@"\b(notes?|nots?)\s+(band karo|band kar do|bas karo|stop karo|rok do)\b", "stop notes"),
        (@"\brecording\s+(band karo|band kar do|rok do|stop karo)\b", "stop recording"),
        (@"\b(gaana|gana|song|music|video)\s+(roko|rok do|band karo|pause karo|ruko)\b", "pause music"),
        (@"\b(gaana|gana|song|music|video)\s+(chalao|chala do|bajao|baja do|play karo|shuru karo)\b", "play music"),
        (@"\b(agla|next|dusra|doosra)\s+(gaana|gana|song)\b|\bagla\s+(chalao|lagao)\b", "next song"),
        (@"\b(pichla|previous|pehle wala)\s+(gaana|gana|song)\b", "previous song"),
        (@"^(ruko|ruk jao|bas|bas karo|chup|chup karo|chup ho jao|band karo|rehne do)$", "stop"),
    ];

    /// <summary>A Hinglish (or Hindi) command in the English the quick commands know; unchanged when it isn't one.</summary>
    public static string ToCommand(string said)
    {
        var text = ToRoman(said).ToLowerInvariant();
        text = string.Join(" ", Regex.Replace(text, @"[^a-z0-9 ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        // Sound-alike spellings speech recognition writes for Hinglish words.
        text = Regex.Replace(text, @"\b(caro|kro|karoo|carro)\b", "karo");
        text = Regex.Replace(text, @"\b(colo|kolo|khulo|kholoo)\b", "kholo");
        text = Regex.Replace(text, @"\b(awas|awaz|awaj|avaz|avaj|awaas|aawaz|avaaj)\b", "awaaz");
        text = Regex.Replace(text, @"\b(bund|bandh)\b", "band");
        text = Regex.Replace(text, @"\bawaaz (cum|come|calm)\b", "awaaz kam");
        // "volume 40 karo", "volume chalis pe kar do"
        text = Regex.Replace(text, @"\b(das|bees|tees|chalis|chaalis|pachas|pachaas|saath|sattar|assi|nabbe|sau)\b", m => m.Value switch
        {
            "das" => "10", "bees" => "20", "tees" => "30", "chalis" or "chaalis" => "40", "pachas" or "pachaas" => "50",
            "saath" => "60", "sattar" => "70", "assi" => "80", "nabbe" => "90", _ => "100",
        });
        var number = Regex.Match(text, @"^(aa?waa?z|aawaaz|avaaz|aavaaz|sound|volume|roshni|brightness)\s+(\d{1,3})\s*(pe|par|tak)?\s*(karo|kar do|kardo|set karo|rakho|kar dijiye)?$");
        if (number.Success) return $"{(number.Groups[1].Value is "roshni" or "brightness" ? "brightness" : "volume")} {number.Groups[2].Value}";
        // "chrome kholo", "spotify chala do", "notepad open karo"
        var open = Regex.Match(text, @"^(?<app>.+?)\s+(kholo|khol do|kholiye|khol dijiye|open karo|open kar do|chalao|chalaao|chala do|chalaa do|start karo)$");
        if (open.Success) return $"open {AppNames.GetValueOrDefault(open.Groups["app"].Value, open.Groups["app"].Value)}";
        foreach (var (pattern, english) in Phrases)
            if (Regex.IsMatch(text, pattern)) return english;
        return said;
    }

    static readonly string[] Yes = ["haan", "han", "haa", "ha", "haan ji", "ji haan", "ji", "theek hai", "thik hai", "kar do", "karo", "chalo", "haan kar do", "bilkul", "ok kar do"];
    static readonly string[] No = ["nahi", "nahin", "na", "mat karo", "rehne do", "ruko", "nahi chahiye", "abhi nahi", "cancel karo"];
    static readonly string[] Thanks = ["shukriya", "dhanyavaad", "dhanyawad", "bas", "bas itna hi", "theek hai bas", "thik hai bas", "ho gaya", "bas ho gaya"];

    /// <summary>A Hinglish yes or no to a card, or null.</summary>
    public static bool? Answer(string said)
    {
        var text = string.Join(" ", Regex.Replace(ToRoman(said).ToLowerInvariant(), @"[^a-z ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (No.Contains(text)) return false;
        if (Yes.Contains(text)) return true;
        return null;
    }

    /// <summary>"Shukriya", "bas": the conversation is over.</summary>
    public static bool IsGoodbye(string said)
    {
        var text = string.Join(" ", Regex.Replace(ToRoman(said).ToLowerInvariant(), @"[^a-z ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Thanks.Contains(text);
    }
}
