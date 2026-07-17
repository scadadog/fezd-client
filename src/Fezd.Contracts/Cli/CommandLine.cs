using System;
using System.Collections.Generic;
using System.Linq;

namespace Fezd.Contracts.Cli
{
    /// <summary>
    /// Minimal, dependency-free argument parser shared by the Windows and Linux
    /// binaries (kept AOT/trim-safe). Supports:
    ///   - positional arguments
    ///   - <c>--flag</c> boolean switches
    ///   - <c>--key value</c> and <c>--key=value</c> options
    /// CLI values always override config file values downstream.
    /// </summary>
    public sealed class CommandLine
    {
        private readonly List<string> _positionals = new List<string>();
        private readonly Dictionary<string, string> _options =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Positionals => _positionals;

        public static CommandLine Parse(string[] args)
        {
            var cl = new CommandLine();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string key = arg.Substring(2);
                    int eq = key.IndexOf('=');
                    if (eq >= 0)
                    {
                        cl._options[key.Substring(0, eq)] = key.Substring(eq + 1);
                        continue;
                    }

                    // Look ahead: if the next token is a value (not another flag), consume it.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        cl._options[key] = args[++i];
                    }
                    else
                    {
                        cl._flags.Add(key);
                    }
                }
                else if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
                {
                    // Short flag(s), e.g. -v or -h. No value consumption.
                    cl._flags.Add(arg.Substring(1));
                }
                else
                {
                    cl._positionals.Add(arg);
                }
            }
            return cl;
        }

        public bool HasFlag(params string[] names) =>
            names.Any(n => _flags.Contains(n) || _options.ContainsKey(n));

        public string GetOption(string name, string fallback = null) =>
            _options.TryGetValue(name, out string v) ? v : fallback;

        public string GetOption(string[] names, string fallback = null)
        {
            foreach (string n in names)
                if (_options.TryGetValue(n, out string v))
                    return v;
            return fallback;
        }

        public int GetInt(string name, int fallback)
        {
            string raw = GetOption(name);
            return int.TryParse(raw, out int v) ? v : fallback;
        }

        /// <summary>
        /// Resolves a tri-state boolean CLI switch:
        ///   --name -> true, --no-name -> false, absent -> null.
        /// </summary>
        public bool? GetSwitch(string name)
        {
            if (_flags.Contains(name) || _options.ContainsKey(name))
                return true;
            if (_flags.Contains("no-" + name) || _options.ContainsKey("no-" + name))
                return false;
            return null;
        }
    }
}
