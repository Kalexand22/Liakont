namespace Liakont.Agent;

using System.ServiceProcess;

internal static class Program
{
    private static void Main()
    {
        using var service = new AgentService();
        ServiceBase.Run(service);
    }
}
