using Microsoft.Extensions.DependencyInjection;

namespace VNotch.Fork;

// faridfox fork: the single composition hook. Upstream's ServiceConfigurator.ConfigureServices calls Configure as its
// last statement (one marked line). Register fork services here; keep it free of side effects because upstream's
// tests call ConfigureServices too (ForkUi.InitializeOnce does nothing outside the real app).
internal static class ForkServices
{
    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<ForkSettingsService>();
        ForkUi.InitializeOnce();
    }
}
