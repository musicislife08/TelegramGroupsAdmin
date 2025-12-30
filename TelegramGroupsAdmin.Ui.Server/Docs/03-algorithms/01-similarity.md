# Similarity Detection (TF-IDF)

The Similarity algorithm uses **TF-IDF (Term Frequency-Inverse Document Frequency)** and cosine similarity to compare new messages against known spam samples.

## How It Works

1. Convert message to TF-IDF vector
2. Compare against spam sample vectors using cosine similarity
3. Return highest similarity score (0.0-1.0)
4. Convert to confidence percentage (0-100)

## Configuration

- **Threshold**: 0.7-0.8 recommended
- **Training Samples**: Requires at least 10 spam samples
- **Performance**: Medium (~87ms average)

## Best Practices

- Build diverse spam sample library (different spam types)
- Regularly review and add new spam patterns
- Use Training Mode to collect samples automatically
- Monitor false positive rate and adjust threshold

## Technical Details

This algorithm is particularly effective at detecting:
- Repeated promotional messages
- Similar phishing attempts
- Template-based spam campaigns
