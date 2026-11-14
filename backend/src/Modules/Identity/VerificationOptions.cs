namespace Identity;

public sealed class VerificationOptions
{
    public const string SectionName = "Verification";

    public string BaseUrl { get; set; } = "https://ialine2dc.onrender.com";

    public string ApiKey { get; set; } = "";
}
