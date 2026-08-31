namespace SecsFrame.GuidedDemo.Models;

internal sealed record GuidedStepResult(
    int StepNumber,
    string Title,
    string Summary,
    IReadOnlyList<DemoEvidence> Evidence,
    string? Code);
