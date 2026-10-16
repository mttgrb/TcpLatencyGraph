# TcpLatencyGraph

Opens a TCP connection to a host and port once a second and graphs how long the connect takes. Useful when ICMP is blocked, or when you want to know whether a specific service (RDP, SQL, a web server) is slow rather than just the host.

## Download

Grab `TcpLatency.exe` from the [latest release](https://github.com/mttgrb/TcpLatencyGraph/releases/latest). Single exe, no installer, no admin rights needed.

## Usage

Start it, type a hostname or IP and a port (or `host:port` in the first box), press Enter. A graph window opens and starts measuring. You can open as many graphs as you like from the same start window, and closing the start window doesn't close the graphs. The app quits when the last window is gone.

Hover over the graph to see the time and result of a sample. The box on the right shows last/min/max/avg/jitter for what's on screen, and timeouts, refused connections and loss since the window was opened.

Failed attempts leave a gap in the line and get a cross at the top:

- red: timeout, nothing answered within 3 seconds
- orange: the connection was refused (the host is up but nothing listens on that port) or some other error, e.g. the name doesn't resolve. The tooltip says which.

The graph starts at 5 minutes wide, grows until it shows an hour, and scrolls from there. Stop pauses, Start carries on in the same graph. Download saves the graph as an image, or every sample as CSV if you pick that file type.

The hostname is looked up once and then every 5 minutes, so DNS time is never counted as latency. If a name has both IPv4 and IPv6 addresses, IPv4 is used. The IP is shown in the title bar.

### Command line

```
TcpLatency.exe [options] <host> <port>
TcpLatency.exe [options] <host>:<port>
```

Skips the start window and opens the graph directly. For IPv6 use `[::1]:3389` or pass the port separately.

```
-i <ms>       interval between connects, default 1000
-t <ms>       timeout, default 3000
-w <sec>      how much history the graph shows, default 3600
-log <file>   append every result to a CSV file
```

If you pass options without a host, the start window opens and every graph started from it uses them.

For example, to watch RDP on a server overnight:

```
TcpLatency.exe -log C:\Temp\rdp.csv myserver.local:3389
```

A new connect only starts once the previous one is done. So if the host is down you get one sample per timeout, not one per second.

Note that Windows itself retries for about 2 seconds before reporting a closed port as refused, so refused attempts show up every 2-3 seconds.

### CSV files

The export and the log file have the same columns: `Timestamp,Host,Port,IP,LatencyMs,Status,Detail`. LatencyMs is empty when the connect failed, Status is one of `Ok`, `Timeout`, `Refused`, `Error`, `DnsFailed`.

They're comma separated. If you have Excel set to a European locale it'll open everything in one column; use Data > From Text/CSV instead.

## Building

Needs .NET Framework 4.8 (already on any recent Windows) and the .NET SDK, or Visual Studio with the .NET desktop workload.

```
dotnet build -c Release
```

The exe ends up in `bin\Release\net48\`.

## License

MIT, see [LICENSE](LICENSE).
