using Microsoft.Extensions.Hosting;

namespace Aurora.Worker;

/// <summary>
/// Entry point for the background host. Deliberately bare: the scheduler, the outbox dispatcher,
/// the migration runner and the provisioning saga runner are wired through
/// <c>Aurora.Composition</c> by later tasks (B-06 onward), never directly here.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        IHost host = builder.Build();

        host.Run();
    }
}
