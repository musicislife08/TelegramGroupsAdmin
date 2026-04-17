# Stop Word Recommendations

TGA can analyze your spam patterns and suggest which words to add or remove from your stop words list. Navigate to **Settings** -> **Training Data** -> **Stop Words Library** and look for the **Generate Recommendations** button.

## What It Does

The recommendation engine compares your spam training data against legitimate messages to find:

- **Words to add** — Words that appear frequently in spam but rarely in normal messages (high spam-to-legit ratio)
- **Words to remove** — Words in your stop list that trigger too many false positives (precision below 70%)
- **Performance cleanup** — Words that are slowing down detection without being effective (when check execution time exceeds 200ms)

## Generating Recommendations

1. Select an analysis period (defaults to the last 30 days)
2. Click **Generate Recommendations**
3. Review the results

You need at least **50 spam samples** and **100 legitimate message samples** for the analysis to produce meaningful results. If you don't have enough training data yet, keep reviewing detections in the Reports page to build up your sample set.

## Reviewing Suggestions

Each recommendation shows you the evidence:

**For additions:** The word, how often it appears in spam vs. legitimate messages, and the spam-to-legit ratio. A word that appears 5x more often in spam than in normal messages is a strong candidate.

**For removals:** The word, its precision percentage, and why it's being recommended for removal. A word with 60% precision means 40% of the time it flags a legitimate message — that's too high.

**For performance cleanup:** The word, its efficiency score, and estimated time savings if removed. These appear only when your stop word check is running slower than 200ms.

## Accepting Suggestions

Click **Add** or **Remove** on individual recommendations to apply them. Each action immediately updates your stop words list — there's no batch apply. This lets you cherry-pick the suggestions you agree with.
