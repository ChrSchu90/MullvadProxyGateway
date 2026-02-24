#!/bin/bash

cd "$(dirname "$0")"

DOCKER_FILE="Dockerfile"
IMAGE_NAME="mullvad-proxy-gateway:dev"
NET_PROJECT=./GostGen/source/GostGen.csproj
NET_BUILD_ARGS="-p:DebugType=embedded -p:PublishSingleFile=true -p:Version=0.0.1 --verbosity normal --configuration Release --self-contained"
GOST_VERSION="3.1.0"

dotnet publish ${NET_PROJECT} -r linux-musl-x64 ${NET_BUILD_ARGS} -o ./GostGen/publish/linux/amd64 && \
  dotnet publish ${NET_PROJECT} -r linux-musl-arm ${NET_BUILD_ARGS} -o ./GostGen/publish/linux/arm/v7 && \
  dotnet publish ${NET_PROJECT} -r linux-musl-arm64 ${NET_BUILD_ARGS} -o ./GostGen/publish/linux/arm64/v8 && \
  docker buildx build --progress=plain --rm --platform linux/amd64,linux/arm/v7,linux/arm64/v8 --build-arg GOST_VERSION=${GOST_VERSION} -f ${DOCKER_FILE} -t ${IMAGE_NAME} . && \
  docker volume create mullvadproxygateway_data  && \
  docker run --rm -it -v mullvadproxygateway_data:/data -p 1080:1080 -p 9100:9100 -p 2000-3000:2000-3000 --cap-add NET_ADMIN --sysctl net.ipv4.conf.all.src_valid_mark=1 ${IMAGE_NAME}