namespace RedNote.RecommendationService.Features;

public sealed class RecommendationOptions
{
    public string Endpoint { get; set; } = "http://localhost:8088";
    public string ApiKey { get; set; } = "";
    public bool Initialize { get; set; }
}
