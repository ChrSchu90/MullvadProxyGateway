FROM --platform=$BUILDPLATFORM alpine:3.23 AS build
ARG BUILDPLATFORM=linux/amd64
ARG GOST_VERSION=3.1.0

# Install debug tools
RUN apk add --no-cache curl

# Install dotnet dependencies
RUN apk add --no-cache icu-libs icu-data-full

# Install WireGuard dependencies + setup
RUN apk add --no-cache iproute2 iptables ip6tables openresolv wireguard-tools && \
    echo "wireguard" >> /etc/modules && \
    sed -i 's|\[\[ $proto == -4 \]\] && cmd sysctl -q net\.ipv4\.conf\.all\.src_valid_mark=1|[[ $proto == -4 ]] \&\& [[ $(sysctl -n net.ipv4.conf.all.src_valid_mark) != 1 ]] \&\& cmd sysctl -q net.ipv4.conf.all.src_valid_mark=1|' /usr/bin/wg-quick

# Add GostGen for config file generation
COPY GostGen/publish/${BUILDPLATFORM} .
RUN chmod a+x /GostGen

# Download and add ghost (https://github.com/go-gost/gost)
RUN set -eux; \
    apk add --no-cache --virtual .fetch-deps tar; \
    case "${BUILDPLATFORM}" in \
      "linux/amd64") GOST_ARCH="linux_amd64" ;; \
      "linux/arm/v7") GOST_ARCH="linux_armv7" ;; \
      "linux/arm64/v8") GOST_ARCH="linux_arm64" ;; \
      "") echo "BUILDPLATFORM is empty — are you using buildx / BuildKit?"; exit 1 ;; \
      *) echo "Unsupported BUILDPLATFORM: ${BUILDPLATFORM}"; exit 1 ;; \
    esac; \
    wget -qO /tmp/gost.tar.gz "https://github.com/go-gost/gost/releases/download/v${GOST_VERSION}/gost_${GOST_VERSION}_${GOST_ARCH}.tar.gz"; \
    tar -xzf /tmp/gost.tar.gz -C /; \
    rm -f /tmp/gost.tar.gz; \
    apk del .fetch-deps; \
    chmod a+x /gost;

# Cleanup
RUN rm -rf /root/.cache && mkdir -p /root/.cache && \
    rm -rf /tmp/* /var/tmp/* /var/cache/apk/* /var/cache/distfiles/*

# Healthcheck
HEALTHCHECK --interval=15s --timeout=5s --retries=3 --start-period=10s CMD \
  sh -c "curl -fs https://am.i.mullvad.net/json | grep -q '\"mullvad_exit_ip\":true'"

# Add run script
COPY run.sh /run.sh
RUN chmod a+x /run.sh

LABEL org.opencontainers.image.source=https://github.com/ChrSchu90/MullvadProxyGateway
LABEL org.opencontainers.image.description="Turn single a Mullvad WireGuard client into a powerful, reusable proxy gateway. Route traffic from any device through it and connect seamlessly to any Mullvad provided city worldwide."
LABEL org.opencontainers.image.licenses=MIT

VOLUME ["/data"]

CMD [ "/run.sh" ]
