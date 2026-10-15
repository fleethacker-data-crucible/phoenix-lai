// Studio entry point. C# 5 only (compiled by Windows PowerShell 5.1 Add-Type).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Phoenix
{
    public static class App
    {
        // smokeTestMs > 0: open the window, close it after that many ms, return 0.
        public static int Run(int smokeTestMs)
        {
            Application.EnableVisualStyles();

            MainForm form = new MainForm();
            if (smokeTestMs > 0)
            {
                Timer t = new Timer();
                t.Interval = smokeTestMs;
                t.Tick += delegate { t.Stop(); form.Close(); };
                form.Shown += delegate { t.Start(); };
            }
            Application.Run(form);
            return 0;
        }
    }

    public class MainForm : Form
    {
        public MainForm()
        {
            Text = "Local LLM Studio";
            ClientSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;

            StatusStrip status = new StatusStrip();
            status.Items.Add(new ToolStripStatusLabel("No model loaded"));
            Controls.Add(status);
        }
    }
}
