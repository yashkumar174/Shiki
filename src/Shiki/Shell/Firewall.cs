using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shiki.Core;

namespace Shiki.Shell;

/// <summary>
/// Whether Windows Firewall lets the phone in to Shiki, and a way to let it in. Reading the rules
/// needs no permission; adding the rule asks Windows (one "Yes" on its prompt).
/// </summary>
public static class Firewall
{
    public enum Verdict { Open, Closed, Unknown }

    /// <summary>What the check found, and whether the network the PC is on now is one Windows calls public.</summary>
    public readonly record struct Check(Verdict Verdict, bool PublicNetwork, string Detail);

    const string RuleName = "Shiki phone link";
    /// <summary>The port the iPhone talks to (the Android link's ports go with the program rule too).</summary>
    const int PhonePort = 47814;

    // NET_FW_PROFILE_TYPE2: domain, private, public.
    static readonly (int Bit, string Name)[] Profiles = [(1, "domain"), (2, "private"), (4, "public")];

    static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Shiki.exe");

    /// <summary>Is there a way in for the phone on every network the PC is on now? Takes a moment: call it off the UI thread.</summary>
    public static Check Look()
    {
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type == null) return new(Verdict.Unknown, false, "no firewall policy");
            dynamic policy = Activator.CreateInstance(type)!;
            // Only the network the phone comes in on matters (not VirtualBox's or WSL's, which Windows calls public).
            int active = HomeProfile() ?? (int)policy.CurrentProfileTypes;
            bool isPublic = (active & 4) != 0;
            string me = Normal(Exe);

            int openFor = 0, closedFor = 0;
            foreach (var (bit, _) in Profiles)
            {
                if ((active & bit) == 0) continue;
                // Firewall off, or letting everything in by default: nothing to do on this one.
                if (!(bool)policy.FirewallEnabled[bit] || (int)policy.DefaultInboundAction[bit] == 1) openFor |= bit;
            }

            foreach (dynamic rule in policy.Rules)
            {
                if (!(bool)rule.Enabled || (int)rule.Direction != 1) continue; // inbound only
                int profiles = (int)rule.Profiles;
                if ((profiles & active) == 0) continue;
                string? program = rule.ApplicationName as string;
                bool mine = !string.IsNullOrEmpty(program)
                    ? string.Equals(Normal(program), me, StringComparison.OrdinalIgnoreCase)
                    : CoversPort((int)rule.Protocol, rule.LocalPorts as string);
                if (!mine) continue;
                if ((int)rule.Action == 0) closedFor |= profiles & active; // a block wins over any allow
                else openFor |= profiles & active;
            }

            var shut = Profiles.Where(p => (active & p.Bit) != 0 && ((closedFor & p.Bit) != 0 || (openFor & p.Bit) == 0)).Select(p => p.Name).ToList();
            var detail = $"on {string.Join("+", Profiles.Where(p => (active & p.Bit) != 0).Select(p => p.Name))} network; {(shut.Count == 0 ? "Shiki is let in" : $"no way in on {string.Join("+", shut)}")} ({me})";
            return new(shut.Count == 0 ? Verdict.Open : Verdict.Closed, isPublic, detail);
        }
        catch (Exception ex)
        {
            Log.Error("Checking the firewall for the phone link", ex);
            return new(Verdict.Unknown, false, ex.Message);
        }
    }

    /// <summary>
    /// Asks Windows to let Shiki in on private networks (and on public ones too when the PC is on
    /// one now). Null when it worked; otherwise what to tell the person.
    /// </summary>
    public static async Task<string?> LetIn(bool includePublic)
    {
        var profiles = includePublic ? "private,public" : "private";
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"),
            $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{Exe}\" enable=yes profile={profiles}")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = Process.Start(start);
            if (process == null) return "Windows didn't start it. Try once more.";
            await process.WaitForExitAsync();
            Log.Info($"Firewall rule for the phone link ({profiles}): exit {process.ExitCode}");
            return process.ExitCode == 0 ? null : "Windows couldn't add it. Try once more.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Windows didn't allow it. Try again and press Yes when it asks.";
        }
        catch (Exception ex)
        {
            Log.Error("Letting the phone in through the firewall", ex);
            return "That didn't work. Try once more.";
        }
    }

    /// <summary>
    /// The firewall profile (as a NET_FW_PROFILE_TYPE2 bit) of the network this PC's home address
    /// is on, which is the one the phone reaches it through. Null when it can't tell.
    /// </summary>
    static int? HomeProfile()
    {
        try
        {
            var ip = Core.Phone.PhoneLink.LocalAddress();
            if (ip == null) return null;
            var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == ip));
            var index = nic?.GetIPProperties().GetIPv4Properties()?.Index;
            if (index == null) return null;
            using var search = new System.Management.ManagementObjectSearcher(@"root\StandardCimv2",
                $"SELECT NetworkCategory FROM MSFT_NetConnectionProfile WHERE InterfaceIndex = {index}");
            foreach (var profile in search.Get())
            {
                // NetworkCategory: 0 public, 1 private, 2 domain.
                return Convert.ToInt32(profile["NetworkCategory"]) switch { 1 => 2, 2 => 1, _ => 4 };
            }
        }
        catch (Exception ex) { Log.Error("Finding which network the phone comes in on", ex); }
        return null;
    }

    static string Normal(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))); }
        catch { return path; }
    }

    /// <summary>A rule with no program: does it open the phone's port for TCP?</summary>
    static bool CoversPort(int protocol, string? ports)
    {
        if (protocol is not (6 or 256)) return false; // TCP, or any
        if (string.IsNullOrWhiteSpace(ports)) return false;
        foreach (var part in ports.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "*") return true;
            var ends = part.Split('-');
            if (ends.Length == 1 && int.TryParse(ends[0], out var one) && one == PhonePort) return true;
            if (ends.Length == 2 && int.TryParse(ends[0], out var lo) && int.TryParse(ends[1], out var hi) && lo <= PhonePort && PhonePort <= hi) return true;
        }
        return false;
    }
}
