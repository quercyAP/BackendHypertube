using Hypertube.Application.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Hypertube.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string? _sendGridApiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly bool _emailSendingEnabled;

    public EmailService(ILogger<EmailService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        _sendGridApiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
        _fromEmail = Environment.GetEnvironmentVariable("EMAIL_FROM") ?? "noreply@hypertube.com";
        _fromName = Environment.GetEnvironmentVariable("EMAIL_FROM_NAME") ?? "Hypertube";
        _emailSendingEnabled = bool.Parse(Environment.GetEnvironmentVariable("EMAIL_ENABLED") ?? "false");
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default)
    {
        var resetUrl = $"{_configuration["App:FrontendUrl"]}/reset-password?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(toEmail)}";

        var subject = "Reset Your Hypertube Password";
        var htmlContent = EmailTemplates.GetPasswordResetEmail(resetUrl, toEmail);
        var plainTextContent = $"Reset your password by visiting: {resetUrl}\n\nThis link will expire in 24 hours.";

        await SendEmailAsync(toEmail, subject, plainTextContent, htmlContent, cancellationToken);
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string username, CancellationToken cancellationToken = default)
    {
        var subject = $"Welcome to Hypertube, {username}!";
        var htmlContent = EmailTemplates.GetWelcomeEmail(username);
        var plainTextContent = $"Welcome to Hypertube, {username}!\n\nWe're excited to have you on board. Start exploring thousands of movies and enjoy seamless streaming.";

        await SendEmailAsync(toEmail, subject, plainTextContent, htmlContent, cancellationToken);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string plainTextContent, string htmlContent, CancellationToken cancellationToken)
    {
        if (!_emailSendingEnabled)
        {
            _logger.LogWarning(
                "Email sending is DISABLED. Email would be sent to: {ToEmail} | Subject: {Subject}",
                toEmail,
                subject
            );
            _logger.LogInformation("Email preview URL (not sent): Check logs above");
            return;
        }

        if (string.IsNullOrEmpty(_sendGridApiKey))
        {
            _logger.LogError(
                "SendGrid API Key not configured! Cannot send email to {ToEmail}. " +
                "Please set SENDGRID_API_KEY environment variable or Email:SendGridApiKey in appsettings.json",
                toEmail
            );
            _logger.LogWarning(
                "Email details - To: {ToEmail} | Subject: {Subject}",
                toEmail,
                subject
            );
            return;
        }

        try
        {
            var client = new SendGridClient(_sendGridApiKey);
            var from = new EmailAddress(_fromEmail, _fromName);
            var to = new EmailAddress(toEmail);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);

            var response = await client.SendEmailAsync(msg, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Email sent successfully to {ToEmail} | Subject: {Subject} | StatusCode: {StatusCode}",
                    toEmail,
                    subject,
                    response.StatusCode
                );
            }
            else
            {
                var responseBody = await response.Body.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Failed to send email to {ToEmail}. StatusCode: {StatusCode} | Response: {Response}",
                    toEmail,
                    response.StatusCode,
                    responseBody
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception occurred while sending email to {ToEmail} | Subject: {Subject}",
                toEmail,
                subject
            );
        }
    }
}
