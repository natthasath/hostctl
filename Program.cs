using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

internal class Program
{
    private static readonly string HostsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
    private static readonly string MetaDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "hostctl");
    private static readonly string MetaFile = Path.Combine(MetaDir, "hosts.tags.json");

    private const string Esc = "";
    private static readonly bool AnsiEnabled = EnableAnsi();

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* best effort; keep system default if unsupported */ }

        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var first = args[0];
        if (first == "-h" || first.Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }
        if (first == "-V" || first.Equals("--version", StringComparison.OrdinalIgnoreCase))
        {
            PrintVersion();
            return 0;
        }
        if (first.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1) return PrintCommandHelp(args[1]);
            PrintHelp();
            return 0;
        }

        var cmd = first.ToLowerInvariant();
        var rest = args.Skip(1).Select(a => a == "-y" ? "--yes" : a).ToArray();

        if (HasFlag(rest, "-h", "--help"))
            return PrintCommandHelp(cmd);
        if (HasFlag(rest, "-V", "--version"))
        {
            PrintVersion();
            return 0;
        }

        var opt = ParseOptions(rest);

        try
        {
            switch (cmd)
            {
                case "list":
                    DoList(opt);
                    break;
                case "add":
                    DoAdd(opt);
                    break;
                case "edit":
                    DoEdit(opt);
                    break;
                case "remove":
                    DoRemove(opt);
                    break;
                case "tags":
                    DoTags(opt);
                    break;
                case "backup":
                    DoBackup();
                    break;
                default:
                    Console.Error.WriteLine($"Unknown command: {cmd}");
                    Console.Error.WriteLine("Run 'hostctl --help' for usage.");
                    return 2;
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("✖ " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("✖ " + ex.Message);
            return 1;
        }

        return 0;
    }

    // ---------------- Help ----------------

    private static int PrintCommandHelp(string cmd)
    {
        switch (cmd.ToLowerInvariant())
        {
            case "list": PrintListHelp(); return 0;
            case "add": PrintAddHelp(); return 0;
            case "edit": PrintEditHelp(); return 0;
            case "remove": PrintRemoveHelp(); return 0;
            case "tags": PrintTagsHelp(); return 0;
            case "backup": PrintBackupHelp(); return 0;
            default:
                Console.Error.WriteLine($"Unknown command: {cmd}");
                Console.Error.WriteLine("Run 'hostctl --help' for usage.");
                return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("hostctl CLI");
        Console.WriteLine();
        Console.WriteLine(Underline("Usage:"));
        Console.WriteLine("hostctl [OPTIONS]");
        Console.WriteLine("hostctl <COMMAND> [ARGS]");
        Console.WriteLine();
        Console.WriteLine(Underline("Commands:"));
        Console.WriteLine("  list              List host entries");
        Console.WriteLine("  add               Add a new host entry");
        Console.WriteLine("  edit              Edit an existing host entry");
        Console.WriteLine("  remove            Remove a host entry");
        Console.WriteLine("  tags              List all tags currently in use");
        Console.WriteLine("  backup            Create a timestamped backup of the hosts file");
        Console.WriteLine("  help              Print this message or the help of the given subcommand(s)");
        Console.WriteLine();
        Console.WriteLine(Underline("Options:"));
        PrintHelpVersionOptions();
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  add, edit, remove, and backup require Administrator privileges (they write to the hosts file).");
        Console.WriteLine("  Every write creates a timestamped backup and prints its path: <hosts>.bak_yyyyMMdd_HHmmss");
        Console.WriteLine("  Exit codes: 0 = success, 1 = error, 2 = invalid usage/arguments.");
        Console.WriteLine($"  Tags metadata: {MetaFile}");
        Console.WriteLine($"  Hosts file    : {HostsPath}");
    }

    private static void PrintListHelp()
    {
        Console.WriteLine("List host entries");
        Console.WriteLine();
        Console.WriteLine(Underline("Usage:"));
        Console.WriteLine("hostctl list [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine(Underline("Options:"));
        PrintOptionLine("--tag <NAME>", "Only show entries that have this tag");
        PrintOptionLine("--all", "Include disabled (commented-out) entries");
        PrintOptionLine("--sort <KEY>", "Sort the table by a column", "[possible values: ip, name, tag]");
        PrintOptionLine("--desc", "Reverse the sort order");
        PrintOptionLine("--json", "Print machine-readable JSON instead of a table");
        PrintHelpVersionOptions();
    }

    private static void PrintAddHelp()
    {
        Console.WriteLine("Add a new host entry");
        Console.WriteLine();
        Console.WriteLine(Underline("Usage:"));
        Console.WriteLine("hostctl add --ip <IP> --host <HOSTNAME> [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine(Underline("Options:"));
        PrintOptionLine("--ip <IP>", "IP address for the new entry (required)");
        PrintOptionLine("--host <HOSTNAME>", "Hostname to add; comma-separate to add multiple at the same IP (required)");
        PrintOptionLine("--tag <TAGS>", "Comma-separated tags to attach, e.g. web,dev");
        PrintOptionLine("--comment <TEXT>", "Trailing comment to attach to the entry");
        PrintOptionLine("--dry-run", "Show what would be added without writing to the hosts file");
        PrintHelpVersionOptions();
    }

    private static void PrintEditHelp()
    {
        Console.WriteLine("Edit an existing host entry");
        Console.WriteLine();
        Console.WriteLine(Underline("Usage:"));
        Console.WriteLine("hostctl edit --host <HOSTNAME> [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine(Underline("Options:"));
        PrintOptionLine("--host <HOSTNAME>", "Existing hostname to edit (required)");
        PrintOptionLine("--ip <IP>", "New IP address");
        PrintOptionLine("--rename <HOSTNAME>", "New hostname");
        PrintOptionLine("--tag <OPS>", "Tag changes, e.g. \"+web,-old\"");
        PrintOptionLine("--dry-run", "Show what would change without writing to the hosts file");
        PrintHelpVersionOptions();
    }

    private static void PrintRemoveHelp()
    {
        Console.WriteLine("Remove a host entry");
        Console.WriteLine();
        Console.WriteLine(Underline("Usage:"));
        Console.WriteLine("hostctl remove --host <HOSTNAME> [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine(Underline("Options:"));
        PrintOptionLine("--host <HOSTNAME>", "Hostname to remove (required)");
        PrintOptionLine("-y, --yes", "Skip the confirmation prompt (required when stdin is not a terminal)");
        PrintOptionLine("--dry-run", "Show what would be removed without writing to the hosts file");
        PrintHelpVersionOptions();
    }

    private static void PrintTagsHelp()
    {
        Console.WriteLine("List all tags currently in use");
        Console.WriteLine();
        Console.WriteLine(Underline("Usage:"));
        Console.WriteLine("hostctl tags [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine(Underline("Options:"));
        PrintOptionLine("--json", "Print machine-readable JSON instead of plain text");
        PrintHelpVersionOptions();
    }

    private static void PrintBackupHelp()
    {
        Console.WriteLine("Create a timestamped backup of the hosts file");
        Console.WriteLine();
        Console.WriteLine(Underline("Usage:"));
        Console.WriteLine("hostctl backup [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine(Underline("Options:"));
        PrintHelpVersionOptions();
    }

    private static void PrintOptionLine(string flag, string description, string? possibleValues = null)
    {
        var indent = flag.StartsWith("--") ? "      " : "  ";
        Console.WriteLine($"{indent}{flag}");
        Console.WriteLine($"          {description}");
        if (possibleValues != null)
        {
            Console.WriteLine();
            Console.WriteLine($"          {possibleValues}");
        }
        Console.WriteLine();
    }

    private static void PrintHelpVersionOptions()
    {
        Console.WriteLine("  -h, --help");
        Console.WriteLine("          Print help (see a summary with '-h')");
        Console.WriteLine();
        Console.WriteLine("  -V, --version");
        Console.WriteLine("          Print version");
    }

    private static void PrintVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var fileVer = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        var asmVer = asm.GetName().Version?.ToString();
        var ver = infoVer ?? fileVer ?? asmVer ?? "0.0.0";
        Console.WriteLine(ver);
    }

    // ---------------- ANSI underline ----------------

    private static string Underline(string s) => AnsiEnabled ? $"{Esc}[4m{s}{Esc}[0m" : s;

    private static bool EnableAnsi()
    {
        if (Console.IsOutputRedirected) return false;

        var handle = GetStdHandle(StdOutputHandle);
        if (!GetConsoleMode(handle, out var mode)) return false;
        if ((mode & EnableVirtualTerminalProcessing) != 0) return true;
        return SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    // ---------------- Commands ----------------

    private static void DoList(Dictionary<string, string?> opt)
    {
        var all = opt.ContainsKey("all");
        var tagFilter = Get(opt, "tag");
        var sortKey = Get(opt, "sort").ToLowerInvariant();
        var desc = opt.ContainsKey("desc");
        var json = opt.ContainsKey("json");

        if (sortKey.Length > 0 && sortKey != "ip" && sortKey != "name" && sortKey != "tag")
            throw new ArgumentException($"Invalid --sort value: {sortKey} (expected ip, name, or tag)");

        var lines = File.ReadAllLines(HostsPath);
        var tagsMap = LoadTags();

        var rows = new List<Row>();
        foreach (var (entry, _, disabled) in EnumerateHostEntries(lines))
        {
            if (entry == null) continue;
            if (disabled && !all) continue;

            if (!string.IsNullOrWhiteSpace(tagFilter))
            {
                if (!tagsMap.TryGetValue(entry.Hostname, out var entryTags) ||
                    !entryTags.Contains(tagFilter!, StringComparer.OrdinalIgnoreCase))
                    continue;
            }

            var tags = tagsMap.TryGetValue(entry.Hostname, out var ts)
                ? ts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();

            rows.Add(new Row(entry.Ip, entry.Hostname, tags, string.IsNullOrWhiteSpace(entry.Comment) ? "" : entry.Comment, disabled));
        }

        // Sorting
        if (sortKey.Length > 0)
        {
            if (sortKey == "ip")
            {
                rows = rows
                    .OrderBy(r => ParseIpSortKey(r.Ip), IpKeyComparer.Instance)
                    .ThenBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else if (sortKey == "name")
            {
                rows = rows
                    .OrderBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => ParseIpSortKey(r.Ip), IpKeyComparer.Instance)
                    .ToList();
            }
            else if (sortKey == "tag")
            {
                rows = rows
                    .OrderBy(r => r.Tags.Length == 0 ? "" : r.Tags[0], StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => ParseIpSortKey(r.Ip), IpKeyComparer.Instance)
                    .ToList();
            }
        }
        if (desc) rows.Reverse();

        if (json)
        {
            var data = rows.Select(r => new
            {
                ip = r.Ip,
                hostname = r.Host,
                tags = r.Tags,
                comment = r.Comment,
                disabled = r.Disabled
            });
            Console.WriteLine(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        RenderTable(rows);
    }

    private static void DoAdd(Dictionary<string, string?> opt)
    {
        var ip = Require(opt, "ip", "--ip is required");
        var hostCsv = Require(opt, "host", "--host is required");
        var comment = Get(opt, "comment");
        var tagCsv = Get(opt, "tag");
        var dryRun = opt.ContainsKey("dry-run");

        var hosts = hostCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var h in hosts)
        {
            if (IsHostPresent(h))
                throw new InvalidOperationException($"Hostname already exists: {h}");
        }

        var cmt = string.IsNullOrWhiteSpace(comment) ? "" : $"  # {comment}";
        var tags = string.IsNullOrWhiteSpace(tagCsv)
            ? Array.Empty<string>()
            : tagCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (dryRun)
        {
            Console.WriteLine("Dry run — no changes written.");
            foreach (var h in hosts)
                Console.WriteLine($"  + {ip} {h}{cmt}");
            if (tags.Length > 0)
                Console.WriteLine($"  + tags: {string.Join(",", tags)} -> {string.Join(",", hosts)}");
            return;
        }

        EnsureAdmin();

        var sb = new StringBuilder();
        foreach (var line in File.ReadAllLines(HostsPath))
            sb.AppendLine(line);
        foreach (var h in hosts)
            sb.AppendLine($"{ip} {h}{cmt}");

        BackupInternal();
        File.WriteAllText(HostsPath, sb.ToString());

        if (tags.Length > 0)
        {
            var map = LoadTags();
            foreach (var h in hosts)
            {
                if (!map.TryGetValue(h, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[h] = set;
                }
                foreach (var t in tags) set.Add(t);
            }
            SaveTags(map);
        }

        Console.WriteLine("Added.");
    }

    private static void DoEdit(Dictionary<string, string?> opt)
    {
        var host = Require(opt, "host", "--host (existing) is required");
        var newIp = Get(opt, "ip");
        var rename = Get(opt, "rename");
        var tagOps = Get(opt, "tag");
        var dryRun = opt.ContainsKey("dry-run");

        var lines = File.ReadAllLines(HostsPath).ToList();
        var idx = FindHostLineIndex(lines, host);
        if (idx < 0)
            throw new InvalidOperationException($"Host not found in hosts file: {host}");

        var (entry, _, _) = ParseEntry(lines[idx]);
        if (entry == null)
            throw new InvalidOperationException($"Cannot parse host line for: {host}");

        var ip = string.IsNullOrWhiteSpace(newIp) ? entry.Ip : newIp!;
        var toName = string.IsNullOrWhiteSpace(rename) ? entry.Hostname : rename!;
        var comment = entry.Comment;
        var newLine = $"{ip} {toName}" + (string.IsNullOrWhiteSpace(comment) ? "" : $"  # {comment}");

        if (dryRun)
        {
            Console.WriteLine("Dry run — no changes written.");
            Console.WriteLine($"  - {lines[idx]}");
            Console.WriteLine($"  + {newLine}");
            if (!string.IsNullOrWhiteSpace(tagOps))
                Console.WriteLine($"  ~ tags: {tagOps}");
            return;
        }

        EnsureAdmin();

        lines[idx] = newLine;

        BackupInternal();
        File.WriteAllLines(HostsPath, lines);

        if (!string.IsNullOrWhiteSpace(tagOps))
        {
            var map = LoadTags();
            if (!map.TryGetValue(toName, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[toName] = set;
            }
            foreach (var op in tagOps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (op.StartsWith("+")) set.Add(op[1..]);
                else if (op.StartsWith("-")) set.Remove(op[1..]);
                else set.Add(op);
            }
            if (!toName.Equals(host, StringComparison.OrdinalIgnoreCase) && map.ContainsKey(host))
            {
                foreach (var t in map[host]) set.Add(t);
                map.Remove(host);
            }
            SaveTags(map);
        }

        Console.WriteLine("Edited.");
    }

    private static void DoRemove(Dictionary<string, string?> opt)
    {
        var host = Require(opt, "host", "--host is required");
        var yes = opt.ContainsKey("yes");
        var dryRun = opt.ContainsKey("dry-run");

        var lines = File.ReadAllLines(HostsPath).ToList();
        var idx = FindHostLineIndex(lines, host);
        if (idx < 0)
            throw new InvalidOperationException($"Host not found in hosts file: {host}");

        if (dryRun)
        {
            Console.WriteLine("Dry run — no changes written.");
            Console.WriteLine($"  - {lines[idx]}");
            return;
        }

        EnsureAdmin();

        if (!yes)
        {
            if (Console.IsInputRedirected)
                throw new InvalidOperationException("Refusing to prompt for confirmation in a non-interactive session; pass --yes to proceed.");

            Console.Write($"Remove host '{host}' ({lines[idx].Trim()})? [y/N] ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted.");
                return;
            }
        }

        lines.RemoveAt(idx);

        BackupInternal();
        File.WriteAllLines(HostsPath, lines);

        var tags = LoadTags();
        if (tags.Remove(host))
            SaveTags(tags);

        Console.WriteLine("Removed.");
    }

    private static void DoTags(Dictionary<string, string?> opt)
    {
        var json = opt.ContainsKey("json");
        var map = LoadTags();

        if (json)
        {
            var data = map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k.Key, v => v.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
            Console.WriteLine(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        if (map.Count == 0)
        {
            Console.WriteLine("(no tags)");
            return;
        }
        foreach (var kv in map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{kv.Key}: {string.Join(", ", kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}");
        }
    }

    private static void DoBackup()
    {
        EnsureAdmin();
        BackupInternal();
    }

    // ---------------- Rendering ----------------

    private static void RenderTable(List<Row> rows)
    {
        // Fixed column widths to match your sample
        const int W_IP = 14;
        const int W_HOST = 27;
        const int W_TAGS = 19;

        string H(string s, int w) => PadOrTrim(s, w);
        string Sep(int w) => new string('-', w);

        Console.WriteLine($"{H("IP", W_IP)}  {H("HOSTNAME", W_HOST)}  {H("TAGS", W_TAGS)}  {"COMMENT"}");
        Console.WriteLine($"{Sep(W_IP)}  {Sep(W_HOST)}  {Sep(W_TAGS)}  {Sep(27)}");

        foreach (var r in rows)
        {
            var ipText = r.Disabled ? "#" + r.Ip : r.Ip;
            var tagsText = string.Join(",", r.Tags);
            Console.WriteLine($"{H(ipText, W_IP)}  {H(r.Host, W_HOST)}  {H(tagsText, W_TAGS)}  {r.Comment}");
        }
    }

    private static string PadOrTrim(string s, int width)
    {
        if (s.Length == width) return s;
        if (s.Length < width) return s.PadRight(width);
        // trim with ellipsis if too long
        return (width >= 1) ? (s.Substring(0, Math.Max(0, width - 1)) + "…") : s;
    }

    // ---------------- Helpers ----------------

    private static bool HasFlag(string[] args, string shortFlag, string longFlag)
        => args.Any(a => a == shortFlag || a.Equals(longFlag, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;

            if (a.Contains('='))
            {
                var parts = a.Split('=', 2);
                dict[parts[0][2..]] = parts[1];
            }
            else
            {
                var key = a[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    dict[key] = args[i + 1];
                    i++;
                }
                else
                {
                    dict[key] = null;
                }
            }
        }
        return dict;
    }

    private static string Get(Dictionary<string, string?> dict, string key)
        => dict.TryGetValue(key, out var v) ? v ?? "" : "";

    private static string Require(Dictionary<string, string?> dict, string key, string message)
    {
        var v = Get(dict, key);
        if (string.IsNullOrWhiteSpace(v))
            throw new ArgumentException(message);
        return v;
    }

    private static void EnsureAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(id);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Administrator privilege is required to modify the hosts file.");
    }

    private static bool IsHostPresent(string host)
    {
        foreach (var (entry, _, disabled) in EnumerateHostEntries(File.ReadAllLines(HostsPath)))
        {
            if (!disabled && entry != null && string.Equals(entry.Hostname, host, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IEnumerable<(HostEntry? entry, string raw, bool disabled)> EnumerateHostEntries(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var (e, raw, disabled) = ParseEntry(line);
            yield return (e, raw, disabled);
        }
    }

    private static (HostEntry? entry, string raw, bool disabled) ParseEntry(string line)
    {
        var raw = line;
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return (null, raw, false);

        // A line that starts with '#' is either a disabled host entry ("#1.2.3.4 foo")
        // or a free-text comment ("# note to self") — distinguished below by whether
        // the first token parses as an IP address.
        var disabled = trimmed.StartsWith("#");
        var body = disabled ? trimmed.TrimStart('#').TrimStart() : trimmed;

        string? comment = null;
        var hashIdx = body.IndexOf('#');
        if (hashIdx >= 0)
        {
            comment = body[(hashIdx + 1)..].Trim();
            body = body[..hashIdx].Trim();
        }
        if (string.IsNullOrWhiteSpace(body))
            return (null, raw, disabled);

        var parts = body.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return (null, raw, disabled);

        var ip = parts[0];
        var host = parts[1];

        if (disabled && !IPAddress.TryParse(ip, out _))
            return (null, raw, disabled);

        return (new HostEntry { Ip = ip, Hostname = host, Comment = comment ?? "" }, raw, disabled);
    }

    private static int FindHostLineIndex(List<string> lines, string host)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var (e, _, disabled) = ParseEntry(lines[i]);
            if (!disabled && e != null && string.Equals(e.Hostname, host, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string BackupInternal()
    {
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var bak = HostsPath + $".bak_{ts}";
        if (File.Exists(bak))
        {
            var i = 1;
            string candidate;
            do
            {
                candidate = $"{bak}_{i}";
                i++;
            } while (File.Exists(candidate));
            bak = candidate;
        }
        File.Copy(HostsPath, bak, overwrite: false);
        Console.WriteLine($"Backup created: {bak}");
        return bak;
    }

    private static Dictionary<string, HashSet<string>> LoadTags()
    {
        Directory.CreateDirectory(MetaDir);
        if (!File.Exists(MetaFile))
            return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(MetaFile);
        var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ??
                   new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in data)
            map[kv.Key] = new HashSet<string>(kv.Value ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return map;
    }

    private static void SaveTags(Dictionary<string, HashSet<string>> map)
    {
        Directory.CreateDirectory(MetaDir);
        var data = map.ToDictionary(k => k.Key, v => v.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(MetaFile, json);
    }

    // ---------------- Sorting helpers ----------------

    private static byte[] ParseIpSortKey(string ip)
    {
        if (IPAddress.TryParse(ip, out var addr))
        {
            var bytes = addr.GetAddressBytes();
            if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                var v6 = new byte[16];
                v6[10] = 0xff; v6[11] = 0xff;
                Buffer.BlockCopy(bytes, 0, v6, 12, 4);
                return v6;
            }
            return bytes;
        }
        return Enumerable.Repeat((byte)0xFF, 16).ToArray();
    }

    private sealed class IpKeyComparer : IComparer<byte[]>
    {
        public static readonly IpKeyComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var len = Math.Min(x.Length, y.Length);
            for (int i = 0; i < len; i++)
            {
                int cmp = x[i].CompareTo(y[i]);
                if (cmp != 0) return cmp;
            }
            return x.Length.CompareTo(y.Length);
        }
    }

    private record Row(string Ip, string Host, string[] Tags, string Comment, bool Disabled);
    private record HostEntry { public string Ip { get; init; } = ""; public string Hostname { get; init; } = ""; public string Comment { get; init; } = ""; }
}
