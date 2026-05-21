using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TEİASRestfulApi;
using TEİASRestfulApi.DTOs; // YtbsSettings'in bulunmasını sağlayan satır

var builder = Host.CreateApplicationBuilder(args);

// Uygulamayı bir Windows Servisi olarak yapılandır
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TEIAS Veri Aktarim Servisi";
});

// 1. Ayarları Yapılandır
builder.Services.Configure<YtbsSettings>(builder.Configuration.GetSection("YtbsSettings"));

// 2. HTTP Client ve Servisi Kaydet
builder.Services.AddHttpClient<YTBSClient>();

// 3. Worker Servisi Başlat
builder.Services.AddHostedService<YtbsWorker>();

var host = builder.Build();
host.Run();