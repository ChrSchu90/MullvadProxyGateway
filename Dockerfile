FROM --platform=$BUILDPLATFORM alpine:3.23
ARG BUILDPLATFORM=linux/amd64
ARG GOST_VERSION=3.2.6

# Install required tools, dotnet dependencies and wireguard dependencies + fixes
RUN apk add --no-cache curl \
    icu-libs icu-data-full \
    iproute2 iptables ip6tables openresolv wireguard-tools && \
    echo "wireguard" >> /etc/modules && \
    sed -i 's|\[\[ $proto == -4 \]\] && cmd sysctl -q net\.ipv4\.conf\.all\.src_valid_mark=1|[[ $proto == -4 ]] \&\& [[ $(sysctl -n net.ipv4.conf.all.src_valid_mark) != 1 ]] \&\& cmd sysctl -q net.ipv4.conf.all.src_valid_mark=1|' /usr/bin/wg-quick && \
    rm -rf /tmp/* /var/tmp/* /var/cache/distfiles/*

# Download and add GOST binary (https://github.com/go-gost/gost) for socks5 proxy server
RUN set -eux; \
    apk add --no-cache --virtual .fetch-deps wget tar; \
    case "${BUILDPLATFORM}" in \
      "linux/amd64") GOST_ARCH="linux_amd64" ;; \
      "linux/arm/v7") GOST_ARCH="linux_armv7" ;; \
      "linux/arm64/v8") GOST_ARCH="linux_arm64" ;; \
      "") echo "BUILDPLATFORM is empty — are you using buildx / BuildKit?"; exit 1 ;; \
      *) echo "Unsupported BUILDPLATFORM: ${BUILDPLATFORM}"; exit 1 ;; \
    esac; \
    wget -qO /tmp/gost.tar.gz "https://github.com/go-gost/gost/releases/download/v${GOST_VERSION}/gost_${GOST_VERSION}_${GOST_ARCH}.tar.gz"; \
    tar -xzf /tmp/gost.tar.gz -C /; \
    chmod a+x /gost; \
    apk del .fetch-deps; \
    rm -rf /tmp/* /var/tmp/* /var/cache/distfiles/*;

# Add project binaries
COPY --chmod=755 run.sh /run.sh
COPY --chmod=755 GostGen/publish/${BUILDPLATFORM} .

HEALTHCHECK --interval=15s --timeout=5s --retries=3 --start-period=15s CMD \
  sh -c "curl -fs https://am.i.mullvad.net/json | grep -q '\"mullvad_exit_ip\":true'"

# TCP 1080 = local proxy
# TCP 9100 = Prometheus Metrics
# TCP 2000-3000 = Mullvad City proxies
EXPOSE 1080 9100 2000-3000

VOLUME ["/data"]
CMD [ "/run.sh" ]