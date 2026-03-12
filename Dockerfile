FROM alpine:3.23

ARG TARGETOS
ARG TARGETARCH
ARG TARGETVARIANT
ARG GOST_VERSION=3.2.6

# Install required tools, dotnet dependencies and wireguard dependencies + fixes
RUN apk add --no-cache curl grep \
    icu-libs icu-data-full \
    iproute2 iptables ip6tables openresolv wireguard-tools && \
    echo "wireguard" >> /etc/modules && \
    rm -rf /etc/wireguard && \
    ln -s /config/wg_confs /etc/wireguard && \
    sed -i 's|\[\[ $proto == -4 \]\] && cmd sysctl -q net\.ipv4\.conf\.all\.src_valid_mark=1|[[ $proto == -4 ]] \&\& [[ $(sysctl -n net.ipv4.conf.all.src_valid_mark) != 1 ]] \&\& cmd sysctl -q net.ipv4.conf.all.src_valid_mark=1|' /usr/bin/wg-quick && \
    rm -rf /tmp/* /var/tmp/* /var/cache/distfiles/*

# Download and add GOST binary (https://github.com/go-gost/gost) for socks5 proxy server
RUN apk add --no-cache --virtual .fetch-deps wget tar && \
    wget -qO /tmp/gost.tar.gz "https://github.com/go-gost/gost/releases/download/v${GOST_VERSION}/gost_${GOST_VERSION}_${TARGETOS}_${TARGETARCH}${TARGETVARIANT}.tar.gz" && \
    tar -xzf /tmp/gost.tar.gz -C / && \
    chmod a+x /gost && \
    apk del .fetch-deps && \
    rm -rf /tmp/* /var/tmp/* /var/cache/distfiles/*

# Add project binaries
COPY --chmod=755 run.sh /run.sh
COPY --chmod=755 GostGen/publish/${TARGETOS}/${TARGETARCH}${TARGETVARIANT} .

HEALTHCHECK --interval=30s --timeout=30s --retries=5 --start-period=30s CMD \
  sh -c "curl -fs https://am.i.mullvad.net/json | grep -q '\"mullvad_exit_ip\":true'"

# TCP 1080 = local proxy
# TCP 9100 = GOST Prometheus Metrics
# TCP 2000-5000 = Mullvad proxies Cities + Pools (amount of used ports is dynamic and depends on config)
EXPOSE 1080 9100 2000-5000

VOLUME ["/data"]
CMD [ "/run.sh" ]