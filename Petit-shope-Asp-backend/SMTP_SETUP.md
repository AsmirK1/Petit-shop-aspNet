SMTP setup and testing

This document shows how to enable real SMTP sending for the ASP.NET backend and how to test registration email delivery.

1. Configure SMTP (appsettings.json or environment variables)

Add the following to `appsettings.json` (or set equivalent environment variables):

{
"Smtp": {
"Enabled": "true",
"Host": "smtp.example.com",
"Port": "587",
"EnableSsl": "true",
"User": "smtp-user@example.com",
"Pass": "smtp-password",
"From": "no-reply@yourdomain.com"
}
}

Notes:

- Set `Smtp:Enabled` to "true" to enable sending. When disabled, the app logs emails and returns verification links in responses (developer convenience).
- You can set these using environment variables as `Smtp__Enabled`, `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Pass`, `Smtp__From`.

2. Recommended environment variables (macOS / bash/zsh):

export Smtp**Enabled=true
export Smtp**Host=smtp.gmail.com # your SMTP server host (not an email address)
export Smtp**Port=587
export Smtp**EnableSsl=true
export Smtp**User=vandakisaeed@gmail.com
export Smtp**Pass='kdwx lpbg yoad prbq'
export Smtp\_\_From=no-reply@yourdomain.com

Restart the backend after changing config.

Optional (recommended): use a local `smtp.env` file

1. Copy the example file:

```bash
cp smtp.env.example smtp.env
```

2. Edit `smtp.env` and fill your real credentials. The repo's `.gitignore` will ignore this file so you don't accidentally commit secrets.

3. Run the backend using the helper script that loads `smtp.env` first:

```bash
./run-with-smtp.sh
```

The helper script sources `smtp.env` and then runs `dotnet run` so the process sees the variables.

3. Test the registration flow (example using curl)

- Register a user (backend will send an email to the provided address):

curl -v -X POST "http://localhost:5062/api/auth/buyer/register" \
 -H "Content-Type: application/json" \
 -d '{"name":"Test User","email":"you@domain.com","password":"secret123"}'

- The recipient inbox should receive an HTML email containing the verification link `.../api/auth/verify?token=...`.

4. Troubleshooting

- If no email arrives:
  - Check backend logs for errors (startup and runtime). The app logs SMTP failures.
  - Ensure `Smtp:Host`/`Port`/`User`/`Pass` are correct and that the server accepts the connection from your machine.
  - If using Gmail/GSuite, ensure SMTP access is allowed (App Password or less-secure-app settings may be needed).

- Local dev alternative:
  - Keep `Smtp:Enabled` = "false" (the default in development). The API will return `verifyUrl` and `token` in the registration response so you can copy the link and verify manually.

5. Security

- Do not commit real SMTP credentials to source control. Use environment variables or a secure secrets store.
- For production, prefer a transactional email provider (SendGrid, Mailgun, SES) and use secure credentials and rate limits.

6. Verify endpoint

After receiving the email, open the link or call:

curl "http://localhost:5062/api/auth/verify?token=THE_TOKEN"

This sets `EmailVerified = true` and enables login.
