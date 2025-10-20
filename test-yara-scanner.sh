#!/bin/bash

# Test script for YARA scanner with EICAR test file
# This validates that the YaraScannerService can detect threats

set -e

echo "=== YARA Scanner Test ==="
echo ""

# Create test directory
TEST_DIR="./test-files"
mkdir -p "$TEST_DIR"

# Create EICAR test file
echo "Creating EICAR test file..."
echo 'X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*' > "$TEST_DIR/eicar.com"

# Create clean test file
echo "Creating clean test file..."
echo "This is a perfectly safe text file with no threats." > "$TEST_DIR/clean.txt"

# Create fake crypto stealer pattern
echo "Creating fake crypto wallet stealer test..."
cat > "$TEST_DIR/fake_stealer.txt" << 'EOF'
# Fake crypto stealer for testing YARA rules
import os

# Target multiple wallet paths
wallet_paths = [
    "\\Bitcoin\\wallet.dat",
    "\\Ethereum\\keystore\\",
    "\\Exodus\\exodus.wallet",
    "\\atomic\\Local Storage\\"
]

# Harvest seed phrases
keywords = ["seed phrase", "recovery phrase", "mnemonic"]

# Exfiltration via webhook
webhook_url = "https://discord.com/api/webhooks/fake"
EOF

echo ""
echo "Test files created in $TEST_DIR:"
ls -lh "$TEST_DIR"

echo ""
echo "=== Test Results ==="
echo "1. EICAR file should be detected by eicar_test.yar"
echo "2. Fake stealer should be detected by crypto_stealers.yar"
echo "3. Clean file should pass all rules"
echo ""
echo "To test with the actual scanner, you'll need to:"
echo "1. Set FILESCANNING__TIER1__YARA__RULESPATH=$(pwd)/yara-rules"
echo "2. Run the application with file scanning enabled"
echo "3. Upload these test files via Telegram"
echo ""
echo "For manual YARA testing (if yara command available):"
echo "  yara -r ./yara-rules/ $TEST_DIR/eicar.com"
echo "  yara -r ./yara-rules/ $TEST_DIR/fake_stealer.txt"
echo "  yara -r ./yara-rules/ $TEST_DIR/clean.txt"
