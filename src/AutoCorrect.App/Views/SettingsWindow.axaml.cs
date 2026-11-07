using Avalonia.Controls;
using Avalonia.Input;
using AutoCorrect.App.ViewModels;

namespace AutoCorrect.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        Activated += async (_, _) =>
        {
            if (DataContext is SettingsViewModel vm)
                await vm.OnWindowActivatedAsync().ConfigureAwait(true);
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || !vm.IsRecording)
            return;

        // Map Avalonia key + modifiers into our chord model.
        var mods = e.KeyModifiers;
        var win = mods.HasFlag(KeyModifiers.Meta);
        var ctrl = mods.HasFlag(KeyModifiers.Control);
        var alt = mods.HasFlag(KeyModifiers.Alt);
        var shift = mods.HasFlag(KeyModifiers.Shift);

        var vk = MapToVk(e.Key);
        if (vk == 0)
            return;

        if (vm.TryAcceptRecordedChord(win, ctrl, alt, shift, vk))
            e.Handled = true;
    }

    private static uint MapToVk(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return (uint)('A' + (key - Key.A));
        if (key is >= Key.D0 and <= Key.D9)
            return (uint)('0' + (key - Key.D0));
        if (key is >= Key.F1 and <= Key.F24)
            return (uint)(0x70 + (key - Key.F1));

        return key switch
        {
            Key.Space => 0x20,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            _ => 0
        };
    }
}
