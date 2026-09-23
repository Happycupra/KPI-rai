namespace Produktionsplanung.App.Services;

public sealed record QualificationLevelOption(int Level, string Name);

public static class QualificationLevelCatalog
{
    public const int None = 0;
    public const int Training = 1;
    public const int Qualified = 2;
    public const int Expert = 3;
    public const int Advanced = 4;
    public const int Administrator = 5;
    public const int MaxLevel = Administrator;

    public static IReadOnlyList<QualificationLevelOption> EmployeeChoices { get; } = new[]
    {
        new QualificationLevelOption(None, "0 · keine"),
        new QualificationLevelOption(Training, "1 · in Ausbildung"),
        new QualificationLevelOption(Qualified, "2 · qualifiziert"),
        new QualificationLevelOption(Expert, "3 · Experte/Trainer"),
        new QualificationLevelOption(Advanced, "4 · Level 4"),
        new QualificationLevelOption(Administrator, "5 · Admin")
    };

    public static IReadOnlyList<QualificationLevelOption> RequirementChoices { get; } = EmployeeChoices
        .Where(x => x.Level > None)
        .ToArray();

    public static bool IsSupportedEmployeeLevel(int level) =>
        level is >= None and <= Administrator;

    public static bool IsSupportedRequirementLevel(int level) =>
        level is >= Training and <= Administrator;

    public static string DisplayName(int level) =>
        EmployeeChoices.FirstOrDefault(x => x.Level == level)?.Name ?? $"Level {level}";
}
