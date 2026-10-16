# TcpLatencyGraph

Opens a TCP connection to a host and port once a second and graphs how long the connect takes. Useful when ICMP is blocked, or when you want to know whether a specific service (RDP, SQL, a web server) is slow rather than just the host.

## Download

Grab `TcpLatency.exe` from the [latest release](https://github.com/mttgrb/TcpLatencyGraph/releases/latest). Single exe, no installer, no admin rights needed.

## Usage

Start it, type a hostname or IP and a port, press Enter. A graph window opens and starts measuring. You can open as many graphs as you like from the same start window, and closing the start window doesn't close the graphs. The app quits when the last window is gone.

Hover over the graph to see the time and latency of a sample. The box in the top right shows min/max/avg and how many attempts failed.

A few things to know:

- The X axis starts at 5 minutes and keeps growing, it never scrolls. Leave it running for a day and the line gets pretty dense.
- The Y axis starts at 50 ms and grows to fit the slowest successful connect.
- Anything that fails or takes 1 second or longer counts as a timeout and is drawn at the top of the graph.

### Command line

```
TcpLatency.exe <host> <port>
```

Skips the start window and opens the graph directly, e.g.

```
TcpLatency.exe myserver.local 3389
TcpLatency.exe 8.8.8.8 53
```

## Building

Needs .NET Framework 4.8 (already on any recent Windows) and the .NET SDK, or Visual Studio with the .NET desktop workload.

```
dotnet build -c Release
```

The exe ends up in `bin\Release\net48\`.

## License

MIT, see [LICENSE](LICENSE).
