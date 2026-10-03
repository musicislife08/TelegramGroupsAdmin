# Analytics Dashboard

The Analytics page gives you a bird's-eye view of how your spam detection is performing across all your groups. Navigate to **Analytics** in the sidebar to access it.

Most tabs offer a date range selector (Last 7 / 30 Days, and Last 90 Days on Welcome Analytics) to adjust the reporting window; Content Detection always shows the last 30 days.

**Who sees what:** GlobalAdmin and Owner accounts see every tab. Admin-level accounts can use **Message Trends**, which is scoped to their own chats; the global tabs (Content Detection, Performance, Welcome Analytics) appear but are disabled, and the global cards on the home dashboard are greyed out with no numbers. See [Web User Management](../admin/01-web-user-management.md#permission-levels).

## Content Detection

The default tab shows your overall detection activity:

- **Total content checks** — how many messages TGA has analyzed
- **Spam detected** — count and percentage of messages flagged as spam
- **Stop words in use** — how many of your stop words are active
- **Training samples** — how many spam/ham examples you've provided

The **recent spam checks table** shows individual detections with timestamps, scores, and which checks triggered. Use this to spot-check whether detections look accurate.

The **OpenAI False Positive Prevention** section shows how often the AI veto overrode algorithmic detections — useful for gauging whether the AI provider is earning its keep.

## Message Trends

Switch to this tab to understand messaging patterns in your groups:

- **Key metrics** — Total messages, daily average, active users, spam rate
- **Daily volume chart** — See when your groups are most active and when spam peaks
- **Most active users** — Ranking of who's posting the most
- **Per-chat breakdown** — Compare activity across your different groups

Use the chat filter to focus on a single group or view all groups combined.

## Performance

This tab answers the question: "How accurate is my spam detection?"

- **Overall Accuracy** — Percentage of correct decisions (true positives + true negatives)
- **False Positive Rate** — How often legitimate messages get flagged as spam. Lower is better.
- **False Negative Rate** — How often actual spam slips through. Lower is better.
- **Response Time** — Average and P95 detection latency

The **Algorithm Performance table** breaks down each detection check individually, showing hit rate, average score contribution, and false positive count. This helps you decide which checks to tune or disable.

## Welcome Analytics

If you use the Welcome system (exams, profile scanning), this tab tracks join activity:

- **Total joins** and **Acceptance Rate** — What percentage of new users pass your gates
- **Average time to accept** — How quickly users complete your welcome process
- **Timeout Rate** — How many users fail to respond in time
- **Per-chat breakdown** — Compare join patterns across groups

A high timeout rate might mean your welcome exam is too difficult or your timeout is too short.
