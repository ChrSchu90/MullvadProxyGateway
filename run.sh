#!/bin/sh

printf "\n\n\n############ Starting Gost Config Generator... ############\n"
./GostGen

printf "\n\n\n############ Preparing DNS resolver... ############\n"
resolvconf -a control 2>/dev/null < /etc/resolv.conf
resolvconf -u

printf "\n\n\n############ Starting WireGuard VPN... ############\n"
WG_CONFIG_PATTERN="/data/*.conf"
WG_STARTED=""
for cfg in $WG_CONFIG_PATTERN; do
  [ -f "$cfg" ] || continue
  if ! grep -q "^\[Interface\]" "$cfg" || ! grep -q "^\[Peer\]" "$cfg"; then
    printf "**** Found %s but it doesn't seem valid, skipping ****\n" "$cfg"
    continue
  fi
  printf "**** Trying WireGuard config: %s ****\n" "$cfg"
  if wg-quick up "$cfg" >/dev/null; then
    if curl -fsS --connect-timeout 5 --max-time 5 https://am.i.mullvad.net/json | grep -q '"mullvad_exit_ip":true'; then
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

printf "\n\n\n############ Starting Proxies... ############\n"
exec ./gost -C /data/gost.yaml
