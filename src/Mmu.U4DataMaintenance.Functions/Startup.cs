using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mmu.U4DataMaintenance.Functions;
using Mmu.U4DataMaintenance.Functions.Extensions;
using Mmu.Integration.Common.Utilities.Data;
using Mmu.Integration.Common.Utilities.Data.Extensions;
using Mmu.Integration.Common.Utilities.FieldTransform;
using Mmu.Integration.Common.Utilities.Management;
using Mmu.Integration.Common.Utilities.Management.Interfaces;
using Mmu.Integration.Common.Utilities.Mapping;
using Mmu.U4DataMaintenance.Functions.Helpers;

[assembly: FunctionsStartup(typeof(Startup))]
namespace Mmu.U4DataMaintenance.Functions
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            //List<KeyValuePair<string, string>> list = config.AsEnumerable().ToList();
            //builder.Services.AddLogging();
            builder.Services.AddU4Service(config);
            builder.Services.AddDataService();
            builder.Services.AddSingleton(config);
            builder.Services.AddScoped<ITransformer, U4FlexiFieldTransformer>();
            builder.Services.AddScoped<ITransformer, MappedFieldTransformer>();
            builder.Services.AddScoped<IMappingService, MappingService>();
            builder.Services.AddSingleton<ITransformer, AosPeriodTransformer>();
            //TODO: Check if this is Required
            //builder.Services.AddSingleton<ITransformer, HesaModeOfAttendanceTransfomer>();
            builder.Services.AddScoped<IETLService, ETLService>();
            builder.Services.AddScoped<ILoggerInjector, LoggerInjector>();
            builder.Services.AddSingleton<IHttpRequestMessageFactory, TokenMessageFactory>();
            //builder.Services.AddSingleton<ITokenService<CookieInfo>, TokenService<CookieInfo>>();
        }
    }
}
