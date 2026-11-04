#!/usr/bin/env bash
# Build the image, start the portal, then wait for /health.
# Usage (on the server): sudo bash ~/app/deploy/ec2/start.sh
set -euo pipefail

cd "$(dirname "$0")"
SHARED_NETWORK=ec2_default

if ! docker network inspect "$SHARED_NETWORK" >/dev/null 2>&1; then
  echo "ERROR: Docker network $SHARED_NETWORK not found. Are the other RouteGo containers running?" >&2
  exit 1
fi

echo ">> Building image (first time takes 5-10 minutes)"
docker build -t routego/app:latest ../..

echo ">> Starting the portal"
if ! docker compose up -d; then
  echo "ERROR: portal start failed. Last logs:" >&2
  docker compose ps -a
  docker compose logs --tail 50 app nginx
  exit 1
fi

echo ">> Waiting for the portal to be healthy"
for _ in $(seq 1 30); do
  if curl -fs http://localhost:8080/health >/dev/null 2>&1; then
    docker compose ps -a
    echo ">> DEPLOY OK — portal is healthy on port 8080"
    exit 0
  fi
  sleep 2
done

echo "ERROR: portal did not become healthy. Last logs:" >&2
docker compose ps -a
docker compose logs --tail 50 app nginx
exit 1
