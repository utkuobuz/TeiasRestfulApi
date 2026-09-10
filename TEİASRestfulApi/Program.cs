using Microsoft.Extensions.Configuration;
using TEİASRestfulApi;
using TEİASRestfulApi.DTOs;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TEIAS Veri Aktarim Servisi";
});

builder.Services.Configure<YtbsSettings>(builder.Configuration.GetSection("YtbsSettings"));
builder.Services.AddHttpClient<YTBSClient>();
builder.Services.AddSingleton<YtbsMailSender>();
builder.Services.AddSingleton<YtbsValueScaleStore>();
builder.Services.AddSingleton<YtbsReportedLimitStore>();
builder.Services.AddSingleton<YtbsSlotDeliveryStore>();
builder.Services.AddHostedService<YtbsWorker>();

var host = builder.Build();
host.Run();
