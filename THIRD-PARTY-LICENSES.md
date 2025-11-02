# Third-Party Licenses and Attributions

TelegramGroupsAdmin is built on the shoulders of many excellent open-source projects. This document acknowledges the third-party software, libraries, and resources used in this project.

---

## Bundled Components (Included in Docker Image)

### FFmpeg
- **Version:** 7.0.2
- **License:** LGPL 2.1+ (static build without GPL codecs)
- **Usage:** Media file processing (video/audio thumbnails, format conversion)
- **Source:** https://ffmpeg.org/
- **Distribution:** https://johnvansickle.com/ffmpeg/
- **Notes:** FFmpeg is called as a separate process, not statically linked. The LGPL allows this usage pattern.

### Tesseract OCR
- **License:** Apache License 2.0
- **Usage:** Optical character recognition for image spam detection
- **Source:** https://github.com/tesseract-ocr/tesseract
- **Traineddata:** English language data from tesseract-ocr/tessdata_best
- **Notes:** Fully compatible with MIT license.

### EFF Large Wordlist (Modified)
- **Original Source:** Electronic Frontier Foundation (EFF)
- **License:** Creative Commons (assumed public domain/permissive use)
- **URL:** https://www.eff.org/deeplinks/2016/07/new-wordlists-random-passphrases
- **Modifications:** Filtered from 7,776 to 7,299 words (removed 477 problematic words via AI review)
- **Usage:** Secure passphrase generation for backup encryption
- **File:** `TelegramGroupsAdmin.Core/Security/eff_large_wordlist.txt`
- **Notes:** EFF wordlist is provided for public use. Modified version embedded as resource.

---

## Major Dependencies (NuGet Packages)

### Telegram & Communication
- **Telegram.Bot** - MIT License - https://github.com/TelegramBots/Telegram.Bot
- **SendGrid** - MIT License - https://github.com/sendgrid/sendgrid-csharp

### Web Framework & UI
- **ASP.NET Core** (.NET 9.0) - MIT License - https://github.com/dotnet/aspnetcore
- **MudBlazor** - MIT License - https://github.com/MudBlazor/MudBlazor
- **Blazor-ApexCharts** - MIT License - https://github.com/apexcharts/Blazor-ApexCharts

### Database & ORM
- **Entity Framework Core** - MIT License - https://github.com/dotnet/efcore
- **Npgsql** - PostgreSQL License - https://github.com/npgsql/npgsql
- **Dapper** - Apache License 2.0 - https://github.com/DapperLib/Dapper

### Background Jobs
- **TickerQ** - MIT License - https://github.com/Salgat/TickerQ

### Machine Learning & AI
- **Microsoft.ML** - MIT License - https://github.com/dotnet/machinelearning
- **OpenAI API** (external service) - https://platform.openai.com/

### Security & Authentication
- **ASP.NET Core Data Protection** - MIT License - https://github.com/dotnet/aspnetcore
- **Otp.NET** - MIT License - https://github.com/kspearrin/Otp.NET
- **QRCoder** - MIT License - https://github.com/codebude/QRCoder

### Utilities & Parsing
- **AngleSharp** - MIT License - https://github.com/AngleSharp/AngleSharp
- **CsvHelper** - MS-PL / Apache 2.0 - https://github.com/JoshClose/CsvHelper
- **DiffPlex** - Apache License 2.0 - https://github.com/mmanela/diffplex
- **SixLabors.ImageSharp** - Apache License 2.0 (v2+) - https://github.com/SixLabors/ImageSharp

### Virus Scanning
- **nClam** - Apache License 2.0 - https://github.com/tekmaven/nClam
- **ClamAV** (external service) - GPL 2.0 - https://www.clamav.net/
- **VirusTotal API** (external service) - https://www.virustotal.com/

### Testing
- **NUnit** - MIT License - https://github.com/nunit/nunit
- **Testcontainers** - MIT License - https://github.com/testcontainers/testcontainers-dotnet
- **NSubstitute** - BSD 3-Clause - https://github.com/nsubstitute/NSubstitute
- **WireMock.Net** - Apache License 2.0 - https://github.com/WireMock-Net/WireMock.Net

---

## External Services (API Keys Required)

These services are accessed via API and are not bundled with the software:

- **OpenAI GPT-4** - Text-based spam detection and prompt generation
- **OpenAI Vision API** - Image-based spam detection
- **VirusTotal** - File threat intelligence scanning
- **CAS.chat** - Combot Anti-Spam user database
- **SendGrid** - Email delivery for verification and notifications
- **ClamAV** - Real-time virus scanning (self-hosted in Docker)

---

## Inspiration & Attribution

### tg-spam by @umputun
- **Project:** https://github.com/umputun/tg-spam
- **License:** MIT License
- **Attribution:** This project's anti-spam system is heavily inspired by and derived from tg-spam's excellent implementation. Many spam detection algorithms, patterns, and approaches are based on tg-spam's work. Highly recommended for single-group deployments!

---

## License Compatibility Summary

TelegramGroupsAdmin is licensed under the **MIT License**.

All bundled components and dependencies are compatible with MIT licensing:
- **MIT** - Fully compatible (most NuGet packages)
- **Apache 2.0** - Fully compatible (Tesseract, ImageSharp, etc.)
- **LGPL 2.1+** - Compatible when used as separate process (FFmpeg)
- **PostgreSQL License** - Fully compatible (Npgsql)
- **BSD 3-Clause** - Fully compatible (NSubstitute)
- **Creative Commons/Public Domain** - Fully compatible (EFF Wordlist)

External services (ClamAV GPL 2.0, OpenAI proprietary) are accessed via API/network and do not affect the project's MIT license.

---

## Obtaining Source Code

### Bundled Components
- **FFmpeg:** Source code available at https://ffmpeg.org/download.html
- **Tesseract:** Source code available at https://github.com/tesseract-ocr/tesseract
- **EFF Wordlist:** Original available at https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt

### NuGet Packages
All NuGet dependencies are publicly available at https://www.nuget.org/ and their respective source repositories are linked above.

### License Files
Full license texts for all dependencies can be found in their respective source repositories.

---

## Questions or Concerns?

If you have questions about licensing or need clarification on any third-party component, please open an issue at:
https://github.com/weekenders/TelegramGroupsAdmin/issues

---

**Last Updated:** 2025-11-02
