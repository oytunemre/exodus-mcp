namespace Exodus.Mcp.Tests.Live;

/// <summary>A fact that runs only against a real Exodus: skipped unless <c>EXODUS_LIVE=1</c>.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EXODUS_LIVE") != "1")
        {
            Skip = "Set EXODUS_LIVE=1 with Exodus running on EXODUS_BASE_URL";
        }
    }
}
