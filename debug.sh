#!/bin/bash

cd "$(dirname "$0")" || exit

DOCKER_FILE="Dockerfile"
IMAGE_NAME="mullvad-proxy-gateway:dev"
NET_PROJECT=./GostGen/source/GostGen.csproj
NET_BUILD_ARGS="-p:DebugType=embedded -p:PublishSingleFile=true -p:Version=0.0.1 --verbosity normal --configuration Release --self-contained"
DOCKER_PLATFORM=linux/amd64 # linux/amd64 linux/arm64/v8 linux/arm/v7
GOST_VERSION="3.2.6"

dotnet publish ${NET_PROJECT} -r linux-musl-x64 ${NET_BUILD_ARGS} -o ./GostGen/publish/linux/amd64 && \
  dotnet publish ${NET_PROJECT} -r linux-musl-arm64 ${NET_BUILD_ARGS} -o ./GostGen/publish/linux/arm64 && \
  dotnet publish ${NET_PROJECT} -r linux-musl-arm ${NET_BUILD_ARGS} -o ./GostGen/publish/linux/armv7 && \
  docker buildx build --load --progress=plain --platform ${DOCKER_PLATFORM} --build-arg GOST_VERSION=${GOST_VERSION} -f ${DOCKER_FILE} -t ${IMAGE_NAME} . && \
  docker volume create mullvadproxygateway_data  && \
  docker run --rm -it --platform ${DOCKER_PLATFORM} --network host -v mullvadproxygateway_data:/data --cap-add NET_ADMIN --sysctl net.ipv4.conf.all.src_valid_mark=1 ${IMAGE_NAME}