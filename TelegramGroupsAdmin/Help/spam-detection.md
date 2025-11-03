---
Title: Spam Detection Overview
Description: Learn how the 9-algorithm spam detection system works, including confidence scoring and OpenAI veto mode.
Icon: Shield
Color: Primary
SearchKeywords: spam detection algorithms confidence scoring openai veto orchestration
Order: 1
ShowInIndex: true
---

# Spam Detection Overview

TelegramGroupsAdmin uses a **multi-algorithm orchestration system** that runs up to 9 different spam detection checks in parallel on each message. Each algorithm analyzes different aspects of the message and returns a confidence score (0-100).

## How It Works

Individual algorithm scores are combined using **weighted averaging** to produce a final confidence score. Higher confidence = more likely to be spam.

## The 9 Detection Algorithms

| Algorithm | What It Does | Performance | Requirements |
|-----------|--------------|-------------|--------------|
| **Stop Words** | Matches messages against customizable keyword list | Fast (~45ms) | Configure stop words list |
| **CAS (Combot Anti-Spam)** | Checks users against public spammer database | Fast (~12ms) | None (external API) |
| **Similarity (TF-IDF)** | Compares messages to known spam samples | Medium (~87ms) | Training samples needed |
| **Naive Bayes** | ML classifier learns word frequency patterns | Fast | 50+ spam + 50+ ham samples |
| **Spacing Detection** | Detects deliberate spacing to evade filters | Fast | None |
| **Invisible Characters** | Finds zero-width Unicode abuse | Fast | None |
| **Translation** | Translates foreign messages for analysis | Medium | OpenAI API key |
| **OpenAI Verification** | GPT-4 context analysis (veto or detection) | Slow (~1.2s) | OpenAI API key |
| **URL/File Scanning** | Malicious URL/file detection | Variable | ClamAV + VirusTotal |

## Decision Flow

1. **Message Received** - New message arrives in monitored chat
2. **Fast Algorithms Run** - Stop Words, CAS, Spacing, Invisible Chars execute in parallel (~50ms)
3. **ML Algorithms Run** - Similarity and Bayes analyze patterns if trained (~100ms)
4. **Scores Aggregated** - Weighted average produces final confidence (0-100)
5. **OpenAI Veto (Optional)** - If enabled and confidence < veto threshold, GPT-4 reviews (~1.2s)
6. **Action Taken**:
   - **≥85 (Auto-Ban)**: Delete message + ban user
   - **70-84 (Review)**: Send to review queue
   - **<70 (Pass)**: Message allowed

## OpenAI Veto Mode

### Veto Mode ON (Recommended)

OpenAI acts as **quality control**. Other algorithms flag spam, OpenAI confirms or vetoes.

**Benefits:**
- Reduces false positives
- Lower API costs (only runs on suspicious messages)
- Best for most communities

### Veto Mode OFF (Detection)

OpenAI actively searches for spam on **every message**.

**Benefits:**
- Higher accuracy
- Significantly higher API costs
- Use only for high-risk communities

## Training Mode

Training Mode sends **all detections** to the review queue instead of auto-banning. Use this when:

- **First-time setup**: Test algorithms before enabling auto-ban
- **Testing new algorithms**: Validate accuracy before production use
- **Threshold tuning**: Observe detection patterns before adjustment
- **Building training samples**: Review queue feeds Similarity and Bayes

> **Best Practice:** Start with Training Mode enabled for 7-14 days to build training data and validate thresholds, then disable for production use.

## Performance Characteristics

| Configuration | Average Detection Time | P95 Detection Time |
|---------------|------------------------|-------------------|
| Fast algorithms only (Stop Words, CAS, Spacing) | ~50ms | ~120ms |
| Fast + ML algorithms (+ Similarity, Bayes) | ~150ms | ~400ms |
| All algorithms without OpenAI | ~200ms | ~600ms |
| All algorithms with OpenAI Veto Mode | ~255ms | ~821ms |
| All algorithms with OpenAI Detection Mode | ~1.4s | ~2.5s |

> **Measured in Production:** The default configuration (all 9 algorithms + OpenAI Veto) averages 255ms per message with P95 at 821ms. This handles 500-5,000 messages/day easily.

## Next Steps

- [Configure Algorithms](/help/algorithms) - Learn how to configure each of the 9 detection algorithms
- [Best Practices](/help/best-practices) - Recommended configurations for different group sizes
- [ML Threshold Tuning](/help/ml-tuning) - Optimize algorithm thresholds with machine learning
