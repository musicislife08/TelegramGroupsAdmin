# Naive Bayes Classifier

The Naive Bayes algorithm uses statistical word frequency analysis to classify messages as spam or ham (legitimate). It runs as a singleton service trained at startup via a background job, so classification is near-instantaneous.

## How It Works

1. The message text is preprocessed (emojis removed) and tokenized
2. The classifier calculates P(spam|message) using Bayes' theorem
3. If the probability falls in the uncertain range (40--60%), the check **abstains**
4. Otherwise, the probability is mapped to a tiered score

## Scoring Tiers

| Spam Probability | Points |
|------------------|--------|
| 99%+             | 5.0    |
| 95--98%          | 3.5    |
| 80--94%          | 2.0    |
| 70--79%          | 1.0    |
| 61--69%          | 0.5    |
| 40--60%          | Abstain (uncertain) |
| Below 40%        | Abstain (likely ham) |

## Abstention

The check abstains (returns 0 points with no vote) when:

- The message is too short (below the configured minimum length)
- The classifier has not been trained (insufficient data)
- The spam probability is 60% or below (uncertain or likely ham)
- An error occurs during classification

## Training

The classifier learns from:

- **Spam samples**: Messages marked as spam in the review queue
- **Ham samples**: Normal messages from trusted users
- **Background training**: A Quartz job retrains the model periodically; no per-request training overhead

## Advantages

- Near-instantaneous classification (singleton, pre-trained)
- Adapts to your specific community's language patterns
- Low false positive rate with sufficient training data
- No external API dependencies

Trusted and admin users skip this check entirely.
