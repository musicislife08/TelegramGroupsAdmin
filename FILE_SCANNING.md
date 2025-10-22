# File Scanning System

**Status**: Phase 1-2 Complete (ClamAV + VirusTotal, 96-98% coverage), Phase 3 (Windows AMSI) Deferred

## Overview

Two-tier malware detection: **Tier 1** (ClamAV local, unlimited) runs first. If clean, **Tier 2** (VirusTotal cloud, 500/day quota) runs. Fail-open when quota exhausted.

**Deployment**: Docker Compose (ClamAV container), VirusTotal API key optional
**Coverage**: 96-98% (ClamAV 95-97% + VirusTotal edge cases)
**Quota**: 15,000 cloud scans/month (500/day VirusTotal)
**Cost**: $0 (all free tiers)

## Architecture

### Flow
1. File received → Calculate SHA256 hash
2. Check cache (24hr TTL) → If found, return cached result
3. **Tier 1**: ClamAV scans → If infected, delete + ban
4. **Tier 2**: If Tier 1 clean + quota available → VirusTotal scans → If infected, delete + ban
5. Cache result → Take action

### Scanners

**Tier 1 - ClamAV** (✅ Complete):
- Docker: `clamav/clamav:latest`
- Port: 3310 TCP
- Signatures: 10M+, auto-update daily
- Scan time: ~55ms (EICAR test)
- Memory: ~1.2GB

**Tier 2 - VirusTotal** (✅ Complete):
- API v3, hash-first optimization (check hash before upload)
- Quota: 500/day, 4/min
- Multi-engine: 70+ AV engines, ≥2 detections = malicious
- Async polling: 10 polls × 2s = 20s timeout
- Fail-open on quota exhausted or timeout

**Tier 2 - Other Services** (Stub):
- MetaDefender (40/day), Hybrid Analysis (30/month), Intezer (10/month)
- Quota tracking implemented, API integration pending

**Windows AMSI** (❌ Deferred):
- Multi-AV via AMSI API not implemented
- Marginal benefit (1-2%) vs complexity
- See CLAUDE.md if reconsidering

## Critical Implementation Notes

### Hash-First Optimization
Always check VirusTotal hash reputation (GET `/api/v3/files/{hash}`) before uploading. Hash lookups are free and ~100ms vs 5-10s for upload+scan. Reduces quota consumption 30-50%.

### File Size Limits
- Telegram max: 2GB (Premium), 20MB (standard)
- Default config: 100MB limit
- Files >100MB skipped (fail-open)
- Adjust per threat model: High-security → 500MB-2GB, Public groups → 100MB

### Archive Handling
ClamAV auto-scans .zip/.tar/.7z/.rar (recursion limit: 17 levels, 10K files, 25MB extracted). Password-protected archives cannot be scanned.

**Policy Options**:
- Permissive (default): Allow password-protected archives
- Strict: Block all password-protected archives
- Whitelist-only: Allow only for trusted users

### Fail-Close Option
Default: Fail-open (allow file if all scanners fail/quota exhausted)

Per-chat override: `"failOpen": false` → Delete file when scan cannot complete
Use for: High-security groups, compliance requirements

### Privacy & Cloud Services
- **ClamAV**: 100% local, files never leave infrastructure
- **VirusTotal**: Hash lookup (privacy-preserving) → Upload if unknown
- **Hash-only**: VirusTotal free tier supports hash lookups without file upload
- **Local-only mode**: Disable Tier 2 entirely (ClamAV only, unlimited scans)

## Configuration

### Environment Variables
```bash
# Required for Tier 2
VIRUSTOTAL__APIKEY=your_api_key_here

# Optional (future services)
METADEFENDER__APIKEY=key
HYBRIDANALYSIS__APIKEY=key
INTEZER__APIKEY=key
```

### Configuration Schema (JSONB)

