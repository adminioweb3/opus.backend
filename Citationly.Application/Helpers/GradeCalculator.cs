namespace Citationly.Application.Helpers;

/// <summary>
/// Converts a 0-100 composite score into a US-style letter grade.
/// </summary>
public static class GradeCalculator
{
    public static string ToGrade(int score) => score switch
    {
        >= 97 => "A+",
        >= 93 => "A",
        >= 90 => "A-",
        >= 87 => "B+",
        >= 83 => "B",
        >= 80 => "B-",
        >= 77 => "C+",
        >= 73 => "C",
        >= 70 => "C-",
        >= 67 => "D+",
        >= 63 => "D",
        >= 60 => "D-",
        _     => "F"
    };
}
