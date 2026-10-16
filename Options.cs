using System;
using System.Collections.Generic;
using System.Globalization;

namespace TcpLatencyGraph
{
    internal sealed class Options
    {
        public int IntervalMs = 1000;
        public int TimeoutMs = 3000;
        public int WindowSeconds = 3600;
        public string LogPath;
    }

    internal sealed class CommandLine
    {
        public const string Usage =
            "Usage: TcpLatency.exe [options] <host> <port>\n" +
            "       TcpLatency.exe [options] <host>:<port>\n\n" +
            "Options:\n" +
            "  -i <ms>        interval between connects, default 1000\n" +
            "  -t <ms>        timeout, default 3000\n" +
            "  -w <sec>       how much history the graph shows, default 3600\n" +
            "  -log <file>    append every result to a CSV file\n\n" +
            "Examples:\n" +
            "  TcpLatency.exe google.com 443\n" +
            "  TcpLatency.exe -t 1000 -log C:\\Temp\\rdp.csv myserver:3389";

        public Options Options = new Options();
        public string Host;
        public int Port;
        public bool ShowHelp;

        public static bool TryParse(string[] args, out CommandLine result, out string error)
        {
            result = new CommandLine();
            error = null;
            var positional = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Trim();
                if (arg.Length == 0)
                    continue;

                string opt = arg.ToLowerInvariant();
                if (opt == "-h" || opt == "-?" || opt == "/?" || opt == "-help" || opt == "--help")
                {
                    result.ShowHelp = true;
                    return true;
                }

                if (opt == "-i" || opt == "-t" || opt == "-w" || opt == "-log")
                {
                    if (i + 1 >= args.Length)
                    {
                        error = arg + " needs a value.";
                        return false;
                    }

                    string value = args[++i].Trim();
                    int number;
                    switch (opt)
                    {
                        case "-i":
                            if (!TryParseRange(value, 100, 3600000, out number))
                            {
                                error = "-i must be between 100 and 3600000 ms.";
                                return false;
                            }
                            result.Options.IntervalMs = number;
                            break;
                        case "-t":
                            if (!TryParseRange(value, 100, 60000, out number))
                            {
                                error = "-t must be between 100 and 60000 ms.";
                                return false;
                            }
                            result.Options.TimeoutMs = number;
                            break;
                        case "-w":
                            if (!TryParseRange(value, 60, 604800, out number))
                            {
                                error = "-w must be between 60 and 604800 seconds.";
                                return false;
                            }
                            result.Options.WindowSeconds = number;
                            break;
                        default:
                            if (value.Length == 0)
                            {
                                error = "-log needs a file name.";
                                return false;
                            }
                            result.Options.LogPath = value;
                            break;
                    }
                    continue;
                }

                if (arg.StartsWith("-"))
                {
                    error = "Unknown option " + arg + ".";
                    return false;
                }

                positional.Add(arg);
            }

            if (positional.Count == 0)
                return true;

            string host;
            int port;
            if (positional.Count == 1)
            {
                if (!Target.TryParseHostPort(positional[0], out host, out port))
                {
                    error = "Expected <host> <port> or <host>:<port>.";
                    return false;
                }
            }
            else if (positional.Count == 2)
            {
                host = Target.CleanHost(positional[0]);
                if (!Target.IsValidHost(host))
                {
                    error = "Invalid hostname or IP address: " + positional[0];
                    return false;
                }
                if (!Target.TryParsePort(positional[1], out port))
                {
                    error = "Invalid port: " + positional[1];
                    return false;
                }
            }
            else
            {
                error = "Too many arguments.";
                return false;
            }

            result.Host = host;
            result.Port = port;
            return true;
        }

        private static bool TryParseRange(string s, int min, int max, out int value)
        {
            return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                && value >= min && value <= max;
        }
    }

    internal static class Target
    {
        public static bool TryParsePort(string s, out int port)
        {
            return int.TryParse((s ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
                && port >= 1 && port <= 65535;
        }

        // "[::1]" -> "::1"
        public static string CleanHost(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length > 2 && s.StartsWith("[") && s.EndsWith("]"))
                s = s.Substring(1, s.Length - 2);
            return s;
        }

        public static bool IsValidHost(string host)
        {
            return host.Length > 0 && host.IndexOfAny(new[] { ' ', '\t', ',', '[', ']' }) < 0;
        }

        // Accepts host:port and [ipv6]:port. A bare IPv6 address has more than one
        // colon and is rejected, since there is no way to tell where the port starts.
        public static bool TryParseHostPort(string s, out string host, out int port)
        {
            host = null;
            port = 0;
            s = (s ?? "").Trim();

            if (s.StartsWith("["))
            {
                int close = s.IndexOf(']');
                if (close < 2 || close + 1 >= s.Length || s[close + 1] != ':')
                    return false;
                host = s.Substring(1, close - 1);
                return IsValidHost(host) && TryParsePort(s.Substring(close + 2), out port);
            }

            int sep = s.IndexOf(':');
            if (sep <= 0 || sep != s.LastIndexOf(':'))
                return false;

            host = s.Substring(0, sep);
            return IsValidHost(host) && TryParsePort(s.Substring(sep + 1), out port);
        }
    }
}
