# TcpLatency

![Status](https://img.shields.io/badge/status-active-brightgreen)
![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-Framework%204.8-lightgrey)
![License](https://img.shields.io/badge/license-Private-red)

**TcpLatency** is a lightweight Windows tool that measures TCP connection latency and displays it on a live, auto-scaling graph.  
It’s fully portable — no installer, no admin rights, just launch and test.

---

## ✨ Features

- Portable single-file EXE
- Real-time TCP latency measurement
- Dynamic vertical scaling (Y axis auto-adjusts)
- Expanding time window (X axis grows forever)
- Clean & minimal UI
- Multiple graphs supported
- Error feedback inside UI (no popup spam)
- Input window can be closed without stopping graphs
- Safe exit when last graph is closed

---

## 🚀 Usage

1. Run **TcpLatency.exe**
2. Enter:
   - IP or hostname
   - TCP port
3. Press **Enter** or click **Start**
4. A graph window opens and begins reading latency

### Multiple sessions?
Just repeat step 3 — each graph is independent.

### Exit behavior:
- Closing the input window does **not** close active graphs  
- App exits only when all graphs are closed

---

## 📊 Graph Logic

### Horizontal axis (time):
- Starts at **300 seconds**
- Grows by **1 second per tick**
- Never scrolls, always extends

### Vertical axis (latency):
- Starts at **50 ms**
- Auto-expands above that if needed

### Sampling:
- 1 TCP connect per second
- `TcpClient.Connect()` + Stopwatch

---

## 🖥 Requirements

- Windows 10 / 11
- .NET Framework 4.8 (preinstalled on most systems)

---

## 🛠 Build Instructions

### Prerequisites
- Visual Studio 2022 (or newer)
- Workload: **.NET Desktop Development**
- .NET Framework 4.8 targeting pack
