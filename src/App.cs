// Studio UI: model picker, chat with streaming output, settings, tokens/sec. C# 5 only (WinForms, .NET Framework 4.8).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
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
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = smokeTestMs;
                t.Tick += delegate { t.Stop(); form.Close(); };
                form.Shown += delegate { t.Start(); };
            }
            Application.Run(form);
            return 0;
        }

        // Drives the real window: loads the model, sends one message, waits for the reply, closes.
        // Returns the transcript text of the reply; throws if anything fails or times out.
        public static string RunScripted(string modelPath, string message, int maxTokens, int timeoutMs)
        {
            Application.EnableVisualStyles();
            MainForm form = new MainForm();
            form.Settings.MaxTokens = maxTokens;
            form.Settings.Temperature = 0;
            string error = null;
            System.Windows.Forms.Timer watchdog = new System.Windows.Forms.Timer();
            watchdog.Interval = timeoutMs;
            watchdog.Tick += delegate { watchdog.Stop(); error = "timed out"; form.Close(); };
            form.ModelLoaded += delegate(string err)
            {
                if (err != null) { error = "load failed: " + err; form.Close(); return; }
                form.SendMessage(message);
            };
            form.ReplyFinished += delegate(string err)
            {
                if (err != null) error = "generation failed: " + err;
                form.Close();
            };
            form.Shown += delegate { watchdog.Start(); form.LoadModel(modelPath); };
            Application.Run(form);
            watchdog.Stop();
            if (error != null) throw new InvalidOperationException(error);
            return form.LastReply;
        }
    }

    public sealed class StudioSettings
    {
        [Category("Sampling"), Description("0 = always pick the most likely token.")]
        public float Temperature { get; set; }
        [Category("Sampling"), DisplayName("Top K"), Description("Keep only the K most likely tokens (0 = off).")]
        public int TopK { get; set; }
        [Category("Sampling"), DisplayName("Top P"), Description("Keep the smallest set of tokens whose probability reaches P (1 = off).")]
        public float TopP { get; set; }
        [Category("Sampling"), DisplayName("Repeat penalty"), Description("Penalize tokens used in the last N tokens (1 = off).")]
        public float RepeatPenalty { get; set; }
        [Category("Sampling"), DisplayName("Repeat window"), Description("How many recent tokens the repeat penalty looks at.")]
        public int RepeatLastN { get; set; }
        [Category("Sampling"), Description("Random seed (-1 = random).")]
        public int Seed { get; set; }

        [Category("Generation"), DisplayName("Max tokens"), Description("Longest reply, in tokens.")]
        public int MaxTokens { get; set; }
        [Category("Generation"), DisplayName("System prompt"), Description("Instructions given to the model before the chat.")]
        public string SystemPrompt { get; set; }

        [Category("Model"), DisplayName("Context length"), Description("Tokens of history the model can see. Applies when a model is loaded.")]
        public int ContextLength { get; set; }
        [Category("Model"), DisplayName("Use GPU"), Description("Run on the GPU through Vulkan; otherwise the CPU. Applies when a model is loaded.")]
        public bool UseGpu { get; set; }

        public StudioSettings()
        {
            Temperature = 0.7f; TopK = 40; TopP = 0.95f; RepeatPenalty = 1.1f; RepeatLastN = 64; Seed = -1;
            MaxTokens = 512; SystemPrompt = "You are a helpful assistant."; ContextLength = 4096; UseGpu = true;
        }

        public SamplerSettings Sampler()
        {
            SamplerSettings s = new SamplerSettings();
            s.Temperature = Temperature; s.TopK = TopK; s.TopP = TopP;
            s.RepeatPenalty = RepeatPenalty; s.RepeatLastN = RepeatLastN; s.Seed = Seed;
            return s;
        }
    }

    public delegate void StatusEvent(string error);

    public class MainForm : Form
    {
        public readonly StudioSettings Settings = new StudioSettings();
        public event StatusEvent ModelLoaded, ReplyFinished;
        public string LastReply = "";

        readonly RichTextBox _chat = new RichTextBox();
        readonly TextBox _input = new TextBox();
        readonly Button _send = new Button(), _stop = new Button();
        readonly PropertyGrid _grid = new PropertyGrid();
        readonly ToolStripStatusLabel _status = new ToolStripStatusLabel(), _speed = new ToolStripStatusLabel(), _backend = new ToolStripStatusLabel();
        readonly ToolStripLabel _modelLabel = new ToolStripLabel("No model loaded");
        readonly ToolStripButton _open = new ToolStripButton("Open model..."), _newChat = new ToolStripButton("New chat");

        readonly List<ChatMessage> _history = new List<ChatMessage>();
        ChatEngine _engine;
        volatile bool _busy, _cancel;
        Thread _worker;

        public MainForm()
        {
            Text = "Local LLM Studio";
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(700, 450);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            ToolStrip tools = new ToolStrip();
            tools.GripStyle = ToolStripGripStyle.Hidden;
            _open.Click += delegate { PickModel(); };
            _newChat.Click += delegate { NewChat(); };
            tools.Items.Add(_open);
            tools.Items.Add(_newChat);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(_modelLabel);

            _chat.ReadOnly = true;
            _chat.Dock = DockStyle.Fill;
            _chat.BorderStyle = BorderStyle.None;
            _chat.BackColor = SystemColors.Window;
            _chat.Font = new Font("Segoe UI", 10.5f);
            _chat.DetectUrls = false;

            _input.Multiline = true;
            _input.Dock = DockStyle.Fill;
            _input.ScrollBars = ScrollBars.Vertical;
            _input.Font = new Font("Segoe UI", 10.5f);
            _input.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; SendFromInput(); }
            };

            _send.Text = "Send"; _send.Dock = DockStyle.Top; _send.Height = 34;
            _stop.Text = "Stop"; _stop.Dock = DockStyle.Top; _stop.Height = 34; _stop.Enabled = false;
            _send.Click += delegate { SendFromInput(); };
            _stop.Click += delegate { _cancel = true; };
            Panel buttons = new Panel();
            buttons.Dock = DockStyle.Right; buttons.Width = 90; buttons.Padding = new Padding(6, 0, 0, 0);
            buttons.Controls.Add(_stop);
            buttons.Controls.Add(_send);

            Panel inputRow = new Panel();
            inputRow.Dock = DockStyle.Bottom; inputRow.Height = 90; inputRow.Padding = new Padding(0, 6, 0, 0);
            inputRow.Controls.Add(_input);
            inputRow.Controls.Add(buttons);

            Panel chatPanel = new Panel();
            chatPanel.Dock = DockStyle.Fill; chatPanel.Padding = new Padding(10);
            chatPanel.Controls.Add(_chat);
            chatPanel.Controls.Add(inputRow);

            _grid.Dock = DockStyle.Fill;
            _grid.SelectedObject = Settings;
            _grid.ToolbarVisible = false;
            _grid.HelpVisible = true;

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel2;
            split.Panel1.Controls.Add(chatPanel);
            split.Panel2.Controls.Add(_grid);

            StatusStrip strip = new StatusStrip();
            _status.Spring = true; _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Text = "Open a .gguf model file to start.";
            strip.Items.Add(_status);
            strip.Items.Add(_speed);
            strip.Items.Add(_backend);

            Controls.Add(split);
            Controls.Add(tools);
            Controls.Add(strip);
            Load += delegate { split.SplitterDistance = Math.Max(300, ClientSize.Width - 320); };
            FormClosing += delegate { _cancel = true; if (_worker != null) _worker.Join(5000); if (_engine != null) _engine.Dispose(); };
            UpdateButtons();
        }

        void UpdateButtons()
        {
            _send.Enabled = !_busy && _engine != null;
            _stop.Enabled = _busy;
            _open.Enabled = !_busy;
            _newChat.Enabled = !_busy;
        }

        void Ui(MethodInvoker a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void Append(string text, Color color, bool bold)
        {
            _chat.SelectionStart = _chat.TextLength;
            _chat.SelectionLength = 0;
            _chat.SelectionColor = color;
            _chat.SelectionFont = new Font(_chat.Font, bold ? FontStyle.Bold : FontStyle.Regular);
            _chat.AppendText(text);
            _chat.ScrollToCaret();
        }

        // Local files only: the picker is the one way a model gets in.
        void PickModel()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Open model";
                dlg.Filter = "GGUF models (*.gguf)|*.gguf";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadModel(dlg.FileName);
            }
        }

        public void LoadModel(string path)
        {
            if (_busy) return;
            _busy = true; UpdateButtons();
            _status.Text = "Loading " + Path.GetFileName(path) + "...";
            ChatEngine old = _engine;
            _engine = null;
            int ctx = Math.Max(256, Settings.ContextLength);
            bool gpu = Settings.UseGpu;
            _worker = new Thread(delegate()
            {
                string err = null;
                ChatEngine eng = null;
                try
                {
                    if (old != null) old.Dispose();
                    eng = ChatEngine.Load(path, ctx, gpu, delegate(string msg) { Ui(delegate { Append(msg + "\n", Color.DarkOrange, false); }); });
                }
                catch (Exception ex) { err = ex.Message; }
                Ui(delegate
                {
                    _busy = false;
                    _engine = eng;
                    if (eng != null)
                    {
                        _modelLabel.Text = eng.ModelName + "  (" + Path.GetFileName(path) + ")";
                        _backend.Text = eng.Backend;
                        _status.Text = "Ready. Context " + eng.ContextLength + " tokens.";
                        NewChat();
                    }
                    else
                    {
                        _status.Text = "Could not load the model: " + err;
                    }
                    UpdateButtons();
                    if (ModelLoaded != null) ModelLoaded(err);
                });
            });
            _worker.IsBackground = true;
            _worker.Start();
        }

        void NewChat()
        {
            _history.Clear();
            _chat.Clear();
            if (_engine != null) _engine.ClearCache();
            _speed.Text = "";
        }

        void SendFromInput()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0) return;
            _input.Clear();
            SendMessage(text);
        }

        public void SendMessage(string text)
        {
            if (_busy || _engine == null) return;
            _busy = true; _cancel = false; UpdateButtons();

            Append("You\n", Color.SteelBlue, true);
            Append(text + "\n\n", SystemColors.WindowText, false);
            Append("Assistant\n", Color.SeaGreen, true);

            List<ChatMessage> msgs = new List<ChatMessage>();
            if (!string.IsNullOrEmpty(Settings.SystemPrompt)) msgs.Add(new ChatMessage("system", Settings.SystemPrompt));
            msgs.AddRange(_history);
            msgs.Add(new ChatMessage("user", text));

            SamplerSettings sampler = Settings.Sampler();
            int maxTokens = Math.Max(1, Settings.MaxTokens);
            ChatEngine eng = _engine;
            _status.Text = "Reading the prompt...";

            _worker = new Thread(delegate()
            {
                System.Text.StringBuilder reply = new System.Text.StringBuilder();
                string err = null;
                GenerationStats st = null;
                bool first = true;
                try
                {
                    st = eng.Generate(msgs, sampler, maxTokens, delegate(string piece)
                    {
                        reply.Append(piece);
                        bool f = first; first = false;
                        Ui(delegate
                        {
                            if (f) _status.Text = "Generating...";
                            Append(piece, SystemColors.WindowText, false);
                        });
                    }, delegate { return _cancel; });
                }
                catch (Exception ex) { err = ex.Message; }

                Ui(delegate
                {
                    Append("\n\n", SystemColors.WindowText, false);
                    _busy = false;
                    LastReply = reply.ToString();
                    if (err == null)
                    {
                        _history.Add(new ChatMessage("user", text));
                        _history.Add(new ChatMessage("assistant", LastReply));
                        _speed.Text = string.Format("{0:N1} tokens/s  ({1} tokens, prompt {2} new + {3} cached at {4:N0} tok/s)",
                            st.TokensPerSecond, st.GeneratedTokens, st.PromptTokens, st.ReusedTokens, st.PromptTokensPerSecond);
                        _status.Text = "Done (" + st.StopReason + ").";
                    }
                    else
                    {
                        _status.Text = "Error: " + err;
                        Append("[" + err + "]\n\n", Color.Firebrick, false);
                    }
                    UpdateButtons();
                    if (ReplyFinished != null) ReplyFinished(err);
                });
            });
            _worker.IsBackground = true;
            _worker.Start();
        }
    }
}
