using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyHi.Companion.Features.Diagnostics;

public sealed class ChecklistStepDefinition
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("part")]
    public required string Part { get; init; }

    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("answerType")]
    public required string AnswerType { get; init; }

    [JsonPropertyName("blocking")]
    public bool Blocking { get; init; }
}

/// <summary>One question in the probe checklist (TASKS.md 0.9), with its live answer.</summary>
public sealed partial class ChecklistStep(ChecklistStepDefinition definition) : ObservableObject
{
    public string Id { get; } = definition.Id;

    public string Part { get; } = definition.Part;

    public string Question { get; } = definition.Question;

    public string AnswerType { get; } = definition.AnswerType;

    public bool Blocking { get; } = definition.Blocking;

    [ObservableProperty]
    private string answer = string.Empty;

    [ObservableProperty]
    private bool isPreFilled;
}

/// <summary>A Part A-G group of steps, for the CollectionView's grouped display.</summary>
public sealed class ChecklistGroup(string part, IEnumerable<ChecklistStep> steps) : List<ChecklistStep>(steps)
{
    public string Part { get; } = part;
}
