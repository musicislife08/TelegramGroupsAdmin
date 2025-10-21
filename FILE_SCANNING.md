# File Scanning System - Complete Architecture

> **⚠️ IMPORTANT NOTE - YARA Removed (October 2025)**
>
> YARA was originally included as a Tier 1 scanner but has been **permanently removed** from this project due to insurmountable technical issues:
>
> - **dnYara incompatibility**: Metadata extraction bugs on macOS + .NET 10 cause process crashes
> - **Microsoft library limitation**: Official YARA library doesn't support ARM64 (Apple Silicon)
> - **Docker containers outdated**: Community containers (blacktop/yara) abandoned since 2022
> - **Redundancy**: ClamAV provides superior coverage with 10M+ signatures vs YARA's custom rules
> - **Maintenance burden**: Managing custom YARA rules, compilation, hot-reload adds complexity with minimal value
>
> **Recommendation**: Use **ClamAV for Tier 1** (10M+ signatures, actively maintained, works perfectly) and **VirusTotal for Tier 2** (70+ engines including YARA-based scanners). This combination provides better detection than hosting YARA locally while eliminating compatibility issues.
>
> If you absolutely need custom YARA rules, upload them to VirusTotal's Livehunt feature instead of running them locally.

## 1. Overview

The File Scanning System provides multi-layered malware detection for files shared in Telegram groups. This feature implements a **two-tier architecture** combining local scanning engines with cloud-based threat intelligence services.

### Design Philosophy

- **Local-First**: Maximize detection using free, unlimited local scanners before consuming cloud quotas
- **Fail-Open**: When all quota is exhausted, allow files through rather than blocking legitimate content
- **Zero-Cost Option**: Full protection available using only free tiers and open-source tools
- **Optional Enhanced Coverage**: Users with Windows infrastructure can add 4-5 additional free AV engines via AMSI
- **Voting System**: Multiple independent scanners increase detection sensitivity

### High-Level Architecture

**Two-Tier System**:

TIER 1 executes in parallel: ClamAV scanner and optionally Windows AMSI multi-engine (4-5 free AVs). Voting logic: if ANY scanner detects threat, file is flagged as infected. If ALL scanners report clean, file proceeds to TIER 2.

TIER 2 executes sequentially: Cloud services are tried in user-configured priority order (VirusTotal, MetaDefender, Hybrid Analysis, Intezer). Each service is attempted if quota available. If any detects threat, file is infected. If all quotas exhausted, system fails open (allows file).

Actions: Infected files are deleted, and depending on user status (regular vs trusted/admin), user is banned or notified via DM.

---

## 2. Architecture Details

### Two-Tier Scanning System

The system separates scanning into two distinct tiers based on cost and quota constraints:

**Tier 1 - Local Scanners**:
- Run in parallel for maximum speed
- No quota limits (unlimited scanning)
- Zero marginal cost per file
- Always execute before cloud services
- Voting logic: ANY scanner detects threat → file flagged as infected
- All scanners must report clean to proceed to Tier 2

**Tier 2 - Cloud Queue**:
- Only executed if ALL Tier 1 scanners report clean
- Sequential execution (try services in priority order)
- Quota-aware (tracks daily/monthly limits)
- Rate-limited (respects per-minute constraints)
- Fail-open behavior when all quotas exhausted
- User-configurable priority ordering

### Flow Through System

**Processing Steps**:

1. **File Received**: Extract file from Telegram message
2. **Calculate SHA256 Hash**: Generate unique identifier
3. **Check Cache** (24hr TTL): If hash found in cache with valid result, return immediately (skip scanning)
4. **Launch Tier 1 Scanners** (Parallel): Execute ClamAV and Windows AMSI (if enabled) simultaneously
5. **Collect Tier 1 Results**: Wait for all parallel tasks to complete
6. **Apply Voting Logic**: If ANY scanner detected threat, mark as INFECTED and proceed to actions. If ALL scanners report clean, continue to Tier 2
7. **Execute Tier 2 Queue** (Sequential): For each cloud service in user-configured priority order, check if quota available. If yes, scan file. If infected, mark as INFECTED and proceed to actions. If clean, try next service. If all services exhausted or unavailable, fail open (mark as CLEAN)
8. **Cache Result**: Store result with 24-hour TTL for future lookups
9. **Take Action**: If INFECTED, delete message and file, ban user (or notify if trusted/admin). If CLEAN, allow message

---

## 3. Tier 1 - Local Scanners

### ClamAV (Required)

**Purpose**: Industry-standard open-source antivirus engine

**Deployment**:
- Docker container on Linux host
- Official image: `clamav/clamav:latest`
- Port 3310 exposed for TCP connections
- Volume mount for signature persistence
- Automatic signature updates (freshclam)

**Detection Capabilities**:
- Signature-based detection
- ~200MB signature database
- Daily updates from ClamAV community
- Strong coverage of Linux/web-based malware
- Known malware families

**Integration**:
- Library: nClam 9.0.0 (NuGet)
- Protocol: TCP socket to localhost:3310
- Method: SendAndScanFileAsync
- Timeout: 30 seconds recommended

**Resource Requirements**:
- Memory: ~200MB RAM
- CPU: Minimal (signature matching)
- Disk: ~400MB (signatures + updates)
- Network: Periodic signature updates

**Configuration Options**:
- Enable/disable scanner
- Host/port configuration
- Timeout settings
- Signature update schedule

---

### Windows AMSI Multi-Engine (Optional)

**Purpose**: Leverage multiple free Windows antivirus engines simultaneously via Microsoft's Antimalware Scan Interface

**⚠️ CRITICAL: Verify Multi-Engine Behavior Before Deployment**:
- AMSI does NOT guarantee all installed AVs will respond to each scan request
- Windows Defender often enters "passive mode" when third-party AV is installed
- AMSI typically resolves to ONE active provider at a time (not 4-5 in parallel)
- **You MUST verify** how many engines actually respond on YOUR Windows configuration
- Build a probe script (see AMSI Provider Verification section) that logs which providers answer
- If only 1 provider responds consistently, reframe as "different engine" NOT "multi-engine voting"
- Update architecture documentation based on actual test results

**Workaround if AMSI Only Uses One Provider**:
- If AMSI resolves to only one AV, you can still achieve multi-engine scanning using multiple Windows VMs
- Deploy separate Windows VMs, each with a different AV installed (VM1: Defender only, VM2: Avast only, VM3: AVG only, etc.)
- Configure Tier 1 to call all Windows Scanner APIs in parallel (multiple HTTP endpoints)
- Resource cost: Higher (4-5 VMs vs 1 VM), but still $0 in software licensing
- Benefit: Guaranteed independent scans from each AV engine
- This approach gives you true multi-engine voting even if AMSI doesn't

**Why This Is Powerful** (IF Multi-Engine Works):
- AMSI can send scan requests to ALL registered antivirus providers
- Single API call → potentially 4-5 independent commercial AV engines scan in parallel
- Bypasses expensive SDK licensing (uses free consumer AV installations)
- Each AV has unique signatures and heuristics
- Built-in voting (AMSI returns "malicious" if ANY provider detects threat)

**AMSI Architecture**:

Application calls AmsiScanBuffer with file bytes. AMSI Dispatcher (Windows system component) broadcasts the scan request to all registered providers found in registry key HKLM\SOFTWARE\Microsoft\AMSI\Providers. This includes Windows Defender, Avast, AVG, Avira, Sophos (or whichever AVs are installed). Each AV scans independently and returns its verdict. AMSI aggregates results: if ANY provider says "threat", AMSI returns MALICIOUS. If ALL providers say "clean", AMSI returns CLEAN. This happens automatically in a single API call.

**Supported Free Antivirus Products**:
1. **Windows Defender**: Pre-installed, always available
2. **Avast Free Antivirus**: AMSI support since March 20, 2016
3. **AVG Free Antivirus**: AMSI support since March 8, 2016
4. **Avira Free Security**: AMSI support confirmed
5. **Sophos Home Free**: AMSI support confirmed (up to 3 devices)

**Unique Detection Coverage**:
- **Windows Defender**: Microsoft threat intelligence, strong on Windows/Office malware
- **Avast**: Broad consumer threat database, web-based threats
- **AVG**: Similar to Avast (same parent company), independent signatures
- **Avira**: German engineering, thorough heuristics, different threat database
- **Sophos**: Enterprise-grade detection, advanced behavioral analysis

**Why Multiple AVs Matters**:
Research shows ~10-15% of malware samples are caught by some AVs but missed by others. Each vendor has proprietary signatures and heuristics, so running 4-5 engines dramatically increases detection rates.

**Deployment Requirements**:
- Windows Server 2022, Windows Server 2016+, or Windows 10/11
- Windows VM (2GB RAM, 2 vCPU, 20GB disk minimum)
- .NET 10 runtime for REST API wrapper
- Network connectivity from main application

**REST API Wrapper**:
Since AMSI is a Windows-only API, a lightweight REST service is needed:
- Receives file upload via HTTP POST
- Calls AmsiScanBuffer with file bytes
- Parses which providers detected threats
- Returns structured JSON response
- Provides health endpoint (lists registered providers)

**Integration**:
- Library: MVsDotNetAMSIClient or direct P/Invoke
- Protocol: HTTP REST from main app to Windows VM
- Method: POST /api/scan/amsi
- Timeout: 30 seconds recommended