```json
{
  "tier1": {
    "clamav": {
      "enabled": true,
      "host": "localhost",
      "port": 3310,
      "timeoutSeconds": 30
    }
  },
  "tier2": {
    "cloudQueuePriority": ["VirusTotal", "MetaDefender"],
    "virusTotal": {
      "enabled": true,
      "dailyLimit": 500,
      "perMinuteLimit": 4
    },
    "metaDefender": { "enabled": false },
    "failOpenWhenExhausted": true
  },
  "general": {
    "cacheEnabled": true,
    "cacheTTLHours": 24,
    "maxFileSizeBytes": 104857600,
    "alwaysRunForAllUsers": true
  }
}
```

### Per-Chat Overrides
Most deployments use global config. Per-chat overrides in `configs.file_scanning_config` JSONB column allow customization per group.

## Database Schema

### file_scan_results
```sql
CREATE TABLE file_scan_results (
  id BIGSERIAL PRIMARY KEY,
  file_hash VARCHAR(64) NOT NULL,
  scanner VARCHAR(50) NOT NULL,
  result VARCHAR(20) NOT NULL,
  threat_name VARCHAR(255),
  scan_duration_ms INT,
  scanned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  metadata JSONB,
  INDEX idx_file_scan_results_hash (file_hash)
);
```

**Usage**: Cache lookup before scanning (WHERE scanned_at > NOW() - INTERVAL '24 hours')

### file_scan_quota
```sql
CREATE TABLE file_scan_quota (
  id BIGSERIAL PRIMARY KEY,
  service VARCHAR(50) NOT NULL,
  quota_type VARCHAR(10) NOT NULL,
  quota_window_start TIMESTAMPTZ NOT NULL,
  count INT NOT NULL DEFAULT 0,
  limit_value INT NOT NULL,
  UNIQUE(service, quota_type, quota_window_start)
);
```

**Usage**: Check count < limit_value before scanning, increment after scan

## Integration Points

### MessageProcessingService
1. Detect Document attachment in Message
2. Extract file metadata (name, size, MIME)
3. Download file bytes via Telegram Bot API
4. Calculate SHA256 hash
5. Schedule FileScanJob via TickerQ

### FileScanJob (TickerQ)
```csharp
[TickerFunction(Key = "file_scan_{MessageId}_{FileHash}")]
public async Task ExecuteAsync(TickerFunctionContext<FileScanJobPayload> context)
{
    // 1. Check cache by hash
    // 2. Tier 1: ClamAV scan
    // 3. If clean: Tier 2 cloud queue (sequential priority order)
    // 4. Cache result
    // 5. Take action (delete if infected)
}
```

**Polling**: 5s (instant execution)
**Retry**: Re-throws exceptions for TickerQ retry logic
**Cleanup**: Deletes temp files after scan

### Actions by User Status

**Regular Users**:
- Delete message + file
- Ban from chat
- DM notification (or chat reply if DM disabled)
- Audit log

**Trusted/Admin Users**:
- Delete message + file
- DM notification only (NO ban)
- Audit log

## Monitoring & Observability

### Key Metrics
- Files scanned per hour/day
- Detection rate by scanner (ClamAV vs VirusTotal)
- Cache hit rate
- Quota consumption (daily/monthly)
- Average scan duration

### Logging

**Threat Detections**:
```
[THREAT] File detected as malicious
  File: document.pdf (SHA256: abc123...)
  Scanner: ClamAV
  Threat: Trojan.Generic.12345
  Action: Deleted + Banned
```

**Quota Events**:
```
[QUOTA] VirusTotal quota exhausted
  Usage: 500/500 (daily)
  Next Reset: 2025-10-19 00:00 UTC
```

**Scanner Failures**:
```
[ERROR] ClamAV unavailable
  Error: TCP timeout after 30s
  Fallback: Skipped to Tier 2
```

### Health Checks
- ClamAV: `docker exec clamav clamscan --version`
- VirusTotal: API health endpoint
- Signature age: ClamAV logs show last update time

## Troubleshooting

### ClamAV Signatures Not Updating
**Symptoms**: Signature age > 24 hours, detection rate drops

