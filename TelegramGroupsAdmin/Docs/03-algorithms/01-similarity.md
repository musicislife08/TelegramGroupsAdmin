# Similarity Detection (ML.NET SDCA)

The Similarity algorithm uses an **ML.NET SDCA (Stochastic Dual Coordinate Ascent)** classifier to predict whether a message is spam based on trained text features. It replaces the older TF-IDF cosine similarity approach with a proper machine learning model.

## How It Works

1. The message text is fed into the ML.NET SDCA classifier
2. The model returns a spam probability (0.0--1.0)
3. If the probability is below the configured threshold, the check **abstains** (contributes 0 points)
4. Otherwise, the probability is mapped to a tiered score

## Scoring Tiers

| Spam Probability | Points |
|------------------|--------|
| 95%+             | 5.0    |
| 85--94%          | 3.5    |
| 70--84%          | 2.0    |
| 60--69%          | 1.0    |
| Below 60%        | Abstain |

## Abstention

The check abstains (returns 0 points with no vote) when:

- The message is too short (below the configured minimum length)
- The ML model has not been trained yet
- The spam probability falls below the similarity threshold
- An error occurs during classification

## Configuration

- **Similarity Threshold**: Controls the minimum probability required before the check contributes a score. Messages below this threshold cause the check to abstain rather than vote "clean."
- **Min Message Length**: Messages shorter than this are skipped
- **Training**: Requires spam and ham samples. The model is trained via a background job and loaded into memory at startup.

## What It Detects

- Repeated promotional messages and spam campaigns
- Phishing attempts that follow known patterns
- Template-based spam with minor variations
- Any text pattern the model has been trained on

Trusted and admin users skip this check entirely.
