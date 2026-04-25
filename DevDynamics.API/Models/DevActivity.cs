namespace DevDynamics.API.Models;

public class DevActivity
{
    public int Id { get; set; }
    public DateTime Time { get; set; }
    public int Commits { get; set; }
    public int PullRequests { get; set; }
    public int Merges { get; set; }
    public int Meetings { get; set; }
    public int Documentation { get; set; }

    public string Contributor { get; set; } = string.Empty;

    // ✅ NEW FIELD
    public string Company { get; set; } = string.Empty;

    public DateTime PROpenedAt { get; set; }
    public DateTime PRMergedAt { get; set; }
}