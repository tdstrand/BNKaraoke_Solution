#!/usr/bin/env bash

set -euo pipefail

# As of 2024-09-01 the Big Buck Bunny clip (aqz-KE-bpKQ) only exposes MP4 tracks via
# the Android player client when SABR is active. Override the URL if that changes.
VIDEO_URL="${1:-https://www.youtube.com/watch?v=aqz-KE-bpKQ}"
TMP_DIR="$(mktemp -d)"
OUTPUT_PATH="${TMP_DIR}/sabr-test.mp4"

cleanup() {
  rm -rf "${TMP_DIR}"
}

trap cleanup EXIT

echo "Running yt-dlp SABR regression check for ${VIDEO_URL}" >&2

ANDROID_EXTRACTOR_ARGS="youtube:player_client=android"
if [[ -n "${YOUTUBE_API_KEY:-}" ]]; then
  ANDROID_EXTRACTOR_ARGS+=",api_key=${YOUTUBE_API_KEY}"
fi

PRIMARY_FORMAT="bestvideo[ext=mp4]+bestaudio[ext=m4a]/b[ext=mp4]"
FALLBACK_FORMAT="bv*+ba/b"

echo "Attempting primary MP4 download (${PRIMARY_FORMAT})" >&2
if yt-dlp \
  --output "${OUTPUT_PATH}" \
  -f "${PRIMARY_FORMAT}" \
  --merge-output-format mp4 \
  --extractor-args "${ANDROID_EXTRACTOR_ARGS}" \
  "${VIDEO_URL}"; then
  echo "Primary attempt succeeded" >&2
  exit 0
fi

echo "Primary attempt failed, attempting SABR fallback (${FALLBACK_FORMAT})" >&2
yt-dlp \
  --output "${OUTPUT_PATH}" \
  -f "${FALLBACK_FORMAT}" \
  --extractor-args "${ANDROID_EXTRACTOR_ARGS}" \
  --recode-video mp4 \
  "${VIDEO_URL}"

echo "Fallback attempt succeeded" >&2