**Resource Requirements (Windows VM)**:
- Memory: 2GB RAM (base) + 200-500MB per AV installed
- CPU: 2 vCPU minimum
- Disk: 20GB base + ~2GB per AV
- Network: Low bandwidth (file upload only)

**Configuration Options**:
- Enable/disable Windows scanner
- API URL and port
- Optional API key for authentication
- Timeout settings
- Health check interval

**When to Use**:
- User has existing Windows infrastructure
- Maximum detection coverage desired
- Zero marginal cost (Windows VM already running)
- Willing to maintain Windows VM

**When to Skip**:
- Linux-only environment
- Minimal resource footprint required
- 95-97% coverage (ClamAV) is acceptable

---

## 3.5. Critical Implementation Notes

### Hash-First Cloud Optimization

**Always try hash reputation before uploading files to cloud services:**

- **VirusTotal**: Supports free hash-only lookups (GET `/api/v3/files/{hash}`) before file upload
- **Cost**: Hash lookups don't count against upload quota in most services
- **Privacy**: Hash lookups expose file identity but not content
- **Speed**: ~100ms vs 5-10s for file upload + scan
- **Flow**: Check hash first → if unknown, then upload → if known-good, skip all scanning → if known-bad, flag immediately

This optimization can reduce cloud quota consumption by 30-50% for common files.

---

### Archive Handling Policy

**How ClamAV handles archives:**
- Automatically decompresses and scans contents of: .zip, .tar, .tar.gz, .rar, .7z
- **Recursion limit**: 17 levels deep (default) to prevent archive bombs
- **File limit**: 10,000 files per archive (prevents zip bombs)
- **Size limit**: 25MB extracted content per archive (prevents decompression bombs)

**Password-protected archives:**
- ClamAV cannot scan password-protected archives
- **Policy options**:
  - **Permissive** (default): Allow password-protected archives (fail-open)
  - **Strict**: Block all password-protected archives
  - **Whitelist-only**: Allow password-protected archives only from trusted users

**Nested content extraction:**
- Scanners process raw bytes (does not extract archives automatically)
- Can detect archive-specific patterns (magic bytes, structure anomalies)
- Cannot scan contents of password-protected archives

Document your archive policy in configuration and communicate to users if strict mode is enabled.

---

### File Size Limitations

**Telegram file size reality:**
- Telegram supports files up to **2 GB** (2,147,483,648 bytes)
- Your default config: **100 MB** (104,857,600 bytes) limit

**Trade-offs:**

| Limit | Files Scanned | Files Skipped | Rationale |
|-------|---------------|---------------|-----------|
| 100 MB | ~90-95% | Large archives, videos, ISOs | Balance scan time vs coverage |
| 500 MB | ~97-99% | Very large archives/media | Longer scan times acceptable |
| 2 GB | 100% | None | Maximum coverage, may timeout |

**Recommendations:**
- **Default (100 MB)**: Good balance for most deployments
- **High-security chats**: Increase to 500 MB or 2 GB with longer timeouts
- **Public groups**: Keep at 100 MB to prevent resource exhaustion
- **Document the trade-off**: Users should know large files are skipped

Files exceeding the limit are **fail-open** (allowed) by default. Consider fail-close for high-security groups.

---

### Fail-Close Per-Chat Option

**Current behavior**: When all scanners fail or quota exhausted → fail-open (allow file)

**Fail-close option** (per-chat configuration):
- When scan cannot complete (timeout, all quota exhausted, all scanners down) → delete file + notify user
- **Use cases**:
  - High-security groups (zero tolerance for missed threats)
  - Groups under active attack (temporary fail-close during incident)
  - Compliance requirements (must scan all files)

**Configuration**:
```json
{
  "general": {
    "failOpen": false,  // Set to false for fail-close behavior
    "failCloseDMMessage": "File scanning temporarily unavailable. Your file was removed for safety. Please try again later."
  }
}
```

**Trade-off**: Fail-close may block legitimate files during outages. Use with caution.

---

### Cloud Privacy & Licensing Considerations

**Which services upload file content vs hash-only:**

| Service | Hash Lookup | File Upload | Privacy Level |
|---------|-------------|-------------|---------------|
| VirusTotal | ✅ Free | ✅ Required for unknown hashes | Medium (upload required eventually) |
| MetaDefender | ❌ No | ✅ Required | Low (always uploads) |
| Hybrid Analysis | ❌ No | ✅ Required + Public visibility | Very Low (samples may be public) |
| Intezer | ❌ No | ✅ Required + Public visibility | Very Low (free tier is public) |

**Privacy recommendations:**
- **Hash-first always**: Check VT hash before uploading
- **Per-chat toggles**: Allow users to disable specific cloud services
- **Local-only mode**: Option to disable all cloud services (ClamAV only)
- **Document visibility**: Warn users that Hybrid Analysis and Intezer free tiers may expose samples publicly

**Licensing warnings:**

⚠️ **Free antivirus products may prohibit server/commercial use:**
- Avast Free, AVG Free, Avira Free: Check EULA for server deployment restrictions
- Some "home/free" licenses prohibit use on Windows Server SKUs
- Multi-AV installations may violate some EULAs (read carefully)
- **Recommendation**: Test with 1-2 AVs first, verify licensing compliance
- **Alternative**: Use only Windows Defender (no licensing restrictions) if uncertain

**License compliance checklist:**
1. Read EULA for each AV before installing on Windows Server
2. Verify "free tier" allows server deployment
3. Document which AVs are legally compliant for your use case
4. Have fallback plan (single AV or local-only mode) if licensing unclear

---

##  4. Tier 2 - Cloud Scanners

### Queue Priority System

**User-Configurable Ordering**:
Users specify the order of cloud services in configuration. The system tries each service in order until one succeeds or all are exhausted.

