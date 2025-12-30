# Naive Bayes Classifier

The Naive Bayes algorithm uses statistical word frequency analysis to classify messages as spam or ham (legitimate).

## How It Works

1. Learn word probabilities from training data
2. Calculate P(spam|message) using Bayes' theorem
3. Compare against probability threshold
4. Return confidence based on probability difference

## Configuration

- **Threshold**: 50-80% probability recommended
- **Training Requirements**: Minimum 50 spam + 50 ham samples
- **Performance**: Fast (near-instantaneous)

## Training

The classifier learns from:
- **Spam samples**: Messages marked as spam in review queue
- **Ham samples**: Normal messages from trusted users
- **Continuous learning**: Adapts to new spam patterns over time

## Advantages

- Very fast once trained
- Adapts to your specific community's language
- Low false positive rate with good training data
- No external API dependencies
