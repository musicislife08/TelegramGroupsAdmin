# Security Policy

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability in TelegramGroupsAdmin, please help us by reporting it responsibly.

**Please DO NOT open a public GitHub issue for security vulnerabilities.**

Instead, please email us at:

**security@weekenders.us**

### What to Include in Your Report

To help us understand and address the issue quickly, please include:

- Description of the vulnerability
- Steps to reproduce the issue
- Potential impact
- Any suggested fixes (if you have them)
- Your contact information for follow-up questions

### What to Expect

- **Acknowledgment:** We'll acknowledge receipt of your report within 48 hours
- **Updates:** We'll keep you informed about our progress
- **Timeline:** We aim to release fixes for critical vulnerabilities within 7-14 days
- **Credit:** With your permission, we'll credit you in the security advisory and changelog

## Supported Versions

We provide security updates for the latest stable release. Older versions may not receive security patches.

| Version | Supported          |
| ------- | ------------------ |
| Latest  | ✅ Yes             |
| Older   | ❌ No              |

## Security Best Practices

When deploying TelegramGroupsAdmin, we recommend:

### Network Security
- Use a reverse proxy (Traefik, Nginx, Caddy) with HTTPS/TLS
- Do not expose PostgreSQL port to the public internet
- Run behind a firewall in your homelab network

### Configuration Security
- Never commit `compose.yml` with real API keys to version control
- Use strong PostgreSQL passwords (not the default `CHANGE_ME`)
- Rotate API keys periodically
- Restrict file permissions on `/data/app/keys/` directory (contains encryption keys)

### Application Security
- Keep the application updated to the latest version
- Enable TOTP 2FA for all accounts (already mandatory)
- Regularly review audit logs for suspicious activity
- Limit Owner/GlobalAdmin permissions to trusted individuals

### Data Protection
- **Never delete `/data/app/keys/`** - contains encryption keys for sensitive data
- Backup encryption keys securely and separately from database backups
- Use encrypted backups with strong passphrases
- Store backups in a secure location

### API Key Security
- Restrict API key permissions where possible (e.g., OpenAI rate limits)
- Monitor API usage for unexpected spikes
- Use SendGrid API keys with minimal permissions (send only)

## Known Security Considerations

### Data Access
- This application processes and stores Telegram group messages
- Admins with database access can view all stored messages
- API keys are encrypted at rest using ASP.NET Core Data Protection
- Ensure only trusted individuals have server/container access

### Third-Party Services
- OpenAI, VirusTotal, and SendGrid receive data for processing
- Review their privacy policies and terms of service
- Consider data residency and compliance requirements for your use case

### Single Instance Constraint
- Only one instance can run per bot token (Telegram API limitation)
- Running multiple instances causes bot disconnection
- Do not expose the application to untrusted users who could restart services

## Disclosure Policy

- We practice responsible disclosure
- Security advisories will be published after fixes are released
- Critical vulnerabilities will be disclosed with CVE identifiers if applicable

## Contact

For security concerns, email: **security@weekenders.us**

For general questions and support, use [GitHub Issues](https://github.com/weekenders/TelegramGroupsAdmin/issues).

---

Thank you for helping keep TelegramGroupsAdmin secure!
