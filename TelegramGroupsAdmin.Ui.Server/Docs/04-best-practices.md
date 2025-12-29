# Best Practices

Follow these recommendations to get the best results from TelegramGroupsAdmin.

## Initial Setup

### 1. Start with Training Mode

Enable Training Mode for 7-14 days to:
- Build training samples for ML algorithms
- Validate detection thresholds
- Identify false positive patterns
- Understand your group's spam profile

### 2. Configure Stop Words

Create a stop words list based on:
- Common spam keywords in your community's language
- Scam/phishing terms
- Inappropriate content for your group

### 3. Enable OpenAI Veto Mode

Use Veto Mode (not Detection Mode) to:
- Reduce false positives
- Lower API costs
- Maintain high accuracy

## Ongoing Maintenance

### Review Queue Management

- Check review queue daily during first week
- Mark false positives to improve ML training
- Add new spam patterns to training samples

### Threshold Tuning

Use ML Threshold Tuning when you have:
- 100+ total detections
- At least 50 OpenAI veto events
- Stable spam patterns

### Performance Monitoring

Watch for:
- High false positive rate (>5%)
- Slow detection times (>1s average)
- API quota limits

## Group Size Recommendations

### Small Groups (<100 members)
- Enable all algorithms except OpenAI Detection
- Use OpenAI Veto Mode
- Manual review queue is manageable

### Medium Groups (100-1,000 members)
- Enable all algorithms
- Use OpenAI Veto with 85 threshold
- Auto-ban confidence ≥90

### Large Groups (>1,000 members)
- Enable all algorithms
- Lower veto threshold to 80
- Auto-ban confidence ≥85
- Consider dedicated moderation team
