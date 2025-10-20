# License Compatibility Analysis for TelegramGroupsAdmin

**Date**: October 19, 2025
**Purpose**: Analyze license compatibility for open-sourcing under GPLv2 or GPLv3

## Current Dependencies License Breakdown

### ✅ MIT License (GPLv2 Compatible)
- **MudBlazor** 8.13.0 - MIT
- **Dapper** 2.1.66 - Apache 2.0 (see note below)
- **nClam** 9.0.0 - MIT
- **Otp.NET** 1.4.0 - MIT
- **QRCoder** 1.7.0 - MIT
- **DiffPlex** 1.9.0 - Apache 2.0 (see note below)
- **CsvHelper** 33.1.0 - MS-PL/Apache 2.0

### ⚠️ Apache 2.0 License (GPLv3 Compatible, GPLv2 INCOMPATIBLE)
- **Microsoft.EntityFrameworkCore.*** 10.0.0-rc.2 - Apache 2.0
- **Microsoft.Extensions.*** 10.0.0-rc.2 - Apache 2.0
- **Microsoft.AspNetCore.*** 10.0.0-rc.2 - Apache 2.0
- **TickerQ** 2.5.3 - Apache 2.0
- **SendGrid** 9.29.3 - MIT
- **SixLabors.ImageSharp** 3.1.11 - Apache 2.0
- **Blazor-ApexCharts** 6.0.2 - MIT
- **AngleSharp** 1.3.1-beta - MIT

### ✅ PostgreSQL License (GPLv2 Compatible)
- **Npgsql** 10.0.0-rc.1 - PostgreSQL License (permissive, BSD-like)

### ✅ BSD License (GPLv2 Compatible)
- **Telegram.Bot** 22.7.3-dev.4 - LGPL-3.0

### 🔴 GPL License
- **ClamAV** (via nClam TCP client) - GPLv2

## License Compatibility Matrix

| Your License Choice | Apache 2.0 Deps | ClamAV (GPLv2) | Status |
|---------------------|-----------------|----------------|--------|
| **GPLv2** | ❌ Incompatible | ✅ Compatible | **BLOCKED** |
| **GPLv3** | ✅ Compatible | ❌ Incompatible* | **BLOCKED** |
| **AGPL-3.0** | ✅ Compatible | ❌ Incompatible* | **BLOCKED** |
| **Apache 2.0** | ✅ Compatible | ⚠️ Must use IPC | **VIABLE** |
| **MIT** | ✅ Compatible | ⚠️ Must use IPC | **VIABLE** |

*ClamAV is GPLv2-only, not "GPLv2-or-later", so it cannot be upgraded to GPLv3

## 🎯 Recommended Licensing Strategy

### Option 1: Keep Current Architecture (RECOMMENDED)
**License**: MIT or Apache 2.0 (permissive)

**Rationale**:
- Your current Docker/IPC architecture keeps ClamAV as a separate process
- Network communication (TCP socket) is NOT considered "linking" under GPL
- You can use Apache 2.0 dependencies freely
- Users install ClamAV separately (GPL applies to ClamAV only, not your code)
- Maximum flexibility for users and contributors

**Implementation**:
```
TelegramGroupsAdmin/ (MIT or Apache 2.0)
├── Your application code (proprietary or open source)
├── Uses nClam TCP client (MIT) to communicate with clamd
└── ClamAV runs in Docker (GPLv2 applies to ClamAV binary only)
```

**GPL Compliance**:
- ✅ No linking with GPL code (IPC only)
- ✅ Apache 2.0 dependencies allowed
- ✅ Can remain closed-source OR open-source under any license
- ✅ No GPL contamination

### Option 2: Replace Apache 2.0 Dependencies
**License**: GPLv2

**Requirements**:
- Replace all Microsoft.* packages with GPL-compatible alternatives
- Replace TickerQ with a GPLv2-compatible job queue
- Replace SixLabors.ImageSharp with a GPL-compatible image library

**Feasibility**: ❌ **NOT VIABLE**
- .NET heavily relies on Microsoft.* packages (no alternatives)
- Replacing EF Core would require complete rewrite
- ImageSharp has no GPL-compatible .NET alternative

### Option 3: Dual Licensing
**License**: GPLv3 + Commercial Exception

**Structure**:
- Open source: GPLv3 (allows Apache 2.0 deps)
- ClamAV: Separate process via IPC (GPL isolation)
- Commercial: Sell proprietary licenses for closed-source use

**Complexity**: Medium - requires legal review

## 🚨 Critical Decision Point

**If you plan to open-source under GPL**, you MUST:

1. **Choose GPLv3** (not GPLv2) to allow Apache 2.0 dependencies
2. **Keep ClamAV as separate process** (Docker/IPC) to avoid GPL contamination
3. **Document that ClamAV is optional** and licensed separately under GPLv2
4. **Use network communication only** (never link libclamav directly)

**Alternatively, use MIT/Apache 2.0** for maximum flexibility and contributor friendliness.

## 📄 Recommended LICENSE File Header

If going with **MIT License** (most permissive):

```
MIT License

Copyright (c) 2025 [Your Name/Organization]

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

[Standard MIT license text...]

---

## Third-Party Components

This software optionally integrates with ClamAV for malware scanning:
- ClamAV is licensed under GNU General Public License v2 (GPLv2)
- ClamAV is NOT bundled with this software
- Users must install ClamAV separately via Docker or system package manager
- Communication with ClamAV occurs via network socket (TCP port 3310)
- No GPL code is linked or distributed with this software

For full ClamAV licensing, see: https://www.clamav.net/
```

If going with **GPLv3** (copyleft, allows Apache 2.0 deps):

```
GNU GENERAL PUBLIC LICENSE
Version 3, 29 June 2007

[Standard GPLv3 license text...]

---

## Third-Party Components

This software integrates with ClamAV for malware scanning:
- ClamAV is licensed under GNU General Public License v2 (GPLv2)
- ClamAV runs as a separate process (Docker container or system service)
- Communication occurs via network socket (TCP), maintaining license separation
- ClamAV source code: https://github.com/Cisco-Talos/clamav

Apache 2.0 licensed dependencies are used under GPLv3 compatibility.
```

## 🎓 Key Takeaways

1. **Your current Docker/IPC architecture is GPL-safe** - no code modification needed
2. **MIT or Apache 2.0 licenses are most contributor-friendly** for .NET projects
3. **GPLv3 works IF you keep ClamAV separate** (which you already do)
4. **GPLv2 is NOT viable** due to Apache 2.0 dependency incompatibility
5. **Never use P/Invoke to libclamav** - this would force GPL on entire codebase

## Next Steps

1. ✅ Keep Docker/IPC architecture (already implemented)
2. ⏸️ Choose license: MIT (recommended), Apache 2.0, or GPLv3
3. ⏸️ Add LICENSE file to repository root
4. ⏸️ Add license headers to source files
5. ⏸️ Document ClamAV as optional third-party component
6. ⏸️ Review with legal counsel if choosing GPL
