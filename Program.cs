using System;
using System.Windows.Forms;

namespace TcpLatencyGraph
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Check for command line arguments
            if (args.Length == 2)
            {
                string target = args[0].Trim();
                string portStr = args[1].Trim();

                // Validate target and port
                if (!string.IsNullOrWhiteSpace(target) && int.TryParse(portStr, out int port) && port > 0 && port <= 65535)
                {
                    // Launch graph directly
                    GraphForm graph = new GraphForm(target, port);
                    graph.Show();
                    Application.Run(graph); // Use the graph as main form
                    return;
                }
                else
                {
                    // Show error message for invalid arguments
                    MessageBox.Show($"Invalid arguments.\n\nUsage: TcpLatency.exe <hostname/ip> <port>\n\nExamples:\n  TcpLatency.exe google.com 80\n  TcpLatency.exe 8.8.8.8 53", 
                                    "Invalid Arguments", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // Default behavior: show startup form
            StartupForm startup = new StartupForm();
            startup.Show();

            Application.Run(); // no main form → stays alive until last closes
        }
    }
}
