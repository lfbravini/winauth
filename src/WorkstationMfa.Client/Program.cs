using System.Runtime.InteropServices;
using WorkstationMfa.Core.Totp;

namespace WorkstationMfa.Client;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--silent-provision", StringComparison.OrdinalIgnoreCase))
            return SilentProvision(args);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
        return 0;
    }

    private static int SilentProvision(string[] args)
    {
        try
        {
            string? upn = null;
            string? secret = null;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("--upn", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    upn = args[++i];
                else if (args[i].Equals("--secret", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    secret = args[++i];
                else if (upn is null)
                    upn = args[i];
                else if (secret is null)
                    secret = args[i];
            }

            if (string.IsNullOrWhiteSpace(upn) || string.IsNullOrWhiteSpace(secret))
            {
                Console.Error.WriteLine("Usage: WorkstationMfa.Client.exe --silent-provision --upn <upn> --secret <base32>");
                return 2;
            }

            if (!IsValidBase32(secret))
            {
                Console.Error.WriteLine("Invalid Base32 secret.");
                return 3;
            }

            var store = new TotpCredentialStore();
            store.Save(new TotpCredential
            {
                Upn = upn,
                DisplayName = $"Horizon VDI ({upn})",
                Base32Secret = secret,
                Digits = 6,
                Period = 30,
                Hash = "sha1"
            });

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                var exe = Environment.ProcessPath ?? Application.ExecutablePath;
                key?.SetValue("WorkstationMfa", $"\"{exe}\"");
            }
            catch
            {
                // non-fatal
            }

            secret = null;
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool IsValidBase32(string value)
    {
        value = value.Trim().TrimEnd('=').ToUpperInvariant();
        if (value.Length == 0)
            return false;
        foreach (var c in value)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= '2' && c <= '7')))
                return false;
        }
        return true;
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly TotpCredentialStore _store = new();
    private readonly System.Windows.Forms.Timer _timer;
    private MainForm? _form;
    private HotKeyWindow? _hotKey;

    public TrayApplicationContext()
    {
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Workstation MFA",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                CopyCode();
        };

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => UpdateTooltip();
        _timer.Start();

        _hotKey = new HotKeyWindow(CopyAndType);
        UpdateTooltip();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy code", null, (_, _) => CopyCode());
        menu.Items.Add("Show window", null, (_, _) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private void ShowMain()
    {
        if (_form is null || _form.IsDisposed)
            _form = new MainForm(_store);
        _form.Show();
        _form.BringToFront();
    }

    private void CopyCode()
    {
        try
        {
            var code = _store.GenerateCode();
            Clipboard.SetText(code);
            _tray.ShowBalloonTip(1500, "Workstation MFA", "Code copied.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(2500, "Workstation MFA", ex.Message, ToolTipIcon.Error);
        }
    }

    private void CopyAndType()
    {
        try
        {
            var code = _store.GenerateCode();
            Clipboard.SetText(code);
            SendKeys.SendWait(code + "{ENTER}");
        }
        catch
        {
            // ignore hotkey failures
        }
    }

    private void UpdateTooltip()
    {
        try
        {
            if (!_store.Exists)
            {
                _tray.Text = "Workstation MFA (not provisioned)";
                return;
            }
            var cred = _store.Load();
            var code = _store.GenerateCode(cred);
            var remaining = cred!.Period - (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % cred.Period);
            _tray.Text = $"Workstation MFA {code} ({remaining}s)";
        }
        catch
        {
            _tray.Text = "Workstation MFA";
        }
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _hotKey?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class MainForm : Form
{
    private readonly TotpCredentialStore _store;
    private readonly Label _codeLabel;
    private readonly Label _metaLabel;
    private readonly System.Windows.Forms.Timer _timer;

    public MainForm(TotpCredentialStore store)
    {
        _store = store;
        Text = "Workstation MFA";
        Width = 320;
        Height = 180;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _codeLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Font = new Font("Segoe UI", 28, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        _metaLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Padding = new Padding(8)
        };
        Controls.Add(_metaLabel);
        Controls.Add(_codeLabel);

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => RefreshUi();
        _timer.Start();
        RefreshUi();
    }

    private void RefreshUi()
    {
        try
        {
            var cred = _store.Load();
            if (cred is null)
            {
                _codeLabel.Text = "------";
                _metaLabel.Text = "Not provisioned";
                return;
            }
            var code = _store.GenerateCode(cred);
            var remaining = cred.Period - (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % cred.Period);
            _codeLabel.Text = code;
            _metaLabel.Text = $"{cred.DisplayName}\nRefresh in {remaining}s\nHotkey: Ctrl+Alt+T";
        }
        catch (Exception ex)
        {
            _codeLabel.Text = "------";
            _metaLabel.Text = ex.Message;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}

internal sealed class HotKeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotKeyId = 0x4D46; // MF
    private readonly Action _onHotKey;

    public HotKeyWindow(Action onHotKey)
    {
        _onHotKey = onHotKey;
        CreateHandle(new CreateParams());
        // MOD_CONTROL | MOD_ALT = 0x0002 | 0x0001; VK_T = 0x54
        NativeMethods.RegisterHotKey(Handle, HotKeyId, 0x0002 | 0x0001, 0x54);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotKeyId)
            _onHotKey();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(Handle, HotKeyId);
        DestroyHandle();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
