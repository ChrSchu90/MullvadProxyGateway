#!/bin/sh

docker volume create mullvad-proxy-gateway_data && \
docker run -d \
  --name mullvad-proxy-gateway \
  --restart unless-stopped \
  --network host \
  -v mullvad-proxy-gateway_data:/data \
  --cap-add=NET_ADMIN \
  -e TZ=Europe/Berlin \
  ghcr.io/chrschu90/mullvad-proxy-gateway:1
