#!/bin/bash
set -euo pipefail

# Configuration
CONTAINER_NAME="tga-db"  # From compose/compose.yml
DB_NAME="telegram_groups_admin"
DB_USER="tgadmin"
EXPORT_FILE="/tmp/messages_with_urls_v2.tsv"
OUTPUT_FILE="/tmp/enriched_messages_reviewed_v2.tsv"
RATE_LIMIT_DELAY=0.1  # 10 URLs/sec

echo "=== Part 1: Export Messages and Scrape URLs (v2 - Using jq) ==="
echo ""

# Step 1: Export messages with urls column from database via docker exec
# Replace newlines with literal \n to keep TSV format intact
echo "[1/3] Exporting all messages with URLs from database..."
docker exec -i "$CONTAINER_NAME" psql -U "$DB_USER" -d "$DB_NAME" -t -A -F $'\t' > "$EXPORT_FILE" <<'SQL'
SELECT
    m.message_id,
    REPLACE(REPLACE(m.message_text, E'\r', ''), E'\n', '\n') as message_text,
    m.urls
FROM messages m
WHERE m.message_text IS NOT NULL
  AND m.urls IS NOT NULL
  AND m.message_text NOT LIKE '%━━━ URL Previews ━━━%'
ORDER BY m.message_id;
SQL

message_count=$(wc -l < "$EXPORT_FILE")
echo "   ✓ Exported $message_count messages"
echo ""

# Step 2: Scrape URLs and build enriched messages
echo "[2/3] Scraping URLs and building previews..."
echo -e "message_id\tenriched_text" > "$OUTPUT_FILE"

processed=0
while IFS=$'\t' read -r message_id message_text urls_json; do
    ((processed++))
    echo "[$processed/$message_count] Processing message $message_id..."

    # Unescape literal \n back to actual newlines
    message_text=$(echo -e "$message_text")

    # Parse JSON array of URLs using jq
    if [ -z "$urls_json" ] || [ "$urls_json" = "null" ]; then
        echo "   ⚠ No URLs found, skipping"
        continue
    fi

    preview_section=""
    url_count=0

    # Process each URL from JSON array
    while IFS= read -r url; do
        ((url_count++))
        echo "   Scraping URL #$url_count: $url"

        # Fetch HTML with 10s timeout
        html=$(curl -s -L --max-time 10 \
            -A "Mozilla/5.0 (TelegramGroupsAdmin/1.0)" \
            -H "Accept: text/html" \
            "$url" 2>/dev/null || true)

        if [ -z "$html" ]; then
            echo "      ✗ Failed to fetch, skipping"
            sleep "$RATE_LIMIT_DELAY"
            continue
        fi

        # Extract metadata using sed (portable, no -P flag needed)
        title=$(echo "$html" | sed -n 's/.*<title[^>]*>\([^<]*\)<\/title>.*/\1/p' | head -1 | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
        description=$(echo "$html" | sed -n 's/.*<meta[^>]*name="description"[^>]*content="\([^"]*\)".*/\1/p' | head -1)
        og_title=$(echo "$html" | sed -n 's/.*<meta[^>]*property="og:title"[^>]*content="\([^"]*\)".*/\1/p' | head -1)
        og_desc=$(echo "$html" | sed -n 's/.*<meta[^>]*property="og:description"[^>]*content="\([^"]*\)".*/\1/p' | head -1)

        # Build preview for this URL
        preview_section+="$url"$'\n'
        [ -n "$title" ] && preview_section+="$title"$'\n'
        [ -n "$description" ] && preview_section+="$description"$'\n'
        [ -n "$og_title" ] && preview_section+="$og_title"$'\n'
        [ -n "$og_desc" ] && preview_section+="$og_desc"$'\n'
        preview_section+=$'\n'  # Blank line between URLs

        echo "      ✓ Extracted: title=${title:0:50}..."
        sleep "$RATE_LIMIT_DELAY"  # Rate limiting
    done < <(echo "$urls_json" | jq -r '.[]')

    # Build enriched message text
    if [ -n "$preview_section" ]; then
        enriched_text="${message_text}"$'\n\n'"━━━ URL Previews ━━━"$'\n\n'"${preview_section}"

        # Append to output file (TSV format, escape tabs and newlines for PostgreSQL)
        enriched_text_escaped=$(echo "$enriched_text" | sed 's/\\/\\\\/g; s/\t/\\t/g; s/\r/\\r/g')
        enriched_text_escaped=$(echo "$enriched_text_escaped" | awk '{printf "%s\\n", $0}' | sed 's/\\n$//')

        printf "%s\t%s\n" "$message_id" "$enriched_text_escaped" >> "$OUTPUT_FILE"
        echo "   ✓ Enriched with $url_count URL(s)"
    fi
    echo ""
done < "$EXPORT_FILE"

echo ""
echo "=== Part 1 Complete ==="
echo "Output saved to: $OUTPUT_FILE"
echo ""
echo "NEXT STEPS:"
echo "1. Review the file: less $OUTPUT_FILE"
echo "2. Verify a few enriched messages look correct"
echo "3. If satisfied, run: ./backfill_part2_import_v2.sh"
echo ""