**Diagnosis**:
```bash
docker logs clamav | grep freshclam
docker exec clamav freshclam --version
```

**Resolution**:
- Restart ClamAV container
- Check disk space (full disk prevents updates)
- Verify network connectivity to database.clamav.net

### VirusTotal Quota Exhausted
**Symptoms**: Tier 2 falling back to fail-open early

**Diagnosis**:
```sql
SELECT * FROM file_scan_quota WHERE service = 'VirusTotal';
```

**Resolution**:
- Wait for quota reset (daily at 00:00 UTC)
- Enable additional cloud services (MetaDefender, etc.)
- Investigate traffic spike (cache working? Tier 1 detection down?)

### False Positives
**Symptoms**: Legitimate files flagged as malicious

**Diagnosis**:
- Check which scanner detected it (ClamAV vs VirusTotal)
- Review threat name (generic = more likely false positive)
- Test file manually with multiple scanners

**Resolution**:
- Whitelist file hash in cache
- Report false positive to ClamAV/VirusTotal
- Consider excluding specific file types

### Slow Scanning
**Symptoms**: Scans taking > 30s, timeouts

**Diagnosis**:
- Check scan duration metrics (which scanner is slow?)
- Review file sizes (large files take longer)
- Check system resources (CPU, memory, disk I/O)

**Resolution**:
- Increase timeout settings
- Set lower max file size limit
- Scale up resources

## Security Considerations

### File Handling
- Store uploaded files in `/tmp` with unique GUID filenames
- Delete files immediately after scanning (regardless of result)
- Set maximum file size (prevents memory/disk exhaustion)
- Never execute files being scanned

### API Security
- Store API keys in environment variables only
- Rotate keys periodically
- Rate limiting per user (prevent quota abuse)

### Privacy
- ClamAV: 100% local (no external communication)
- VirusTotal: Hash lookup first (privacy-preserving)
- Option to disable Tier 2 entirely (local-only mode)
- Document cloud service visibility in UI

## Implementation Status

### Phase 1: Tier 1 - ClamAV ✅ COMPLETE (Oct 19, 2025)
- Docker Compose configuration
- nClam 9.0.0 integration
- ClamAVScannerService implementation
- Tier1VotingCoordinator (single-scanner ready)
- FileScanResultRepository (hash caching)
- FileScanningCheck (IContentCheck, always_run=true)
- Configuration system (JSONB)
- MessageProcessingService integration
- FileScanJob (TickerQ, 5s polling)

**YARA removed**: ARM incompatibility, redundant with ClamAV's 10M+ signatures

### Phase 2: Tier 2 - Cloud Queue ✅ COMPLETE (Oct 20, 2025)
- ICloudScannerService interface
- FileScanQuotaRepository (calendar-based windows)
- VirusTotal service (hash-first, async polling, 70+ engines)
- Tier2QueueCoordinator (sequential priority execution)
- MetaDefender/Hybrid Analysis/Intezer stubs (quota tracking only)
- Integration with FileScanningCheck

### Phase 3: Windows AMSI ❌ DEFERRED (Indefinitely)
**Rationale**: ClamAV+VirusTotal already 96-98% coverage, marginal 1-2% improvement not worth Windows VM complexity, licensing concerns, AMSI multi-engine behavior inconsistent

### Phase 4: UI & Monitoring ⏳ IN PROGRESS
See CLAUDE.md Phase 4.22 for UI completion status

### Phase 5: Documentation ✅ COMPLETE
This document

## Next Steps

**Pending Work**:
- Complete MetaDefender/Hybrid Analysis/Intezer API implementations (optional, low priority)
- Add TickerQ daily/monthly quota reset jobs
- Per-minute rate limiting (sliding window for VirusTotal 4/min)

**Optional Enhancements**:
- Custom ML model (Phase 6)
- Hash-based reputation (NSRL, Team Cymru MHR)
- Sandbox integration (Cuckoo, Hybrid Analysis dynamic analysis)

---

**See CLAUDE.md for overall project roadmap and status**
