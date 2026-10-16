using System;
using System.Windows.Forms;

namespace SimsRedux.Creator.App
{
    /// <summary>SimsRedux: the Sims Redux Creator - see MainForm.</summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Sims Redux Creator", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
        }
    }
}
