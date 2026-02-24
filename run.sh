#!/bin/sh

BLUE="\033[38;5;117m"
NC="\033[0m"

printf "\n\n\n${BLUE}############ Starting Gost Config Generator... ############${NC}\n"
./GostGen

printf "\n\n\n${BLUE}############ Starting WireGard VPN... ############${NC}\n"
resolvconf -a control 2>/dev/null < /etc/resolv.conf
resolvconf -u
WG_CONFIG_PATTERN="/data/*.conf"
WG_CHECK="curl -fsS --connect-timeout 5 --max-time 5 https://am.i.mullvad.net/json | grep -q '\"mullvad_exit_ip\":true'"
WG_STARTED=""
cleanup() {
  if [ -n "$WG_STARTED" ]; then
    printf "**** Shutdown WireGuard: %s ****\n" "$WG_STARTED"
    wg-quick down "$WG_STARTED" >/dev/null
    WG_STARTED=""
  fi
}
trap cleanup EXIT INT TERM
for cfg in $WG_CONFIG_PATTERN; do
  [ -f "$cfg" ] || continue
  if ! grep -q "^\[Interface\]" "$cfg" || ! grep -q "^\[Peer\]" "$cfg"; then
    printf "**** Found %s but it doesn't seem valid, skipping\n****" "$cfg"
    continue
  fi
  printf "**** Trying WireGuard config: %s ****\n" "$cfg"
  if wg-quick up "$cfg" >/dev/null; then
    if sh -c "$WG_CHECK"; then
      WG_STARTED="$cfg"
      printf "**** WireGuard started with: %s ****\n" "$cfg"
      break
    fi
    printf "**** Connectivity check failed for: %s ****\n" "$cfg"
    wg-quick down "$cfg" >/dev/null
  else
    printf "**** Failed to start with: %s ****\n" "$cfg"
  fi
done

if [ -z "$WG_STARTED" ]; then
  printf "**** No valid WireGuard config found in %s ****\n" "$WG_CONFIG_PATTERN"
  exit 1
fi

printf "\n\n\n${BLUE}############ Starting Proxies... ############${NC}\n"
./gost -C /data/gost.yaml
