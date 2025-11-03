using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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

    private static int Main(string[] args)
    {
        if (args.Length == 0 || HasAny(args, "--help", "-h") ||
            (args.Length > 0 && args[0].Equals("help", StringComparison.OrdinalIgnoreCase)))
        {
            PrintHelp();
            return 0;
        }
        if (HasAny(args, "--version", "-V", "-v") ||
            (args.Length > 0 && args[0].Equals("version", StringComparison.OrdinalIgnoreCase)))
        {
            PrintVersion();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        var opt = ParseOptions(args.Skip(1).ToArray());

        try
        {
            switch (cmd)
            {
                case "list":
                    DoList(opt);
                    break;
                case "add":
                    EnsureAdmin();
                    DoAdd(opt);
                    break;
                case "edit":
                    EnsureAdmin();
                    DoEdit(opt);
                    break;
                case "remove":
                    EnsureAdmin();
                    DoRemove(opt);
                    break;
                case "tags":
                    DoTags();
                    break;
                case "backup":
                    EnsureAdmin();
                    DoBackup();
                    break;
                case "help":
                    PrintHelp();
                    break;
                case "version":
                    PrintVersion();
                    break;
                default:
                    Console.Error.WriteLine($"Unknown command: {cmd}");
                    Console.Error.WriteLine();
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("✖ " + ex.Message);
            return 1;
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("hostctl - Windows hosts manager with tags");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  hostctl list [--tag <name>] [--all] [--sort ip|name|tag] [--desc]");
        Console.WriteLine("  hostctl add --ip <ip> --host <hostname[,hostname2]> [--tag tag1,tag2] [--comment \"text\"]");
        Console.WriteLine("  hostctl edit --host <old> [--ip <newip>] [--rename <newhost>] [--tag \"+web,-old\"]");
        Console.WriteLine("  hostctl remove --host <hostname>");
        Console.WriteLine("  hostctl tags");
        Console.WriteLine("  hostctl backup");
        Console.WriteLine("  hostctl --help | -h");
        Console.WriteLine("  hostctl --version | -V | -v");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  • Commands that modify the hosts file require Administrator privileges.");
        Console.WriteLine($"  • Tags metadata: {MetaFile}");
        Console.WriteLine($"  • Hosts file    : {HostsPath}");
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

    // ---------------- Commands ----------------

    private static void DoList(Dictionary<string, string?> opt)
    {
        var all = opt.ContainsKey("all");
        var tagFilter = Get(opt, "tag");
        var sortKey = Get(opt, "sort")?.ToLowerInvariant(); // "", "ip", "name", "tag"
        var desc = opt.ContainsKey("desc");

        var lines = File.ReadAllLines(HostsPath);
        var tagsMap = LoadTags();

        var rows = new List<Row>();
        foreach (var (entry, rawLine) in EnumerateHostEntries(lines))
        {
            if (entry == null) continue;
            if (!all && rawLine.TrimStart().StartsWith("#")) continue;

            if (!string.IsNullOrWhiteSpace(tagFilter))
            {
                if (!tagsMap.TryGetValue(entry.Hostname, out var entryTags) ||
                    !entryTags.Contains(tagFilter!, StringComparer.OrdinalIgnoreCase))
                    continue;
            }

            var tags = tagsMap.TryGetValue(entry.Hostname, out var ts)
                ? ts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();

            rows.Add(new Row(entry.Ip, entry.Hostname, tags, string.IsNullOrWhiteSpace(entry.Comment) ? "" : entry.Comment));
        }

        // Sorting
        if (!string.IsNullOrWhiteSpace(sortKey))
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
            // unknown sort key → keep insertion order
        }
        if (desc) rows.Reverse();

        // Render as fixed-width table
        RenderTable(rows);
    }

    private static void DoAdd(Dictionary<string, string?> opt)
    {
        var ip = Require(opt, "ip", "--ip is required");
        var hostCsv = Require(opt, "host", "--host is required");
        var comment = Get(opt, "comment");
        var tagCsv = Get(opt, "tag");

        var hosts = hostCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var sb = new StringBuilder();
        foreach (var line in File.ReadAllLines(HostsPath))
            sb.AppendLine(line);

        foreach (var h in hosts)
        {
            if (IsHostPresent(h))
                throw new InvalidOperationException($"Hostname already exists: {h}");
            var cmt = string.IsNullOrWhiteSpace(comment) ? "" : $"  # {comment}";
            sb.AppendLine($"{ip} {h}{cmt}");
        }

        BackupInternal();
        File.WriteAllText(HostsPath, sb.ToString());

        if (!string.IsNullOrWhiteSpace(tagCsv))
        {
            var tags = tagCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
        var tagOps = Get(opt, "tag"); // e.g. "+web,-old"

        var lines = File.ReadAllLines(HostsPath).ToList();
        var idx = FindHostLineIndex(lines, host);
        if (idx < 0)
            throw new InvalidOperationException($"Host not found in hosts file: {host}");

        var (entry, _) = ParseEntry(lines[idx]);
        if (entry == null)
            throw new InvalidOperationException($"Cannot parse host line for: {host}");

        var ip = string.IsNullOrWhiteSpace(newIp) ? entry.Ip : newIp!;
        var toName = string.IsNullOrWhiteSpace(rename) ? entry.Hostname : rename!;
        var comment = entry.Comment;

        lines[idx] = $"{ip} {toName}" + (string.IsNullOrWhiteSpace(comment) ? "" : $"  # {comment}");

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
        var lines = File.ReadAllLines(HostsPath).ToList();
        var idx = FindHostLineIndex(lines, host);
        if (idx < 0)
            throw new InvalidOperationException($"Host not found in hosts file: {host}");

        lines.RemoveAt(idx);

        BackupInternal();
        File.WriteAllLines(HostsPath, lines);

        var tags = LoadTags();
        if (tags.Remove(host))
            SaveTags(tags);

        Console.WriteLine("Removed.");
    }

    private static void DoTags()
    {
        var map = LoadTags();
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
        BackupInternal(true);
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
            var tagsText = string.Join(",", r.Tags);
            Console.WriteLine($"{H(r.Ip, W_IP)}  {H(r.Host, W_HOST)}  {H(tagsText, W_TAGS)}  {r.Comment}");
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

    private static bool HasAny(string[] args, params string[] flags)
        => args.Any(a => flags.Any(f => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)));

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
        foreach (var (entry, _) in EnumerateHostEntries(File.ReadAllLines(HostsPath)))
        {
            if (entry != null && string.Equals(entry.Hostname, host, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IEnumerable<(HostEntry? entry, string raw)> EnumerateHostEntries(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var (e, raw) = ParseEntry(line);
            yield return (e, raw);
        }
    }

    private static (HostEntry? entry, string raw) ParseEntry(string line)
    {
        var raw = line;
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return (null, raw);

        string? comment = null;
        var hashIdx = trimmed.IndexOf('#');
        if (hashIdx >= 0)
        {
            comment = trimmed[(hashIdx + 1)..].Trim();
            trimmed = trimmed[..hashIdx].Trim();
        }
        if (string.IsNullOrWhiteSpace(trimmed))
            return (null, raw);

        var parts = trimmed.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return (null, raw);

        var ip = parts[0];
        var host = parts[1];

        return (new HostEntry { Ip = ip, Hostname = host, Comment = comment ?? "" }, raw);
    }

    private static int FindHostLineIndex(List<string> lines, string host)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var (e, _) = ParseEntry(lines[i]);
            if (e != null && string.Equals(e.Hostname, host, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static void BackupInternal(bool announce = false)
    {
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var bak = HostsPath + $".bak_{ts}";
        File.Copy(HostsPath, bak, overwrite: false);
        if (announce) Console.WriteLine($"Backup created: {bak}");
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

    private record Row(string Ip, string Host, string[] Tags, string Comment);
    private record HostEntry { public string Ip { get; init; } = ""; public string Hostname { get; init; } = ""; public string Comment { get; init; } = ""; }
}
