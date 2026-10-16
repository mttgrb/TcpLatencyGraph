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

            var options = new Options();

            if (args.Length > 0)
            {
                CommandLine commandLine;
                string error;
                if (!CommandLine.TryParse(args, out commandLine, out error))
                {
                    MessageBox.Show(error + "\n\n" + CommandLine.Usage,
                                    "Invalid Arguments", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (commandLine.ShowHelp)
                {
                    MessageBox.Show(CommandLine.Usage, "TCP Latency Graph",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                else
                {
                    options = commandLine.Options;
                    if (commandLine.Host != null)
                    {
                        // Launch graph directly
                        Application.Run(new GraphForm(commandLine.Host, commandLine.Port, options));
                        return;
                    }
                }
            }

            // Default behavior: show startup form
            StartupForm startup = new StartupForm(options);
            startup.Show();

            Application.Run(); // no main form → stays alive until last closes
        }
    }
}
