#!/bin/sh

docker volume create mullvad-proxy-gateway_data && \
docker run -d \
  --name mullvad-proxy-gateway \
  --restart unless-stopped \
  -p 1080:1080 \
  -p 9100:9100 \
  -p 2000-5000:2000-5000 \
  -v mullvad-proxy-gateway_data:/data \
  --cap-add=NET_ADMIN \
  --sysctl net.ipv4.conf.all.src_valid_mark=1 \
  -e TZ=Europe/Berlin \
  ghcr.io/chrschu90/mullvad-proxy-gateway:1
