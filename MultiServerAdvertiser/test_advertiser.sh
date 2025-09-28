#!/bin/bash
cd /home/main/work/ss14-adv/MultiServerAdvertiser
echo "=== Testing Fixed Advertiser ==="
echo "Starting advertiser for 60 seconds..."
timeout 60s ./publish/MultiServerAdvertiser 2>&1 | tee advertiser_test.log
echo "=== Test completed ==="