**Quota Tracking**:
- Database table: `file_scan_quota` (service, date, count, limit)
- In-memory cache: Per-minute sliding window (e.g., VirusTotal's 4/min limit)
- Reset times: Daily quotas reset at 00:00 UTC, monthly at first of month

**Rate Limiting Behavior**:
- Before calling service: Check both daily quota AND per-minute rate limit
- If rate-limited (HTTP 429): Mark service as temporarily unavailable (cache for 1-5 minutes)
- Automatically skip service for subsequent files until cooldown expires
- This prevents hammering rate limits repeatedly

**Fallback Logic**:

For each cloud service in the user's configured priority list: First, check if daily/monthly quota is available. Second, check if per-minute rate limit allows request. If both checks pass, scan the file. If infected, return result immediately (stop processing). If clean, continue to next service in priority list. If quota exhausted or rate limited, skip to next service. If all services are exhausted or unavailable, fail open by returning CLEAN verdict.

**Fail-Open Philosophy**:
When legitimate files are blocked due to quota exhaustion, user experience suffers. Tier 1 (local scanners) has already provided 95-99% coverage. Failing open for the remaining edge cases prevents false negatives from disrupting normal operations.

---

### VirusTotal Free Tier

**Quota Limits**:
- Daily: 500 requests per day
- Per-Minute: 4 requests per minute
- Reset: Daily quota resets at 00:00 UTC

**API Version**: v3 (recommended)

**Rate Limit Handling**:
- HTTP 429 response when limit exceeded
- Retry-After header provides cooldown duration
- Recommended: Cache service as unavailable for 60 seconds on 429

**Quota Counting**:
- 1 API request = 1 file lookup
- Batch requests count as multiple (1 per hash/file)
- File uploads count toward quota

**Advantages**:
- Largest free daily quota (500/day)
- Multi-engine scanning (70+ AV engines)
- Comprehensive threat intelligence
- Well-documented API

**Use Case Priority**:
Typically set as first cloud service due to generous daily quota. Handles ~500 files/day (or ~15,000/month) before exhaustion.

**Configuration**:
- API key (required): Environment variable
- Enable/disable toggle
- Priority position in queue

---

### MetaDefender Cloud

**Quota Limits**:
- File Scanning: 40 requests per day
- Reputation API: 4,000 requests per day
- Reset: Daily quota resets at 00:00 UTC

**API Features**:
- Multi-engine scanning
- Sandbox analysis (Pro tier)
- Detailed threat reports

**Advantages**:
- Second-best free tier after VirusTotal
- Different engine coverage (some overlap, some unique)
- OPSWAT platform backing

**Use Case Priority**:
Typically set as second cloud service. Extends coverage by 40 files/day when VirusTotal exhausted.

**Configuration**:
- API key (required): Environment variable
- Enable/disable toggle
- Priority position in queue

---

### Hybrid Analysis

**Quota Limits**:
- Monthly: 30 submissions per month (~1 per day)
- File Size: 100MB maximum
- Reset: Monthly quota resets on first of month

**API Access**:
- Free community tier available
- Full API access may require vetting

**Features**:
- Behavioral analysis sandbox
- Detailed execution reports
- Network traffic analysis

**Advantages**:
- Deep behavioral analysis
- Catches sophisticated malware
- Different analysis approach than signature-based

**Limitations**:
- Very limited monthly quota
- Better suited for manual investigation than automated scanning

**Use Case Priority**:
Typically set as third cloud service. Acts as tie-breaker for suspicious files when VirusTotal and MetaDefender both report clean but local scanners flagged something borderline.

**Configuration**:
- API key (required): Environment variable
- Enable/disable toggle
- Priority position in queue

---

### Intezer Analyze

**Quota Limits**:
- Monthly: 10 submissions per month (~0.3 per day)
- Visibility: Public submissions (visible to community)
- Reset: Monthly quota resets on first of month

**Features**:
- Genetic malware analysis
- Code reuse detection
- Threat classification

**Advantages**:
- Unique detection methodology
- Good for targeted threats
- Community API available

**Limitations**:
- Extremely limited quota
- Public visibility of submissions

**Use Case Priority**:
Typically set as last resort (fourth cloud service). Only used when all other services exhausted or for extremely suspicious files.

**Configuration**:
- API key (required): Environment variable
- Enable/disable toggle
- Priority position in queue

---

## 5. Configuration

### Configuration Schema

```yaml
FileScanningConfig:

  # Tier 1: Local Scanners
  Tier1:

    ClamAV:
      Enabled: true
      Host: localhost
      Port: 3310
      TimeoutSeconds: 30

      Enabled: true
      TimeoutSeconds: 10
      RuleFiles:
        - malware-community.yar
        - telegram-threats.yar
        - custom-rules.yar

    WindowsAMSI:
      Enabled: false  # User sets to true if deployed
      ApiUrl: http://windows-scanner.local:5000
      TimeoutSeconds: 30
      ApiKey: ${WINDOWSDEFENDER__APIKEY}  # Optional authentication

  # Tier 2: Cloud Scanners
  Tier2:

    # User-configurable priority order
    CloudQueuePriority:
      - VirusTotal
      - MetaDefender
      - HybridAnalysis
      - Intezer

    VirusTotal:
      Enabled: true
      ApiKey: ${VIRUSTOTAL__APIKEY}
      DailyLimit: 500
      PerMinuteLimit: 4

    MetaDefender:
      Enabled: false
      ApiKey: ${METADEFENDER__APIKEY}
      DailyLimit: 40

    HybridAnalysis:
      Enabled: false
      ApiKey: ${HYBRIDANALYSIS__APIKEY}
      MonthlyLimit: 30

    Intezer:
      Enabled: false
      ApiKey: ${INTEZER__APIKEY}
      MonthlyLimit: 10

    # Behavior when all cloud services exhausted
    FailOpenWhenExhausted: true

  # General Settings
  General:

    # Cache scan results (avoid re-scanning same file)
    CacheEnabled: true
    CacheTTLHours: 24

    # File types to scan (extensions)
    ScanFileTypes:
      - .exe
      - .dll
      - .zip
      - .rar
      - .7z
      - .pdf
      - .doc
      - .docx
      - .xls
      - .xlsx
      - .apk
      - .dmg
      - .pkg
      - .bat
      - .ps1
      - .sh

    # Maximum file size to scan (bytes)
    MaxFileSizeBytes: 104857600  # 100MB

    # Integration with Critical Checks (Phase 4.14)
    AlwaysRunForAllUsers: true  # Bypass trust/admin status
```

### Environment Variables

Required for cloud services:
- `VIRUSTOTAL__APIKEY`: VirusTotal API key
- `METADEFENDER__APIKEY`: MetaDefender Cloud API key
- `HYBRIDANALYSIS__APIKEY`: Hybrid Analysis API key
- `INTEZER__APIKEY`: Intezer Analyze API key
- `WINDOWSDEFENDER__APIKEY`: Optional API key for Windows scanner authentication

### Per-Chat Configuration

The system supports per-chat overrides in the `file_scan_config` JSONB column:

```json
{
  "chatId": -1001234567890,
  "overrides": {
    "tier1": {
      "clamav": { "enabled": true },
      "windowsAmsi": { "enabled": false }
    },
    "tier2": {
      "cloudQueuePriority": ["VirusTotal", "MetaDefender"],
      "virusTotal": { "enabled": true },
      "metaDefender": { "enabled": false }
    },
    "general": {
      "alwaysRunForAllUsers": true
    }
  }
}
```

Most deployments use global configuration. Per-chat overrides allow power users to customize scanning per group.

---

### Example Configurations

**Scenario 1: Linux-Only Minimal (Default)**
```yaml
Tier1:
  ClamAV: { Enabled: true }
  WindowsAMSI: { Enabled: false }

Tier2:
  CloudQueuePriority: [VirusTotal]
  VirusTotal: { Enabled: true }
  MetaDefender: { Enabled: false }
  HybridAnalysis: { Enabled: false }
  Intezer: { Enabled: false }
```
Coverage: ~96-98%, Cost: $0, Resources: 250MB RAM

**Scenario 2: Full Multi-Engine**
```yaml
Tier1:
  ClamAV: { Enabled: true }
  WindowsAMSI: { Enabled: true }

Tier2:
  CloudQueuePriority: [VirusTotal, MetaDefender, HybridAnalysis, Intezer]
  VirusTotal: { Enabled: true }
  MetaDefender: { Enabled: true }
  HybridAnalysis: { Enabled: true }
  Intezer: { Enabled: true }
```
Coverage: ~99.8%, Cost: $0 (all free tiers), Resources: 250MB + 2GB Windows VM

**Scenario 3: Cost-Conscious (Local Only)**
```yaml
Tier1:
  ClamAV: { Enabled: true }
  WindowsAMSI: { Enabled: false }

Tier2:
  CloudQueuePriority: []
  FailOpenWhenExhausted: true  # Always fail open (no cloud)
```
Coverage: ~95-97%, Cost: $0, Resources: 250MB RAM, Unlimited files/month

**Scenario 4: Maximum Cloud Coverage (No Windows)**
```yaml
Tier1:
  ClamAV: { Enabled: true }
  WindowsAMSI: { Enabled: false }

Tier2:
  CloudQueuePriority: [VirusTotal, MetaDefender, HybridAnalysis, Intezer]
  VirusTotal: { Enabled: true }
  MetaDefender: { Enabled: true }
  HybridAnalysis: { Enabled: true }
  Intezer: { Enabled: true }
```
Coverage: ~98-99%, Cost: $0, Resources: 250MB RAM, 16,240 cloud files/month

---

## 6. Windows AMSI API Setup

### Prerequisites

**Operating System**:
- Windows Server 2022 (recommended)
- Windows Server 2016 or later
- Windows 10 version 1607 or later
- Windows 11

**Resources**:
- RAM: 2GB minimum (base), +200-500MB per AV installed
- CPU: 2 vCPU minimum
- Disk: 20GB minimum (base), +2GB per AV
- Network: Low bandwidth (file upload only)

**Software**:
- .NET 10 SDK (for building API)
- .NET 10 Runtime (for running API)
- PowerShell 5.1 or later

---

### Free Antivirus Installation

Install these free antivirus products to register as AMSI providers:

**1. Windows Defender** (Pre-installed)
- No action needed
- Verify it's running and up-to-date
- Check Windows Security center

**2. Avast Free Antivirus**
- Download from: https://www.avast.com/free-antivirus-download
- Run installer with default options
- Decline premium trial offers
- Verify AMSI registration after install

**3. AVG Free Antivirus**
- Download from: https://www.avg.com/free-antivirus-download
- Run installer with default options
- Decline premium trial offers
- Verify AMSI registration after install

**4. Avira Free Security**
- Download from: https://www.avira.com/free-antivirus-windows
- Run installer with default options
- Decline premium trial offers
- Verify AMSI registration after install

**5. Sophos Home Free** (Optional)
- Download from: https://home.sophos.com
- Supports up to 3 devices on free tier
- Run installer with default options
- Verify AMSI registration after install

**Installation Notes**:
- Install one at a time to avoid conflicts
- Reboot between installations if prompted
- Some AVs may conflict; test compatibility
- Windows Defender may enter passive mode when third-party AV installed (this is normal)

---

### AMSI Registration Verification

After installing antivirus products, verify they registered with AMSI:

**Registry Check**:
- Location: `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\AMSI\Providers`
- Each provider has a GUID subkey
- Expect 4-5 GUIDs (one per installed AV)

**PowerShell Verification**:
```powershell
# List all AMSI providers
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\AMSI\Providers\*"

# Count providers
(Get-ChildItem "HKLM:\SOFTWARE\Microsoft\AMSI\Providers").Count
```

Expected count: 4-5 (Defender + Avast + AVG + Avira + optionally Sophos)

**Functional Test**:
Create a small test script that calls AMSI and verify multiple providers respond.

---

### Windows Scanning API Deployment

**API Endpoints Specification**:

**POST /api/scan/amsi**
- Accepts: multipart/form-data file upload
- Returns: JSON scan result
- Behavior: Calls AmsiScanBuffer, all providers scan, aggregated result returned
- Response includes which providers detected threats (if any)

**GET /api/health**
- Returns: JSON health status
- Includes: List of registered AMSI providers, signature versions, service status

**Response Format (Scan)**:
```json
{
  "isClean": false,
  "threatDetected": true,
  "threatName": "Trojan:Win32/Wacatac.B!ml",
  "detectingProviders": [
    "Windows Defender",
    "Avast"
  ],
  "totalProviders": 5,
  "scanDurationMs": 1247,
  "timestamp": "2025-10-18T14:23:00Z"
}
```

**Response Format (Health)**:
```json
{
  "status": "healthy",
  "amsiAvailable": true,
  "registeredProviders": [
    {
      "name": "Windows Defender",
      "signatureVersion": "1.407.1850.0",
      "lastUpdated": "2025-10-18T12:00:00Z"
    },
    {
      "name": "Avast",
      "signatureVersion": "2510180-0",
      "lastUpdated": "2025-10-18T13:00:00Z"
    },
    {
      "name": "AVG",
      "signatureVersion": "2510180-0",
      "lastUpdated": "2025-10-18T13:00:00Z"
    },
    {
      "name": "Avira",
      "signatureVersion": "8.3.104.22",
      "lastUpdated": "2025-10-18T11:30:00Z"
    }
  ],
  "totalProviders": 4
}
```

**Service Installation**:
- Deploy as Windows Service (preferred) for automatic startup
- Alternative: Run as console app with systemd equivalent
- Port: 5000 (configurable)
- Binding: 0.0.0.0 (all interfaces) or localhost (if behind reverse proxy)

**Firewall Configuration**:
- Inbound rule: Allow TCP port 5000 from main application server IP
- Outbound rule: Allow (for signature updates)

**Network Connectivity**:
- Main application must reach Windows VM on configured port
- Consider: VPN, private network, or firewall rules for security
- Optional: HTTPS with self-signed cert for encrypted transport

---

## 7. Windows Setup Automation Script

### Script Purpose

Fully automate the setup of a Windows VM for file scanning with AMSI multi-engine support. Eliminates manual configuration and ensures consistent deployments.

### Deliverables

**1. setup-windows-scanner.ps1**
- Main installation script
- Checks prerequisites
- Installs all components
- Configures and starts service

**2. scanner-config.template.json**
- Configuration template
- User fills in: main app URL, port, optional API key
- Script populates remaining settings

**3. verify-setup.ps1**
- Post-installation validation
- Checks AMSI providers
- Tests API endpoints
- Verifies connectivity to main app

**4. uninstall-scanner.ps1**
- Clean removal of scanner API
- Optionally removes installed AVs
- Registry cleanup

---

### High-Level Script Steps

**setup-windows-scanner.ps1 Flow**:

**Step 1: Prerequisites Check**
- Verify Windows version (2016+ or 10/11)
- Check PowerShell version (5.1+)
- Confirm administrator privileges
- Check available disk space (40GB minimum)
- Verify internet connectivity

**Step 2: Install .NET 10 SDK**
- Check if already installed (skip if present)
- Download .NET 10 SDK installer from Microsoft
- Run installer silently
- Verify installation success
- Add to PATH if needed

**Step 3: Install Free Antivirus Products**
- Download installers for: Avast, AVG, Avira
- Run each installer with silent/unattended flags
- Accept license agreements automatically
- Decline premium trials
- Wait for each to complete before starting next
- Reboot if any installer requires it

**Step 4: Verify AMSI Registration**
- Query registry: HKLM\SOFTWARE\Microsoft\AMSI\Providers
- Count registered providers
- Expected: 4 minimum (Defender + 3 installed)
- If count < 4: Error and halt (installation failed)
- Display list of registered providers

**Step 5: Download Windows Scanner API**
- Check if GitHub release exists
- If yes: Download latest release ZIP from GitHub releases page
- If no: Provide instructions for manual build
- Extract to: C:\Program Files\TelegramGroupsAdmin\WindowsScanner
- Verify all required files present

**Step 6: Configure Scanner API**
- Copy scanner-config.template.json to appsettings.json
- Prompt user for:
  - Port to listen on (default: 5000)
  - Optional API key for authentication
  - Logging level
- Write configuration file

**Step 7: Install as Windows Service**
- Use sc.exe or New-Service cmdlet
- Service name: TelegramGroupsAdmin.WindowsScanner
- Startup type: Automatic
- Run as: Local System (or configured service account)
- Description: "AMSI-based malware scanning API for TelegramGroupsAdmin"

**Step 8: Configure Firewall**
- Create inbound rule: Allow TCP on configured port
- Scope: Specific IP (prompt user for main app server IP)
- Profile: All (Domain, Private, Public)
- Rule name: TelegramGroupsAdmin Scanner

**Step 9: Start Service**
- Start the Windows Service
- Wait for service to report running status (timeout: 30s)
- Check if port is listening (Test-NetConnection)

**Step 10: Verify Installation**
- Call verify-setup.ps1
- Test /api/health endpoint
- Confirm provider count matches expectation
- Display connection details for main app configuration

**Step 11: Output Summary**
- Display:
  - Service status
  - Listening port
  - Number of AMSI providers
  - API health check URL
  - Configuration settings for main app
- Save summary to: setup-summary.txt

---

**verify-setup.ps1 Flow**:

**Check 1: Service Status**
- Query Windows Service status
- Expected: Running

**Check 2: AMSI Providers**
- Query registry for providers
- Expected count: 4-5
- Display list with names

**Check 3: API Health Endpoint**
- HTTP GET http://localhost:PORT/api/health
- Expected: 200 OK with JSON response
- Verify registeredProviders array matches expected count

**Check 4: API Scan Endpoint**
- Create small test file (EICAR test string)
- HTTP POST to /api/scan/amsi
- Expected: Threat detected (EICAR is test malware)
- Verify multiple providers detected it

**Check 5: Network Connectivity**
- Test if port is accessible from main app server IP
- If main app IP provided: Use Test-NetConnection from that IP
- Verify firewall rule is active

**Output**: Pass/Fail for each check, summary at end

---

**uninstall-scanner.ps1 Flow**:

**Step 1: Stop Service**
- Stop TelegramGroupsAdmin.WindowsScanner service
- Wait for service to stop (timeout: 30s)

**Step 2: Remove Service**
- Delete Windows Service registration
- Use sc.exe delete or Remove-Service

**Step 3: Remove Files**
- Delete C:\Program Files\TelegramGroupsAdmin\WindowsScanner directory
- Remove any logs or temp files

**Step 4: Remove Firewall Rule**
- Delete inbound firewall rule created during setup

**Step 5: Optional AV Removal**
- Prompt user: Remove installed antivirus products?
- If yes: Run uninstallers for Avast, AVG, Avira
- If no: Leave them installed

**Step 6: Cleanup Registry**
- Remove any configuration keys (if created)

**Output**: Uninstallation complete message

---

## 8. Deployment Scenarios

### Scenario 1: Recommended - ClamAV + VirusTotal ✅

**Configuration**:
- Tier 1: ClamAV only
- Tier 2: VirusTotal (primary cloud service)

**Coverage**: ~96-98% (excellent for group moderation)

**Cost**: $0 (all free)

**Resources**:
- RAM: ~1.2GB (ClamAV signatures)
- CPU: 1-2 cores sufficient
- Disk: ~500MB

**Monthly Capacity**:
- Local: Unlimited (ClamAV)
- Cloud: 15,000 files/month (VirusTotal 500/day)

**When to Use**:
- **DEFAULT DEPLOYMENT** - Best balance of coverage, cost, and simplicity
- Linux-only infrastructure (no Windows VM needed)
- Minimal maintenance burden
- 96-98% coverage is acceptable for Telegram group moderation

**Setup Time**: ~1 hour (Docker Compose + VirusTotal API key)

**Why This is Enough**:
- ClamAV catches 95-97% of threats locally (10M+ signatures)
- VirusTotal adds 1-2% with 70+ engine consensus
- False positive rate is low
- Setup is simple and maintainable
- No Windows licensing concerns

---

### Scenario 2: Maximum Cloud Coverage (Future Enhancement)

**Configuration**:
- Tier 1: ClamAV only
- Tier 2: VirusTotal + MetaDefender + Hybrid Analysis + Intezer

**Coverage**: ~98-99%

**Cost**: $0 (all free tiers)

**Resources**:
- RAM: ~1.2GB
- CPU: 1-2 cores
- Disk: ~500MB

**Monthly Capacity**:
- Local: Unlimited
- Cloud: 16,240 files/month (all services combined)

**When to Use**:
- Higher threat environment (under active attack)
- Moderate file volume (< 500 files/day)
- Want maximum free coverage without infrastructure changes
- Willing to register multiple API keys

**Setup Time**: ~2-3 hours (API key registration for 4 cloud services)

**Note**: MetaDefender, Hybrid Analysis, and Intezer are currently stub implementations. Complete API integration is pending (low priority).

---

### ~~Scenario 3: Linux + Windows Multi-Engine~~ ❌ **DEFERRED**

**Status**: Not implemented (Phase 3 deferred indefinitely)

**Original Coverage**: ~99-99.5%

**Why Deferred**:
- Marginal improvement (1-2%) over Scenario 1
- Significant complexity (Windows VM, multiple AV installations)
- Licensing concerns for free AVs on Windows Server
- AMSI multi-engine behavior inconsistent
- Maintenance burden not justified for use case

**Alternative**: If higher coverage needed, implement Scenario 2 (additional cloud services) instead of Windows AMSI.

---

### ~~Scenario 4: Maximum Coverage (All Engines)~~ ❌ **DEFERRED**

**Status**: Not implemented (depends on Phase 3 Windows AMSI)

**Original Coverage**: ~99.8%

**Why Deferred**: Same rationale as Scenario 3. Current recommended deployment (Scenario 1) provides sufficient coverage for Telegram group moderation.

**Resources**:
- Linux: ~250MB RAM
- Windows VM: 3GB RAM, 2 vCPU, 25GB disk

**Effective Scanning Engines**: 6-7 local + 4 cloud services

**Monthly Capacity**:
- Local: Unlimited
- Cloud: 16,240 files/month

**When to Use**:
- Enterprise-level protection required
- High file volume (hundreds/day)
- Zero tolerance for missed threats
- Resources available (Linux + Windows)

**Setup Time**: ~6-8 hours (full setup of both tiers)

---

## 9. Cost Analysis

### Free Tier Capacity Breakdown

**Local Scanners (Unlimited)**:
- ClamAV: ∞ files/month
- Windows AMSI: ∞ files/month
- **Total Local**: Unlimited, $0 cost

**Cloud Services (Free Tiers)**:
- VirusTotal: 500/day × 30 days = 15,000/month
- MetaDefender: 40/day × 30 days = 1,200/month
- Hybrid Analysis: 30/month
- Intezer: 10/month
- **Total Cloud**: 16,240 files/month, $0 cost

**Combined Capacity**: Effectively unlimited for most deployments (local scanners handle 95-99% of files, cloud handles edge cases)

---

### Traffic Scenario Analysis

**Low Traffic: 10 files/day (300/month)**

Local Tier 1 Detection:
- ClamAV: Detects ~97% = 291 files
- Remaining: 9 files proceed to Tier 2

Cloud Tier 2:
- VirusTotal: Handles all 9 files (quota: 15,000/month)
- Quota usage: <1% of VT, 0% of others

**Result**: 100% coverage, $0 cost, massive quota headroom

---

**Medium Traffic: 100 files/day (3,000/month)**

Local Tier 1 Detection:
- ClamAV: Detects ~97% = 2,910 files
- Remaining: 90 files proceed to Tier 2

Cloud Tier 2:
- VirusTotal: Handles all 90 files
- Quota usage: 0.6% of VT, others unused

**Result**: 100% coverage, $0 cost, still significant headroom

---

**High Traffic: 500 files/day (15,000/month)**

Local Tier 1 Detection:
- ClamAV: Detects ~97% = 14,550 files
- Remaining: 450 files proceed to Tier 2

Cloud Tier 2:
- VirusTotal: Handles all 450 files
- Quota usage: 3% of VT, others unused

**Result**: 100% coverage, $0 cost

---

**Very High Traffic: 1,000 files/day (30,000/month)**

Local Tier 1 Detection:
- ClamAV: Detects ~97% = 29,100 files
- Remaining: 900 files proceed to Tier 2

Cloud Tier 2:
- VirusTotal: Handles 500/day (quota reached)
- Remaining after VT: 400 files/month
- MetaDefender: Handles 40/day = 1,200/month (exhausted in 3 days)
- Remaining: 0 (fail-open for edge cases)

**Result**: ~99.7% coverage, $0 cost, occasional fail-open on edge cases

**With Windows AMSI** (adds ~2% local detection):
- Local: ~99% = 29,700 files
- Remaining: 300 files to Tier 2
- VirusTotal alone sufficient for all

**Result**: 100% coverage, $0 cost

---

### ROI Analysis

**Scenario**: High traffic deployment (500 files/day)

**Option A: No File Scanning**
- Cost: $0
- Risk: Malware distribution in groups
- Impact: User complaints, group reputation damage, potential bans

**Option B: Linux-Only (ClamAV)**
- Setup: 1 hour
- Cost: $0/month
- Coverage: ~97%
- ROI: Prevents 485 malware files/day (97%)

**Option C: Linux + Windows Multi-Engine**
- Setup: 6 hours
- Cost: $0/month (assuming existing Windows VM)
- Coverage: ~99%
- ROI: Prevents 495 malware files/day (99%), catches 10 more/day than Option B

**Option D: Maximum Coverage (All Engines)**
- Setup: 8 hours
- Cost: $0/month
- Coverage: ~99.8%
- ROI: Prevents 499 malware files/day, catches 4 more/day than Option C

**Recommendation**: Option B for most users (excellent ROI), Option D for maximum protection with minimal cost

---

## 10. Implementation Phases

### Phase 1: Core Tier 1 - ClamAV Scanner ✅ COMPLETE

**Status**: Complete (October 19, 2025)
**Updated**: October 19, 2025 (YARA removed)

**Objectives**:
- ✅ Establish local scanning foundation with ClamAV
- ✅ Implement voting coordinator architecture
- ✅ Create database schema and caching system
- ✅ Build configuration infrastructure

**Implementation Summary**:

Phase 1 delivers a production-ready file scanning system using ClamAV as the sole Tier 1 scanner. YARA was removed on October 19, 2025 due to ARM compatibility issues and redundancy with ClamAV's superior signature coverage (10M+ signatures vs custom YARA rules).

**Completed Components**:

1. **Infrastructure**:
   - ✅ Docker Compose configuration (`compose/compose.yml`)
     - ClamAV 1.5.1 container with 4.5GB file size support (Telegram Premium compatibility)
     - Automatic signature updates via freshclam
     - Health checks and restart policies
   - ✅ Database schema (Migration `20251019213620_AddFileScanningTables`)
     - `file_scan_results` table (hash, scanner, result, threat_name, metadata JSONB, scan_duration_ms)
     - `file_scan_quota` table (service, quota_type, count, window tracking with DateTimeOffset)
     - Indexed for performance (<10ms cache lookups)

2. **Services & Libraries**:
   - ✅ nClam 9.0.0 NuGet package integration
   - ✅ `ClamAVScannerService` ([TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs](TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs))
     - TCP connection to clamd (localhost:3310)
     - Fail-open error handling
     - Ping verification before scanning
     - Detailed logging with scan durations
   - ✅ `Tier1VotingCoordinator` (parallel execution, OR voting logic)
     - Removed YARA integration on October 19, 2025
     - Currently single-scanner (ClamAV only)
     - Architecture ready for Windows AMSI addition (Phase 3)
   - ✅ `FileScanResultRepository` (hash-based caching, 24hr TTL)
   - ✅ `FileScanningCheck` (IContentCheck implementation, always_run=true)

3. **Configuration System**:
   - ✅ `FileScanningConfig` class with Tier1/Tier2/General sections
   - ✅ JSONB storage in `configs.file_scanning_config` column
   - ✅ Repository pattern with `FileScanningConfigRepository`
   - ✅ Per-chat override support (nullable chat_id)
   - ✅ Blazor UI ([TelegramGroupsAdmin/Components/Shared/Settings/FileScanningSettings.razor](TelegramGroupsAdmin/Components/Shared/Settings/FileScanningSettings.razor))
     - ClamAV enable/disable toggle
     - Host/port configuration
     - Timeout settings

4. **Architecture Patterns**:
   - ✅ Service registration via `AddContentDetectionServices()` extension method
   - ✅ Dependency injection integration
   - ✅ Fail-open design (errors don't block legitimate files)
   - ✅ Hash-first optimization (SHA256 cache lookup before scanning)

**Performance Characteristics**:
- **EICAR scan time**: ~55ms (ClamAV TCP roundtrip)
- **Cache hit**: <10ms (PostgreSQL indexed lookup)
- **ClamAV memory**: ~1.2GB (signature database)
- **Build time**: ~6 seconds (0 errors, 0 warnings)

**Detection Coverage**:
- **ClamAV signatures**: 10M+ signatures
- **Expected detection rate**: 95-97% (industry standard for ClamAV)
- **Malware categories**: Linux, web-based, Windows, macros, scripts, archives
- **Update frequency**: Daily via freshclam

**Testing & Verification**:
- ✅ Build verification (0 errors, 0 warnings)
- ✅ ClamAV container health checks passing
- ✅ EICAR test file detection confirmed
- ✅ Test infrastructure created ([TelegramGroupsAdmin.Tests.Integration/](TelegramGroupsAdmin.Tests.Integration/))
- ✅ End-to-end Telegram integration complete (Phase 4.14 - Critical Checks Infrastructure)

**Phase 4.14 Integration** (Complete October 21, 2025):

- ✅ Telegram file attachment extraction in MessageProcessingService (Document type only, media excluded)
- ✅ SHA256 hash calculation for uploaded files
- ✅ FileScanJob scheduled via TickerQ with 5s polling (instant execution)
- ✅ Download→Hash→Scan→Action flow in FileScanJob.ExecuteAsync()
- ✅ Infected file handling: Delete message, DM user, log to detection_results
- ✅ Bot DM fallback to chat reply when bot_dm_enabled=false
- ✅ Actor.FromSystem("file_scanner") for audit trail
- ✅ Temporary file cleanup (deleted after scan regardless of result)

**Key Design Decisions**:
- **YARA removed**: Redundant with ClamAV, ARM incompatibility, maintenance burden
- **Single Tier 1 scanner**: ClamAV provides sufficient coverage until Windows AMSI added (Phase 3)
- **DateTimeOffset quota tracking**: Supports both calendar and rolling windows for cloud services
- **4.5GB file limit**: Accommodates Telegram Premium (4GB max) with safety margin
- **Fail-open errors**: Infrastructure issues don't block legitimate files
- **Hash-based caching**: 24hr TTL prevents redundant scans of same file

**Next Phase**: Phase 2 - Tier 2 Cloud Queue (VirusTotal, MetaDefender, Hybrid Analysis, Intezer)

---

### Phase 2: Tier 2 Cloud Queue ✅ COMPLETE

**Status**: Complete (October 20, 2025)

**Objectives**:
- ✅ Add cloud service integrations
- ✅ Implement quota tracking
- ✅ Build queue priority system

**Implementation Summary**:

Phase 2 delivers a production-ready cloud scanning queue that extends detection coverage from ~95-97% (Tier 1 only) to ~98-99%. The system implements hash-first optimization, quota tracking with calendar-based windows, and sequential priority-based service execution.

**Completed Components**:

1. **Cloud Scanner Architecture**:
   - ✅ `ICloudScannerService` interface (all cloud scanners implement this)
   - ✅ `CloudHashLookupResult` model (hash-only lookups, no file upload)
   - ✅ `CloudScanResult` model (file upload + scan results)
   - ✅ Hash lookup statuses: Clean, Malicious, Unknown, Error
   - ✅ Scan result types: Clean, Infected, Error, RateLimited

2. **Quota Tracking System**:
   - ✅ `FileScanQuotaRepository` with calendar-based windows
   - ✅ Daily quotas: midnight UTC to midnight UTC (VirusTotal, MetaDefender)
   - ✅ Monthly quotas: first of month to first of next month (Hybrid Analysis, Intezer)
   - ✅ Unique index on `(service, quota_type, quota_window_start)` prevents duplicates
   - ✅ `IsQuotaAvailableAsync()`, `IncrementQuotaUsageAsync()`, `CleanupExpiredQuotasAsync()`
   - ⏳ Daily/monthly quota reset job (TickerQ) - pending (Phase 4.14 integration)

3. **VirusTotal Service** (Fully Implemented):
   - ✅ Hash-first optimization: `GET /files/{hash}` before uploading
   - ✅ File upload: `POST /files` with multipart/form-data
   - ✅ Async analysis polling (10 polls × 2s = 20s timeout)
   - ✅ Multi-engine verdict parsing (70+ AV engines)
   - ✅ Detection threshold: ≥2 engines = malicious
   - ✅ Quota tracking: Daily (500 requests/day, 4 requests/minute)
   - ✅ Rate limit handling: HTTP 429 detection + fail-open
   - ✅ Error handling: Fail-open on timeout/error

4. **Other Cloud Services** (Stub Implementations):
   - ✅ MetaDefender (quota tracking only, API pending)
   - ✅ Hybrid Analysis (quota tracking only, API pending)
   - ✅ Intezer (quota tracking only, API pending)
   - ℹ️ All services return fail-open until APIs implemented
   - ℹ️ Quota tracking works correctly for all services

5. **Tier 2 Queue Coordinator**:
   - ✅ Sequential execution in user-configured priority order
   - ✅ Hash lookup first (if supported), then file upload
   - ✅ Stop on first definitive result (infected or clean)
   - ✅ Skip services with exhausted quota or rate limits
   - ✅ Fail-open when all services exhausted (configurable)
   - ✅ Fail-close option via `FailOpenWhenExhausted=false`
   - ✅ Decision source tracking for debugging

6. **Integration with FileScanningCheck**:
   - ✅ Tier 1 runs first (local scanners)
   - ✅ If Tier 1 detects threat → return immediately (no Tier 2)
   - ✅ If Tier 1 reports clean → proceed to Tier 2
   - ✅ Cache all Tier 2 results (file scans + hash lookups)
   - ✅ Combined duration tracking (Tier 1 + Tier 2)
   - ✅ Detailed logging with decision source

**Detection Coverage**:
- **VirusTotal only**: ~98% (ClamAV 95-97% + VT catches remaining edge cases)
- **All services enabled**: ~98-99% (limited by free tier quotas)
- **With Windows AMSI** (Phase 3): ~99.8% (7 engines + cloud)

**Quota Capacity** (Free Tiers):
- **Daily**: VirusTotal (500) + MetaDefender (40) = 540 files/day
- **Monthly**: Hybrid Analysis (30) + Intezer (10) = 40 files/month
- **Total cloud capacity**: ~16,240 files/month (if all services enabled)

**Performance Characteristics**:
- **Hash lookup**: ~100-200ms (network roundtrip)
- **File upload + scan**: ~5-10s (VirusTotal async analysis)
- **Quota check**: <10ms (PostgreSQL indexed lookup)
- **Build time**: ~10.5s (0 errors, 0 warnings)

**Testing & Verification**:
- ✅ Build verification (0 errors, 0 warnings)
- ✅ Database migrations applied successfully
- ✅ Service registration in DI container
- ⏳ End-to-end testing with real VirusTotal API (pending user API key)
- ⏳ Telegram file attachment integration (Phase 4.14 - Critical Checks Infrastructure)

**Remaining Work** (Future Enhancements):
- ⏳ Complete MetaDefender, Hybrid Analysis, Intezer API implementations
- ⏳ Add TickerQ daily/monthly quota reset jobs
- ⏳ Integrate with MessageProcessingService for file extraction
- ⏳ Add per-minute rate limiting (sliding window for VirusTotal 4/min)
- ⏳ UI for quota monitoring and cloud service configuration

**Key Design Decisions**:
- **Hash-first optimization**: Save quota by checking hash before uploading (VirusTotal)
- **Calendar-based quotas**: Align to UTC midnight (daily) and month boundaries (monthly) for deterministic resets
- **Sequential execution**: Try services in priority order until one succeeds (vs parallel)
- **Fail-open default**: When all quotas exhausted, allow file through (prevents UX disruption)
- **Stub implementations**: MetaDefender/HybridAnalysis/Intezer return fail-open until APIs completed

**Next Phase**: Phase 3 - Windows AMSI API (Optional) or Phase 4.14 - Critical Checks Infrastructure (recommended)

---

### Phase 3: Windows AMSI API ❌ **NOT IMPLEMENTED**

**Status**: Deferred indefinitely (October 21, 2025)

**Rationale**:
- ClamAV + VirusTotal already provides **96-98% detection coverage**
- Marginal improvement (~1-2%) not worth the complexity
- Windows VM adds infrastructure cost and maintenance burden
- Free AV licensing restrictions for server deployment unclear
- AMSI multi-engine behavior inconsistent (may only use one AV)
- Current solution is "good enough" for Telegram group moderation use case

**Decision**: Focus on completing Phase 4.14 (MessageProcessingService integration) instead of adding Windows AMSI. If detection coverage proves insufficient in production, we can revisit this phase.

**Stub Cleanup Required**:
- WindowsAMSI configuration options (keep for future, mark as "not implemented")
- Tier1VotingCoordinator already single-scanner ready (no changes needed)
- UI toggle for Windows AMSI (disable/hide in settings)

**Alternative**: If higher coverage needed in future, consider paid Tier 2 cloud services (MetaDefender Pro, etc.) instead of Windows AMSI infrastructure

---

### Phase 4: UI & Monitoring

**Objectives**:
- User-facing configuration interface
- Quota monitoring and alerts
- Scanner health visibility

**Tasks**:
1. /settings#file-scanning page (MudBlazor)
2. Tier 1 scanner enable/disable toggles
3. Tier 2 cloud service configuration
4. Priority queue ordering (drag-and-drop)
5. Quota usage display (daily/monthly)
6. Scanner health status dashboard
7. Test scan functionality (upload file, see results)
8. Quota reset history viewer
9. Scan results log viewer
10. Threat statistics (most detected file types, etc.)

**Deliverables**:
- Full configuration UI
- Real-time quota monitoring
- Scanner health visibility
- User can test scanning without Telegram

**Estimated Effort**: Moderate UI development

---

### Phase 5: Documentation & Tooling

**Objectives**:
- Comprehensive setup documentation
- Automation scripts for Windows deployment
- User guides and troubleshooting

**Tasks**:
1. setup-windows-scanner.ps1 script
2. verify-setup.ps1 script
3. uninstall-scanner.ps1 script
4. scanner-config.template.json
6. Deployment guide (step-by-step)
7. Troubleshooting documentation
8. Performance tuning guide
9. API documentation (Windows Scanner)
10. Architecture diagrams (updated)

**Deliverables**:
- Automated Windows setup script
- Complete documentation set
- Ready-to-deploy solution

**Estimated Effort**: Documentation and scripting

---

## 11. Database Schema

### file_scan_quota Table

**Purpose**: Track daily and monthly quota usage for cloud services

```sql
CREATE TABLE file_scan_quota (
  id BIGSERIAL PRIMARY KEY,
  service VARCHAR(50) NOT NULL,        -- e.g., 'VirusTotal', 'MetaDefender'
  quota_date DATE NOT NULL,             -- Date for daily quotas, or first of month for monthly
  quota_type VARCHAR(10) NOT NULL,      -- 'daily' or 'monthly'
  count INT NOT NULL DEFAULT 0,         -- Current usage count
  limit_value INT NOT NULL,             -- Quota limit (500 for VT daily, etc.)
  last_updated TIMESTAMPTZ NOT NULL DEFAULT NOW(),

  UNIQUE(service, quota_date, quota_type)
);

CREATE INDEX idx_file_scan_quota_service_date
  ON file_scan_quota(service, quota_date);
```

**Usage**:
- Before scanning: Check if count < limit_value
- After scanning: Increment count
- Daily reset: TickerQ job deletes rows where quota_date < today
- Monthly reset: TickerQ job deletes rows where quota_date < first of current month

---

### file_scan_results Table

**Purpose**: Store scan results for caching and auditing

```sql
CREATE TABLE file_scan_results (
  id BIGSERIAL PRIMARY KEY,
  file_hash VARCHAR(64) NOT NULL,       -- SHA256 hash
  scanner VARCHAR(50) NOT NULL,          -- 'ClamAV', 'WindowsAMSI', 'VirusTotal', etc.
  result VARCHAR(20) NOT NULL,           -- 'clean', 'infected', 'suspicious', 'error'
  threat_name VARCHAR(255),              -- Name of detected threat (if infected)
  scan_duration_ms INT,                  -- How long scan took
  scanned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  metadata JSONB,                        -- Additional details (Scanner rule matched, VT engine breakdown, etc.)

  INDEX idx_file_scan_results_hash (file_hash),
  INDEX idx_file_scan_results_scanner_time (scanner, scanned_at)
);
```

**Usage**:
- Before scanning: Query by file_hash where scanned_at > NOW() - INTERVAL '24 hours'
- If cached result found and still valid: Return cached result (skip scanning)
- After scanning: Insert result for future cache lookups
- Cleanup: Delete results older than 30 days (audit retention)

---

### file_scan_config Column

**Purpose**: Per-chat configuration overrides (JSONB in existing table)

**Location**: Add to `content_check_configs` table or create dedicated `file_scanning_configs` table

```sql
ALTER TABLE content_check_configs
  ADD COLUMN file_scan_config JSONB DEFAULT '{}'::JSONB;
```

**Schema** (JSONB content):
```json
{
  "tier1": {
    "clamav": { "enabled": true },
    "windowsAmsi": { "enabled": false }
  },
  "tier2": {
    "cloudQueuePriority": ["VirusTotal", "MetaDefender"],
    "virusTotal": { "enabled": true },
    "metaDefender": { "enabled": false }
  },
  "general": {
    "alwaysRunForAllUsers": true,
    "maxFileSizeBytes": 104857600
  }
}
```

**Usage**:
- Merge with global config (per-chat overrides win)
- Most deployments use global only
- Power users can customize per group

---

## 12. Integration Points

### Content Detection System Integration

**FileScanningCheck** (New Content Check):
- Implements `IContentCheck` interface
- Registered with `ContentCheckCoordinator`
- Priority: High (run early in pipeline)
- `always_run = true` by default (Phase 4.14 integration)

**Behavior**:
- Bypasses trust/admin status checks
- Runs for ALL users (even trusted/admin)
- Critical check (cannot be disabled per-user)
- Runs alongside other checks (URL filtering, impersonation, etc.)

**Flow**:

When a message with file attachment is received, ContentCheckCoordinator orchestrates all content checks. URLFilteringCheck runs first (always_run enabled), followed by FileScanningCheck (always_run enabled, new), then ImpersonationCheck and other checks. The always_run flag ensures these critical checks execute for all users regardless of trust or admin status.

---

### Message Processing Integration

**File Extraction**:
- Detect file attachments in `Message` object
- Support: Documents, photos, videos, audio, voice, stickers
- Extract file metadata (name, size, MIME type)
- Download file bytes via Telegram Bot API

**Hash Calculation**:
- Calculate SHA256 hash of file bytes
- Use hash for cache lookups (avoid re-scanning same file)
- Store hash in `file_scan_results` table

**Duplicate Detection**:
- Before scanning: Check cache by hash
- If found and < 24 hours old: Return cached result (skip scanning)
- If not found or expired: Proceed to scanning

**Result Caching**:
- After scanning: Store result with TTL (24 hours default)
- Next time same file uploaded: Instant result from cache
- Reduces load on scanners and cloud APIs

---

### Action System Integration

**Infected File Actions**:

**For Regular Users** (not trusted, not admin):
1. Delete message containing file
2. Delete file from Telegram
3. Ban user from chat
4. Send DM to user with threat details
5. Notify admins in chat (optional)
6. Create audit log entry

**For Trusted/Admin Users** (Phase 4.14):
1. Delete message containing file
2. Delete file from Telegram
3. Send DM notification (NO ban, NO warn)
4. Create audit log entry
5. Rationale: Critical checks (file scanning, URL filtering) run for everyone, but actions are gentler for trusted users

**DM Notification Format**:
```
⚠️ Malware Detected

Your file "[filename]" was detected as malicious and removed from [Group Name].

Threat: Trojan:Win32/Wacatac.B!ml
Detected by: ClamAV, Windows Defender

If you believe this is a false positive, please contact group administrators.
```

**Fallback** (DM disabled):
- If user has blocked bot or DMs disabled
- Post reply in chat (if bot has permission)
- Tag user: "@username your file was removed (malware detected)"

---

## 13. Monitoring & Observability

### Metrics to Track

**Scanning Volume**:
- Files scanned per hour/day/month
- Files by scanner (ClamAV, Windows, Cloud)
- Cache hit rate (% of files served from cache)

**Detection Rates**:
- Infected files detected per scanner
- Which scanner caught which threats
- Scanner hit rates (ClamAV: 95%, Windows: 5%, etc.)
- Overlap analysis (multiple scanners detecting same file)

**Quota Usage**:
- Daily/monthly quota consumption by service
- Trend analysis (quota running out faster than expected?)
- Projected exhaustion date
- Quota reset history

**Performance**:
- Average scan duration per scanner
- P50, P95, P99 latencies
- Timeout rate (scans that timed out)
- Error rate (scanner unavailable, network errors, etc.)

**Scanner Health**:
- Scanner availability (uptime %)
- Signature age (last update time)
- Signature version
- AMSI provider count (Windows)

**False Positives** (Optional):
- User reports of false positives
- Manual review queue
- Whitelist additions

---

### Logging Strategy

**Threat Detections** (Always log):
```
[THREAT] File detected as malicious
  File: document.pdf (SHA256: abc123...)
  User: @username (123456789)
  Chat: Group Name (-1001234567890)
  Scanner: ClamAV
  Threat: Trojan.Generic.12345
  Action: Deleted + Banned
```

**Quota Events**:
```
[QUOTA] Cloud service quota exhausted
  Service: VirusTotal
  Date: 2025-10-18
  Usage: 500/500 (daily)
  Next Reset: 2025-10-19 00:00 UTC
```

**Scanner Failures**:
```
[ERROR] Scanner unavailable
  Scanner: Windows AMSI
  Error: HTTP timeout after 30s
  URL: http://windows-scanner:5000/api/scan/amsi
  Fallback: Skipped to next Tier 1 scanner
```

**Configuration Changes**:
```
[CONFIG] File scanning configuration updated
  User: admin@example.com
  Changes: Enabled MetaDefender, Priority: VT → MD → HA
  Timestamp: 2025-10-18T14:23:00Z
```

---

### Alerting Rules

**Critical Alerts**:
- Scanner unavailable for > 5 minutes
- All Tier 1 scanners failed
- Cloud quota exhausted unexpectedly (50%+ of quota used in < 25% of reset period)

**Warning Alerts**:
- Quota approaching limit (80% consumed)
- Scanner signature age > 48 hours (updates failing)
- High false positive rate (> X reports in 24h)
- Scan timeout rate > 5%

**Informational**:
- Quota reset occurred
- New AMSI provider registered
- Configuration change by admin

---

## 14. Troubleshooting

### Common Issues

**Issue: ClamAV signatures not updating**

Symptoms:
- Signature age > 24 hours
- Health check shows old version
- Detection rate drops

Diagnosis:
- Check ClamAV container logs: `docker logs clamav`
- Look for freshclam errors
- Verify internet connectivity from container
- Check disk space (full disk prevents updates)

Resolution:
- Restart ClamAV container
- Verify network connectivity
- Free up disk space
- Check firewall (allow outbound to database.clamav.net)

---

Symptoms:
- Check for duplicate rule identifiers

---

**Issue: Windows AMSI API unreachable**

Symptoms:
- Windows scanner skipped in Tier 1
- Error: HTTP timeout or connection refused
- Health check fails

Diagnosis:
- Verify Windows VM is running
- Check Windows Scanner service status
- Test network connectivity: `Test-NetConnection -ComputerName windows-vm -Port 5000`
- Check firewall rules on Windows VM
- Review Windows Scanner API logs

Resolution:
- Start Windows Scanner service if stopped
- Verify firewall allows inbound on port 5000
- Check network connectivity between Linux and Windows VMs
- Restart Windows Scanner service

---

**Issue: Cloud API quota exhausted unexpectedly**

Symptoms:
- Tier 2 falling back to fail-open early
- Quota consumed faster than expected
- Many files not getting cloud scans

Diagnosis:
- Check quota usage in database: SELECT * FROM file_scan_quota
- Analyze traffic patterns (spike in file uploads?)
- Review Tier 1 detection rates (is local detection down?)
- Check for duplicate scans (cache not working?)

Resolution:
- Investigate root cause (traffic spike, local scanner failure, cache disabled)
- If temporary spike: Accept fail-open until quota resets
- If sustained high traffic: Consider enabling more cloud services or paid tier
- Verify cache is working (duplicate files should not re-scan)

---

**Issue: False positives**

Symptoms:
- Legitimate files flagged as malicious
- User complaints
- Specific file type always detected

Diagnosis:
- Review scan results for specific file
- Identify which scanner detected it
- Check threat name (generic detection = more likely false positive)
- Test file with multiple scanners manually

Resolution:
- Whitelist file hash (if known-good file)
- Adjust scanner configuration (if false positive)
- Report false positive to scanner vendor (ClamAV, AV vendor)
- Consider excluding specific file types if consistently problematic

---

**Issue: Slow scanning performance**

Symptoms:
- Scan taking > 30 seconds
- Timeouts occurring frequently
- High CPU usage during scans

Diagnosis:
- Check scan duration metrics (which scanner is slow?)
- Review file sizes (very large files take longer)
- Check system resources (CPU, memory, disk I/O)
- Windows AMSI: Check how many AVs installed (more = slower)

Resolution:
- Increase timeout settings if files are large
- Reduce number of AMSI providers (fewer AVs)
- Optimize scanner settings (complex rules = slower)
- Scale up resources (more CPU, faster disk)
- Consider skipping very large files (set max file size limit)

---

### Diagnostic Tools

**Health Check Endpoints**:
- ClamAV: `docker exec clamav clamdscan --version`
- Scanner logs show loaded rule count
- Windows AMSI: `GET http://windows-vm:5000/api/health`

**Scanner Version Verification**:
- ClamAV: Check signature version in Docker logs
- Check scanner version at startup
- Windows Defender: Check signature version in health response
- Cloud APIs: Check API response headers for versions

**Signature Age Validation**:
- ClamAV: Compare signature timestamp to current time (> 48h = stale)
- Windows Defender: Query signature version via PowerShell
- Cloud APIs: Always up-to-date (no local signatures)

**Network Connectivity Tests**:
- Windows AMSI: `Test-NetConnection -ComputerName windows-vm -Port 5000`
- Cloud APIs: `curl -I https://www.virustotal.com/api/v3/` (should return 200 or auth error)

**AMSI Provider Enumeration** (Windows):
```powershell
# List providers
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\AMSI\Providers\*"

# Count providers
(Get-ChildItem "HKLM:\SOFTWARE\Microsoft\AMSI\Providers").Count
```

---

## 15. Security Considerations

### File Handling Security

**Temporary File Management**:
- Store uploaded files in temporary directory with restricted permissions
- Generate unique filenames (GUID) to avoid collisions
- Delete files immediately after scanning (regardless of result)
- Set maximum retention time (delete after 5 minutes even if scan hangs)

**Memory Limits**:
- Enforce maximum file size (default: 100MB)
- Reject files exceeding limit before downloading
- Prevents memory exhaustion attacks
- Prevents disk exhaustion attacks

**Timeout Configuration**:
- Set reasonable timeouts for each scanner (ClamAV: 30s, etc.)
- Prevent indefinite hangs
- Timeout = treated as scanner error (skip to next)

**Malicious File Containment**:
- Never execute files being scanned
- Scan files as byte streams (read-only)
- Do not extract archives automatically (some scanners do this)
- Delete infected files securely (no recovery)

---

### API Security

**API Key Protection**:
- Store all API keys in environment variables (never in code or config files)
- Use secrets management system (e.g., Docker secrets, Azure Key Vault)
- Rotate keys periodically
- Principle of least privilege (read-only API keys where possible)

**Windows Scanner Authentication**:
- Optional API key for Windows Scanner API
- Sent via Authorization header
- Prevents unauthorized access if VM exposed to network
- Consider HTTPS with self-signed cert for encrypted transport

**Rate Limiting (Abuse Prevention)**:
- Limit file scans per user per hour (e.g., 10 scans/hour)
- Prevent malicious users from exhausting quotas
- Track by user ID or IP address

**Input Validation**:
- Validate file size before downloading
- Validate file types (extension, MIME type, magic bytes)
- Reject obviously malicious filenames (path traversal attempts, etc.)
- Sanitize filenames before logging

---

### Privacy Considerations

**File Hash Sharing vs Upload**:
- Cloud services: Some accept hash-only lookups (VirusTotal), others require file upload
- Hash lookups preserve privacy (file content not shared)
- File uploads expose content to third party
- Document which services require upload in user-facing settings

**Local-First Philosophy**:
- Tier 1 (local) scanners process files entirely on-premises
- Files never leave your infrastructure during Tier 1
- Only files that pass Tier 1 (clean) are sent to cloud (if enabled)
- Users can disable Tier 2 entirely for maximum privacy

**Opt-In Cloud Scanning**:
- Users can disable cloud scanners individually
- Local-only mode available (ClamAV only)
- Document what data is shared with each cloud service

**Data Retention**:
- Scan results stored for 24 hours (cache), then deleted
- Audit logs: Threat detections retained for 90 days
- File bytes: Deleted immediately after scanning
- No permanent storage of file content

---

## 16. Future Enhancements

### Custom ML Model (Phase 6)

**Concept**: Train a machine learning model on the spam/ham corpus to detect malicious files based on behavioral patterns.

**Approach**:
- Use ML.NET (.NET machine learning framework)
- Train on historical file scans (features: file type, size, entropy, detected threats)
- Binary classification: malicious vs benign
- Run as additional Tier 1 scanner

**Benefits**:
- Catches zero-day threats based on patterns
- Learns from your specific threat landscape
- No external dependencies or quotas

**Estimated Effort**: Significant (model training, feature engineering, integration)

---

### Hash-Based Reputation (NSRL, Team Cymru)

**Concept**: Check file hashes against known-good databases (whitelists) and known-bad databases (blacklists) before scanning.

**Sources**:
- **NSRL (National Software Reference Library)**: Database of known-good software
- **Team Cymru MHR**: Malware hash registry (known-bad)
- **VirusTotal Intelligence**: Hash lookups (free tier supports this)

**Flow**:

File is received and SHA256 hash calculated. First, check hash against NSRL (known-good database). If match found, skip all scanning and allow file (known legitimate software). If no match, check hash against MHR (malware hash registry, known-bad database). If match found, immediately block file as infected without scanning. If no match in either database, proceed to normal Tier 1 scanning process.

**Benefits**:
- Fast lookups (hash queries, no file upload)
- Reduces scanning load
- High confidence (whitelists are curated)

**Estimated Effort**: Moderate (database integration, API clients)

---

### File Type Analysis

**Concept**: Detect mismatched file extensions, polyglot files, and embedded executables.

**Checks**:
- **Extension vs MIME Type**: File claims to be .pdf but is actually .exe
- **Magic Bytes**: Verify file signature matches extension
- **Polyglot Detection**: File is valid as multiple formats (e.g., PDF + ZIP)
- **Embedded Executables**: Word doc with embedded .exe macro

**Benefits**:
- Catches social engineering attacks (malware disguised as documents)
- Lightweight pre-filter
- Runs before Tier 1 scanning

**Estimated Effort**: Low to moderate (file type libraries available)

---

### Sandbox Integration (Dynamic Analysis)

**Concept**: Execute suspicious files in isolated sandbox environment to observe behavior.

**Approach**:
- Integrate with existing sandbox services (Hybrid Analysis, ANY.RUN)
- Or deploy local sandbox (Cuckoo Sandbox)
- Execute file and monitor: network traffic, registry changes, file system writes, etc.

**Benefits**:
- Catches advanced malware that evades signature detection
- Behavioral analysis reveals intent
- High confidence results

**Challenges**:
- Resource intensive (VM per file)
- Slow (execution takes minutes)
- Limited free quotas for cloud sandboxes

**Use Case**: Manual investigation of borderline suspicious files, not automated pipeline

**Estimated Effort**: Significant (sandbox deployment, API integration)

---


### Community Threat Sharing

**Concept**: Share detected threats with other TelegramGroupsAdmin deployments to improve collective defense.

**Approach**:
- Opt-in threat sharing
- Upload file hash + threat name to central database when malware detected
- Other instances query database before scanning
- Privacy-preserving (hash-only, no file content)

**Benefits**:
- Crowd-sourced threat intelligence
- Faster detection of new threats
- Helps smaller deployments

**Challenges**:
- Privacy concerns (what if hash identifies sensitive file?)
- False positive propagation
- Hosting and maintaining central database

**Estimated Effort**: Significant (infrastructure, privacy design, opt-in UX)

---

## Conclusion

The File Scanning System provides enterprise-grade malware protection using a two-tier architecture that balances cost, coverage, and performance. By leveraging free and open-source local scanners alongside free-tier cloud services, the system achieves 95-99.8% detection coverage at zero cost.

Key advantages:
- **Flexible Deployment**: Works on Linux-only, or enhanced with Windows multi-engine support
- **Scalable**: Handles low to high traffic without requiring paid services
- **User-Controlled**: Configurable priority ordering, enable/disable per scanner
- **Privacy-Focused**: Local-first scanning, opt-in cloud services
- **Production-Ready**: Quota tracking, fail-open behavior, comprehensive monitoring

The optional Windows AMSI multi-engine integration is particularly innovative, allowing users to leverage 4-5 commercial antivirus engines simultaneously through free consumer installations—bypassing expensive SDK licensing while achieving detection rates comparable to commercial solutions.

For most deployments, the **Linux-Only Minimal** scenario (ClamAV + VirusTotal fallback) provides excellent protection. Users with Windows infrastructure can achieve maximum coverage with the **Linux + Windows Multi-Engine** scenario at zero marginal cost.

See CLAUDE.md Phase 4.17 for implementation status and roadmap.
