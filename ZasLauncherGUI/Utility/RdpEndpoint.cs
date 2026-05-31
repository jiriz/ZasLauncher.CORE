using System;

namespace ZasLauncherGUI.Utility;

internal static class RdpEndpoint
{
    public const int DefaultPort = 3389;

    public static (string Host, int Port) Normalize(string host, int port)
    {
        var normalizedHost = host.Trim();
        var normalizedPort = NormalizePort(port);

        if (TrySplitHostPort(normalizedHost, out var splitHost, out var splitPort))
        {
            normalizedHost = splitHost;

            if (normalizedPort == DefaultPort)
                normalizedPort = splitPort;
        }

        return (normalizedHost, normalizedPort);
    }

    public static int NormalizePort(int port) =>
        port is > 0 and <= 65535 ? port : DefaultPort;

    public static string Format(string host, int port)
    {
        var normalizedPort = NormalizePort(port);

        if (normalizedPort == DefaultPort)
            return host;

        var displayHost = host.Contains(':') && !host.StartsWith('[')
            ? $"[{host}]"
            : host;

        return $"{displayHost}:{normalizedPort}";
    }

    private static bool TrySplitHostPort(string value, out string host, out int port)
    {
        host = value;
        port = DefaultPort;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.StartsWith('['))
        {
            var endBracket = value.IndexOf("]:", StringComparison.Ordinal);

            if (endBracket > 0 &&
                TryParsePort(value[(endBracket + 2)..], out port))
            {
                host = value[1..endBracket];
                return true;
            }

            return false;
        }

        var colonIndex = value.LastIndexOf(':');

        if (colonIndex <= 0 || value.IndexOf(':') != colonIndex)
            return false;

        if (!TryParsePort(value[(colonIndex + 1)..], out port))
            return false;

        host = value[..colonIndex];
        return true;
    }

    private static bool TryParsePort(string value, out int port)
    {
        if (int.TryParse(value, out port) && port is > 0 and <= 65535)
            return true;

        port = DefaultPort;
        return false;
    }
}
