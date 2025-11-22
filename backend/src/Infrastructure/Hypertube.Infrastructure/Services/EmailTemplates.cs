namespace Hypertube.Infrastructure.Services;

public static class EmailTemplates
{
    public static string GetPasswordResetEmail(string resetUrl, string userEmail)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Reset Your Password</title>
</head>
<body style=""margin: 0; padding: 0; font-family: Arial, sans-serif; background-color: #f4f4f4;"">
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #f4f4f4; padding: 20px;"">
        <tr>
            <td align=""center"">
                <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1);"">
                    <!-- Header -->
                    <tr>
                        <td style=""background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 40px 20px; text-align: center;"">
                            <h1 style=""color: #ffffff; margin: 0; font-size: 28px;"">🎬 Hypertube</h1>
                        </td>
                    </tr>

                    <!-- Body -->
                    <tr>
                        <td style=""padding: 40px 30px;"">
                            <h2 style=""color: #333333; margin-top: 0;"">Reset Your Password</h2>
                            <p style=""color: #666666; font-size: 16px; line-height: 1.6;"">
                                We received a request to reset the password for your Hypertube account (<strong>{userEmail}</strong>).
                            </p>
                            <p style=""color: #666666; font-size: 16px; line-height: 1.6;"">
                                Click the button below to reset your password. This link will expire in 24 hours.
                            </p>

                            <!-- Button -->
                            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin: 30px 0;"">
                                <tr>
                                    <td align=""center"">
                                        <a href=""{resetUrl}"" style=""display: inline-block; padding: 16px 40px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: #ffffff; text-decoration: none; border-radius: 6px; font-size: 16px; font-weight: bold;"">
                                            Reset My Password
                                        </a>
                                    </td>
                                </tr>
                            </table>

                            <p style=""color: #666666; font-size: 14px; line-height: 1.6; margin-top: 30px;"">
                                If you didn't request a password reset, you can safely ignore this email. Your password will not be changed.
                            </p>

                            <p style=""color: #999999; font-size: 12px; line-height: 1.6; margin-top: 20px; padding-top: 20px; border-top: 1px solid #eeeeee;"">
                                If the button doesn't work, copy and paste this link into your browser:<br>
                                <a href=""{resetUrl}"" style=""color: #667eea; word-break: break-all;"">{resetUrl}</a>
                            </p>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style=""background-color: #f8f9fa; padding: 20px; text-align: center;"">
                            <p style=""color: #999999; font-size: 12px; margin: 0;"">
                                © 2025 Hypertube. All rights reserved.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    public static string GetWelcomeEmail(string username)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Welcome to Hypertube</title>
</head>
<body style=""margin: 0; padding: 0; font-family: Arial, sans-serif; background-color: #f4f4f4;"">
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #f4f4f4; padding: 20px;"">
        <tr>
            <td align=""center"">
                <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1);"">
                    <!-- Header -->
                    <tr>
                        <td style=""background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 40px 20px; text-align: center;"">
                            <h1 style=""color: #ffffff; margin: 0; font-size: 32px;"">🎬 Welcome to Hypertube!</h1>
                        </td>
                    </tr>

                    <!-- Body -->
                    <tr>
                        <td style=""padding: 40px 30px;"">
                            <h2 style=""color: #333333; margin-top: 0;"">Hi {username}! 👋</h2>
                            <p style=""color: #666666; font-size: 16px; line-height: 1.6;"">
                                Welcome to <strong>Hypertube</strong> - your new favorite place to stream movies via BitTorrent!
                            </p>
                            <p style=""color: #666666; font-size: 16px; line-height: 1.6;"">
                                We're excited to have you on board. Start exploring thousands of movies and enjoy seamless streaming.
                            </p>

                            <div style=""background-color: #f8f9fa; border-left: 4px solid #667eea; padding: 20px; margin: 30px 0;"">
                                <h3 style=""color: #333333; margin-top: 0; font-size: 18px;"">✨ What you can do:</h3>
                                <ul style=""color: #666666; font-size: 16px; line-height: 1.8; margin: 10px 0;"">
                                    <li>Search and discover movies</li>
                                    <li>Stream directly via BitTorrent</li>
                                    <li>Add comments and rate movies</li>
                                    <li>Manage your profile</li>
                                </ul>
                            </div>

                            <p style=""color: #666666; font-size: 16px; line-height: 1.6;"">
                                If you have any questions or need help, feel free to reach out to our support team.
                            </p>

                            <p style=""color: #666666; font-size: 16px; line-height: 1.6; margin-top: 30px;"">
                                Happy streaming! 🍿
                            </p>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style=""background-color: #f8f9fa; padding: 20px; text-align: center;"">
                            <p style=""color: #999999; font-size: 12px; margin: 0;"">
                                © 2025 Hypertube. All rights reserved.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }
}
