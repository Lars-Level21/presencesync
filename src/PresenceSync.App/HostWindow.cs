using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace PresenceSync.App;

/// <summary>
/// Invisible top-level window: receives global hotkeys and is the fallback parent HWND for WAM sign-in dialogs.
/// </summary>
internal sealed class HostWindow : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x80;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _hotkeys = new();
    private int _nextId = 1;

    public HostWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("PresenceSyncHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
        });
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>Registers e.g. "Ctrl+Alt+D". Returns false if the format is invalid or the combination is taken.</summary>
    public bool RegisterHotkey(string definition, Action action, ILogger log)
    {
        if (!TryParse(definition, out var modifiers, out var virtualKey))
        {
            log.LogWarning("Hotkey ungültig: {Hotkey}", definition);
            return false;
        }

        var id = _nextId++;
        if (!RegisterHotKey(Handle, id, modifiers | ModNoRepeat, (uint)virtualKey))
        {
            log.LogWarning("Hotkey {Hotkey} konnte nicht registriert werden (bereits belegt?)", definition);
            return false;
        }

        _hotkeys[id] = action;
        log.LogInformation("Hotkey {Hotkey} registriert", definition);
        return true;
    }

    public void UnregisterAllHotkeys()
    {
        foreach (var id in _hotkeys.Keys)
            UnregisterHotKey(Handle, id);
        _hotkeys.Clear();
    }

    internal static bool TryParse(string definition, out uint modifiers, out int virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        var key = Key.None;
        foreach (var raw in definition.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control" or "strg": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": modifiers |= ModWin; break;
                default:
                    var name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (key != Key.None || !Enum.TryParse(name, ignoreCase: true, out key))
                        return false;
                    break;
            }
        }
        virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return key != Key.None && modifiers != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _hotkeys.TryGetValue((int)wParam, out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAllHotkeys();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
