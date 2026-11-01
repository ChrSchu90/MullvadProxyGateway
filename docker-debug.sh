#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")" || exit

DOCKER_FILE=Dockerfile
IMAGE_NAME=mullvad-proxy-gateway:dev
DOCKER_PLATFORM=linux/amd64 # linux/amd64 linux/arm64/v8 linux/arm/v7
APP_VERSION=0.0.1-debug
GOST_VERSION=3.3.2

docker buildx build --load --progress=plain --platform ${DOCKER_PLATFORM} --build-arg APP_VERSION="${APP_VERSION}" --build-arg GOST_VERSION=${GOST_VERSION} -f ${DOCKER_FILE} -t ${IMAGE_NAME} . && \
  docker volume create mullvadproxygateway_data && \
  docker run --rm -it --platform ${DOCKER_PLATFORM} -p 1080:1080 -p 9100:9100 -p 2000-3000:2000-3000 -v mullvadproxygateway_data:/data --cap-add NET_ADMIN --sysctl net.ipv4.conf.all.src_valid_mark=1 ${IMAGE_NAME}