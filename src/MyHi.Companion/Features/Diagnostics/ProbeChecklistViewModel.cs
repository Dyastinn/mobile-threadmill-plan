using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyHi.Companion.Core.Formatting;
using MyHi.Companion.Features.Bluetooth;
using MyHi.Companion.Features.Shared;

namespace MyHi.Companion.Features.Diagnostics;

/// <summary>Turns 05a-FTMS-Probe-Procedure.md Parts A-G into an in-app form (TASKS.md 0.9).</summary>
public sealed partial class ProbeChecklistViewModel : BaseViewModel
{
    private readonly TreadmillConnection _connection;
    private readonly CaptureSessionManager _captures;
    private readonly ILogger<ProbeChecklistViewModel> _logger;
    private readonly string _answersFilePath = Path.Combine(FileSystem.AppDataDirectory, "probe-checklist-answers.json");

    public ProbeChecklistViewModel(TreadmillConnection connection, CaptureSessionManager captures, ILogger<ProbeChecklistViewModel> logger)
    {
        _connection = connection;
        _captures = captures;
        _logger = logger;
    }

    public ObservableCollection<ChecklistStep> Steps { get; } = [];

    public ObservableCollection<ChecklistGroup> Groups { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Steps.Count > 0)
        {
            return;
        }

        using var stream = await FileSystem.OpenAppPackageFileAsync("ProbeChecklist.json");
        var definitions = await JsonSerializer.DeserializeAsync<List<ChecklistStepDefinition>>(stream) ?? [];

        var savedAnswers = LoadSavedAnswers();
        foreach (var definition in definitions)
        {
            var step = new ChecklistStep(definition);
            if (savedAnswers.TryGetValue(step.Id, out var savedAnswer))
            {
                step.Answer = savedAnswer;
            }

            Steps.Add(step);
        }

        Groups.Clear();
        foreach (var group in Steps.GroupBy(s => s.Part))
        {
            Groups.Add(new ChecklistGroup(group.Key, group));
        }

        await PrefillFromLiveDataAsync();
    }

    private async Task PrefillFromLiveDataAsync()
    {
        if (_connection.State != ConnectionState.Ready)
        {
            return;
        }

        SetPrefilled("Q5", _connection.NegotiatedMtu.ToString());

        var characteristics = _connection.Services.SelectMany(s => s.Characteristics).ToList();
        await PrefillHexAsync(characteristics, "2ACC", "Q1", "A2");
        await PrefillHexAsync(characteristics, "2AD4", "Q2", "A3");
        await PrefillHexAsync(characteristics, "2AD3", "A4");
    }

    private async Task PrefillHexAsync(IEnumerable<GattCharacteristicInfo> characteristics, string shortUuid, params string[] stepIds)
    {
        var characteristic = characteristics.FirstOrDefault(c => string.Equals(c.ShortUuid, shortUuid, StringComparison.OrdinalIgnoreCase));
        if (characteristic is null || !characteristic.Properties.HasFlag(Plugin.BLE.Abstractions.CharacteristicPropertyType.Read))
        {
            return;
        }

        try
        {
            var (bytes, _) = await _connection.RunExclusiveAsync(() => characteristic.Native.ReadAsync());
            foreach (var id in stepIds)
            {
                SetPrefilled(id, HexHelpers.ToHex(bytes));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prefill read failed for {Uuid}", shortUuid);
        }
    }

    private void SetPrefilled(string id, string value)
    {
        var step = Steps.FirstOrDefault(s => s.Id == id);
        if (step is null || !string.IsNullOrWhiteSpace(step.Answer))
        {
            return;
        }

        step.Answer = value;
        step.IsPreFilled = true;
    }

    [RelayCommand]
    private void Save()
    {
        var answers = Steps.ToDictionary(s => s.Id, s => s.Answer);
        File.WriteAllText(_answersFilePath, JsonSerializer.Serialize(answers));
    }

    private Dictionary<string, string> LoadSavedAnswers()
    {
        if (!File.Exists(_answersFilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_answersFilePath)) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse saved checklist answers");
            return [];
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        Save();

        var markdown = BuildMarkdown();
        await Clipboard.Default.SetTextAsync(markdown);

        var recorder = _captures.EnsureSession();
        var exportPath = Path.Combine(Path.GetDirectoryName(recorder.FilePath)!, $"probe-checklist-{DateTimeOffset.UtcNow:yyyy-MM-dd-HHmm}.json");
        File.WriteAllText(exportPath, JsonSerializer.Serialize(Steps.ToDictionary(s => s.Id, s => new { s.Part, s.Question, s.Answer })));

        StatusMessage = "Markdown copied to clipboard; raw answers written next to the capture session.";
    }

    private string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Probe Checklist Results");
        sb.AppendLine();
        sb.AppendLine($"Exported: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        foreach (var group in Steps.GroupBy(s => s.Part))
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (var step in group)
            {
                var blockingMark = step.Blocking ? " **[blocking]**" : string.Empty;
                sb.AppendLine($"- **{step.Question}**{blockingMark}");
                sb.AppendLine($"  {(string.IsNullOrWhiteSpace(step.Answer) ? "_(unanswered)_" : step.Answer)}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
