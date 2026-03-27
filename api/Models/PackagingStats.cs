namespace Company.Function.Models;

public class PackagingStats
{
    public int TotalRuns { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Running { get; set; }
    public int SucceededWithWarnings { get; set; }
    public double SuccessRate { get; set; }
    public List<PackagingRunEntity> RecentRuns { get; set; } = new();
}
