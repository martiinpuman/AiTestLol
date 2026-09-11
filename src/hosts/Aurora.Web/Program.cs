using Microsoft.AspNetCore.Builder;

namespace Aurora.Web;

/// <summary>
/// Entry point for the web host. Deliberately bare: tenancy resolution, authentication,
/// Blazor Server components and the versioned REST API are wired through
/// <c>Aurora.Composition</c> by later tasks (B-06 onward), never directly here.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        WebApplication app = builder.Build();

        app.Run();
    }
}
