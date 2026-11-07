using System.Collections.ObjectModel;
using AutoCorrect.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoCorrect.App.ViewModels;

public sealed class RewritePreset
{
    public required string Label { get; init; }
    public required string Instruction { get; init; }
}

public partial class RewritePromptViewModel : ViewModelBase
{
    public ObservableCollection<RewritePreset> Presets { get; } = [];

    [ObservableProperty] private string _selectedPreview = "";
    [ObservableProperty] private string _instruction = "";
    [ObservableProperty] private string _newPresetLabel = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";

    public bool CanSubmit =>
        !IsBusy && !string.IsNullOrWhiteSpace(Instruction) && !string.IsNullOrWhiteSpace(SelectedPreview);

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public event Action? CancelRequested;
    public event Action? SubmitRequested;

    public RewritePromptViewModel()
    {
        ReloadPresets();
    }

    public void ReloadPresets()
    {
        Presets.Clear();
        foreach (var p in BuiltinPresets())
            Presets.Add(p);
        foreach (var saved in RewritePresetsStore.Load())
            Presets.Add(new RewritePreset { Label = saved.Label, Instruction = saved.Instruction });
    }

    private static IEnumerable<RewritePreset> BuiltinPresets() =>
    [
        new()
        {
            Label = "Email",
            Instruction =
                "Rewrite as a clear professional email body. Keep the meaning, use short paragraphs, " +
                "and a polite but direct tone. Do not invent a subject line, greeting, or signature " +
                "unless the selection already has them."
        },
        new()
        {
            Label = "Slack",
            Instruction =
                "Rewrite for Slack or chat: concise, natural, and skimmable. Prefer short sentences. " +
                "Keep any @mentions, links, and emoji that are already present. Do not add fluff."
        },
        new()
        {
            Label = "Formal reply",
            Instruction =
                "Rewrite as a formal written reply. Keep the original meaning, use precise wording, " +
                "and avoid slang or casual contractions. Stay concise."
        },
        new() { Label = "More formal", Instruction = "Make this more formal and professional." },
        new() { Label = "Shorter", Instruction = "Make this shorter and more concise while keeping the meaning." },
        new() { Label = "Clearer", Instruction = "Make this clearer and easier to read." },
        new() { Label = "Friendlier", Instruction = "Make this friendlier and more conversational." },
        new() { Label = "Expand", Instruction = "Expand this slightly with a bit more detail, staying on topic." },
        new() { Label = "Fix grammar", Instruction = "Fix grammar and spelling only; do not change tone or meaning." },
    ];

    partial void OnInstructionChanged(string value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnSelectedPreviewChanged(string value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    [RelayCommand]
    private void UsePreset(RewritePreset? preset)
    {
        if (preset is null || IsBusy)
            return;
        Instruction = preset.Instruction;
    }

    [RelayCommand]
    private void SaveCurrentAsPreset()
    {
        if (string.IsNullOrWhiteSpace(Instruction))
        {
            StatusMessage = "Type an instruction first.";
            return;
        }

        var label = string.IsNullOrWhiteSpace(NewPresetLabel)
            ? "Custom " + DateTime.Now.ToString("MMM d HH:mm")
            : NewPresetLabel.Trim();
        RewritePresetsStore.Upsert(label, Instruction);
        NewPresetLabel = "";
        ReloadPresets();
        StatusMessage = "Saved preset “" + label + "”.";
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsBusy)
            return;
        CancelRequested?.Invoke();
    }

    [RelayCommand]
    private void Submit()
    {
        if (!CanSubmit)
            return;
        SubmitRequested?.Invoke();
    }
}
