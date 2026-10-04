#!/usr/bin/env bash
# Starts all three modules (Linux / macOS / Git Bash). Stop everything with Ctrl+C.
set -e
cd "$(dirname "$0")"

dotnet build AssetHub.sln -c Debug --nologo -v q

pids=()
trap 'kill "${pids[@]}" 2>/dev/null || true' EXIT INT TERM

for m in Identity Assets Booking; do
  (cd "src/AssetHub.$m" && dotnet run --no-build --no-launch-profile) &
  pids+=($!)
  # Identity first, so the other modules can reach it
  [ "$m" = "Identity" ] && sleep 4
done

echo
echo "Identity & Organization : http://localhost:5001"
echo "Assets & Allocation     : http://localhost:5002"
echo "Booking & Audit         : http://localhost:5003"
echo "Default admin           : admin@assethub.local / Admin@123"
wait
