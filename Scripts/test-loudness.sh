#!/bin/bash
# Usage: test-loudness.sh <media-file> [target-lufs]
# Runs ffmpeg loudnorm filter and prints the detected input loudness.

if [ -z "$1" ]; then
  echo "Usage: $0 <media-file> [target-lufs]"
  exit 1
fi

TARGET=${2:--14}

ffmpeg -i "$1" -af loudnorm=I=$TARGET:TP=-1.5:LRA=11 -f null - 2>&1 | grep -E "Input Integrated"
