using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TEİASRestfulApi.DTOs;

namespace TEİASRestfulApi;

public class YtbsMailSender
{
    private readonly IOptionsMonitor<YtbsSettings> _settings;
    private readonly ILogger<YtbsMailSender> _logger;

    public YtbsMailSender(IOptionsMonitor<YtbsSettings> settings, ILogger<YtbsMailSender> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string subject, string body, CancellationToken cancellationToken = default)
    {
        YtbsMailSettings mail = _settings.CurrentValue.Mail;
        if (!mail.CanSend)
        {
            _logger.LogWarning(
                "YTBS mail atlandı (Enabled={Enabled} alıcı={Adet} şifre_var={Sifre}). Alıcıları appsettings.Local.json → YtbsSettings:Mail:To içinden değiştirin.",
                mail.Enabled,
                mail.Recipients.Count,
                !string.IsNullOrWhiteSpace(mail.Password));
            return false;
        }

        var message = new MimeMessage();
        string from = mail.From ?? mail.UserName!;
        message.From.Add(new MailboxAddress(mail.FromName, from));
        foreach (string to in mail.Recipients)
        {
            message.To.Add(MailboxAddress.Parse(to));
        }

        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        try
        {
            client.CheckCertificateRevocation = mail.CheckCertificateRevocation;
            client.ServerCertificateValidationCallback = AcceptWhenRevocationOffline;
            SecureSocketOptions tls = mail.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(mail.Host, mail.Port, tls, cancellationToken);
            await client.AuthenticateAsync(mail.UserName!, mail.Password!, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            _logger.LogInformation("YTBS özet maili gönderildi. Konu: {Konu} alıcı={Adet}", subject, mail.Recipients.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YTBS özet maili gönderilemedi.");
            return false;
        }
    }

    /// <summary>
    /// Gmail sertifikası geçerliyken Windows “iptal durumu sorgulanamadı” deyip TLS’i kesmesin.
    /// İsim uyuşmazlığı veya gerçekten bozuk zincir yine reddedilir.
    /// </summary>
    public static bool AcceptWhenRevocationOffline(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (certificate is null || errors != SslPolicyErrors.RemoteCertificateChainErrors)
        {
            return false;
        }

        if (chain is { ChainStatus.Length: > 0 })
        {
            return chain.ChainStatus.All(static s =>
                s.Status is X509ChainStatusFlags.NoError
                    or X509ChainStatusFlags.OfflineRevocation
                    or X509ChainStatusFlags.RevocationStatusUnknown);
        }

        return true;
    }
}
